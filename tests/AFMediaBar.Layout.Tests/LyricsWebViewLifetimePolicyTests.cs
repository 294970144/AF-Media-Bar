// Guards the browser ownership transitions that could otherwise leak a WebView or delay lyrics after a track change.
// Runtime WebView2 behavior is verified separately on a Windows desktop.
using AFMediaBar.Classes.Services.Lyrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>Regression checks for taskbar WebView2 lyrics ownership. / 任务栏 WebView2 歌词所有权的回归检查。</summary>
[TestClass]
public sealed class LyricsWebViewLifetimePolicyTests
{
    [TestMethod]
    public void NoLyricsNeverStartsTheBrowserButExistingBrowserGetsGrace()
    {
        Assert.AreEqual(LyricsWebViewLifetimeAction.None,
            LyricsWebViewLifetimePolicy.Resolve(true, true, false, false, 0, false));
        Assert.AreEqual(LyricsWebViewLifetimeAction.ReleaseAfterGrace,
            LyricsWebViewLifetimePolicy.Resolve(true, true, false, false, 0, true));
    }

    [TestMethod]
    public void LyricsArrivingDuringGraceReuseTheExistingBrowser()
    {
        Assert.AreEqual(LyricsWebViewLifetimeAction.Create,
            LyricsWebViewLifetimePolicy.Resolve(true, true, false, true, 20, false));
        Assert.AreEqual(LyricsWebViewLifetimeAction.Retain,
            LyricsWebViewLifetimePolicy.Resolve(true, true, false, true, 20, true));
    }

    [TestMethod]
    public void DisabledOrUnloadedHostReleasesImmediately()
    {
        Assert.AreEqual(LyricsWebViewLifetimeAction.ReleaseNow,
            LyricsWebViewLifetimePolicy.Resolve(true, false, false, true, 20, true));
        Assert.AreEqual(LyricsWebViewLifetimeAction.ReleaseNow,
            LyricsWebViewLifetimePolicy.Resolve(false, true, false, true, 20, true));
        Assert.AreEqual(LyricsWebViewLifetimeAction.ReleaseNow,
            LyricsWebViewLifetimePolicy.Resolve(true, true, true, true, 20, true));
    }
}
