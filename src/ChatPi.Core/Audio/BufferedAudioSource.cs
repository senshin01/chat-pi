using NAudio.Wave;

namespace ChatPi.Core.Audio;

/// <summary>
/// Single producer / single consumer PCM ring. Only the producer touches the
/// decoder and DSP. Device Read copies prepared bytes without locks or waits.
/// </summary>
public sealed class BufferedAudioSource : IWaveProvider, IDisposable
{
    private readonly WaveStream _stream;
    private readonly IWaveProvider _native;
    private readonly PlayerSettings _settings;
    private readonly long _startFrame;
    private readonly AutoResetEvent _space = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private byte[] _ring = [];
    private long _written, _read;
    private Thread? _worker;
    private Exception? _failure;
    private int _cancelled, _finished;
    private int _minimumFrames;
    private int _validBits;
    private long _underruns, _clipped;
    private double _peak;
    private bool _disposed;
    private EqualizerChange? _pendingEqualizer;
    private double _preampDb;
    private readonly GraphicEqualizer? _initialEqualizer;
    public WaveFormat WaveFormat { get; private set; }
    public bool DspEnabled => _settings.Dsp.Enabled;
    public double AppliedPreampDb => Volatile.Read(ref _preampDb);
    public TimeSpan Position => TimeSpan.FromSeconds(_startFrame / (double)_stream.WaveFormat.SampleRate +
        Volatile.Read(ref _read) / (double)WaveFormat.BlockAlign / WaveFormat.SampleRate);
    public long SourceFramePosition => _startFrame + Volatile.Read(ref _read) / WaveFormat.BlockAlign / _settings.Dsp.UpsamplingFactor;
    public long Underruns => Volatile.Read(ref _underruns);
    public long ClippedSamples => Volatile.Read(ref _clipped);
    public double Peak => Volatile.Read(ref _peak);
    public bool EndOfStream => Volatile.Read(ref _finished) != 0 && Volatile.Read(ref _read) == Volatile.Read(ref _written);
    public int BufferMilliseconds => _ring.Length == 0 ? _settings.ReadAheadMilliseconds :
        (int)Math.Ceiling(1000.0 * _ring.Length / WaveFormat.AverageBytesPerSecond);

    public BufferedAudioSource(WaveStream stream, PlayerSettings settings)
    {
        settings.Validate();
        _stream = stream;
        _settings = settings with { Dsp = settings.Dsp with { BandGainsDb = (double[])settings.Dsp.BandGainsDb.Clone() } };
        _native = NativePcmPipeline.Prepare(stream);
        _startFrame = stream.Position / stream.WaveFormat.BlockAlign;
        var rate = checked(_native.WaveFormat.SampleRate * settings.Dsp.UpsamplingFactor);
        WaveFormat = DspEnabled ? new WaveFormat(rate, 32, _native.WaveFormat.Channels) : _native.WaveFormat;
        _validBits = WaveFormat.BitsPerSample;
        if (DspEnabled)
        {
            _initialEqualizer = new GraphicEqualizer(_native.WaveFormat.SampleRate, _native.WaveFormat.Channels, settings.Dsp);
            _preampDb = _initialEqualizer.AppliedPreampDb;
        }
    }

    public bool TryUpdateEqualizer(DspSettings settings)
    {
        settings.Validate();
        if (settings.UpsamplingFactor != _settings.Dsp.UpsamplingFactor || settings.Enabled != DspEnabled) return false;
        if (!DspEnabled) return true;
        var eq = new GraphicEqualizer(_native.WaveFormat.SampleRate, _native.WaveFormat.Channels, settings);
        Interlocked.Exchange(ref _pendingEqualizer, new EqualizerChange(eq, settings.DitherEnabled));
        return true;
    }
    private sealed record EqualizerChange(GraphicEqualizer Equalizer, bool Dither);

    public void ConfigureOutput(WaveFormat format, int? validBits = null)
    {
        if (_worker is not null) throw new InvalidOperationException("Output format is already active.");
        if (!DspEnabled) return;
        if (format.SampleRate != WaveFormat.SampleRate || format.Channels != WaveFormat.Channels ||
            format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat) ||
            format.BitsPerSample is not (16 or 24 or 32) ||
            (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample != 32))
            throw new ArgumentException("Unsupported DSP output format.");
        WaveFormat = format;
        _validBits = validBits ?? format.BitsPerSample;
        if (_validBits < 16 || _validBits > format.BitsPerSample) throw new ArgumentOutOfRangeException(nameof(validBits));
    }
    public void RequireDeviceFrames(int frames)
    {
        if (_worker is not null) throw new InvalidOperationException("Buffer is already active.");
        _minimumFrames = Math.Max(_minimumFrames, frames);
    }

    public void Start()
    {
        if (_worker is not null) return;
        var frames = Math.Max(Math.Max(256 * _settings.Dsp.UpsamplingFactor, _minimumFrames),
            WaveFormat.SampleRate * _settings.ReadAheadMilliseconds / 1000);
        _ring = new byte[checked(frames * WaveFormat.BlockAlign)];
        _worker = new Thread(Produce) { IsBackground = true, Name = "Chat Pi audio preparation" };
        _worker.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("音声の先読みが完了しませんでした。");
        ThrowFailure();
    }

    private void Produce()
    {
        try
        {
            var channels = _native.WaveFormat.Channels;
            var input = new byte[256 * _native.WaveFormat.BlockAlign];
            var samples = new double[256 * channels];
            var alternate = new double[samples.Length];
            GraphicEqualizer? nextEq = null;
            var crossfadeFrames = Math.Max(1, _native.WaveFormat.SampleRate / 50);
            var crossfadePosition = 0;
            var output = new double[256 * _settings.Dsp.UpsamplingFactor * channels];
            var packed = new byte[Math.Max(input.Length, output.Length * WaveFormat.BitsPerSample / 8)];
            var eq = _initialEqualizer;
            var converter = DspEnabled ? new DspSampleConverter(WaveFormat, _validBits, _settings.Dsp.DitherEnabled) : null;
            using var resampler = _settings.Dsp.UpsamplingFactor > 1
                ? new SoxrResampler(_native.WaveFormat.SampleRate, _settings.Dsp.UpsamplingFactor, channels) : null;
            // Warm up causal EQ and SRC around a seek without playing pre-target audio.
            var preroll = DspEnabled ? Math.Min(_startFrame, _stream.WaveFormat.SampleRate / 5) : 0;
            var skipFrames = preroll * _settings.Dsp.UpsamplingFactor;
            if (preroll > 0) _stream.Position = (_startFrame - preroll) * _stream.WaveFormat.BlockAlign;
            while (Volatile.Read(ref _cancelled) == 0)
            {
                var read = _native.Read(input, 0, input.Length);
                if (read % _native.WaveFormat.BlockAlign != 0) throw new InvalidDataException("音声データのサンプル境界が不正です。");
                if (!DspEnabled)
                {
                    if (read == 0) break;
                    Publish(input.AsSpan(0, read));
                    continue;
                }
                var frames = read / _native.WaveFormat.BlockAlign;
                if (read > 0)
                {
                    DspSampleConverter.Decode(input.AsSpan(0, read), samples.AsSpan(0, frames * channels), _native.WaveFormat);
                    if (nextEq is null && Interlocked.Exchange(ref _pendingEqualizer, null) is { } change)
                    {
                        nextEq = change.Equalizer;
                        crossfadePosition = 0;
                        converter!.Dither = change.Dither;
                    }
                    if (nextEq is not null)
                    {
                        samples.AsSpan(0, frames * channels).CopyTo(alternate);
                        nextEq.Process(alternate.AsSpan(0, frames * channels));
                    }
                    eq!.Process(samples.AsSpan(0, frames * channels));
                    if (nextEq is not null)
                    {
                        for (var frame = 0; frame < frames; frame++)
                        {
                            var mix = Math.Min(1, (crossfadePosition + frame + 1) / (double)crossfadeFrames);
                            for (var ch = 0; ch < channels; ch++)
                            {
                                var index = frame * channels + ch;
                                samples[index] += (alternate[index] - samples[index]) * mix;
                            }
                        }
                        crossfadePosition += frames;
                        if (crossfadePosition >= crossfadeFrames)
                        {
                            eq = nextEq; nextEq = null;
                            Volatile.Write(ref _preampDb, eq.AppliedPreampDb);
                        }
                    }
                }
                if (resampler is null)
                {
                    if (read == 0) break;
                    Pack(samples, frames);
                }
                else
                {
                    var consumed = 0;
                    do
                    {
                        var written = resampler.Process(samples, consumed, frames - consumed, output, out var used, end: read == 0);
                        consumed += used;
                        if (written > 0) Pack(output, written);
                        if (read == 0 && written == 0) break;
                        if (read > 0 && used == 0 && written == 0) throw new InvalidOperationException("リサンプラーが進行しませんでした。");
                    } while (Volatile.Read(ref _cancelled) == 0 && (read == 0 || consumed < frames));
                    if (read == 0) break;
                }
            }

            void Pack(double[] data, int frames)
            {
                var skip = (int)Math.Min(skipFrames, frames);
                skipFrames -= skip;
                frames -= skip;
                if (frames == 0) return;
                converter!.Convert(data.AsSpan(skip * channels, frames * channels), packed);
                Volatile.Write(ref _clipped, converter.ClippedSamples);
                Volatile.Write(ref _peak, converter.Peak);
                Publish(packed.AsSpan(0, frames * WaveFormat.BlockAlign));
            }
        }
        catch (Exception exception) { Volatile.Write(ref _failure, exception); }
        finally { Volatile.Write(ref _finished, 1); _ready.Set(); }
    }

    private void Publish(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty && Volatile.Read(ref _cancelled) == 0)
        {
            var written = Volatile.Read(ref _written);
            var free = _ring.Length - (int)(written - Volatile.Read(ref _read));
            if (free == 0) { _ready.Set(); _space.WaitOne(10); continue; }
            var count = Math.Min(free, data.Length);
            var index = (int)(written % _ring.Length);
            var first = Math.Min(count, _ring.Length - index);
            data[..first].CopyTo(_ring.AsSpan(index));
            data.Slice(first, count - first).CopyTo(_ring);
            Volatile.Write(ref _written, written + count);
            data = data[count..];
            if (written + count >= _ring.Length) _ready.Set();
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("Invalid output buffer.");
        if (count % WaveFormat.BlockAlign != 0) throw new ArgumentException("A whole number of frames is required.");
        if (count == 0) return 0;
        ThrowFailure();
        var finished = Volatile.Read(ref _finished) != 0;
        var read = Volatile.Read(ref _read);
        var available = Volatile.Read(ref _written) - read;
        var copied = (int)Math.Min(count, available);
        if (copied > 0)
        {
            var index = (int)(read % _ring.Length);
            var first = Math.Min(copied, _ring.Length - index);
            _ring.AsSpan(index, first).CopyTo(buffer.AsSpan(offset));
            _ring.AsSpan(0, copied - first).CopyTo(buffer.AsSpan(offset + first));
            Volatile.Write(ref _read, read + copied);
            _space.Set();
        }
        if (copied < count && !finished)
        {
            Array.Clear(buffer, offset + copied, count - copied);
            Interlocked.Increment(ref _underruns);
            return count; // Silence on a deadline miss; never confuse starvation with EOF.
        }
        return copied;
    }

    /// <summary>For startup/offline renderers only. Never call from a device callback.</summary>
    public void WaitForData(int bytes)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (Volatile.Read(ref _written) - Volatile.Read(ref _read) < Math.Min(bytes, _ring.Length) && Volatile.Read(ref _finished) == 0)
        {
            ThrowFailure();
            if (Environment.TickCount64 >= deadline) throw new TimeoutException("Audio preparation timed out.");
            Thread.Sleep(1);
        }
        ThrowFailure();
    }
    private void ThrowFailure()
    {
        var failure = Volatile.Read(ref _failure);
        if (failure is not null) throw new InvalidOperationException("音声処理に失敗しました。" + failure.Message, failure);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Volatile.Write(ref _cancelled, 1);
        _space.Set();
        _worker?.Join();
        _space.Dispose();
        _ready.Dispose();
    }
}
