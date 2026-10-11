using System.Buffers.Binary;
using NAudio.Wave;

namespace ChatPi.Core.Audio;

/// <summary>Double DSP samples quantized once at the device's effective precision.</summary>
public sealed class DspSampleConverter(WaveFormat format, int validBits, bool dither)
{
    private uint _random = 0x71aab46d;
    public long ClippedSamples { get; private set; }
    public double Peak { get; private set; }
    public bool Dither { get; set; } = dither;

    public void Convert(ReadOnlySpan<double> input, Span<byte> output)
    {
        var bytes = format.BitsPerSample / 8;
        var scale = Math.ScaleB(1, validBits - 1);
        for (var i = 0; i < input.Length; i++)
        {
            var sample = input[i];
            if (!double.IsFinite(sample)) throw new InvalidDataException("DSP に不正なサンプルが含まれています。");
            Peak = Math.Max(Peak, Math.Abs(sample));
            var destination = output.Slice(i * bytes, bytes);
            if (format.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                if (Math.Abs(sample) > 1) ClippedSamples++;
                BinaryPrimitives.WriteSingleLittleEndian(destination, (float)Math.Clamp(sample, -1, 1));
                continue;
            }
            if (sample < -1 || sample > 1 - 1 / scale) ClippedSamples++;
            var value = sample * scale;
            if (Dither) value += NextUnit() - NextUnit(); // TPDF, two quantization steps peak-to-peak.
            var integer = (int)Math.Clamp(Math.Round(value, MidpointRounding.ToEven), -scale, scale - 1);
            integer <<= format.BitsPerSample - validBits;
            if (bytes == 4) BinaryPrimitives.WriteInt32LittleEndian(destination, integer);
            else if (bytes == 2) BinaryPrimitives.WriteInt16LittleEndian(destination, (short)integer);
            else { destination[0] = (byte)integer; destination[1] = (byte)(integer >> 8); destination[2] = (byte)(integer >> 16); }
        }
    }
    private double NextUnit()
    {
        _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
        return _random / 4294967296.0;
    }
    public static void Decode(ReadOnlySpan<byte> input, Span<double> output, WaveFormat format)
    {
        var bytes = format.BitsPerSample / 8;
        for (var i = 0; i < output.Length; i++)
        {
            var sample = input.Slice(i * bytes, bytes);
            output[i] = format.Encoding == WaveFormatEncoding.IeeeFloat ? BinaryPrimitives.ReadSingleLittleEndian(sample) : bytes switch
            {
                2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,
                3 => ((sample[0] << 8 | sample[1] << 16 | sample[2] << 24) >> 8) / 8388608.0,
                4 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0,
                _ => throw new NotSupportedException("Unsupported decoded PCM format.")
            };
        }
    }
}
