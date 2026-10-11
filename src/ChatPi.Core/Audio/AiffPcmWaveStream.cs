using System.Buffers.Binary;
using NAudio.Wave;

namespace ChatPi.Core.Audio;

/// <summary>Uncompressed big-endian AIFF PCM. Conversion changes byte order only.</summary>
public sealed class AiffPcmWaveStream : WaveStream
{
    private readonly FileStream _input;
    private readonly long _dataStart;
    private readonly long _length;

    public AiffPcmWaveStream(string path)
    {
        _input = File.OpenRead(path);
        try
        {
            Span<byte> header = stackalloc byte[12];
            _input.ReadExactly(header);
            if (!header[..4].SequenceEqual("FORM"u8) || !header[8..].SequenceEqual("AIFF"u8))
                throw new NotSupportedException("AIFF は非圧縮 PCM に対応しています。圧縮 AIFF-C は未対応です。");
            var end = Math.Min(_input.Length, 8L + BinaryPrimitives.ReadUInt32BigEndian(header[4..]));
            long dataBytes = -1;
            uint frames = 0;
            Span<byte> chunk = stackalloc byte[8];
            Span<byte> info = stackalloc byte[18];
            Span<byte> sound = stackalloc byte[8];
            while (_input.Position <= end - 8)
            {
                _input.ReadExactly(chunk);
                var size = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                var start = _input.Position;
                if (size > end - start) throw new InvalidDataException("AIFF のチャンク長が不正です。");
                if (chunk[..4].SequenceEqual("COMM"u8))
                {
                    if (size < 18) throw new InvalidDataException("AIFF の音声情報が不正です。");
                    _input.ReadExactly(info);
                    var channels = BinaryPrimitives.ReadUInt16BigEndian(info);
                    frames = BinaryPrimitives.ReadUInt32BigEndian(info[2..]);
                    var bits = BinaryPrimitives.ReadUInt16BigEndian(info[6..]);
                    var exponent = BinaryPrimitives.ReadUInt16BigEndian(info[8..]);
                    var mantissa = BinaryPrimitives.ReadUInt64BigEndian(info[10..]);
                    var rate = Math.ScaleB((double)mantissa, exponent - 16383 - 63);
                    if (channels is < 1 or > 2 || bits is not (8 or 16 or 24 or 32) ||
                        (exponent & 0x8000) != 0 || !double.IsFinite(rate) || rate is < 1 or > 768000 || rate != Math.Round(rate))
                        throw new NotSupportedException("AIFF はモノラル／ステレオの PCM 8/16/24/32 bit に対応しています。");
                    WaveFormat = new WaveFormat((int)rate, bits, channels);
                }
                else if (chunk[..4].SequenceEqual("SSND"u8))
                {
                    if (size < 8) throw new InvalidDataException("AIFF の音声データが不正です。");
                    _input.ReadExactly(sound);
                    var offset = BinaryPrimitives.ReadUInt32BigEndian(sound);
                    if (offset > size - 8) throw new InvalidDataException("AIFF の音声オフセットが不正です。");
                    _dataStart = start + 8 + offset;
                    dataBytes = size - 8 - offset;
                }
                _input.Position = start + size + (size & 1);
            }
            if (WaveFormat is null || dataBytes < 0) throw new InvalidDataException("AIFF の音声チャンクがありません。");
            _length = checked((long)frames * WaveFormat.BlockAlign);
            if (_length > dataBytes) throw new InvalidDataException("AIFF の音声データが途中で切れています。");
            Position = 0;
        }
        catch { _input.Dispose(); throw; }
    }

    public override WaveFormat WaveFormat { get; } = null!;
    public override long Length => _length;
    public override long Position
    {
        get => _input.Position - _dataStart;
        set => _input.Position = _dataStart + Math.Clamp(value, 0, Length) / WaveFormat.BlockAlign * WaveFormat.BlockAlign;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytes = (int)Math.Min(count / WaveFormat.BlockAlign * WaveFormat.BlockAlign, Length - Position);
        _input.ReadExactly(buffer.AsSpan(offset, bytes));
        var sampleBytes = WaveFormat.BitsPerSample / 8;
        for (var i = offset; i < offset + bytes; i += sampleBytes)
        {
            if (sampleBytes == 1) buffer[i] ^= 0x80; // AIFF 8-bit is signed; WAV PCM 8-bit is unsigned.
            else buffer.AsSpan(i, sampleBytes).Reverse();
        }
        return bytes;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _input.Dispose();
        base.Dispose(disposing);
    }
}
