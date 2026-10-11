using NAudio.Wave;

namespace ChatPi.Core.Audio;

public enum PlayerState { Empty, Stopped, Playing, Paused, Completed }

public sealed record PlaybackBackend(IWavePlayer Player, WaveFormat OutputFormat, string DeviceName, IDisposable? Device = null) : IDisposable
{
    public void Dispose()
    {
        try { Player.Dispose(); }
        finally { Device?.Dispose(); }
    }
}

public interface IAudioSourceFactory
{
    WaveStream Open(string path);
}

public interface IPlaybackBackendFactory
{
    PlaybackBackend Create(IWaveProvider source, PlayerSettings settings);
}

/// <summary>
/// Call commands on one owning thread; dispatch posts driver callbacks to that thread.
/// Separating the decoder, pipeline and backend keeps native-rate playback testable.
/// </summary>
public sealed class PlaybackController(IAudioSourceFactory sources, IPlaybackBackendFactory backends, Action<Action> dispatch) : IDisposable
{
    private WaveStream? _source;
    private PlaybackBackend? _backend;
    private BufferedAudioSource? _provider;
    private PlayerSettings? _settings;
    private WaveFormat? _outputFormat;
    private string? _deviceName;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;
    private long _playGeneration;
    private TimeSpan? _restPosition;
    private double _lastPreampDb;
    private long _lastClipped, _lastUnderruns;
    private int _lastBufferMilliseconds = 40;

    public event EventHandler? Changed;
    public event EventHandler<Exception>? Failed;
    public PlayerState State { get; private set; }
    public bool RepeatEnabled { get; set; }
    public string? FilePath { get; private set; }
    public WaveFormat? SourceFormat => _source?.WaveFormat;
    public WaveFormat? OutputFormat => _outputFormat;
    public string? DeviceName => _deviceName;
    public TimeSpan Duration => _source?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan Position => State == PlayerState.Completed ? Duration : _provider?.Position ?? _restPosition ?? _source?.CurrentTime ?? TimeSpan.Zero;
    public double AppliedPreampDb => _provider?.AppliedPreampDb ?? _lastPreampDb;
    public long ClippedSamples => _provider?.ClippedSamples ?? _lastClipped;
    public long BufferUnderruns => _provider?.Underruns ?? _lastUnderruns;
    public int ReadAheadMilliseconds => _provider?.BufferMilliseconds ?? _lastBufferMilliseconds;

    public void Open(string path, PlayerSettings settings, bool autoPlay = true, bool startPaused = false)
    {
        path = AudioFiles.ValidatePath(path);
        settings.Validate();
        Close();
        try
        {
            _source = new SynchronizedWaveStream(sources.Open(path));
            _ = NativePcmPipeline.Prepare(_source);
            _settings = settings;
            RepeatEnabled = settings.RepeatEnabled;
            FilePath = path;
            State = startPaused ? PlayerState.Paused : PlayerState.Stopped;
            if (autoPlay) Play();
            else Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void Play()
    {
        if (_source is null || State == PlayerState.Playing) return;
        if (_backend is null)
        {
            if (_restPosition is { } position) _source.CurrentTime = position;
            if (State == PlayerState.Completed || (_source.Length > 0 && _source.Position >= _source.Length)) _source.Position = 0;
            try
            {
                _provider = new BufferedAudioSource(_source, _settings!);
                _backend = backends.Create(_provider, _settings!);
                _provider.Start();
                _outputFormat = _backend.OutputFormat;
                _deviceName = _backend.DeviceName;
            }
            catch { ReleaseBackend(); throw; }
        }
        DetachStoppedHandler();
        var generation = ++_playGeneration;
        _stoppedHandler = (_, args) => dispatch(() => OnPlaybackStopped(generation, args));
        _backend.Player.PlaybackStopped += _stoppedHandler;
        State = PlayerState.Playing;
        try { _backend.Player.Play(); }
        catch
        {
            DetachStoppedHandler();
            ReleaseBackend();
            State = PlayerState.Stopped;
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (_backend is null || State != PlayerState.Playing) return;
        _backend.Player.Pause();
        State = PlayerState.Paused;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TryUpdateDsp(PlayerSettings settings)
    {
        if (_provider is null || !_provider.TryUpdateEqualizer(settings.Dsp)) return false;
        _settings = settings;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Stop()
    {
        if (_source is null) return;
        DetachStoppedHandler();
        ReleaseBackend();
        _source.Position = 0;
        _restPosition = TimeSpan.Zero;
        State = PlayerState.Stopped;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        if (_source is null) return;
        var previous = State;
        // Stop resets device buffers, so audio queued before the seek cannot leak through.
        DetachStoppedHandler();
        ReleaseBackend();
        _source.CurrentTime = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Duration.Ticks));
        _restPosition = _source.CurrentTime;
        State = previous == PlayerState.Paused ? PlayerState.Paused : PlayerState.Stopped;
        if (previous == PlayerState.Playing && position >= Duration)
        {
            State = PlayerState.Completed;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (previous == PlayerState.Playing) Play();
        else Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackStopped(long generation, StoppedEventArgs args)
    {
        if (generation != _playGeneration || _backend is null) return;
        var repeat = RepeatEnabled && State == PlayerState.Playing &&
            _source is { Length: > 0 } && _provider is { EndOfStream: true };
        DetachStoppedHandler();
        Exception? failure = args.Exception;
        try { ReleaseBackend(); }
        catch (Exception exception) { failure ??= exception; }
        State = failure is null ? PlayerState.Completed : PlayerState.Stopped;
        if (failure is null && repeat)
        {
            try { Play(); return; }
            catch (Exception exception) { failure = exception; State = PlayerState.Stopped; }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (failure is not null) Failed?.Invoke(this, failure);
    }

    private void DetachStoppedHandler()
    {
        _playGeneration++;
        if (_stoppedHandler is not null && _backend is not null)
            _backend.Player.PlaybackStopped -= _stoppedHandler;
        _stoppedHandler = null;
    }

    private void ReleaseBackend()
    {
        var backend = _backend;
        _backend = null;
        var provider = _provider;
        _provider = null;
        // Stop the device before freeing the ring; stop the worker before seeking or closing the decoder.
        try { backend?.Dispose(); }
        finally
        {
            if (provider is not null)
            {
                provider.Dispose();
                _restPosition = provider.Position;
                _lastPreampDb = provider.AppliedPreampDb;
                _lastClipped = provider.ClippedSamples;
                _lastUnderruns = provider.Underruns;
                _lastBufferMilliseconds = provider.BufferMilliseconds;
            }
        }
    }

    public void Close()
    {
        DetachStoppedHandler();
        try { ReleaseBackend(); }
        finally
        {
            _backend = null;
            _provider = null;
            _settings = null;
            _outputFormat = null;
            _deviceName = null;
            _restPosition = null;
            _lastPreampDb = 0;
            _lastClipped = _lastUnderruns = 0;
            _lastBufferMilliseconds = 40;
            _source?.Dispose();
            _source = null;
            FilePath = null;
            State = PlayerState.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Close();
}
