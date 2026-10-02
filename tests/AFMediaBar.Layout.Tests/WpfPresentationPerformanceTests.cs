// Verifies bounded text/animation caches on in-memory STA visuals, without creating taskbar windows or services.
// Each test owns its WPF objects and dispatcher; allocation checks cover only the warmed, unchanged update path.
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class WpfPresentationPerformanceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void MarqueeMetricsReuseMeasurementsAndInvalidateAllFontInputs() => RunSta(() =>
    {
        var element = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 14 };
        var state = new TaskBarMediaControl.MarqueeTextState(element) { Base = "测试 Media bar 123 " };
        var font = TaskBarMediaControl.MarqueeFontKey.Capture(element);
        var natural = TaskBarMediaControl.MeasureMarqueeNaturalWidth(state, font);
        var source = state.RotationSource;
        TaskBarMediaControl.EnsurePrefixWidths(state, source, source.Length, font);
        var prefixes = state.PrefixWidths;
        Assert.AreEqual(natural, prefixes![state.Base.Length], 0.00001);
        state.Position = 4.5;
        state.LeadIn = TimeSpan.FromMilliseconds(200);
        for (var index = 0; index < 100; index++)
        {
            TaskBarMediaControl.MeasureMarqueeNaturalWidth(state, font);
            TaskBarMediaControl.EnsurePrefixWidths(state, source, source.Length, font);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            state.Base = "测试 Media bar 123 ";
            TaskBarMediaControl.MeasureMarqueeNaturalWidth(state, font);
            TaskBarMediaControl.EnsurePrefixWidths(state, state.RotationSource, source.Length, font);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"1000 unchanged marquee metric updates: {allocated} allocated bytes.");
        Assert.AreEqual(0L, allocated);
        Assert.AreSame(prefixes, state.PrefixWidths);
        Assert.AreSame(source, state.RotationSource);
        Assert.AreEqual(4.5, state.Position);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), state.LeadIn);

        foreach (var changed in new[]
        {
            font with { Family = new FontFamily("Arial") },
            font with { Style = FontStyles.Italic },
            font with { Weight = FontWeights.Bold },
            font with { Stretch = FontStretches.Condensed },
            font with { Size = 14.001 },
            font with { Culture = CultureInfo.GetCultureInfo("ar-SA") },
            font with { PixelsPerDip = 1.5 }
        })
        {
            TaskBarMediaControl.MeasureMarqueeNaturalWidth(state, changed);
            TaskBarMediaControl.EnsurePrefixWidths(state, source, source.Length, changed);
            Assert.AreEqual(changed, state.NaturalFont!.Value);
            Assert.AreNotSame(prefixes, state.PrefixWidths, "Changed metrics must discard stale glyph advances.");
            Assert.AreEqual(state.NaturalWidth, state.PrefixWidths![state.Base.Length], 0.00001);
            prefixes = state.PrefixWidths;
        }
        state.Base = "Different content";
        Assert.AreNotSame(source, state.RotationSource);
        TaskBarMediaControl.MeasureMarqueeNaturalWidth(state, font);
        Assert.AreEqual(state.Base, state.NaturalContent);
    });

    [TestMethod]
    public void MarqueeConfigurationKeepsPositionAcrossSnapshotsAndWidthChanges() => RunSta(() =>
    {
        var element = new TextBlock { Text = "A long title that cannot fit into a small taskbar region", FontSize = 14 };
        var state = new TaskBarMediaControl.MarqueeTextState(element);
        Assert.IsTrue(TaskBarMediaControl.ConfigureMarqueeText(state, true, 30));
        state.Position = 4.5;
        state.LeadIn = TimeSpan.FromMilliseconds(200);
        Assert.IsTrue(TaskBarMediaControl.ConfigureMarqueeText(state, true, 40));
        Assert.AreEqual(4.5, state.Position);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), state.LeadIn);
        var window = element.Text;
        Assert.IsTrue(TaskBarMediaControl.ConfigureMarqueeText(state, true, 40));
        Assert.AreEqual(window, element.Text);
        Assert.AreEqual(4.5, state.Position);
        Assert.IsFalse(TaskBarMediaControl.ConfigureMarqueeText(state, false, 40));
        Assert.AreEqual(state.Base, element.Text);
        Assert.AreEqual(0d, state.Transform.X);
        Assert.IsTrue(TaskBarMediaControl.ConfigureMarqueeText(state, true, 40));
        Assert.AreEqual(0d, state.Position);
        element.FontSize = 20;
        state.Position = 3;
        TaskBarMediaControl.ConfigureMarqueeText(state, true, 40);
        Assert.AreEqual(0d, state.Position, "Changed font metrics must restart from a freshly measured line.");
        element.Text = "Another title that also overflows the region";
        TaskBarMediaControl.ConfigureMarqueeText(state, true, 40);
        Assert.AreEqual(element.Text, state.Window);
        Assert.AreEqual("Another title that also overflows the region", state.Base);
    });

    [TestMethod]
    public void SpectrumAnimationKeepsSameTargetAndHandlesMotionChangesAndRebuild() => RunSta(() =>
    {
        var state = new TaskBarMediaControl.SpectrumBarState(new Border(), new ScaleTransform(1, 0.1));
        var full = MotionPolicy.Resolve(true, false, false);
        var instant = MotionPolicy.Resolve(false, false, false);
        TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.8, full);
        Assert.IsTrue(state.Scale.HasAnimatedProperties);
        for (var index = 0; index < 100; index++)
            TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.8, full);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
            TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.8, full);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"1000 identical bar targets: {allocated} allocated bytes.");
        Assert.AreEqual(0L, allocated, "An unchanged target must keep the existing animation.");

        TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.8, instant);
        Assert.IsFalse(state.Scale.HasAnimatedProperties);
        Assert.AreEqual(0.8, state.Scale.ScaleY);
        TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.8, full);
        Assert.IsTrue(state.Scale.HasAnimatedProperties);
        var changedDuration = full with { FastDuration = TimeSpan.FromMilliseconds(200) };
        before = GC.GetAllocatedBytesForCurrentThread();
        TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.8, changedDuration);
        Assert.IsTrue(GC.GetAllocatedBytesForCurrentThread() > before, "Changed duration must replace the clock.");
        TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.1, full);
        Assert.AreEqual(0.1, state.Target!.Value.Scale);
        TaskBarMediaControl.ApplySpectrumBarTarget(state, 0.8, full);
        Assert.AreEqual(0.8, state.Target!.Value.Scale, "Clear then restore must submit the original target again.");
        var rebuilt = new TaskBarMediaControl.SpectrumBarState(new Border(), new ScaleTransform());
        TaskBarMediaControl.ApplySpectrumBarTarget(rebuilt, 0.8, full);
        Assert.IsTrue(rebuilt.Scale.HasAnimatedProperties);
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        rebuilt.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "The STA test did not finish.");
        Assert.IsNull(failure, failure?.ToString());
    }
}
