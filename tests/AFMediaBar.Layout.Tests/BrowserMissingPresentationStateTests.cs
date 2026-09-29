// Verifies the browser presentation hold independently from the backend's longer source-recreation grace.
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Threading;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class BrowserMissingPresentationStateTests
{
    private static readonly MediaSnapshot BrowserFrame = MediaSnapshot.Disconnected with
    {
        IsConnected = true,
        SourceId = "chrome.exe",
        Title = "Video"
    };

    [TestMethod]
    public void RecoveryBeforeTimeoutCancelsTheOldPresentationClear()
    {
        var state = new BrowserMissingPresentationState();
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), BrowserMissingPresentationState.HoldDuration);
        var generation = state.Begin();
        state.CancelPending();

        Assert.IsFalse(state.TryHide(generation, selectedBrowserStillMissing: true));
        Assert.AreEqual(BrowserFrame, state.Project(BrowserFrame));
    }

    [TestMethod]
    public void TimeoutHidesProviderUpdatesUntilAResolvedSnapshotArrives()
    {
        var state = new BrowserMissingPresentationState();
        var generation = state.Begin();
        Assert.IsFalse(state.TryHide(generation, selectedBrowserStillMissing: false));
        Assert.IsTrue(state.TryHide(generation, selectedBrowserStillMissing: true));
        Assert.AreEqual(MediaSnapshot.Disconnected, state.Project(BrowserFrame));

        state.Complete();
        Assert.AreEqual(BrowserFrame, state.Project(BrowserFrame));
    }

    [TestMethod]
    public void LaterGraceExpiryAndAnotherEpisodeCannotReuseAnOldTimeout()
    {
        var state = new BrowserMissingPresentationState();
        var oldGeneration = state.Begin();
        Assert.IsTrue(state.TryHide(oldGeneration, selectedBrowserStillMissing: true));
        state.Complete(); // The backend ultimately publishes its normal disconnected snapshot.
        Assert.AreEqual(MediaSnapshot.Disconnected, state.Project(MediaSnapshot.Disconnected));

        var newGeneration = state.Begin();
        Assert.IsFalse(state.TryHide(oldGeneration, selectedBrowserStillMissing: true));
        Assert.IsTrue(state.TryHide(newGeneration, selectedBrowserStillMissing: true));
    }

    [TestMethod]
    public void PresentationCanClearAtHalfASecondWhileBackendKeepsThreeSecondRecovery()
    {
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        using var selection = new MediaSessionSelectionService(
            () => null, Dispatcher.CurrentDispatcher, () => now);
        var original = new MediaSourceCandidate("browser-old", "chrome.exe", true, "browser-old");
        Assert.AreEqual(original, selection.Resolve([original]));
        Assert.IsNull(selection.Resolve([]));

        var presentation = new BrowserMissingPresentationState();
        var generation = presentation.Begin();
        now += BrowserMissingPresentationState.HoldDuration;
        Assert.IsTrue(presentation.TryHide(generation, selection.IsMissingSessionGraceActive));
        Assert.AreEqual(MediaSnapshot.Disconnected, presentation.Project(BrowserFrame));
        Assert.AreEqual(original.Key, selection.SelectedKey);

        now += TimeSpan.FromSeconds(2.5);
        Assert.IsFalse(selection.IsMissingSessionGraceActive);
        Assert.IsNull(selection.Resolve([]));
        Assert.IsNull(selection.SelectedKey);
        presentation.Complete();
        Assert.AreEqual(MediaSnapshot.Disconnected, presentation.Project(MediaSnapshot.Disconnected));
    }

    [TestMethod]
    public void BrowserRecreationAfterPresentationClearRestoresTheSelection()
    {
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        using var selection = new MediaSessionSelectionService(
            () => null, Dispatcher.CurrentDispatcher, () => now);
        var original = new MediaSourceCandidate("browser-old", "chrome.exe", true, "browser-old");
        Assert.AreEqual(original, selection.Resolve([original]));
        Assert.IsNull(selection.Resolve([]));

        var presentation = new BrowserMissingPresentationState();
        Assert.IsTrue(presentation.TryHide(presentation.Begin(), selectedBrowserStillMissing: true));
        now += TimeSpan.FromSeconds(1);
        var recreated = original with { Key = "browser-new", SessionKey = "browser-new" };
        Assert.AreEqual(recreated, selection.Resolve([recreated]));
        Assert.IsFalse(selection.IsMissingSessionGraceActive);
        presentation.Complete();
        Assert.AreEqual(BrowserFrame, presentation.Project(BrowserFrame));
    }
}
