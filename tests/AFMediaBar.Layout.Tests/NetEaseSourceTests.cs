// 验证无 SMTC 来源选择、双通道隔离与读取失败恢复；不访问真实播放器或网络。
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证双通道来源的选择与信息隔离。 / Verifies dual-channel selection and information isolation.</summary>
[TestClass]
public sealed class NetEaseSourceTests
{
    private static readonly NetEaseSourcePolicy Policy = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static MediaSnapshot Memory(bool playing = true) => MediaSnapshot.Disconnected with
    {
        IsConnected = true, IsPlaying = playing, SourceId = "cloudmusic", SourceName = "NetEase",
        Title = "Memory song", Artist = "Artist", Position = 42, Duration = 180, TimelineUpdatedAt = Now
    };
    private static MediaSourceCandidate Smtc(string id = "CloudMusic.exe", bool playing = false) =>
        new("smtc-1", id, playing, "smtc-1");

    [TestMethod]
    public void MemoryAloneIsASelectableSourceWithoutAControlSession()
    {
        var candidates = Policy.Combine([], Memory());
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(NetEaseSourcePolicy.SelectionKey, candidates[0].Key);
        Assert.IsNull(candidates[0].SessionKey);
        using var selection = new MediaSessionSelectionService(() => null, Dispatcher.CurrentDispatcher);
        Assert.IsTrue(selection.Select(candidates[0].Key, candidates));
        Assert.AreEqual(candidates[0], selection.Resolve(candidates));
        Assert.IsTrue(selection.IsManualSelection);
    }

    [TestMethod]
    public void ChannelsCollapseAndMemoryPlaybackStateWins()
    {
        var candidates = Policy.Combine([Smtc(), new("browser", "msedge", true, "browser")], Memory());
        Assert.AreEqual(2, candidates.Count);
        var netease = candidates.Single(source => source.SourceId == "cloudmusic");
        Assert.IsTrue(netease.IsPlaying);
        Assert.AreEqual("smtc-1", netease.SessionKey);
    }

    [TestMethod]
    public void ManualSelectionSurvivesSmtcArrivalLossAndRecreation()
    {
        using var selection = new MediaSessionSelectionService(() => "browser", Dispatcher.CurrentDispatcher);
        var original = Policy.Combine([], Memory(false));
        Assert.IsTrue(selection.Select(original[0].Key, original));
        foreach (var sessions in new IReadOnlyList<MediaSourceCandidate>[]
        {
            [Smtc()], [], [Smtc() with { Key = "new-session", SessionKey = "new-session" }]
        })
        {
            var candidates = Policy.Combine(sessions, Memory(false))
                .Append(new MediaSourceCandidate("browser", "msedge", true, "browser")).ToArray();
            Assert.AreEqual(NetEaseSourcePolicy.SelectionKey, selection.Resolve(candidates)?.Key);
            Assert.IsFalse(selection.TryAutoSwitchToPlaying(candidates));
            Assert.IsTrue(selection.IsManualSelection);
        }
    }

    [TestMethod]
    public void MemoryNeverStealsAManuallySelectedOtherSource()
    {
        var candidates = Policy.Combine([Smtc("other.exe")], Memory());
        using var selection = new MediaSessionSelectionService(() => null, Dispatcher.CurrentDispatcher);
        selection.Select("smtc-1", candidates);
        Assert.IsFalse(selection.TryAutoSwitchToPlaying(candidates));
        Assert.AreEqual("smtc-1", selection.Resolve(candidates)?.Key);
    }

    [TestMethod]
    public void ExistingBrowserExceptionRemainsUnchanged()
    {
        using var selection = new MediaSessionSelectionService(() => "smtc-1", Dispatcher.CurrentDispatcher);
        var candidates = Policy.Combine([Smtc("msedge")], Memory());
        Assert.AreEqual("smtc-1", selection.Resolve(candidates)?.Key);
        Assert.IsFalse(selection.TryAutoSwitchToPlaying(candidates));
    }

    [TestMethod]
    public void SmtcAddsControlsWithoutReplacingMemoryTrackOrTimeline()
    {
        var memory = Memory();
        var smtc = memory with { Title = "Incomplete title", Position = 0, Duration = 0, IsPlaying = false,
            SourceId = "NetEase.Store!App", CanPlayPause = true, CanSkipNext = true };
        var merged = Policy.Merge(memory, smtc);
        Assert.AreEqual(memory.Title, merged.Title);
        Assert.AreEqual(memory.Position, merged.Position);
        Assert.AreEqual(memory.Duration, merged.Duration);
        Assert.IsTrue(merged.IsPlaying);
        Assert.IsTrue(merged.CanPlayPause);
        Assert.IsTrue(merged.CanSkipNext);
    }

    [TestMethod]
    public void OtherPlayersCanNeverSupplyNetEaseControls()
    {
        var other = Memory() with { SourceId = "spotify", CanPlayPause = true, CanSkipNext = true,
            CanSkipPrevious = true, CanSeek = true, CanChangeRepeat = true };
        var merged = Policy.Merge(Memory(), other);
        Assert.IsFalse(merged.CanPlayPause);
        Assert.IsFalse(merged.CanSkipNext);
        Assert.IsFalse(merged.CanSkipPrevious);
        Assert.IsFalse(merged.CanSeek);
        Assert.IsFalse(merged.CanChangeRepeat);
        Assert.IsFalse(Policy.Merge(null, other).IsConnected);
    }

    [TestMethod]
    public void SmtcArtworkIsUsedOnlyWhenItMatchesTheMemoryTrack()
    {
        var artwork = new System.Windows.Media.DrawingImage();
        var smtc = Memory() with { Artwork = artwork };
        Assert.AreSame(artwork, Policy.Merge(Memory(), smtc).Artwork);
        Assert.IsNull(Policy.Merge(Memory(), smtc with { Title = "Previous song" }).Artwork);
        Assert.IsNull(Policy.Merge(Memory(), smtc with { Artist = "Another artist" }).Artwork);
    }

    [TestMethod]
    public void SameSourceSmtcRemainsAvailableDuringMemoryFailure()
    {
        var smtc = Memory() with { SourceId = "163music!App", Title = "SMTC fallback", CanPlayPause = true };
        var merged = Policy.Merge(Memory() with { IsStale = true }, smtc);
        Assert.AreEqual("SMTC fallback", merged.Title);
        Assert.IsFalse(merged.IsStale);
        Assert.IsTrue(merged.CanPlayPause);
        Assert.IsTrue(Policy.Merge(null, smtc).IsConnected);
    }

    [TestMethod]
    public void BothChannelsShareLegacyAllowListEntriesWithoutAllowingOtherApps()
    {
        foreach (var id in new[] { "cloudmusic", "CloudMusic.exe", "NetEase.Store!App", "163music!App" })
        {
            var settings = new SmtcSourceFilterSettings(true, [id]);
            Assert.IsTrue(MediaSourceFilterPolicy.IsAllowed("cloudmusic", settings, Policy.NormalizeSourceId));
            Assert.IsTrue(MediaSourceFilterPolicy.IsAllowed("NetEase.Store!App", settings, Policy.NormalizeSourceId));
            Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed("spotify", settings, Policy.NormalizeSourceId));
        }
        Assert.IsFalse(MediaSourceFilterPolicy.IsAllowed("cloudmusic", new(true, []), Policy.NormalizeSourceId));
        Assert.IsTrue(MediaSourceFilterPolicy.IsAllowed("cloudmusic", new(false, []), Policy.NormalizeSourceId));
    }

    [TestMethod]
    public void FailureFreezesProgressForThreeSecondsThenRemovesSource()
    {
        var continuity = new NetEaseMemoryContinuity();
        continuity.Update(Memory(), true, Now);
        var stale = continuity.Update(null, true, Now.AddSeconds(1));
        Assert.IsNotNull(stale);
        Assert.IsTrue(stale.IsStale);
        Assert.IsFalse(stale.IsPlaying);
        Assert.AreEqual(42d, TaskbarExperiencePolicy.GetPosition(stale, Now.AddSeconds(3)));
        Assert.IsNotNull(continuity.Update(null, true, Now.AddSeconds(3.99)));
        Assert.IsNull(continuity.Update(null, true, Now.AddSeconds(4)));
        Assert.IsNull(continuity.Update(null, true, Now.AddSeconds(5)));
    }

    [TestMethod]
    public void RecoveryResetsGraceAndPausedInformationIsRetained()
    {
        var continuity = new NetEaseMemoryContinuity();
        continuity.Update(Memory(), true, Now);
        continuity.Update(null, true, Now.AddSeconds(1));
        var paused = continuity.Update(Memory(false), true, Now.AddSeconds(2));
        Assert.IsFalse(paused!.IsStale);
        Assert.IsNotNull(continuity.Update(null, true, Now.AddSeconds(4)));
        Assert.IsNotNull(continuity.Update(null, true, Now.AddSeconds(6)));
        Assert.IsNull(continuity.Update(null, true, Now.AddSeconds(7)));
    }

    [TestMethod]
    public void ExitAndDisableDiscardGraceImmediately()
    {
        var continuity = new NetEaseMemoryContinuity();
        continuity.Update(Memory(), true, Now);
        Assert.IsNull(continuity.Update(null, false, Now.AddMilliseconds(1)));
        Assert.IsNull(continuity.Update(null, true, Now.AddMilliseconds(2)));
        continuity.Update(Memory(), true, Now);
        continuity.Clear();
        Assert.IsNull(continuity.Update(null, true, Now.AddMilliseconds(1)));
    }

    [TestMethod]
    public void TemporaryReadFailureDoesNotLoseSelectionToAnotherPlayingSource()
    {
        using var selection = new MediaSessionSelectionService(() => null, Dispatcher.CurrentDispatcher);
        var original = Policy.Combine([], Memory());
        selection.Resolve(original);
        var candidates = Policy.Combine([Smtc("other.exe", true)], Memory(false) with { IsStale = true });
        Assert.IsFalse(selection.TryAutoSwitchToPlaying(candidates));
        Assert.AreEqual(NetEaseSourcePolicy.SelectionKey, selection.Resolve(candidates)?.Key);
        Assert.AreEqual("smtc-1", selection.Resolve([Smtc("other.exe", true)])?.Key);
    }
}
