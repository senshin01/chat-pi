using System.Buffers.Binary;
using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Core.Tests;

public sealed class DeviceSampleConverterTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void NativeIntegerDeviceOutputRetainsAllBitsAndSeparatesChannels(int bits)
    {
        var bytes = bits / 8;
        var source = new WaveFormat(44100, bits, 2);
        var data = Enumerable.Range(0, source.BlockAlign * 20).Select(i => (byte)(i * 17)).ToArray();
        var converter = new DeviceSampleConverter(source, new DeviceSampleFormat(bytes, bits));
        for (var channel = 0; channel < 2; channel++)
        {
            var output = new byte[20 * bytes];
            converter.WriteChannel(data, output, channel, 20);
            for (var frame = 0; frame < 20; frame++)
                Assert.Equal(data.AsSpan(frame * source.BlockAlign + channel * bytes, bytes).ToArray(), output.AsSpan(frame * bytes, bytes).ToArray());
        }
    }

    [Fact]
    public void Pcm24ToPcm32UsesAnExactLeftShift()
    {
        byte[] data = [0, 0, 0x80, 0xFF, 0xFF, 0x7F, 1, 0, 0];
        var converter = new DeviceSampleConverter(new WaveFormat(96000, 24, 1), DeviceSampleFormat.Pcm32);
        var output = new byte[12];
        converter.WriteChannel(data, output, 0, 3);
        Assert.Equal(int.MinValue, BinaryPrimitives.ReadInt32LittleEndian(output));
        Assert.Equal(0x7FFFFF00, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(4)));
        Assert.Equal(256, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(8)));
    }

    [Fact]
    public void Pcm24In32BitDeviceContainerIsRightAlignedAndSignExtended()
    {
        byte[] data = [0, 0, 0x80, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF];
        var converter = new DeviceSampleConverter(new WaveFormat(96000, 24, 1), new(4, 24));
        var output = new byte[12];
        converter.WriteChannel(data, output, 0, 3);
        Assert.Equal(-8388608, BinaryPrimitives.ReadInt32LittleEndian(output));
        Assert.Equal(8388607, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(4)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(8)));
    }

    [Fact]
    public void Pcm24CanRoundTripThroughAFloatDeviceWithoutLosingLeastSignificantBits()
    {
        byte[] data = [0, 0, 0x80, 0xFF, 0xFF, 0x7F, 1, 0, 0, 0xFF, 0xFF, 0xFF];
        var toFloat = new DeviceSampleConverter(new WaveFormat(96000, 24, 1), DeviceSampleFormat.Float32);
        var floats = new byte[16];
        toFloat.WriteChannel(data, floats, 0, 4);
        var toInteger = new DeviceSampleConverter(WaveFormat.CreateIeeeFloatWaveFormat(96000, 1), DeviceSampleFormat.Pcm24);
        var output = new byte[data.Length];
        toInteger.WriteChannel(floats, output, 0, 4);
        Assert.Equal(data, output);
    }

    [Fact]
    public void IntegerPrecisionLossIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => new DeviceSampleConverter(new WaveFormat(44100, 24, 2), DeviceSampleFormat.Pcm16));
        Assert.Throws<NotSupportedException>(() => new DeviceSampleConverter(new WaveFormat(44100, 32, 2), DeviceSampleFormat.Float32));
    }

    [Fact]
    public void FloatIntegerMappingHandlesFullScaleAndClipping()
    {
        var input = new[] { -2f, -1f, 0f, 1f, 2f }.SelectMany(BitConverter.GetBytes).ToArray();
        var converter = new DeviceSampleConverter(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1), DeviceSampleFormat.Pcm16);
        var output = new byte[10];
        converter.WriteChannel(input, output, 0, 5);
        Assert.Equal(new short[] { short.MinValue, short.MinValue, 0, short.MaxValue, short.MaxValue },
            Enumerable.Range(0, 5).Select(i => BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(i * 2))).ToArray());
    }

    [Fact]
    public void NonFiniteFloatCannotEnterIntegerDeviceBuffers()
    {
        var converter = new DeviceSampleConverter(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1), DeviceSampleFormat.Pcm24);
        Assert.Throws<InvalidDataException>(() => converter.WriteChannel(BitConverter.GetBytes(float.NaN), new byte[3], 0, 1));
    }
}
