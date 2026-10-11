using System.Buffers.Binary;
using NAudio.Wave;

namespace ChatPi.Core.Audio;

public readonly record struct DeviceSampleFormat(int ContainerBytes, int ValidBits, bool IsFloat = false)
{
    public static DeviceSampleFormat Pcm16 => new(2, 16);
    public static DeviceSampleFormat Pcm24 => new(3, 24);
    public static DeviceSampleFormat Pcm32 => new(4, 32);
    public static DeviceSampleFormat Float32 => new(4, 32, true);
}

/// <summary>
/// Interleaved PCM to a single little-endian device channel. Integer widening is
/// exact; narrowing is rejected. Float-to-integer uses full-scale power-of-two
/// scaling and round-to-even; float streams themselves pass through unchanged.
/// </summary>
public sealed class DeviceSampleConverter
{
    private readonly WaveFormat _source;
    private readonly DeviceSampleFormat _output;

    public DeviceSampleConverter(WaveFormat source, DeviceSampleFormat output)
    {
        if (source.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat) ||
            source.BitsPerSample is not (16 or 24 or 32) ||
            (source.Encoding == WaveFormatEncoding.IeeeFloat && source.BitsPerSample != 32))
            throw new NotSupportedException("Unsupported input representation.");
        if (output.ContainerBytes is < 2 or > 4 || output.ValidBits is < 16 or > 32 ||
            output.ValidBits > output.ContainerBytes * 8 || (output.IsFloat && output != DeviceSampleFormat.Float32))
            throw new NotSupportedException("Unsupported device representation.");
        if (source.Encoding == WaveFormatEncoding.Pcm &&
            (output.IsFloat ? source.BitsPerSample > 24 : source.BitsPerSample > output.ValidBits))
            throw new NotSupportedException("ASIO の出力精度が音源より低いため再生を中止しました。WASAPI または別のドライバーを選択してください。");
        _source = source;
        _output = output;
    }

    public void WriteChannel(ReadOnlySpan<byte> interleaved, Span<byte> destination, int channel, int frames)
    {
        if (channel < 0 || channel >= _source.Channels) throw new ArgumentOutOfRangeException(nameof(channel));
        if (frames < 0 || interleaved.Length < frames * _source.BlockAlign || destination.Length < frames * _output.ContainerBytes)
            throw new ArgumentException("Insufficient audio buffer.");
        var sampleBytes = _source.BitsPerSample / 8;
        for (var frame = 0; frame < frames; frame++)
        {
            var input = interleaved.Slice(frame * _source.BlockAlign + channel * sampleBytes, sampleBytes);
            var output = destination.Slice(frame * _output.ContainerBytes, _output.ContainerBytes);
            if (_output.IsFloat)
            {
                if (_source.Encoding == WaveFormatEncoding.IeeeFloat) input.CopyTo(output);
                else BinaryPrimitives.WriteSingleLittleEndian(output, (float)(ReadPcm32(input) / 2147483648.0));
                continue;
            }
            int value;
            if (_source.Encoding == WaveFormatEncoding.Pcm) value = ReadPcm32(input) >> (32 - _output.ValidBits);
            else
            {
                var sample = BinaryPrimitives.ReadSingleLittleEndian(input);
                if (!float.IsFinite(sample)) throw new InvalidDataException("音源に不正な浮動小数点サンプルが含まれています。");
                var scale = Math.Pow(2, _output.ValidBits - 1);
                value = (int)Math.Clamp(Math.Round(sample * scale, MidpointRounding.ToEven), -scale, scale - 1);
            }
            if (_output.ContainerBytes == 2) BinaryPrimitives.WriteInt16LittleEndian(output, (short)value);
            else if (_output.ContainerBytes == 4) BinaryPrimitives.WriteInt32LittleEndian(output, value);
            else { output[0] = (byte)value; output[1] = (byte)(value >> 8); output[2] = (byte)(value >> 16); }
        }
    }

    private static int ReadPcm32(ReadOnlySpan<byte> input) => input.Length switch
    {
        2 => BinaryPrimitives.ReadInt16LittleEndian(input) << 16,
        3 => input[0] << 8 | input[1] << 16 | input[2] << 24,
        _ => BinaryPrimitives.ReadInt32LittleEndian(input)
    };
}
