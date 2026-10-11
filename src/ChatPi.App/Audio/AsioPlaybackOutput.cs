using System.Runtime.InteropServices;
using System.Windows.Threading;
using ChatPi.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace ChatPi.App.Audio;

/// <summary>
/// Uses NAudio's ASIO driver bindings with our tested integer-preserving converter.
/// Driver operations and shutdown run on the owning STA, never in the audio callback.
/// </summary>
internal sealed class AsioPlaybackOutput : IWavePlayer
{
    private readonly AsioDriverExt _driver;
    private readonly IWaveProvider _source;
    private readonly DeviceSampleConverter[] _converters;
    private readonly byte[][] _channels;
    private readonly byte[] _interleaved;
    private readonly int _frames;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Exception? _failure;
    private int _ended;
    private int _zeroBuffers;
    private bool _disposed;

    public AsioPlaybackOutput(AsioDriverExt driver, IWaveProvider source, int channelOffset)
    {
        _driver = driver;
        _source = source;
        var formats = Enumerable.Range(channelOffset, source.WaveFormat.Channels)
            .Select(channel => MapFormat(driver.Capabilities.OutputChannelInfos[channel].type)).ToArray();
        if (formats.Distinct().Count() != 1)
            throw new NotSupportedException("左右で音声形式が異なる ASIO 出力には対応していません。");
        _converters = formats.Select(format => new DeviceSampleConverter(source.WaveFormat, format)).ToArray();
        OutputWaveFormat = formats[0].IsFloat
            ? WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, source.WaveFormat.Channels)
            : new WaveFormat(source.WaveFormat.SampleRate, formats[0].ValidBits, source.WaveFormat.Channels);
        _frames = driver.CreateBuffers(source.WaveFormat.Channels, 0, useMaxBufferSize: false);
        driver.SetChannelOffset(channelOffset, 0);
        _interleaved = new byte[_frames * source.WaveFormat.BlockAlign];
        _channels = formats.Select(format => new byte[_frames * format.ContainerBytes]).ToArray();
        driver.FillBufferCallback = FillBuffer;
        driver.ResetRequestCallback = OnDriverReset;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += OnTick;
    }

    private static DeviceSampleFormat MapFormat(AsioSampleType type) => type switch
    {
        AsioSampleType.Int16LSB => DeviceSampleFormat.Pcm16,
        AsioSampleType.Int24LSB => DeviceSampleFormat.Pcm24,
        AsioSampleType.Int32LSB => DeviceSampleFormat.Pcm32,
        AsioSampleType.Float32LSB => DeviceSampleFormat.Float32,
        AsioSampleType.Int32LSB16 => new(4, 16),
        AsioSampleType.Int32LSB18 => new(4, 18),
        AsioSampleType.Int32LSB20 => new(4, 20),
        AsioSampleType.Int32LSB24 => new(4, 24),
        _ => throw new NotSupportedException($"ASIO の音声形式 {type} は初期版では未対応です。WASAPI または別のドライバーを選択してください。")
    };

    public PlaybackState PlaybackState { get; private set; }
    public WaveFormat OutputWaveFormat { get; }
    public float Volume
    {
        get => 1;
        set { if (value != 1) throw new NotSupportedException("Digital volume is fixed at unity."); }
    }
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    public void Init(IWaveProvider waveProvider) => throw new InvalidOperationException("Already initialized.");
    public void Play()
    {
        Volatile.Write(ref _ended, 0);
        _zeroBuffers = 0;
        Volatile.Write(ref _failure, null);
        PlaybackState = PlaybackState.Playing;
        try { _driver.Start(); _timer.Start(); }
        catch { PlaybackState = PlaybackState.Stopped; throw; }
    }
    public void Pause() { _timer.Stop(); _driver.Stop(); PlaybackState = PlaybackState.Paused; }
    public void Stop() => StopWithReason(null);

    private void FillBuffer(IntPtr[] inputs, IntPtr[] outputs)
    {
        try
        {
            var read = Volatile.Read(ref _failure) is null ? _source.Read(_interleaved, 0, _interleaved.Length) : 0;
            if (read % _source.WaveFormat.BlockAlign != 0) throw new InvalidDataException("音声データのサンプル境界が不正です。");
            Array.Clear(_interleaved, read, _interleaved.Length - read);
            // The second silent callback proves the final double-buffered audio has drained.
            if (read == 0 && ++_zeroBuffers >= 2) Volatile.Write(ref _ended, 1);
            for (var channel = 0; channel < _channels.Length; channel++)
                _converters[channel].WriteChannel(_interleaved, _channels[channel], channel, _frames);
            for (var channel = 0; channel < _channels.Length; channel++)
                Marshal.Copy(_channels[channel], 0, outputs[channel], _channels[channel].Length);
        }
        catch (Exception exception)
        {
            // Managed failures must not cross the native driver callback boundary.
            Volatile.Write(ref _failure, exception);
            foreach (var channel in _channels) Array.Clear(channel);
            // Valid driver buffers can still be silenced after a decoder failure.
            try
            {
                for (var channel = 0; channel < Math.Min(_channels.Length, outputs.Length); channel++)
                    if (outputs[channel] != IntPtr.Zero)
                        Marshal.Copy(_channels[channel], 0, outputs[channel], _channels[channel].Length);
            }
            catch (Exception silenceFailure) { Volatile.Write(ref _failure, silenceFailure); }
        }
    }

    private void OnTick(object? sender, EventArgs args)
    {
        var failure = Volatile.Read(ref _failure);
        if (failure is not null || Volatile.Read(ref _ended) != 0) StopWithReason(failure);
    }
    private void StopWithReason(Exception? exception)
    {
        if (_disposed) return;
        _timer.Stop();
        _driver.Stop();
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, new StoppedEventArgs(exception));
    }
    private void OnDriverReset() => _dispatcher.BeginInvoke(() =>
    {
        if (!_disposed)
            StopWithReason(new InvalidOperationException("ASIO ドライバーの設定が変更されました。音楽ファイルを開き直して再初期化してください。"));
    });
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        try { if (PlaybackState != PlaybackState.Stopped) _driver.Stop(); }
        finally
        {
            _driver.FillBufferCallback = null;
            _driver.ResetRequestCallback = null;
            _driver.ReleaseDriver();
        }
    }
}
