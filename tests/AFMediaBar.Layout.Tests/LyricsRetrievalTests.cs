// 固定取词阶段回归：QQ 命中不并发，失败后并发比较全部备用结果。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsRetrievalTests
{
    [TestInitialize] public void SetUp() => SettingsManager.ResetAll();
    [TestCleanup] public void TearDown() => SettingsManager.ResetAll();
    private static LyricsRequest Request() => new("Song", "Artist", "Album", 30, "123");
    private static LyricsResult Hit(string name, int score) => new(name,
        new LyricDocument([new LyricLine(0, 10, "lyrics")], LyricsSyncType.LineSynced, "Lrc")) { MatchScore = score };

    [TestMethod]
    public async Task QQIsTriedFirstEvenWhenAnotherSourceHasAnExactIdOrHigherPriority()
    {
        var qq = new Provider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Hit("QQ", 80)));
        var exact = new Provider(LyricsSourceCatalog.NetEase, _ => Task.FromResult<LyricsResult?>(Hit("exact", 100)));
        SettingsManager.SetLyricsSourceSettings(new([exact.SourceName, qq.SourceName]));
        var result = await new LyricsService(exact, qq).GetLyricsAsync(Request(), CancellationToken.None);
        Assert.AreEqual("QQ", result!.Source);
        Assert.AreEqual(1, qq.Calls);
        Assert.AreEqual(0, exact.Calls);
    }

    [TestMethod]
    public async Task FallbacksStartTogetherOnlyAfterQQMissesAndHighestScoreWins()
    {
        var qqPending = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var highPending = new TaskCompletionSource<LyricsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var qq = new Provider(LyricsSourceCatalog.QQMusic, _ => qqPending.Task);
        var low = new Provider("low", _ => Task.FromResult<LyricsResult?>(Hit("low", 70)));
        var high = new Provider("high", _ => highPending.Task);
        var lookup = new LyricsService(low, high, qq).GetLyricsAsync(Request(), CancellationToken.None);
        Assert.AreEqual(0, low.Calls);
        Assert.AreEqual(0, high.Calls);
        qqPending.SetResult(null);
        await high.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, low.Calls);
        Assert.IsFalse(lookup.IsCompleted, "An early lower-scoring hit must not end the comparison.");
        highPending.SetResult(Hit("high", 95));
        Assert.AreEqual("high", (await lookup)!.Source);
        Assert.AreEqual(1, qq.Calls);
    }

    [TestMethod]
    public async Task QQTimeoutCancelsCooperativeWorkAndStartsFallbacks()
    {
        var pending = new TaskCompletionSource<LyricsResult?>();
        CancellationToken qqToken = default;
        var qq = new Provider(LyricsSourceCatalog.QQMusic, token => { qqToken = token; return pending.Task; });
        var fallback = new Provider("backup", _ => Task.FromResult<LyricsResult?>(Hit("backup", 90)));
        var result = await new LyricsService(TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(2), qq, fallback)
            .GetLyricsAsync(Request(), CancellationToken.None);
        Assert.AreEqual("backup", result!.Source);
        Assert.IsTrue(qqToken.IsCancellationRequested);
        pending.SetException(new InvalidOperationException("late failure"));
    }

    [TestMethod]
    public async Task EqualScoresUseCatalogueOrderRatherThanArrivalOrLegacySettingsOrder()
    {
        var first = new Provider("first", async _ => { await Task.Delay(20); return Hit("first", 90); });
        var second = new Provider("second", _ => Task.FromResult<LyricsResult?>(Hit("second", 90)));
        SettingsManager.SetLyricsSourceSettings(new(["second", "first"]));
        Assert.AreEqual("first", (await new LyricsService(first, second).GetLyricsAsync(Request(), CancellationToken.None))!.Source);
    }

    [TestMethod]
    public async Task DisabledQQIsSkippedAndDisablingAllSourcesSuppressesExactId()
    {
        var qq = new Provider(LyricsSourceCatalog.QQMusic, _ => Task.FromResult<LyricsResult?>(Hit("QQ", 100)));
        var other = new Provider(LyricsSourceCatalog.NetEase, _ => Task.FromResult<LyricsResult?>(Hit("exact", 90)));
        SettingsManager.SetLyricsSourceSettings(new([other.SourceName]));
        var service = new LyricsService(qq, other);
        Assert.AreEqual("exact", (await service.GetLyricsAsync(Request(), CancellationToken.None))!.Source);
        SettingsManager.SetLyricsSourceSettings(new([]));
        Assert.IsNull(await service.GetLyricsAsync(Request(), CancellationToken.None));
        Assert.AreEqual(0, qq.Calls);
        Assert.AreEqual(1, other.Calls);
    }

    [TestMethod]
    public async Task CancellingDuringQQDoesNotStartFallbacks()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<LyricsResult?>();
        var qq = new Provider(LyricsSourceCatalog.QQMusic, _ => pending.Task);
        var other = new Provider("backup", _ => Task.FromResult<LyricsResult?>(Hit("backup", 100)));
        var lookup = new LyricsService(qq, other).GetLyricsAsync(Request(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => lookup.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(0, other.Calls);
        pending.SetResult(Hit("late", 100));
    }

    private sealed class Provider(string name, Func<CancellationToken, Task<LyricsResult?>> retrieve) : ILyricsProvider
    {
        public string SourceName => name;
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken token)
        {
            Calls++;
            Started.TrySetResult();
            return retrieve(token);
        }
    }
}
