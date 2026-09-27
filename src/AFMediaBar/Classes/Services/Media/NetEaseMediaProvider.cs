using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Players;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 读取网易云客户端内存并提供更精确的进度、歌曲标识、封面和歌词。
/// Reads NetEase client memory and provides precise progress, song identity, artwork, and lyrics.
/// </summary>
public sealed class NetEaseMediaProvider : IMediaSourceProvider, IMemoryPrunable
{
    private const string MemoryPlayerSourceId = NetEaseSourcePolicy.SourceId;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(233);

    /// <summary>
    /// 空闲档位的轮询周期。空闲意味着已经有五分钟没有媒体、十分钟没有用户操作：此时连"有没有在放"都不需要每秒问四次，
    /// 两秒一次足够在用户按下播放后很快跟上，而内存读取频率降到原来的十二分之一。
    /// The poll period at the idle level. Idle means no media for five minutes and no user input for ten, so "is anything playing" does not need to be
    /// asked four times a second: once every two seconds still follows a play press closely while cutting memory reads to a twelfth.
    /// </summary>
    private const int IdlePollIntervalMilliseconds = 2_000;

    /// <summary>
    /// 歌词缓存的容量：来源变多以后必须封顶，否则长时间播放会一直堆积解析结果。
    /// 条数之外还受 <see cref="LyricsCacheBudgetPolicy.DefaultBudgetBytes"/> 约束（见该策略的说明）。
    /// Capacity of the lyric cache: with more sources it has to be capped, otherwise long playback keeps accumulating parsed results. On top of that entry
    /// count it is bounded by <see cref="LyricsCacheBudgetPolicy.DefaultBudgetBytes"/>; see that policy for why.
    /// </summary>
    private const int LyricsCacheCapacity = 64;

    /// <summary>
    /// 封面缓存的容量：每张封面解码后约 256 KB，无界字典会随播放曲目数一直涨（长时间播放是一条稳定的内存增长曲线）。
    /// 只留最后几首的封面足够：封面总与当前曲目一起出现，切回上一首时重新下载一次的代价远小于常驻几十兆。
    /// Capacity of the artwork cache: one decoded cover is about 256 KB, and an unbounded dictionary grows with the number of played
    /// tracks, which is a steady memory climb over a long session. Keeping the last few covers is enough: a cover only appears together
    /// with its track, and re-downloading one beats keeping tens of megabytes resident.
    /// </summary>
    private const int ArtworkCacheCapacity = 8;

    private readonly Dispatcher _dispatcher;
    private readonly Func<INetEaseMemoryReader> _createReader;
    private readonly LyricsService _lyricsService;
    private readonly LruCache<string, BitmapImage?> _artworkCache = new(ArtworkCacheCapacity);
    private readonly HashSet<string> _pendingArtwork = new(StringComparer.OrdinalIgnoreCase);
    private readonly LruCache<string, LyricsResult?> _lyricsCache = new(
        LyricsCacheCapacity,
        result => LyricsCacheBudgetPolicy.EstimateBytes(result?.Document),
        LyricsCacheBudgetPolicy.DefaultBudgetBytes);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);

    /// <summary>缓存代次：取词设置变化时自增，让仍在飞行中的结果写不回来。/ Cache generation: incremented when retrieval settings change, so an in-flight result cannot be written back.</summary>
    private int _lyricsCacheGeneration;
    private readonly object _pollGate = new();
    private CancellationTokenSource? _cancellation;
    // 每次运行独占读取器；取消后在后台释放，再允许下一次运行进入。
    // A run exclusively owns its reader and releases it off-thread before the next run enters.
    private readonly SemaphoreSlim _readerGate = new(1, 1);
    private readonly NetEaseMemoryContinuity _continuity = new();
    private MemoryPruneLevel _pruneLevel;
    private int _currentProcessId;
    private PlayerInfo? _currentInfo;
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private int _version;
    private volatile bool _isDisposed;

    /// <summary>
    /// 轮询周期。剪枝会改写它，而轮询线程在另一个线程上读取，因此这是一个 volatile 字段而不是配置常量。
    /// The poll period. Pruning rewrites it and the polling thread reads it from another thread, so it is a volatile field rather than a constant.
    /// </summary>
    private volatile int _pollIntervalMilliseconds = (int)PollInterval.TotalMilliseconds;

    public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    /// <summary>
    /// 创建网易云来源提供器；实际进程读取在显式启动后进行，并由本实例负责释放。
    /// Creates the NetEase source provider; process reading begins only after explicit start and is owned by this instance.
    /// </summary>
    public NetEaseMediaProvider(LyricsService lyricsService)
        : this(lyricsService, Application.Current.Dispatcher, () => new NetEaseMemoryReader())
    {
    }

    internal NetEaseMediaProvider(LyricsService lyricsService, Dispatcher dispatcher, Func<INetEaseMemoryReader> createReader)
    {
        _dispatcher = dispatcher;
        _createReader = createReader;
        _lyricsService = lyricsService;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// 取词相关设置变化时清空缓存：本提供器每 233 毫秒轮询一次，因此下一次轮询会用新设置重新取词。
    /// Clears the cache after a retrieval-related settings change: this provider polls every 233 ms, so the next poll refetches with
    /// the new settings.
    /// </summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.SmtcSourceFilter) ||
            e.ResetScope is SettingsResetScope.ExtraFeatures or SettingsResetScope.All)
        {
            if (IsAllowed)
                Start();
            else
                StopPolling();
        }
        if (!LyricsCacheInvalidationPolicy.ShouldClearCache(e.PropertyName, e.ResetScope))
        {
            return;
        }

        _lyricsCache.Clear();
        _lyricsCacheGeneration++;
    }

    /// <summary>
    /// 判断来源标识是否属于网易云音乐。
    /// Determines whether the source identifier belongs to NetEase Cloud Music.
    /// </summary>
    public bool CanHandle(string sourceId) => NetEaseSourcePolicy.Matches(sourceId);

    private static bool IsAllowed => MediaSourceFilterPolicy.IsAllowed(
        MemoryPlayerSourceId, SettingsManager.Current.SmtcSourceFilter);

    /// <summary>
    /// 更新 SMTC 基线快照，供来源专用数据合并时保持媒体身份一致。
    /// Updates the SMTC baseline snapshot used to preserve media identity during source-specific enrichment.
    /// </summary>
    public void UpdateSessionSnapshot(MediaSnapshot snapshot)
    {
        Volatile.Write(ref _sessionSnapshot, snapshot.IsConnected && CanHandle(snapshot.SourceId)
            ? snapshot : MediaSnapshot.Disconnected);
    }

    /// <summary>
    /// 幂等启动来源轮询；重复调用不会创建额外计时器。
    /// Idempotently starts source polling without creating additional timers on repeated calls.
    /// </summary>
    public void Start()
    {
        lock (_pollGate)
        {
            if (_isDisposed || _cancellation is not null || !IsAllowed || _pruneLevel >= MemoryPruneLevel.DisplayOff)
                return;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _ = PollAsync(cancellation, cancellation.Token);
        }
    }

    /// <summary>
    /// 停止轮询并释放当前播放器读取器，阻止释放后继续发布快照。
    /// Stops polling and disposes the active player reader so no snapshots are published after disposal.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        StopPolling();
    }

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "netease-source";

    /// <summary>
    /// 按档位丢弃封面与歌词缓存，并放慢或停掉内存轮询。
    /// Drops the artwork and lyric caches for the level and slows down or stops the memory poll.
    ///
    /// 两级处理是有区别的：空闲档位只是"没人看，别那么勤快"，而显示器关闭或系统睡眠时连"看看有没有在放"都不必做——恢复由媒体事件驱动，
    /// 协调器一收到播放状态变化就会把档位调回常规，本提供器随即被重新启动。
    /// The two levels differ on purpose: the idle level only says "nobody is looking, so do not be so eager", while a closed display or a suspending
    /// system does not even need the "is anything playing" check, because the restore is driven by media events: the coordinator drops back to the
    /// ordinary level the moment a playback state changes, which restarts this provider.
    /// </summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        if (_isDisposed)
        {
            return;
        }

        _pruneLevel = level;
        if (level >= MemoryPruneLevel.Idle)
        {
            // 缓存清掉不会让界面变空：正在显示的那张封面由快照自己持有，这里丢掉的只是"下次再要时不用重新下载"的那一份。
            // Clearing the caches does not blank the interface: the cover on screen is held by the snapshot itself, and what is dropped here is only
            // the copy that saved a re-download.
            _artworkCache.Clear();
            _lyricsCache.Clear();
            _lyricsCacheGeneration++;
        }

        if (level >= MemoryPruneLevel.DisplayOff)
        {
            StopPolling();
            return;
        }

        _pollIntervalMilliseconds = level == MemoryPruneLevel.Idle
            ? IdlePollIntervalMilliseconds
            : (int)PollInterval.TotalMilliseconds;

        // 从 L2/L3 回到 L0/L1 时轮询是停着的，这里按需重启（Start 自身幂等）。
        // Coming back from L2/L3 the poll is stopped, so it is restarted here on demand; Start is idempotent by itself.
        Start();
    }

    /// <summary>
    /// 停止轮询并释放内存读取器，保留提供器本身可再次启动。
    /// Stops polling and releases the memory reader while keeping the provider restartable.
    /// </summary>
    private void StopPolling()
    {
        lock (_pollGate)
        {
            var cancellation = _cancellation;
            _cancellation = null;
            cancellation?.Cancel();
        }
        _currentInfo = null;
        _currentProcessId = 0;
        _version++;
        _continuity.Clear();
        _pendingArtwork.Clear();
        _pendingLyrics.Clear();
        if (!_isDisposed)
            SnapshotChanged?.Invoke(this, null);
    }

    private async Task PollAsync(CancellationTokenSource cancellation, CancellationToken token)
    {
        INetEaseMemoryReader? reader = null;
        var entered = false;
        try
        {
            await _readerGate.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            while (!token.IsCancellationRequested)
            {
                var baseline = Volatile.Read(ref _sessionSnapshot);
                var title = baseline.IsConnected ? baseline.Title : null;
                var result = await Task.Run(() =>
                {
                    reader ??= _createReader();
                    return reader.Read(title);
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_isDisposed || token.IsCancellationRequested || !ReferenceEquals(_cancellation, cancellation))
                        return;
                    if (result.ProcessId != _currentProcessId)
                    {
                        _currentProcessId = result.ProcessId;
                        _currentInfo = null;
                        _version++;
                        _continuity.Clear();
                    }
                    if (result.Info is { } info)
                        PublishPlayerInfo(info, token);
                    else
                    {
                        // 迟到的同曲封面/歌词不能覆盖失败缓冲或已退出的进程。
                        // Late artwork/lyrics cannot resurrect a failed or exited source.
                        _currentInfo = null;
                        _version++;
                        PublishSnapshot(_continuity.Update(null, result.ProcessId > 0, DateTimeOffset.UtcNow), token);
                    }
                }, DispatcherPriority.Background, token).Task.ConfigureAwait(false);
                await Task.Delay(_pollIntervalMilliseconds, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetEaseMediaProvider] Poll failed: {ex}");
        }
        finally
        {
            // 即使因异常或 Dispatcher 关闭退出，也撤销本轮尚在途中的补全结果。
            // Revoke enrichment from this run even when an exception or dispatcher shutdown ended it.
            lock (_pollGate)
            {
                cancellation.Cancel();
                if (ReferenceEquals(_cancellation, cancellation))
                    _cancellation = null;
            }
            try
            {
                if (reader is not null)
                    await Task.Run(reader.Dispose).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetEaseMediaProvider] Reader disposal failed: {ex}");
            }
            finally
            {
                if (entered)
                    _readerGate.Release();
                lock (_pollGate)
                {
                    cancellation.Dispose();
                }
            }
        }
    }

    private void PublishPlayerInfo(PlayerInfo info, CancellationToken token)
    {
        var version = _currentInfo is { } current &&
            string.Equals(current.Identity, info.Identity, StringComparison.Ordinal) &&
            string.Equals(current.Cover, info.Cover, StringComparison.Ordinal)
                ? _version
                : ++_version;
        _currentInfo = info;

        ImageSource? artwork = null;
        if (_artworkCache.TryGetValue(info.Cover, out var cachedArtwork))
        {
            artwork = cachedArtwork;
        }
        else if (!string.IsNullOrWhiteSpace(info.Cover) && _pendingArtwork.Add(info.Cover))
        {
            _ = LoadArtworkAsync(info.Cover, version, token);
        }

        var hasCachedLyrics = _lyricsCache.TryGetValue(info.Identity, out var lyrics);
        var shouldLoadLyrics = !hasCachedLyrics && _pendingLyrics.Add(info.Identity);
        PublishSnapshot(_continuity.Update(CreateSnapshot(info, artwork, lyrics), true, DateTimeOffset.UtcNow), token);
        if (shouldLoadLyrics)
        {
            _ = LoadLyricsAsync(info, token);
        }
    }

    private MediaSnapshot CreateSnapshot(PlayerInfo info, ImageSource? artwork, LyricsResult? lyrics) =>
        new(
            true,
            !info.Pause,
            false,
            false,
            false,
            info.Title,
            info.Artists,
            MemoryPlayerSourceId,
            MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, Translations.Get("Service.MediaSource.Unknown")),
            artwork,
            lyrics,
            info.Schedule,
            info.Duration,
            false,
            false,
            MediaRepeatMode.Unavailable,
            1,
            DateTimeOffset.UtcNow);

    private void PublishSnapshot(MediaSnapshot? snapshot, CancellationToken token)
    {
        // 所有发布与缓存写入都在 UI 线程；检查取消以阻止停止前启动的异步结果回流。
        // Publishing and cache writes stay on the UI thread; cancellation rejects results from a stopped run.
        if (_isDisposed || token.IsCancellationRequested || _dispatcher.HasShutdownStarted)
            return;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private async Task LoadArtworkAsync(string coverUrl, int version, CancellationToken token)
    {
        try
        {
            var artwork = await ArtworkLoader.GetImageFromUrlAsync(coverUrl, token);
            if (token.IsCancellationRequested || _isDisposed)
                return;
            _artworkCache.Set(coverUrl, artwork);
            if (artwork is not null && !_isDisposed && version == _version &&
                _currentInfo is { } info && string.Equals(info.Cover, coverUrl, StringComparison.OrdinalIgnoreCase))
            {
                _lyricsCache.TryGetValue(info.Identity, out var lyrics);
                PublishSnapshot(_continuity.Update(CreateSnapshot(info, artwork, lyrics), true, DateTimeOffset.UtcNow), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!token.IsCancellationRequested && !_isDisposed)
                _artworkCache.Set(coverUrl, null);
        }
        finally
        {
            if (!token.IsCancellationRequested)
                _pendingArtwork.Remove(coverUrl);
        }
    }

    private async Task LoadLyricsAsync(PlayerInfo info, CancellationToken token)
    {
        var generation = _lyricsCacheGeneration;
        var version = _version;
        try
        {
            var request = new LyricsRequest(info.Title, info.Artists, info.Album, info.Duration, info.Identity);
            var result = await _lyricsService.GetLyricsAsync(request, token);

            if (token.IsCancellationRequested || _isDisposed || generation != _lyricsCacheGeneration)
                return;

            // 取词过程中设置若被改过，这次结果已经不属于当前配置，写入只会让用户以为设置没生效。
            // If the settings changed while this retrieval ran, the result no longer belongs to the current configuration and
            // writing it would only make the setting look ineffective.
            if (generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(info.Identity, result);
            }

            if (!_isDisposed && version == _version && _currentInfo is { } current &&
                string.Equals(current.Identity, info.Identity, StringComparison.Ordinal))
            {
                _artworkCache.TryGetValue(current.Cover, out var artwork);
                PublishSnapshot(_continuity.Update(CreateSnapshot(current, artwork, result), true, DateTimeOffset.UtcNow), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!token.IsCancellationRequested && !_isDisposed && generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(info.Identity, null);
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
                _pendingLyrics.Remove(info.Identity);
        }
    }

}
