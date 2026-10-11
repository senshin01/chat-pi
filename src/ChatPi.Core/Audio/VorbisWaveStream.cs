using NAudio.Wave;
using NVorbis;

namespace ChatPi.Core.Audio;

/// <summary>Streaming Vorbis decode at its original rate, with sample-accurate seeking.</summary>
public sealed class VorbisWaveStream : WaveStream
{
    private readonly VorbisReader _reader;
    private float[] _samples = [];
    private long? _pendingSeek;

    public VorbisWaveStream(string path)
    {
        var input = File.OpenRead(path);
        try
        {
            _reader = new VorbisReader(input, closeOnDispose: true) { ClipSamples = false };
            if (_reader.Channels is < 1 or > 2)
                throw new NotSupportedException("Ogg Vorbis はシーク可能なモノラル／ステレオのファイルに対応しています。");
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_reader.SampleRate, _reader.Channels);
        }
        catch { _reader?.Dispose(); input.Dispose(); throw; }
    }

    public override WaveFormat WaveFormat { get; }
    public override long Length => checked(_reader.TotalSamples * WaveFormat.BlockAlign);
    public override long Position
    {
        get => (_pendingSeek ?? _reader.SamplePosition) * WaveFormat.BlockAlign;
        set => _pendingSeek = Math.Clamp(value, 0, Length) / WaveFormat.BlockAlign;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("読み込み先の範囲が不正です。");
        var sampleCount = count / WaveFormat.BlockAlign * WaveFormat.Channels;
        if (sampleCount == 0) return 0;
        if (_pendingSeek is { } target)
        {
            // Warm up one maximum-sized Vorbis block before exposing samples.
            // Direct seeking can leave the first overlap window incomplete.
            _reader.SamplePosition = Math.Max(0, target - 8192);
            var remaining = (target - _reader.SamplePosition) * WaveFormat.Channels;
            if (_samples.Length < 8192 * WaveFormat.Channels) _samples = new float[8192 * WaveFormat.Channels];
            while (remaining > 0)
            {
                var discarded = _reader.ReadSamples(_samples, 0, (int)Math.Min(remaining, _samples.Length));
                if (discarded == 0) break;
                remaining -= discarded;
            }
            _pendingSeek = null;
        }
        if (_samples.Length < sampleCount) _samples = new float[sampleCount];
        var read = _reader.ReadSamples(_samples, 0, sampleCount);
        Buffer.BlockCopy(_samples, 0, buffer, offset, read * sizeof(float));
        return read * sizeof(float);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _reader.Dispose();
        base.Dispose(disposing);
    }
}
