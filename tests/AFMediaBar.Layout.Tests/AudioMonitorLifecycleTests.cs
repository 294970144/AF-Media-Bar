// Exercises hosted worker shutdown and display-off boundaries without requesting native audio capture.
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class AudioMonitorLifecycleTests
{
    [TestMethod]
    public async Task HostStopCompletesWhileWorkerWaitsForDemandAndRejectsLateRequests()
    {
        using var resolver = new AudioCaptureDeviceResolver();
        using var monitor = new AudioMonitorService(resolver);
        await monitor.StartAsync(CancellationToken.None);
        await monitor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(monitor.ExecuteTask!.IsCompleted);

        monitor.ResetAfterEnvironmentChange();
        monitor.Prune(MemoryPruneLevel.None);
        var bands = Enumerable.Repeat(1f, SpectrumComponentSettings.DefaultBandCount).ToArray();
        Assert.IsFalse(monitor.GetSpectrum(bands, bands.Length));
        Assert.IsTrue(bands.All(value => value == 0));
        monitor.Dispose();
        monitor.Dispose();
    }

    [TestMethod]
    public async Task DisplayOffSuppressesDemandAndDisposeCancelsParkedWorker()
    {
        using var resolver = new AudioCaptureDeviceResolver();
        using var monitor = new AudioMonitorService(resolver);
        monitor.Prune(MemoryPruneLevel.DisplayOff);
        await monitor.StartAsync(CancellationToken.None);
        var bands = Enumerable.Repeat(1f, SpectrumComponentSettings.DefaultBandCount).ToArray();
        Assert.IsFalse(monitor.GetSpectrum(bands, bands.Length));
        Assert.IsTrue(bands.All(value => value == 0));
        monitor.Dispose();
        await monitor.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
