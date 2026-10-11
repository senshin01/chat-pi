using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace ChatPi.App.Audio;

/// <summary>Windows codecs, explicit native rate/precision, timestamp-aware seeking.</summary>
internal sealed class NativeMediaFoundationWaveStream : WaveStream
{
    private const int AudioStream = MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM;
    // MP4 sources expose this native ALAC subtype in addition to the WAVE-tag GUID.
    private static readonly Guid Mp4Alac = new("616c6163-767a-494d-b478-f29d25dc9037");
    private readonly string _path;
    private IMFSourceReader? _reader;
    private long _length;
    private long _position;
    private long? _pendingSeek;
    private long? _trimUntilFrame = 0;
    private byte[] _decoded = [];
    private int _offset;
    private bool _ended;

    public NativeMediaFoundationWaveStream(string path)
    {
        _path = path;
        MediaFoundationApi.Startup();
        var reader = CreateReader(out var format);
        try
        {
            WaveFormat = format;
            _length = GetDurationBytes(reader, format);
        }
        finally { Marshal.ReleaseComObject(reader); }
    }

    public override WaveFormat WaveFormat { get; }
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, 0, Length) / WaveFormat.BlockAlign * WaveFormat.BlockAlign;
            _pendingSeek = _position;
        }
    }

    private IMFSourceReader CreateReader(out WaveFormat format)
    {
        MediaFoundationInterop.MFCreateSourceReaderFromURL(_path, null, out var reader);
        try
        {
            reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
            reader.SetStreamSelection(AudioStream, true);
            reader.GetNativeMediaType(AudioStream, 0, out var native);
            try
            {
                var type = new MediaType(native);
                var subtype = type.SubType;
                var alac = subtype == AudioSubtypes.MFAudioFormat_ALAC || subtype == Mp4Alac;
                var lossless = subtype == AudioSubtypes.MFAudioFormat_FLAC || alac ||
                    subtype == AudioSubtypes.MFAudioFormat_WMAudio_Lossless;
                var allowed = Path.GetExtension(_path).ToLowerInvariant() switch
                {
                    ".flac" => subtype == AudioSubtypes.MFAudioFormat_FLAC,
                    ".m4a" => alac || subtype == AudioSubtypes.MFAudioFormat_AAC,
                    ".aac" => subtype == AudioSubtypes.MFAudioFormat_AAC || subtype == AudioSubtypes.MFAudioFormat_ADTS || subtype == AudioSubtypes.MEDIASUBTYPE_RAW_AAC1,
                    ".wma" => subtype == AudioSubtypes.MFAudioFormat_WMAudioV8 || subtype == AudioSubtypes.MFAudioFormat_WMAudioV9 || subtype == AudioSubtypes.MFAudioFormat_WMAudio_Lossless,
                    _ => false
                };
                if (!allowed) throw new NotSupportedException($"このコンテナー内の音声コーデックは未対応です ({subtype})。DRM / DSD / 動画は対象外です。");
                if (type.ChannelCount is < 1 or > 2) throw new NotSupportedException("モノラル／ステレオの音声に対応しています。");
                var minimumBits = type.TryGetUInt32(MediaFoundationAttributes.MF_MT_AUDIO_BITS_PER_SAMPLE, 24);
                if (alac) minimumBits = Math.Max(24, minimumBits);
                if (subtype == AudioSubtypes.MFAudioFormat_FLAC)
                {
                    using var input = File.OpenRead(_path);
                    Span<byte> header = stackalloc byte[42];
                    input.ReadExactly(header);
                    if (!header[..4].SequenceEqual("fLaC"u8) || (header[4] & 0x7F) != 0 ||
                        header[5] != 0 || header[6] != 0 || header[7] != 34)
                        throw new InvalidDataException("FLAC の STREAMINFO が不正です。");
                    minimumBits = (((header[20] & 1) << 4) | (header[21] >> 4)) + 1;
                    if (minimumBits is < 8 or > 24) throw new NotSupportedException("FLAC は 8〜24 bit の音源に対応しています。");
                }
                var candidates = lossless
                    ? new[] { new WaveFormat(type.SampleRate, 32, type.ChannelCount), new WaveFormat(type.SampleRate, 24, type.ChannelCount), new WaveFormat(type.SampleRate, 16, type.ChannelCount) }
                    : new[] { WaveFormat.CreateIeeeFloatWaveFormat(type.SampleRate, type.ChannelCount), new WaveFormat(type.SampleRate, 16, type.ChannelCount) };
                foreach (var candidate in candidates)
                {
                    if (lossless && candidate.BitsPerSample < minimumBits) continue;
                    var output = new MediaType(candidate);
                    try
                    {
                        try { reader.SetCurrentMediaType(AudioStream, IntPtr.Zero, output.MediaFoundationObject); }
                        catch (COMException exception) when (exception.HResult == MediaFoundationErrors.MF_E_INVALIDMEDIATYPE) { continue; }
                        format = GetFormat(reader);
                        if (!format.Equals(candidate)) throw new NotSupportedException("デコーダーが音声形式を変更したため、再生を中止しました。");
                        return reader;
                    }
                    finally { Marshal.ReleaseComObject(output.MediaFoundationObject); }
                }
                throw new NotSupportedException("元のサンプルレート／精度を保つ PCM 出力を Windows デコーダーが提供できません。");
            }
            finally { Marshal.ReleaseComObject(native); }
        }
        catch { Marshal.ReleaseComObject(reader); throw; }
    }

    private static WaveFormat GetFormat(IMFSourceReader reader)
    {
        reader.GetCurrentMediaType(AudioStream, out var media);
        try
        {
            var type = new MediaType(media);
            if (type.SubType == AudioSubtypes.MFAudioFormat_PCM) return new WaveFormat(type.SampleRate, type.BitsPerSample, type.ChannelCount);
            if (type.SubType == AudioSubtypes.MFAudioFormat_Float && type.BitsPerSample == 32)
                return WaveFormat.CreateIeeeFloatWaveFormat(type.SampleRate, type.ChannelCount);
            throw new NotSupportedException("デコーダーの出力は PCM / float 32 bit に対応しています。");
        }
        finally { Marshal.ReleaseComObject(media); }
    }

    private static long GetDurationBytes(IMFSourceReader reader, WaveFormat format)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        Marshal.StructureToPtr(new PropVariant(), pointer, false);
        try
        {
            var result = reader.GetPresentationAttribute(MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE,
                MediaFoundationAttributes.MF_PD_DURATION, pointer);
            Marshal.ThrowExceptionForHR(result);
            var duration = (long)Marshal.PtrToStructure<PropVariant>(pointer).Value;
            return checked((long)Math.Round((decimal)duration * format.SampleRate / TimeSpan.TicksPerSecond) * format.BlockAlign);
        }
        finally { PropVariant.Clear(pointer); Marshal.FreeHGlobal(pointer); }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count || count % WaveFormat.BlockAlign != 0) throw new ArgumentException("PCM の読み込み範囲が不正です。");
        if (count == 0) return 0;
        if (_pendingSeek == Length)
        {
            // Seeking to exact EOF must not depend on a codec accepting an end timestamp.
            _decoded = [];
            _offset = 0;
            _ended = true;
            _pendingSeek = null;
            return 0;
        }
        if (_reader is null)
        {
            _reader = CreateReader(out var format);
            if (!format.Equals(WaveFormat)) throw new InvalidDataException("デコーダーの音声形式が変わりました。");
        }
        if (_pendingSeek is { } desired)
        {
            // The FLAC source may finish internally while decoded packets are still buffered.
            if (_ended || Path.GetExtension(_path).Equals(".flac", StringComparison.OrdinalIgnoreCase))
            {
                ReleaseReader();
                _reader = CreateReader(out var format);
                if (!format.Equals(WaveFormat)) throw new InvalidDataException("デコーダーの音声形式が変わりました。");
            }
            var value = PropVariant.FromLong((long)((decimal)desired / WaveFormat.BlockAlign * TimeSpan.TicksPerSecond / WaveFormat.SampleRate));
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
            try
            {
                Marshal.StructureToPtr(value, pointer, false);
                _reader.SetCurrentPosition(Guid.Empty, pointer);
            }
            finally { Marshal.FreeHGlobal(pointer); }
            _decoded = [];
            _offset = 0;
            _ended = false;
            _trimUntilFrame = desired / WaveFormat.BlockAlign;
            _pendingSeek = null;
        }
        var written = 0;
        var emptySamples = 0;
        while (written < count)
        {
            var available = _decoded.Length - _offset;
            if (available > 0)
            {
                var take = Math.Min(available, count - written);
                Buffer.BlockCopy(_decoded, _offset, buffer, offset + written, take);
                _offset += take;
                written += take;
                continue;
            }
            if (_ended) break;
            if (++emptySamples > 1024) throw new InvalidDataException("デコーダーが音声データを返しません。");
            _reader.ReadSample(AudioStream, 0, out _, out var flags, out var timestamp, out var sample);
            try
            {
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0) throw new InvalidDataException("音声デコードに失敗しました。");
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0 && !GetFormat(_reader).Equals(WaveFormat))
                    throw new NotSupportedException("途中で音声形式が変わるファイルは未対応です。");
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0) _ended = true;
                if (sample is null) continue;
                sample.ConvertToContiguousBuffer(out var data);
                try
                {
                    data.Lock(out var pointer, out _, out var bytes);
                    try
                    {
                        _decoded = new byte[bytes];
                        Marshal.Copy(pointer, _decoded, 0, bytes);
                    }
                    finally { data.Unlock(); }
                }
                finally { Marshal.ReleaseComObject(data); }
                if (_decoded.Length % WaveFormat.BlockAlign != 0) throw new InvalidDataException("デコードしたサンプル境界が不正です。");
                _offset = 0;
                if (_trimUntilFrame is { } target)
                {
                    var firstFrame = (long)Math.Round((decimal)unchecked((long)timestamp) * WaveFormat.SampleRate / TimeSpan.TicksPerSecond);
                    _offset = (int)Math.Min(_decoded.Length, Math.Max(0, target - firstFrame) * WaveFormat.BlockAlign);
                    if (_offset < _decoded.Length) _trimUntilFrame = null;
                }
                if (_offset < _decoded.Length) emptySamples = 0;
            }
            finally { if (sample is not null) Marshal.ReleaseComObject(sample); }
        }
        _position += written;
        if (written == 0 && _ended) _length = _position; // Metadata duration can include codec padding.
        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseReader();
        base.Dispose(disposing);
    }

    private void ReleaseReader()
    {
        var reader = _reader;
        _reader = null;
        if (reader is not null) Marshal.ReleaseComObject(reader);
    }
}
