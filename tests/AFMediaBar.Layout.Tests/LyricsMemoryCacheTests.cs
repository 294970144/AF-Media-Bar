// Exercises lyric cache recovery and matching identity through the snapshot owner with a fake source and monotonic clock; no network or SMTC calls.
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证临时失败重试、元数据变化和 SMTC 兜底使用相同的缓存身份。</summary>
[TestClass]
[DoNotParallelize]
public sealed class LyricsMemoryCacheTests
{
    [TestInitialize] public void SetUp() => SettingsManager.ResetAll();
    [TestCleanup] public void TearDown() => SettingsManager.ResetAll();

    private static LyricsRequest Request() => new("Song", "Artist", "Album", 180, null);
    private static LyricsResult Hit() => new("test", new([new LyricLine(0, 10, "lyrics")], LyricsSyncType.LineSynced, "LRC"))
        { MatchScore = 100 };

    [TestMethod]
    public void TemporaryFailureRetriesAfterCooldownButSuccessDoesNotExpire()
    {
        var clock = new CacheClock();
        var hit = Hit();
        var source = new Source(index => index == 1 ? null : hit);
        using var builder = new MediaSnapshotBuilder(new LyricsService(source), clock);
        Assert.IsNull(builder.GetLyrics("session", "test", Request()));
        clock.Advance(29);
        Assert.IsNull(builder.GetLyrics("session", "test", Request()));
        Assert.AreEqual(1, source.Requests.Count);
        clock.Advance(1);
        builder.GetLyrics("session", "test", Request());
        Assert.AreSame(hit, builder.GetLyrics("session", "test", Request()));
        clock.Advance(3600);
        Assert.AreSame(hit, builder.GetLyrics("session", "test", Request()));
        Assert.AreEqual(2, source.Requests.Count);
    }

    [TestMethod]
    public void ConfirmedNoLyricsRemainsCachedAfterFailureCooldown()
    {
        var clock = new CacheClock();
        var absent = LyricsResult.NoLyrics("test", 100);
        var source = new Source(_ => absent);
        using var builder = new MediaSnapshotBuilder(new LyricsService(source), clock);
        builder.GetLyrics("session", "test", Request());
        clock.Advance(3600);
        Assert.AreSame(absent, builder.GetLyrics("session", "test", Request()));
        Assert.AreEqual(1, source.Requests.Count);
    }

    [TestMethod]
    public void AlbumDurationAndSessionChangesCannotReuseAnotherMatchingResult()
    {
        var source = new Source(_ => Hit());
        using var builder = new MediaSnapshotBuilder(new LyricsService(source));
        var request = Request();
        builder.GetLyrics("session", "test", request);
        builder.GetLyrics("session", "test", request);
        Assert.AreEqual(1, source.Requests.Count);
        builder.GetLyrics("session", "test", request with { Album = "Live" });
        builder.GetLyrics("session", "test", request with { DurationSeconds = null });
        builder.GetLyrics("session", "test", request with { DurationSeconds = 240 });
        builder.GetLyrics("other session", "test", request);
        Assert.AreEqual(5, source.Requests.Count);
    }

    [TestMethod]
    public void BrowserAndVideoGateBlocksNormalAndFallbackRequestsUntilEnabled()
    {
        var source = new Source(_ => Hit());
        using var builder = new MediaSnapshotBuilder(new LyricsService(source));

        Assert.IsNull(builder.GetLyrics("session", "chrome.exe", Request()));
        Assert.IsNull(builder.GetLyrics("session", "unknown-player", Request(), isVideo: true));
        builder.RequestOnlineLyrics("session", "chrome.exe", "Song", "Artist", 180);
        Assert.AreEqual(0, source.Requests.Count);

        SettingsManager.SetAllowBrowserAndVideoLyrics(true);
        builder.GetLyrics("session", "chrome.exe", Request());
        Assert.AreEqual(1, source.Requests.Count);

        SettingsManager.SetAllowBrowserAndVideoLyrics(false);
        Assert.IsNull(builder.GetLyrics("session", "chrome.exe", Request()));
        Assert.AreEqual(1, source.Requests.Count);
    }

    [TestMethod]
    public void FallbackReusesSmtcAlbumAndCanRetryTheSameTrack()
    {
        var clock = new CacheClock();
        var hit = Hit();
        var source = new Source(index => index == 1 ? null : hit);
        using var builder = new MediaSnapshotBuilder(new LyricsService(source), clock);
        var request = Request();
        builder.GetLyrics("session", "cloudmusic", request);
        builder.RequestOnlineLyrics("session", "cloudmusic", request.Title, request.Artist, request.DurationSeconds);
        Assert.AreEqual("Album", source.Requests.Single().Album);
        clock.Advance(29);
        builder.RequestOnlineLyrics("session", "cloudmusic", request.Title, request.Artist, request.DurationSeconds);
        Assert.AreEqual(1, source.Requests.Count);
        clock.Advance(1);
        builder.RequestOnlineLyrics("session", "cloudmusic", request.Title, request.Artist, request.DurationSeconds);
        Assert.AreSame(hit, builder.GetLyrics("session", "cloudmusic", request));
        Assert.AreEqual(2, source.Requests.Count);
    }

    [TestMethod]
    public async Task PruningRejectsAnInFlightResultAndAllowsAFreshLookup()
    {
        var pending = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hit = Hit();
        var source = new Source(_ => hit) { Pending = pending };
        using var builder = new MediaSnapshotBuilder(new LyricsService(source));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        builder.EnrichmentCompleted += () => completed.TrySetResult();
        builder.GetLyrics("session", "test", Request());
        builder.GetLyrics("session", "test", Request());
        Assert.AreEqual(1, source.Requests.Count, "An in-flight request must be shared.");
        builder.Prune(MemoryPruneLevel.Idle);
        pending.SetResult(hit);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(builder.GetLyrics("session", "test", Request()), "The result from before pruning must be discarded.");
        Assert.AreEqual(2, source.Requests.Count);
        Assert.AreSame(hit, builder.GetLyrics("session", "test", Request()));
    }

    private sealed class Source(Func<int, LyricsResult?> retrieve) : ILyricsProvider
    {
        public string SourceName => "test";
        internal List<LyricsRequest> Requests { get; } = [];
        internal TaskCompletionSource<LyricsResult?>? Pending { get; init; }
        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Requests.Count == 1 && Pending is { } pending)
                return pending.Task.WaitAsync(cancellationToken);
            return Task.FromResult(retrieve(Requests.Count));
        }
    }

    private sealed class CacheClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(int seconds) => _timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
