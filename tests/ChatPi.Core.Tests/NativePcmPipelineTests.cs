using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Core.Tests;

public sealed class NativePcmPipelineTests
{
    [Theory]
    [InlineData(16, 44100)]
    [InlineData(24, 96000)]
    [InlineData(32, 192000)]
    public void StereoPcmPreservesEveryByteAndSampleRate(int bits, int rate)
    {
        var format = new WaveFormat(rate, bits, 2);
        var bytes = Enumerable.Range(0, format.BlockAlign * 101).Select(i => (byte)(i % 256)).ToArray();
        using var source = new RawSourceWaveStream(bytes, 0, bytes.Length, format);
        var pipeline = NativePcmPipeline.Prepare(source);
        var output = new byte[bytes.Length];
        Assert.Equal(bytes.Length, pipeline.Read(output, 0, output.Length));
        Assert.Equal(bytes, output);
        Assert.Equal(rate, pipeline.WaveFormat.SampleRate);
        Assert.Equal(bits, pipeline.WaveFormat.BitsPerSample);
        Assert.Equal(0, pipeline.Read(output, 0, output.Length));
    }

    [Fact]
    public void Mono24BitDuplicatesSamplesWithoutPrecisionLoss()
    {
        byte[] data = [0x00, 0x00, 0x80, 0xFF, 0xFF, 0x7F, 0x01, 0x00, 0x00];
        using var source = new RawSourceWaveStream(data, 0, data.Length, new WaveFormat(48000, 24, 1));
        var pipeline = NativePcmPipeline.Prepare(source);
        var output = new byte[24];
        Assert.Equal(18, pipeline.Read(output, 3, 18));
        Assert.Equal(new byte[] { 0, 0, 0x80, 0, 0, 0x80, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0x7F, 1, 0, 0, 1, 0, 0 }, output[3..21]);
        Assert.Equal(2, pipeline.WaveFormat.Channels);
        Assert.Equal(48000, pipeline.WaveFormat.SampleRate);
    }

    [Fact]
    public void EightBitUnsignedPcmConvertsExactlyToSigned16Bit()
    {
        byte[] data = [0, 128, 255];
        using var source = new RawSourceWaveStream(data, 0, data.Length, new WaveFormat(44100, 8, 1));
        var pipeline = NativePcmPipeline.Prepare(source);
        var output = new byte[12];
        Assert.Equal(12, pipeline.Read(output, 0, 12));
        Assert.Equal(new byte[] { 0, 128, 0, 128, 0, 0, 0, 0, 0, 127, 0, 127 }, output);
    }

    [Fact]
    public void Float32IsNotNormalizedOrClipped()
    {
        var values = new[] { -1.25f, 0.25f, 1.25f, -0.25f };
        var data = values.SelectMany(BitConverter.GetBytes).ToArray();
        using var source = new RawSourceWaveStream(data, 0, data.Length, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var pipeline = NativePcmPipeline.Prepare(source);
        var output = new byte[data.Length];
        Assert.Equal(data.Length, pipeline.Read(output, 0, output.Length));
        Assert.Equal(data, output);
    }

    [Fact]
    public void CompressedAndMultichannelWaveFormatsAreRejected()
    {
        using var multichannel = new RawSourceWaveStream(new byte[120], 0, 120, new WaveFormat(48000, 16, 6));
        Assert.Throws<NotSupportedException>(() => NativePcmPipeline.Prepare(multichannel));
        using var compressed = new RawSourceWaveStream(new byte[120], 0, 120, WaveFormat.CreateMuLawFormat(8000, 1));
        Assert.Throws<NotSupportedException>(() => NativePcmPipeline.Prepare(compressed));
    }

    [Fact]
    public void ExtensibleHeaderRetains32BitIntegerSubtype()
    {
        var format = DeviceFormats.AsExtensible(new WaveFormat(96000, 32, 2));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) format.Serialize(writer);
        var bytes = stream.ToArray();
        Assert.Equal(new Guid("00000001-0000-0010-8000-00aa00389b71"), new Guid(bytes.AsSpan(bytes.Length - 16)));
        Assert.Equal(96000, format.SampleRate);
        Assert.Equal(32, format.BitsPerSample);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    public void ExtensiblePcmWaveNormalizesItsHeaderWithoutChangingItsSamples(int bits)
    {
        var format = DeviceFormats.AsExtensible(new WaveFormat(96000, bits, 2));
        var bytes = Enumerable.Range(0, format.BlockAlign * 15).Select(i => (byte)(i * 13)).ToArray();
        using var source = new RawSourceWaveStream(bytes, 0, bytes.Length, format);
        var pipeline = NativePcmPipeline.Prepare(source);
        Assert.Equal(WaveFormatEncoding.Pcm, pipeline.WaveFormat.Encoding);
        var output = new byte[bytes.Length];
        Assert.Equal(bytes.Length, pipeline.Read(output, 0, output.Length));
        Assert.Equal(bytes, output);
    }
}
