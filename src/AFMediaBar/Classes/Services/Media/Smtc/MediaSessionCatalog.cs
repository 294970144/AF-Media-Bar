// Owns the SMTC manager, subscriptions and retirement leases; disposal retires active resources.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services.Media.Smtc;

/// <summary>
/// 管理 SMTC 媒体会话生命周期，并将第三方动态集合复制为稳定快照。
/// Owns the SMTC session lifecycle and copies the third-party dynamic collection into stable snapshots.
/// </summary>
public sealed class MediaSessionCatalog : IDisposable
{
    private const int SnapshotRetryCount = 4;

    /// <summary>
    /// 所有读取与后台更新都通过短时租约使用底层管理器；重建或退出只撤销新租约，旧实例等最后一个使用者退出后在后台释放。
    /// Reads and background updates lease the underlying manager; replacement or shutdown stops new leases and retires the old
    /// instance on a background thread after its final user exits.
    /// </summary>
    private readonly ReplaceableResource<MediaManager> _managers;
    private int _isDisposed;

    public bool IsStarted
    {
        get
        {
            using var lease = _managers.TryAcquire();
            return lease?.Value.IsStarted ?? false;
        }
    }

    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties>? AnyMediaPropertyChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo>? AnyPlaybackStateChanged;
    public event Action<MediaSession>? AnySessionOpened;
    public event Action<MediaSession>? AnySessionClosed;
    public event Action<MediaSession>? FocusedSessionChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties>? AnyTimelinePropertyChanged;

    /// <summary>
    /// 创建并启动唯一的 SMTC 会话目录，目录事件由本实例统一转发和释放。
    /// Creates and starts the sole SMTC session catalog whose events are forwarded and released by this instance.
    /// </summary>
    public MediaSessionCatalog()
    {
        var manager = new MediaManager();
        _managers = new ReplaceableResource<MediaManager>(manager, RetireManager);
        try
        {
            AttachEvents(manager);
            manager.Start();
        }
        catch
        {
            _managers.Dispose();
            throw;
        }
    }

    /// <summary>订阅底层媒体管理器的目录事件；构造与重建共用同一份订阅清单。/ Attaches the catalog events of the underlying media manager; construction and a rebuild share this one list.</summary>
    private void AttachEvents(MediaManager manager)
    {
        manager.OnAnyMediaPropertyChanged += OnAnyMediaPropertyChanged;
        manager.OnAnyPlaybackStateChanged += OnAnyPlaybackStateChanged;
        manager.OnAnySessionOpened += OnAnySessionOpened;
        manager.OnAnySessionClosed += OnAnySessionClosed;
        manager.OnFocusedSessionChanged += OnFocusedSessionChanged;
        manager.OnAnyTimelinePropertyChanged += OnAnyTimelinePropertyChanged;
    }

    /// <summary>解除底层媒体管理器的目录事件，方向与 <see cref="AttachEvents"/> 严格对称。/ Detaches the catalog events in the exact reverse order of <see cref="AttachEvents"/>.</summary>
    private void DetachEvents(MediaManager manager)
    {
        manager.OnAnyMediaPropertyChanged -= OnAnyMediaPropertyChanged;
        manager.OnAnyPlaybackStateChanged -= OnAnyPlaybackStateChanged;
        manager.OnAnySessionOpened -= OnAnySessionOpened;
        manager.OnAnySessionClosed -= OnAnySessionClosed;
        manager.OnFocusedSessionChanged -= OnFocusedSessionChanged;
        manager.OnAnyTimelinePropertyChanged -= OnAnyTimelinePropertyChanged;
    }

    /// <summary>
    /// 重建底层媒体管理器。
    ///
    /// 第三方库存在 ForceUpdate 也救不回来的失效状态：系统仍发布着会话，但库的字典里残留着失效条目，
    /// 导致新会话既不加进来、旧条目也读不出内容。这时只能整只换掉管理器，让字典与订阅全部从零开始。
    /// Rebuilds the underlying media manager.
    ///
    /// The third-party library has a broken state that even ForceUpdate cannot fix: the OS still publishes sessions while the
    /// library's dictionary keeps a dead entry, so a new session is never added and the stale one reads nothing. The only cure is a
    /// fresh manager whose dictionary and subscriptions start from zero.
    ///
    /// 这是一条低频、有界的兜底路径：只有"看门狗确认系统真的发布了会话、却怎么都读不到"时才被调用。
    /// This is a low-frequency, bounded fallback: it runs only when the watchdog confirms the OS publishes sessions while the catalog
    /// stays unreadable.
    /// </summary>
    public void Restart()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        var replacement = new MediaManager();
        try
        {
            AttachEvents(replacement);
            replacement.Start();
        }
        catch
        {
            RetireManager(replacement);
            throw;
        }

        if (!_managers.TryReplace(replacement))
        {
            // Exit may have retired the catalog while the replacement was starting.
            RetireManager(replacement);
        }
    }

    /// <summary>
    /// 操作系统当前发布的会话数；目录不可用或读取失败时返回 -1。
    /// 看门狗用它区分"真的没有媒体"（0，不动作）与"库坏了但系统有会话"（&gt;0，触发重建）。
    /// Number of sessions the OS currently publishes, or -1 when the catalog is unavailable or the read fails.
    /// The watchdog uses it to tell "no media at all" (0, do nothing) from "the library is broken while the OS has sessions" (&gt;0,
    /// rebuild).
    /// </summary>
    public int GetOsSessionCount()
    {
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            return -1;
        }

        try
        {
            return lease.Value.WindowsSessionManager?.GetSessions().Count ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 采集目录两侧的对照快照：Windows 报告了哪些会话、库字典里有哪些、其中哪些已读不出内容。
    ///
    /// 这是纯读取，不做任何恢复动作，因此可以在故障态反复采集。字典由 WinRT 事件线程改写，
    /// 采集与 <see cref="TryGetSnapshot"/> 一样只能缩小竞态窗口：某一项读到一半失效时会把它归入"不可读"，
    /// 而不会让整次采集失败。
    /// Captures a two-sided snapshot of the catalog: which sessions Windows reports, which the library dictionary holds, and which of
    /// those already read nothing.
    ///
    /// This is a pure read with no recovery action, so it can be collected repeatedly while broken. The dictionary is mutated on WinRT
    /// event threads, so like <see cref="TryGetSnapshot"/> this only narrows the race window: an entry that goes stale mid-collection is
    /// reported as unreadable rather than failing the whole collection.
    /// </summary>
    public MediaSessionCatalogState CaptureState()
    {
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            return MediaSessionCatalogState.Unavailable;
        }

        var manager = lease.Value;

        List<string> osSourceIds = [];
        try
        {
            if (manager.WindowsSessionManager is { } sessionManager)
            {
                osSourceIds.AddRange(sessionManager.GetSessions()
                    .Select(static session => session.SourceAppUserModelId ?? string.Empty));
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // 采不到系统侧就如实留空：诊断行里 os=0 与"系统确实没有会话"必须能区分开，因此不静默吞掉而是留下痕迹。
            // Leaving the OS side empty when it cannot be read keeps "os=0" distinguishable from "the OS truly has no sessions", so the
            // failure leaves a trace instead of being swallowed.
            Debug.WriteLine($"[MediaSessionCatalog] Failed to enumerate native sessions for diagnostics: {exception.Message}");
        }

        List<string> librarySourceIds = [];
        List<string> unusableSourceIds = [];
        try
        {
            foreach (var session in manager.CurrentMediaSessions.Values)
            {
                var sourceId = session.Id ?? string.Empty;
                librarySourceIds.Add(sourceId);
                if (!MediaSessionGuard.IsUsable(session))
                {
                    unusableSourceIds.Add(sourceId);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // 字典正在被 WinRT 事件线程改写，这一轮采到多少算多少。
            // The dictionary is being mutated on a WinRT event thread, so whatever this round collected stands.
        }

        return new MediaSessionCatalogState(osSourceIds, librarySourceIds, unusableSourceIds);
    }

    /// <summary>
    /// 返回当前动态会话集合的稳定数组快照，并剔除已被第三方库关闭的会话；目录尚未就绪时返回 <see langword="false"/>。
    /// 字典由 WinRT 事件线程改写而本方法在 UI 线程读取，因此剔除只能缩小竞态窗口，消费者仍须通过
    /// <see cref="MediaSessionGuard"/> 判定可用性。
    /// Returns a stable array snapshot of the dynamic session collection with sessions already closed by the third-party
    /// library removed, or <see langword="false"/> while the catalog is unavailable. The dictionary is mutated on WinRT event
    /// threads while this method reads it on the UI thread, so filtering only narrows the race window and consumers must
    /// still check usability through <see cref="MediaSessionGuard"/>.
    /// </summary>
    public bool TryGetSnapshot(out MediaSession[] sessions)
    {
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            sessions = Array.Empty<MediaSession>();
            return false;
        }

        var manager = lease.Value;
        for (var attempt = 0; attempt < SnapshotRetryCount; attempt++)
        {
            try
            {
                sessions = manager.CurrentMediaSessions.Values
                    .Where(MediaSessionGuard.IsUsable)
                    .ToArray();
                return true;
            }
            catch (InvalidOperationException)
            {
                if (attempt + 1 < SnapshotRetryCount)
                {
                    Thread.Yield();
                }
            }
        }

        sessions = Array.Empty<MediaSession>();
        Debug.WriteLine("[MediaSessionCatalog] Failed to copy CurrentMediaSessions after retries.");
        return false;
    }

    /// <summary>
    /// 返回一份绕过第三方库字典的会话快照：直接向 Windows 索取会话，再包装成库会话类型。
    ///
    /// 这条读路径与库的事件链路无关，因此在上游事件缺陷（库的 issue #6）影响下依然有效。
    /// 需要说明的是：库 2.5.6 的 <c>ForceUpdate()</c> 本身就会调用 <c>GetSessions()</c>，并不依赖事件，
    /// 所以"事件链路断了"并不能单独解释重连与重建为何都无效；本路径是绕开字典的旁路读，不是对病因的断言。
    /// 究竟丢在哪一环，由 <see cref="CaptureState"/> 的诊断行给出证据。
    ///
    /// 库字典里已有的会话原样保留（它们带有事件转发，重建会一并丢失），只对系统存在而字典缺失的会话补一个包装实例。
    /// Returns a session snapshot that bypasses the third-party dictionary by asking Windows directly and wrapping the result.
    ///
    /// This read path depends on none of the library's events, so it stays functional under the upstream event defect (the library's
    /// issue #6). One caveat: the library's 2.5.6 <c>ForceUpdate()</c> calls <c>GetSessions()</c> itself and does not rely on events, so
    /// "the event chain broke" cannot by itself explain why reconnecting and rebuilding both failed; this path is a bypass read around
    /// the dictionary, not a claim about the cause. Which stage actually drops the session is answered by the diagnostic line from
    /// <see cref="CaptureState"/>.
    ///
    /// Sessions already present in the library dictionary are kept as they are (they carry event forwarding that a rebuild would drop), and
    /// only sessions the OS publishes while the dictionary lacks them get a wrapper instance.
    /// </summary>
    /// <param name="sessions">合并后的会话快照；目录不可用时返回空数组。/ The merged session snapshot, empty when the catalog is unavailable.</param>
    public MediaSession[] GetSnapshotFromWinRt()
    {
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            return [];
        }

        var manager = lease.Value;
        var managerHandle = manager.WindowsSessionManager;
        if (managerHandle is null)
        {
            return [];
        }

        List<GlobalSystemMediaTransportControlsSession> native;
        try
        {
            native = [.. managerHandle.GetSessions()];
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ObjectDisposedException)
        {
            Debug.WriteLine($"[MediaSessionCatalog] Failed to enumerate native sessions: {exception.Message}");
            return [];
        }

        // 库字典里的会话优先：它们带事件转发，重建包装会丢掉这些订阅。
        // Sessions already in the library dictionary win: they carry event forwarding that rebuilding the wrapper would drop.
        var result = new List<MediaSession>(native.Count);
        var known = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var session in manager.CurrentMediaSessions.Values.Where(MediaSessionGuard.IsUsable))
            {
                result.Add(session);
                known.Add(session.Id ?? string.Empty);
            }
        }
        catch (InvalidOperationException)
        {
            // 字典正在被 WinRT 事件线程改写；这一轮放弃合并，只用原生会话。
            // The dictionary is being mutated on a WinRT event thread; skip the merge this round and use the native sessions alone.
        }

        var added = 0;
        foreach (var controlSession in native)
        {
            if (string.IsNullOrEmpty(controlSession.SourceAppUserModelId) ||
                known.Contains(controlSession.SourceAppUserModelId))
            {
                continue;
            }

            if (NativeSessionAdapter.TryWrap(controlSession, manager, out var wrapped) && wrapped is not null)
            {
                result.Add(wrapped);
                added++;
            }
        }

        if (added > 0)
        {
            Debug.WriteLine($"[MediaSessionCatalog] Recovered {added} session(s) directly from WinRT.");
        }

        return [.. result];
    }

    /// <summary>
    /// 获取当前由 Windows 判定为焦点的媒体会话，底层目录异常时返回空值。
    /// Gets the media session currently focused by Windows, returning null if the underlying catalog is unavailable.
    /// </summary>
    public MediaSession? GetFocusedSession()
    {
        using var lease = _managers.TryAcquire();
        if (lease is null)
        {
            return null;
        }

        try
        {
            return lease.Value.GetFocusedSession();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[MediaSessionCatalog] Failed to get focused session: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 请求底层媒体管理器立即重新枚举会话。只允许从 AF 唯一的后台重同步流程调用：这样 AF 发起的写操作永远只有一个。
    /// Requests an immediate session enumeration from the underlying media manager. Only the single background reconcile flow may call
    /// this, which keeps AF-initiated writes to exactly one at a time.
    /// </summary>
    public void ForceUpdate()
    {
        using var lease = _managers.TryAcquire();
        lease?.Value.ForceUpdate();
    }

    /// <summary>
    /// 幂等解除底层媒体管理器事件并释放会话目录。
    /// Idempotently detaches media-manager events and disposes the session catalog.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _managers.Dispose();
    }

    private void RetireManager(MediaManager manager)
    {
        // MediaManager.Dispose can block on third-party work; never run it under the ownership gate or on the UI read path.
        _ = Task.Run(() =>
        {
            try
            {
                DetachEvents(manager);
            }
            catch (Exception ex)
            {
                LogRetirementFailure("媒体目录退订失败", ex);
            }

            try
            {
                manager.Dispose();
            }
            catch (Exception ex)
            {
                LogRetirementFailure("媒体目录释放失败", ex);
            }
        });
    }

    private static void LogRetirementFailure(string message, Exception ex)
    {
        Debug.WriteLine($"[MediaSessionCatalog] {message}: {ex}");
        try
        {
            AppLogService.Current?.Error("Media", message, ex);
        }
        catch
        {
            // Logging must not fault the detached cleanup task during application shutdown.
        }
    }

    private void OnAnyMediaPropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnyMediaPropertyChanged?.Invoke(session, properties);
        }
    }

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnyPlaybackStateChanged?.Invoke(session, playbackInfo);
        }
    }

    private void OnAnySessionOpened(MediaSession session)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnySessionOpened?.Invoke(session);
        }
    }

    private void OnAnySessionClosed(MediaSession session)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnySessionClosed?.Invoke(session);
        }
    }

    private void OnFocusedSessionChanged(MediaSession session)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            FocusedSessionChanged?.Invoke(session);
        }
    }

    private void OnAnyTimelinePropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            AnyTimelinePropertyChanged?.Invoke(session, timelineProperties);
        }
    }
}
