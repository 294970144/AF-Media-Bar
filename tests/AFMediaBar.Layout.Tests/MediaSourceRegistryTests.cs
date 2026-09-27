// 验证独立来源通过同一契约接入发现、选择、过滤和合并；不启动播放器或访问网络。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>覆盖第二独立来源及普通 SMTC 增强器共存时的仲裁行为。 / Verifies coexistence of independent sources and ordinary SMTC enrichers.</summary>
[TestClass]
public sealed class MediaSourceRegistryTests
{
    [TestMethod]
    public void SecondIndependentSourceParticipatesWithoutSmtcAndPreservesOtherCandidates()
    {
        var netease = new IndependentProvider(new NetEaseSourcePolicy());
        var second = new IndependentProvider(new SecondSourcePolicy());
        var registry = new MediaSourceRegistry([netease, second, new OrdinaryProvider()]);
        var snapshots = new Dictionary<IMediaSourceProvider, MediaSnapshot?>
        {
            [netease] = Memory("cloudmusic"), [second] = Memory("second")
        };
        var browser = new MediaSourceCandidate("browser-session", "browser", true, "browser-session");
        var candidates = registry.BuildCandidates([browser], snapshots);
        Assert.AreEqual(3, candidates.Count);
        Assert.AreEqual(browser, candidates[0]);
        Assert.IsNull(candidates.Single(c => c.SourceId == "second").SessionKey);
        Assert.IsTrue(registry.IsIndependentSelection("source:second"));
        Assert.IsFalse(registry.IsIndependentSelection("browser-session"));
        Assert.AreEqual("second", registry.ResolveSnapshot("source:second", Memory("browser"), snapshots,
            SmtcSourceFilterSettings.Default)!.SourceId);
        Assert.IsNull(registry.ResolveSnapshot("browser-session", Memory("browser"), snapshots,
            SmtcSourceFilterSettings.Default));
        Assert.IsFalse(registry.ResolveSnapshot("source:second", Memory("browser"), snapshots,
            new(true, ["cloudmusic"]))!.IsConnected);
    }

    [TestMethod]
    public void DiscoveryAndFilteringShareAliasesEvenBeforeProvidersPublish()
    {
        var registry = new MediaSourceRegistry([
            new IndependentProvider(new NetEaseSourcePolicy()), new IndependentProvider(new SecondSourcePolicy())]);
        CollectionAssert.AreEquivalent(new[] { "cloudmusic", "second", "browser" },
            registry.DiscoverSourceIds(["CloudMusic.exe", "second.exe", "browser"]).ToArray());
        Assert.IsTrue(registry.IsAllowed("second", new(true, ["SECOND.EXE"])));
        Assert.IsTrue(registry.IsAllowed("CloudMusic.exe", new(true, ["cloudmusic"])));
        Assert.IsFalse(registry.IsAllowed("browser", new(true, ["second.exe"])));
        Assert.AreEqual("browser", registry.NormalizeSourceId("browser"));
        Assert.IsFalse(registry.ResolveSnapshot("source:second", MediaSnapshot.Disconnected,
            new Dictionary<IMediaSourceProvider, MediaSnapshot?>(), SmtcSourceFilterSettings.Default)!.IsConnected);
    }

    [TestMethod]
    public void NetEaseStillCollapsesChannelsAndFallsBackToSmtcWhenMemoryIsStale()
    {
        var provider = new IndependentProvider(new NetEaseSourcePolicy());
        var registry = new MediaSourceRegistry([provider]);
        var snapshots = new Dictionary<IMediaSourceProvider, MediaSnapshot?>
        {
            [provider] = Memory("cloudmusic") with { IsStale = true, Title = "Old" }
        };
        var candidates = registry.BuildCandidates([new("session", "CloudMusic.exe", true, "session")], snapshots);
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual("source:cloudmusic", candidates[0].Key);
        Assert.AreEqual("session", candidates[0].SessionKey);
        var resolved = registry.ResolveSnapshot(candidates[0].Key,
            Memory("CloudMusic.exe") with { Title = "Current", CanPlayPause = true }, snapshots,
            SmtcSourceFilterSettings.Default)!;
        Assert.AreEqual("Current", resolved.Title);
        Assert.AreEqual("cloudmusic", resolved.SourceId);
        Assert.IsTrue(resolved.CanPlayPause);
    }

    [TestMethod]
    public void DuplicateIndependentIdentityIsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() => new MediaSourceRegistry([
            new IndependentProvider(new SecondSourcePolicy()), new IndependentProvider(new SecondSourcePolicy())]));
    }

    private static MediaSnapshot Memory(string sourceId) => MediaSnapshot.Disconnected with
    {
        IsConnected = true, IsPlaying = true, SourceId = sourceId, Title = "Track"
    };

    private class OrdinaryProvider : IMediaSourceProvider
    {
        public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged { add { } remove { } }
        public bool CanHandle(string sourceId) => false;
        public void UpdateSessionSnapshot(MediaSnapshot snapshot) { }
        public void Start() { }
        public void Dispose() { }
    }

    private sealed class IndependentProvider(IMediaSourcePolicy policy) : OrdinaryProvider, IIndependentMediaSourceProvider
    {
        public IMediaSourcePolicy SourcePolicy => policy;
    }

    private sealed class SecondSourcePolicy : IMediaSourcePolicy
    {
        public string SourceId => "second";
        public string SelectionKey => "source:second";
        public bool Matches(string? sourceId) => sourceId is not null &&
            (sourceId.Equals(SourceId, StringComparison.OrdinalIgnoreCase) ||
             sourceId.Equals("second.exe", StringComparison.OrdinalIgnoreCase));
        public IReadOnlyList<MediaSourceCandidate> Combine(IReadOnlyList<MediaSourceCandidate> sessions, MediaSnapshot? memory)
        {
            var matching = sessions.FirstOrDefault(s => Matches(s.SourceId));
            var result = sessions.Where(s => !Matches(s.SourceId)).ToList();
            if (matching is not null || memory is { IsConnected: true })
                result.Add(new(SelectionKey, SourceId, memory?.IsPlaying ?? matching!.IsPlaying, matching?.SessionKey));
            return result;
        }
        public MediaSnapshot Merge(MediaSnapshot? memory, MediaSnapshot smtc) =>
            memory is { IsConnected: true } ? memory : Matches(smtc.SourceId) ? smtc : MediaSnapshot.Disconnected;
    }
}
