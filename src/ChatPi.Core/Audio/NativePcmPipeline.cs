using NAudio.Wave;

namespace ChatPi.Core.Audio;

/// <summary>
/// Native-rate pipeline boundary. Future resampling belongs here, behind an explicit setting.
/// No volume, normalization, DSP or sample-rate conversion is applied in this version.
/// </summary>
public static class NativePcmPipeline
{
    public static IWaveProvider Prepare(IWaveProvider source)
    {
        var format = DeviceFormats.AsStandard(source.WaveFormat);
        if (format.Channels is < 1 or > 2)
            throw new NotSupportedException("初期版はモノラル／ステレオに対応しています。");
        if (!((format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is 8 or 16 or 24 or 32) ||
              (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)))
            throw new NotSupportedException("デコード後の音声は PCM 8/16/24/32 bit または float 32 bit に対応しています。");

        IWaveProvider provider = new FormatOverrideProvider(source, format);
        // An exact integer conversion allows 8-bit WAVs to use ordinary device formats.
        if (format.BitsPerSample == 8) provider = new Unsigned8To16Provider(provider);
        if (format.Channels == 1) provider = new DuplicateMonoProvider(provider);
        return provider;
    }
}

internal sealed class Unsigned8To16Provider(IWaveProvider source) : IWaveProvider
{
    private byte[] _buffer = [];
    public WaveFormat WaveFormat { get; } = new(source.WaveFormat.SampleRate, 16, source.WaveFormat.Channels);
    public int Read(byte[] buffer, int offset, int count)
    {
        var inputCount = count / WaveFormat.BlockAlign * source.WaveFormat.BlockAlign;
        if (_buffer.Length < inputCount) _buffer = new byte[inputCount];
        var read = source.Read(_buffer, 0, inputCount);
        for (var i = 0; i < read; i++)
        {
            buffer[offset + i * 2] = 0;
            buffer[offset + i * 2 + 1] = (byte)(_buffer[i] - 128);
        }
        return read * 2;
    }
}

public sealed class FormatOverrideProvider(IWaveProvider source, WaveFormat format) : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = format;
    public int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
}

/// <summary>Duplicates raw samples into L/R without passing through floating point.</summary>
public sealed class DuplicateMonoProvider : IWaveProvider
{
    private readonly IWaveProvider _source;
    private readonly int _sampleBytes;
    private byte[] _buffer = [];

    public DuplicateMonoProvider(IWaveProvider source)
    {
        if (source.WaveFormat.Channels != 1) throw new ArgumentException("Mono source required.", nameof(source));
        _source = source;
        _sampleBytes = source.WaveFormat.BitsPerSample / 8;
        WaveFormat = source.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
            ? WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2)
            : new WaveFormat(source.WaveFormat.SampleRate, source.WaveFormat.BitsPerSample, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var inputCount = count / WaveFormat.BlockAlign * _sampleBytes;
        if (_buffer.Length < inputCount) _buffer = new byte[inputCount];
        var read = _source.Read(_buffer, 0, inputCount);
        if (read % _sampleBytes != 0) throw new InvalidDataException("音声データのサンプル境界が不正です。");
        for (var index = 0; index < read; index += _sampleBytes)
        {
            Buffer.BlockCopy(_buffer, index, buffer, offset + index * 2, _sampleBytes);
            Buffer.BlockCopy(_buffer, index, buffer, offset + index * 2 + _sampleBytes, _sampleBytes);
        }
        return read * 2;
    }
}
