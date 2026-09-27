// Verifies the NAudio packet-to-spectrum boundary without opening an audio device or recording sound.
using System.Buffers.Binary;
using AFMediaBar.Classes.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LoopbackSampleReaderTests
{
    [DataTestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    public void PcmStereoPacketsKeepFrameBoundariesAndDownmix(int bits)
    {
        var output = new List<float>();
        var reader = new LoopbackSampleReader(new WaveFormat(48000, bits, 2), output.Add);
        var bytesPerSample = bits / 8;
        var packet = new byte[4 * bytesPerSample];
        var half = 1 << (bits - 2);
        int[] values = [half, 0, -half, -half];
        for (var index = 0; index < values.Length; index++)
            for (var octet = 0; octet < bytesPerSample; octet++)
                packet[index * bytesPerSample + octet] = (byte)(values[index] >> (octet * 8));

        reader.Append(packet.AsSpan(0, 2 * bytesPerSample));
        reader.Append(packet.AsSpan(2 * bytesPerSample));

        Assert.AreEqual(2, output.Count);
        Assert.AreEqual(0.25f, output[0], 0.00001f);
        Assert.AreEqual(-0.5f, output[1], 0.00001f);
    }

    [TestMethod]
    public void LargeExtensibleFloatPacketDoesNotOverflowOrPadWithExtraSilence()
    {
        var output = new List<float>();
        // 32-bit WaveFormatExtensible uses IEEE float, the common WASAPI mix format.
        var reader = new LoopbackSampleReader(new WaveFormatExtensible(96000, 32, 6), output.Add);
        const int frames = 4097;
        var packet = new byte[frames * 6 * 4];
        for (var offset = 0; offset < packet.Length; offset += 4)
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(offset), 0.5f);

        reader.Append(packet);
        reader.Append([]);

        Assert.AreEqual(frames, output.Count);
        Assert.IsTrue(output.All(value => Math.Abs(value - 0.5f) < 0.00001f));
    }

    [TestMethod]
    public void NonFiniteFloatSamplesCannotPoisonFftAndSilenceRemainsZero()
    {
        var output = new List<float>();
        var reader = new LoopbackSampleReader(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), output.Add);
        float[] values = [float.NaN, float.PositiveInfinity, 2, -2, 0];
        var packet = new byte[values.Length * 4];
        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(index * 4), values[index]);
        reader.Append(packet);
        CollectionAssert.AreEqual(new float[] { 0, 0, 1, -1, 0 }, output);
    }

    [TestMethod]
    public void PartialFrameIsRejectedBeforePublishingSamples()
    {
        var output = new List<float>();
        var reader = new LoopbackSampleReader(new WaveFormat(48000, 16, 2), output.Add);
        Assert.ThrowsException<ArgumentException>(() => reader.Append(new byte[3]));
        Assert.AreEqual(0, output.Count);
    }
}
