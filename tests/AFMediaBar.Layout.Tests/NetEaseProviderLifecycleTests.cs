// 用受控读取器验证隐藏、重启和退出期间的线程与释放边界；不读取真实进程或访问网络。
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Players;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证读取器取消及释放的线程契约。 / Verifies reader cancellation and disposal threading.</summary>
[TestClass]
[DoNotParallelize]
public sealed class NetEaseProviderLifecycleTests
{
    [TestMethod]
    public void HideCancelsLateResultsAndRestartWaitsForReaderRelease() => RunOnDispatcher(() =>
    {
        var previous = SettingsManager.Current.SmtcSourceFilter;
        using var first = new ControlledReader(block: true, title: "Canceled result");
        using var second = new ControlledReader(block: false);
        var created = 0;
        var published = 0;
        var sawCanceledResult = false;
        var uiThread = Environment.CurrentManagedThreadId;
        var eventThread = 0;
        var hidden = new SmtcSourceFilterSettings(true, []);
        var visible = new SmtcSourceFilterSettings(true, ["cloudmusic"]);
        SettingsManager.Current.SmtcSourceFilter = hidden;
        using var provider = new NetEaseMediaProvider(new LyricsService(), Dispatcher.CurrentDispatcher,
            () => Interlocked.Increment(ref created) == 1 ? first : second);
        provider.SnapshotChanged += (_, snapshot) =>
        {
            eventThread = Environment.CurrentManagedThreadId;
            if (snapshot is not null)
            {
                published++;
                sawCanceledResult |= snapshot.Title == "Canceled result";
            }
        };
        try
        {
            provider.Start();
            provider.Prune(MemoryPruneLevel.None);
            Pump(30);
            Assert.AreEqual(0, created, "Hidden sources must not open a reader.");
            provider.UpdateSessionSnapshot(MediaSnapshot.Disconnected with
                { IsConnected = true, SourceId = "spotify", Title = "Unrelated song" });
            SettingsManager.Current.SmtcSourceFilter = visible;
            PumpUntil(() => first.ReadThread != 0);
            provider.Start();
            Assert.AreEqual(1, created);
            Assert.AreNotEqual(uiThread, first.ReadThread, "Memory reads must not run on the UI thread.");
            Assert.IsNull(first.ExpectedTitle, "Another player's title cannot validate NetEase's FM queue.");

            SettingsManager.Current.SmtcSourceFilter = hidden;
            SettingsManager.Current.SmtcSourceFilter = visible;
            Pump(30);
            Assert.AreEqual(1, created, "A replacement must wait for the canceled reader to finish.");
            Assert.AreEqual(0, published);
            first.Release.Set();
            PumpUntil(() => created == 2 && published > 0);
            Assert.AreEqual(1, first.DisposeCount);
            Assert.AreNotEqual(uiThread, first.DisposeThread);
            Assert.AreEqual(uiThread, eventThread);
            Assert.IsFalse(sawCanceledResult, "A canceled read must never be published after reallowing.");

            provider.Dispose();
            var countAtDisposal = published;
            PumpUntil(() => second.DisposeCount > 0);
            Pump(300);
            Assert.AreEqual(countAtDisposal, published, "No result may arrive after disposal.");
            Assert.AreEqual(1, second.DisposeCount);
        }
        finally
        {
            first.Release.Set();
            provider.Dispose();
            SettingsManager.Current.SmtcSourceFilter = previous;
        }
    });

    [TestMethod]
    public void DispatcherShutdownDoesNotPreventBackgroundReaderDisposal() => RunOnDispatcher(() =>
    {
        var previous = SettingsManager.Current.SmtcSourceFilter;
        using var reader = new ControlledReader(block: true);
        SettingsManager.Current.SmtcSourceFilter = new(false, []);
        using var provider = new NetEaseMediaProvider(new LyricsService(), Dispatcher.CurrentDispatcher, () => reader);
        try
        {
            provider.Start();
            PumpUntil(() => reader.ReadThread != 0);
            provider.Dispose();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
            reader.Release.Set();
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref reader.DisposeCount) == 1, TimeSpan.FromSeconds(5)),
                "Disposal must finish even when the UI no longer processes continuations.");
        }
        finally
        {
            reader.Release.Set();
            provider.Dispose();
            SettingsManager.Current.SmtcSourceFilter = previous;
        }
    });

    [TestMethod]
    public void ReallowingWhileDisplayIsOffWaitsForResume() => RunOnDispatcher(() =>
    {
        var previous = SettingsManager.Current.SmtcSourceFilter;
        var created = 0;
        using var reader = new ControlledReader(block: false);
        SettingsManager.Current.SmtcSourceFilter = new(true, []);
        using var provider = new NetEaseMediaProvider(new LyricsService(), Dispatcher.CurrentDispatcher,
            () => { Interlocked.Increment(ref created); return reader; });
        try
        {
            provider.Prune(MemoryPruneLevel.DisplayOff);
            SettingsManager.Current.SmtcSourceFilter = new(true, ["NetEase.Store!App"]);
            Pump(30);
            Assert.AreEqual(0, created);
            provider.Prune(MemoryPruneLevel.None);
            PumpUntil(() => reader.ReadThread != 0);
            Assert.AreEqual(1, created);
            provider.Dispose();
            PumpUntil(() => reader.DisposeCount > 0);
        }
        finally
        {
            provider.Dispose();
            SettingsManager.Current.SmtcSourceFilter = previous;
        }
    });

    private static void RunOnDispatcher(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Dispatcher test timed out.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            Pump(10);
        Assert.IsTrue(condition(), "Expected lifecycle transition did not finish.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds), DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private sealed class ControlledReader(bool block, string title = "Song") : INetEaseMemoryReader
    {
        public readonly ManualResetEventSlim Release = new(!block);
        public volatile int ReadThread;
        public volatile int DisposeThread;
        public int DisposeCount;
        public string? ExpectedTitle;

        public (PlayerInfo? Info, int ProcessId) Read(string? expectedTitle)
        {
            ExpectedTitle = expectedTitle;
            ReadThread = Environment.CurrentManagedThreadId;
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Test reader was not released.");
            return (new PlayerInfo
            {
                Identity = "1", Title = title, Artists = "Artist", Album = "", Cover = "", Url = "",
                Schedule = 10, Duration = 100, Pause = false
            }, 123);
        }

        public void Dispose()
        {
            DisposeThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref DisposeCount);
        }
    }
}
