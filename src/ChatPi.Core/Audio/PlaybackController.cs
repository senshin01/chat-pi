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
    private IWaveProvider? _provider;
    private PlayerSettings? _settings;
    private WaveFormat? _outputFormat;
    private string? _deviceName;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;
    private long _playGeneration;

    public event EventHandler? Changed;
    public event EventHandler<Exception>? Failed;
    public PlayerState State { get; private set; }
    public string? FilePath { get; private set; }
    public WaveFormat? SourceFormat => _source?.WaveFormat;
    public WaveFormat? OutputFormat => _outputFormat;
    public string? DeviceName => _deviceName;
    public TimeSpan Duration => _source?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan Position => State == PlayerState.Completed ? Duration : _source?.CurrentTime ?? TimeSpan.Zero;

    public void Open(string path, PlayerSettings settings, bool autoPlay = true)
    {
        path = AudioFiles.ValidatePath(path);
        settings.Validate();
        Close();
        try
        {
            _source = new SynchronizedWaveStream(sources.Open(path));
            _provider = NativePcmPipeline.Prepare(_source);
            _settings = settings;
            FilePath = path;
            State = PlayerState.Stopped;
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
            _backend = backends.Create(_provider!, _settings!);
            _outputFormat = _backend.OutputFormat;
            _deviceName = _backend.DeviceName;
        }
        if (State == PlayerState.Completed || _source.Position >= _source.Length) _source.Position = 0;
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

    public void Stop()
    {
        if (_source is null) return;
        DetachStoppedHandler();
        ReleaseBackend();
        _source.Position = 0;
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
        DetachStoppedHandler();
        Exception? failure = args.Exception;
        try { ReleaseBackend(); }
        catch (Exception exception) { failure ??= exception; }
        State = failure is null ? PlayerState.Completed : PlayerState.Stopped;
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
        backend?.Dispose();
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
            _source?.Dispose();
            _source = null;
            FilePath = null;
            State = PlayerState.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Close();
}
