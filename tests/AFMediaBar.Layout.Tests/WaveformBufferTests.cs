// Locks the waveform geometry to the previous interpolation algorithm while verifying reusable-buffer boundaries.
// Allocation measurements apply to the pure waveform writer, not WPF rendering or the audio pipeline.
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class WaveformBufferTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BufferedOutlineMatchesPreviousAlgorithmForAllSupportedCountsAndLegacyLengths()
    {
        foreach (var count in Enumerable.Range(2, 39))
        foreach (var sensitivity in new[] { 30, 100, 400 })
        foreach (var height in new[] { 14d, 24d, 34d })
        {
            var bands = Enumerable.Range(0, count).Select(index => (index % 7) / 6f).ToArray();
            var expected = PreviousOutline(bands, sensitivity, height);
            var destination = new SpectrumPoint[expected.Length + 1];
            destination[^1] = new SpectrumPoint(-1, -2);
            var written = SpectrumPresentationPolicy.WriteWaveformOutline(bands, sensitivity, height, destination);
            Assert.AreEqual(expected.Length, written);
            CollectionAssert.AreEqual(expected, destination[..written]);
            Assert.AreEqual(new SpectrumPoint(-1, -2), destination[^1]);
            CollectionAssert.AreEqual(expected, SpectrumPresentationPolicy.CreateWaveformOutline(bands, sensitivity, height));
        }
    }

    [TestMethod]
    public void BufferRejectsInsufficientCapacityWithoutPartialWritesAndHandlesInvalidLevels()
    {
        var destination = Enumerable.Repeat(new SpectrumPoint(-1, -2), 12).ToArray();
        Assert.ThrowsException<ArgumentException>(() => SpectrumPresentationPolicy.WriteWaveformOutline([0, 1], 100, 24, destination));
        Assert.IsTrue(destination.All(point => point == new SpectrumPoint(-1, -2)));
        Assert.AreEqual(0, SpectrumPresentationPolicy.WriteWaveformOutline([], 100, 24, destination));
        Assert.AreEqual(0, SpectrumPresentationPolicy.WriteWaveformOutline([1], 100, 24, destination));
        var invalid = SpectrumPresentationPolicy.CreateWaveformOutline([float.NaN, float.PositiveInfinity, float.NegativeInfinity], 100, 24);
        Assert.IsTrue(invalid.All(point => double.IsFinite(point.X) && point.Y == 12));
        foreach (var height in new[] { double.NaN, double.PositiveInfinity, -5 })
            Assert.IsTrue(SpectrumPresentationPolicy.CreateWaveformOutline([0, 1], 100, height).All(point => point.Y == 0));
    }

    [TestMethod]
    public void WarmWaveformWriterAllocatesNoManagedBytes()
    {
        var bands = Enumerable.Range(0, 24).Select(index => index / 23f).ToArray();
        var destination = new SpectrumPoint[SpectrumPresentationPolicy.CalculateWaveformPointCount(24)];
        for (var index = 0; index < 100; index++)
            SpectrumPresentationPolicy.WriteWaveformOutline(bands, 100, 24, destination);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
            SpectrumPresentationPolicy.WriteWaveformOutline(bands, 100, 24, destination);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
            PreviousOutline(bands, 100, 24);
        var previousAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"1000 waveform updates: previous {previousAllocated} bytes; reusable writer {allocated} bytes.");
        Assert.AreEqual(0L, allocated);
        Assert.IsTrue(previousAllocated > 0);
    }

    // Retained as a reference for bit-for-bit compatibility when removing the three original temporary arrays.
    private static SpectrumPoint[] PreviousOutline(float[] bands, int sensitivity, double height)
    {
        var count = bands.Length;
        var step = SpectrumPresentationPolicy.CalculateContentWidthDip(count) / (count - 1);
        var maximum = SpectrumPresentationPolicy.ResolveWaveformHalfHeightDip(height);
        var halfHeights = new double[count];
        for (var index = 0; index < count; index++)
            halfHeights[index] = Math.Clamp(bands[index] * sensitivity / 100f, 0f, 1f) * maximum;
        var segments = SpectrumPresentationPolicy.WaveformSegmentsPerGap * (count - 1);
        var upper = new SpectrumPoint[segments + 1];
        for (var segment = 0; segment < count - 1; segment++)
        {
            var p0 = halfHeights[Math.Max(0, segment - 1)];
            var p1 = halfHeights[segment];
            var p2 = halfHeights[segment + 1];
            var p3 = halfHeights[Math.Min(count - 1, segment + 2)];
            for (var offset = 0; offset < SpectrumPresentationPolicy.WaveformSegmentsPerGap; offset++)
            {
                var t = offset / (double)SpectrumPresentationPolicy.WaveformSegmentsPerGap;
                var amplitude = 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
                upper[segment * SpectrumPresentationPolicy.WaveformSegmentsPerGap + offset] = new SpectrumPoint((segment + t) * step, Math.Clamp(amplitude, 0, maximum));
            }
        }
        upper[^1] = new SpectrumPoint((count - 1) * step, Math.Clamp(halfHeights[^1], 0, maximum));
        var outline = new SpectrumPoint[upper.Length * 2];
        for (var index = 0; index < upper.Length; index++)
        {
            outline[index] = new SpectrumPoint(upper[index].X, height / 2 - upper[index].Y);
            var mirrored = upper[upper.Length - 1 - index];
            outline[upper.Length + index] = new SpectrumPoint(mirrored.X, height / 2 + mirrored.Y);
        }
        return outline;
    }
}
