using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Core.Tests;

public sealed class PlaybackControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"chatpi-tests-{Guid.NewGuid():N}");
    private readonly Queue<Action> _callbacks = new();
    private readonly FakeBackendFactory _backends = new();
    private readonly PlaybackController _player;
    private readonly string _path;

    public PlaybackControllerTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "日本語 sample.wav");
        using (var writer = new WaveFileWriter(_path, new WaveFormat(44100, 24, 2)))
            writer.Write(Enumerable.Range(0, 44100 * 6).Select(i => (byte)(i % 256)).ToArray(), 0, 44100 * 6);
        _player = new PlaybackController(new WavSources(), _backends, _callbacks.Enqueue);
    }

    [Fact]
    public void RealWavPassesThroughDecoderAndPipelineUnchanged()
    {
        _player.Open(_path, new PlayerSettings());
        Assert.Equal(PlayerState.Playing, _player.State);
        Assert.Equal(44100, _player.OutputFormat!.SampleRate);
        Assert.Equal(24, _player.SourceFormat!.BitsPerSample);
        var bytes = _backends.Last!.RenderToEnd();
        Assert.Equal(Enumerable.Range(0, 44100 * 6).Select(i => (byte)(i % 256)).ToArray(), bytes);
        Drain();
        Assert.Equal(PlayerState.Completed, _player.State);
        Assert.Equal(_player.Duration, _player.Position);
        _player.Play();
        Assert.Equal(PlayerState.Playing, _player.State);
        Assert.Equal(TimeSpan.Zero, _player.Position);
    }

    [Fact]
    public void PauseResumeStopAndSeekPreserveExpectedState()
    {
        _player.Open(_path, new PlayerSettings());
        _player.Pause();
        Assert.Equal(PlayerState.Paused, _player.State);
        _player.Seek(TimeSpan.FromMilliseconds(500));
        Assert.Equal(PlayerState.Paused, _player.State);
        Assert.InRange(_player.Position.TotalMilliseconds, 499, 501);
        _player.Play();
        Drain(); // A stop callback queued by seeking must not finish the new playback.
        Assert.Equal(PlayerState.Playing, _player.State);
        _player.Stop();
        Assert.Equal(PlayerState.Stopped, _player.State);
        Assert.Equal(TimeSpan.Zero, _player.Position);
    }

    [Fact]
    public void SeekingWhilePlayingResumesWithTheExactPcmSamplesAtTheSelectedTime()
    {
        _player.Open(_path, new PlayerSettings());
        var before = _backends.Last!;
        _player.Seek(TimeSpan.FromMilliseconds(500));
        Assert.True(before.Disposed);
        Assert.Equal(PlayerState.Playing, _player.State);
        Assert.Equal(TimeSpan.FromMilliseconds(500), _player.Position);
        var expected = Enumerable.Range(0, 44100 * 6).Select(i => (byte)(i % 256)).Skip(22050 * 6).ToArray();
        Assert.Equal(expected, _backends.Last!.RenderToEnd());
        Drain();
        Assert.Equal(PlayerState.Completed, _player.State);
    }

    [Fact]
    public void LateCallbackFromPreviousTrackCannotStopNewTrack()
    {
        _player.Open(_path, new PlayerSettings());
        _backends.Last!.Complete();
        _player.Open(_path, new PlayerSettings());
        Drain();
        Assert.Equal(PlayerState.Playing, _player.State);
        Assert.True(_backends.Players[0].Disposed);
    }

    [Fact]
    public void FailedBackendInitializationReleasesFileAndCanRetry()
    {
        _backends.FailCreation = true;
        Assert.Throws<NotSupportedException>(() => _player.Open(_path, new PlayerSettings()));
        Assert.Equal(PlayerState.Empty, _player.State);
        using (File.Open(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        _backends.FailCreation = false;
        _player.Open(_path, new PlayerSettings());
        Assert.Equal(PlayerState.Playing, _player.State);
    }

    [Fact]
    public void DriverErrorIsReportedAsFailureInsteadOfCompletion()
    {
        Exception? observed = null;
        _player.Failed += (_, exception) => observed = exception;
        _player.Open(_path, new PlayerSettings());
        var failure = new IOException("Device disconnected");
        _backends.Last!.Complete(failure);
        Drain();
        Assert.Same(failure, observed);
        Assert.Equal(PlayerState.Stopped, _player.State);
    }

    [Fact]
    public void UnsupportedFileDoesNotInterruptCurrentTrack()
    {
        _player.Open(_path, new PlayerSettings());
        Assert.Throws<NotSupportedException>(() => _player.Open(Path.Combine(_directory, "song.dsf"), new PlayerSettings()));
        Assert.Equal(PlayerState.Playing, _player.State);
    }

    [Fact]
    public void StopReleasesExclusiveOutputAndPlayCreatesANewOne()
    {
        _player.Open(_path, new PlayerSettings());
        var previous = _backends.Last!;
        _player.Stop();
        Assert.True(previous.Disposed);
        _player.Play();
        Assert.NotSame(previous, _backends.Last);
        Assert.Equal(PlayerState.Playing, _player.State);
    }

    [Fact]
    public void SeekingToTheEndCompletesInsteadOfRestartingFromTheBeginning()
    {
        _player.Open(_path, new PlayerSettings());
        _player.Seek(_player.Duration + TimeSpan.FromSeconds(1));
        Assert.Equal(PlayerState.Completed, _player.State);
        Assert.Equal(_player.Duration, _player.Position);
        Assert.True(_backends.Last!.Disposed);
    }

    [Fact]
    public void RepeatRestartsAtTheBeginningAndPreservesEveryPcmSample()
    {
        _player.Open(_path, new PlayerSettings { RepeatEnabled = true });
        var first = _backends.Last!;
        var bytes = first.RenderToEnd();
        Drain();
        Assert.True(first.Disposed);
        Assert.Equal(PlayerState.Playing, _player.State);
        Assert.Equal(TimeSpan.Zero, _player.Position);
        Assert.Equal(bytes, _backends.Last!.RenderToEnd());
        Drain();
        Assert.Equal(3, _backends.Players.Count);
        Assert.Equal(44100, _player.OutputFormat!.SampleRate);
        Assert.Equal(24, _player.OutputFormat.BitsPerSample);
    }

    [Fact]
    public void RepeatCanBeEnabledAndDisabledWithoutRecreatingTheActiveOutput()
    {
        _player.Open(_path, new PlayerSettings());
        var first = _backends.Last!;
        _player.RepeatEnabled = true;
        Assert.Same(first, _backends.Last);
        first.RenderToEnd();
        Drain();
        Assert.Equal(PlayerState.Playing, _player.State);
        _player.RepeatEnabled = false;
        _backends.Last!.RenderToEnd();
        Drain();
        Assert.Equal(PlayerState.Completed, _player.State);
        Assert.Equal(2, _backends.Players.Count);
    }

    [Fact]
    public void ManualStopCancelsAQueuedRepeat()
    {
        _player.Open(_path, new PlayerSettings { RepeatEnabled = true });
        _backends.Last!.RenderToEnd();
        _player.Stop();
        Drain();
        Assert.Equal(PlayerState.Stopped, _player.State);
        Assert.Equal(TimeSpan.Zero, _player.Position);
        Assert.Single(_backends.Players);
    }

    [Fact]
    public void RepeatDoesNotRestartADeviceFailure()
    {
        Exception? observed = null;
        _player.Failed += (_, exception) => observed = exception;
        _player.Open(_path, new PlayerSettings { RepeatEnabled = true });
        _player.Seek(_player.Duration - TimeSpan.FromMilliseconds(1));
        var failure = new IOException("Device disconnected");
        _backends.Last!.Complete(failure);
        Drain();
        Assert.Same(failure, observed);
        Assert.Equal(PlayerState.Stopped, _player.State);
        Assert.Equal(2, _backends.Players.Count);
    }

    [Fact]
    public void FailureToReopenOutputForRepeatIsReportedAndDoesNotRetry()
    {
        Exception? observed = null;
        _player.Failed += (_, exception) => observed = exception;
        _player.Open(_path, new PlayerSettings { RepeatEnabled = true });
        _backends.Last!.RenderToEnd();
        _backends.FailCreation = true;
        Drain();
        Assert.IsType<NotSupportedException>(observed);
        Assert.Equal(PlayerState.Stopped, _player.State);
        Assert.Single(_backends.Players);
    }

    [Fact]
    public void OpeningAnotherTrackCancelsThePreviousTracksQueuedRepeat()
    {
        _player.Open(_path, new PlayerSettings { RepeatEnabled = true });
        _backends.Last!.RenderToEnd();
        _player.Open(_path, new PlayerSettings { RepeatEnabled = true });
        Drain();
        Assert.Equal(PlayerState.Playing, _player.State);
        Assert.Equal(2, _backends.Players.Count);
        Assert.Equal(TimeSpan.Zero, _player.Position);
    }

    private void Drain() { while (_callbacks.TryDequeue(out var callback)) callback(); }
    public void Dispose() { _player.Dispose(); Directory.Delete(_directory, recursive: true); }

    private sealed class WavSources : IAudioSourceFactory
    {
        public WaveStream Open(string path) => new WaveFileReader(path);
    }
    private sealed class FakeBackendFactory : IPlaybackBackendFactory
    {
        public bool FailCreation { get; set; }
        public List<FakePlayer> Players { get; } = [];
        public FakePlayer? Last => Players.LastOrDefault();
        public PlaybackBackend Create(IWaveProvider source, PlayerSettings settings)
        {
            if (FailCreation) throw new NotSupportedException("Unsupported rate");
            var player = new FakePlayer();
            player.Init(source);
            Players.Add(player);
            return new PlaybackBackend(player, source.WaveFormat, "Test output");
        }
    }
    private sealed class FakePlayer : IWavePlayer
    {
        private IWaveProvider? _source;
        public PlaybackState PlaybackState { get; private set; }
        public bool Disposed { get; private set; }
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat => _source!.WaveFormat;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider waveProvider) => _source = waveProvider;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; Complete(); }
        public void Complete(Exception? exception = null) => PlaybackStopped?.Invoke(this, new StoppedEventArgs(exception));
        public byte[] RenderToEnd()
        {
            using var output = new MemoryStream();
            var buffer = new byte[_source!.WaveFormat.BlockAlign * 257];
            int read;
            while (true)
            {
                if (_source is BufferedAudioSource prepared) prepared.WaitForData(buffer.Length);
                read = _source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                output.Write(buffer, 0, read);
            }
            Complete();
            return output.ToArray();
        }
        public void Dispose() => Disposed = true;
    }
}
