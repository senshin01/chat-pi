using System.Runtime.InteropServices;
using ChatPi.App.Audio;
using ChatPi.Core;
using ChatPi.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace ChatPi.Windows.Tests;

public sealed class AsioPlaybackTests
{
    [Theory]
    [InlineData("lossless16.flac", 2, UpsamplingFilter.Standard)]
    [InlineData("lossless16.flac", 4, UpsamplingFilter.Standard)]
    [InlineData("lossless24.flac", 2, UpsamplingFilter.Standard)]
    [InlineData("lossless24.flac", 4, UpsamplingFilter.Standard)]
    [InlineData("reference16.wav", 4, UpsamplingFilter.Standard)]
    [InlineData("tone.mp3", 2, UpsamplingFilter.Standard)]
    [InlineData("lossless16.flac", 2, UpsamplingFilter.SuperSlow)]
    [InlineData("lossless24.flac", 4, UpsamplingFilter.SuperSlow)]
    [InlineData("lossless16.flac", 2, UpsamplingFilter.UltraHigh)]
    [InlineData("lossless16.flac", 4, UpsamplingFilter.UltraHigh)]
    [InlineData("lossless24.flac", 2, UpsamplingFilter.UltraHigh)]
    [InlineData("lossless24.flac", 4, UpsamplingFilter.UltraHigh)]
    [InlineData("reference16.wav", 4, UpsamplingFilter.UltraHigh)]
    [InlineData("tone.mp3", 2, UpsamplingFilter.UltraHigh)]
    public void RestartAfterUpsamplingSendsExactNativeSamplesToAsioBuffers(string file, int factor, UpsamplingFilter filter)
    {
        WpfTestHost.Run(() =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
            using var original = new WindowsAudioSourceFactory().Open(path);
            var native = NativePcmPipeline.Prepare(original);
            var expected = ReadAll(native);
            var state = new DriverState { Rate = native.WaveFormat.SampleRate };
            var settingsFile = Path.Combine(Path.GetTempPath(), $"chatpi-asio-{Guid.NewGuid():N}.json");
            var store = new SettingsStore(settingsFile);
            try
            {
                store.Save(new PlayerSettings { Dsp = new DspSettings { UpsamplingFactor = factor, Filter = filter } });
                // Render through the actual ASIO output's packing/callback, without physical hardware.
                var upsampled = Render(path, store.Load(), state);
                Assert.Contains(upsampled[0], sample => sample != 0);
                Assert.Equal(native.WaveFormat.SampleRate * factor, state.Rate);
                store.Save(store.Load() with { Dsp = new DspSettings { PreampDb = -6, AutoHeadroom = true } });
                var output = Render(path, new SettingsStore(settingsFile).Load(), state);
                Assert.Equal(native.WaveFormat.SampleRate, state.Rate);
                for (var channel = 0; channel < 2; channel++)
                {
                    var frames = expected.Length / native.WaveFormat.BlockAlign;
                    var bytes = new byte[frames * 4];
                    new DeviceSampleConverter(native.WaveFormat, DeviceSampleFormat.Pcm32)
                        .WriteChannel(expected, bytes, channel, frames);
                    Assert.Equal(bytes, output[channel].AsSpan(0, bytes.Length).ToArray());
                    Assert.All(output[channel].Skip(bytes.Length), b => Assert.Equal(0, b));
                }
                Assert.Equal(2, state.Events.Count(e => e.StartsWith("set ", StringComparison.Ordinal)));
                Assert.Equal(2, state.Events.Count(e => e == "buffers"));
                Assert.Equal(4, state.Events.Count(e => e == "dispose"));
            }
            finally { File.Delete(settingsFile); }
        });
    }

    private static byte[][] Render(string path, PlayerSettings settings, DriverState state)
    {
        using var stream = new WindowsAudioSourceFactory().Open(path);
        using var source = new BufferedAudioSource(stream, settings);
        var driver = (TestDriver)AsioDriverSession.Open(() => new TestDriver(state), source.WaveFormat.SampleRate, 0, 2, _ => { });
        // The output owns the driver; the source is prepared only after output negotiation.
        using var output = new AsioPlaybackOutput(driver, source, 0, _ => { });
        source.Start();
        output.Play();
        using var left = new MemoryStream();
        using var right = new MemoryStream();
        var sourceBytes = TestDriver.Frames * source.WaveFormat.BlockAlign;
        var frames = stream.Length / stream.WaveFormat.BlockAlign * settings.Dsp.UpsamplingFactor;
        for (long block = 0; block < (frames + TestDriver.Frames - 1) / TestDriver.Frames + 2; block++)
        {
            source.WaitForData(sourceBytes);
            var channels = driver.Pump();
            left.Write(channels[0]); right.Write(channels[1]);
        }
        Assert.True(source.EndOfStream);
        Assert.Equal(0, source.Underruns);
        Assert.Equal(output.OutputWaveFormat.SampleRate, state.Rate);
        return [left.ToArray(), right.ToArray()];
    }

    [Fact]
    public void RateChangeReleasesTheClockSettingSessionBeforeCreatingPlaybackBuffers()
    {
        var state = new DriverState { Rate = 176400 };
        using (var driver = AsioDriverSession.Open(() => new TestDriver(state), 44100, 0, 2, _ => { }))
        {
            Assert.Equal(new[] { "open", "set 44100", "dispose", "open" }, state.Events);
            Assert.Equal(44100, driver.SampleRate);
            driver.CreateBuffers(2, 0);
        }
        Assert.Equal(new[] { "open", "set 44100", "dispose", "open", "buffers", "dispose" }, state.Events);
    }

    [Fact]
    public void MatchingRateDoesNotReopenOrResetTheClock()
    {
        var state = new DriverState { Rate = 44100 };
        using (AsioDriverSession.Open(() => new TestDriver(state), 44100, 0, 2, _ => { }))
            Assert.Equal(new[] { "open" }, state.Events);
        Assert.Equal(new[] { "open", "dispose" }, state.Events);
    }

    [Fact]
    public void DriverThatRestoresItsDefaultRateIsConfiguredAfterOneReopen()
    {
        var state = new DriverState { Rate = 48000 };
        using var driver = AsioDriverSession.Open(() => { state.Rate = 48000; return new TestDriver(state); }, 44100, 0, 2, _ => { });
        Assert.Equal(44100, driver.SampleRate);
        Assert.Equal(new[] { "open", "set 44100", "dispose", "open", "set 44100" }, state.Events);
    }

    [Fact]
    public void IgnoredRateSettingIsReportedAndReleasedWithoutStarting()
    {
        var state = new DriverState { Rate = 176400, IgnoreRate = true };
        var error = Assert.Throws<InvalidOperationException>(() =>
            AsioDriverSession.Open(() => new TestDriver(state), 44100, 0, 2, _ => { }));
        Assert.Contains("176,400", error.Message);
        Assert.Contains("dispose", state.Events);
        Assert.DoesNotContain("buffers", state.Events);
        Assert.DoesNotContain("start", state.Events);
    }

    [Fact]
    public void ReopenFailureStillReleasesTheFirstSession()
    {
        var state = new DriverState { Rate = 176400 };
        var calls = 0;
        Assert.Throws<IOException>(() => AsioDriverSession.Open(() =>
            ++calls == 1 ? new TestDriver(state) : throw new IOException("Open failed"), 44100, 0, 2, _ => { }));
        Assert.Equal(new[] { "open", "set 44100", "dispose" }, state.Events);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void UnsupportedRateOrMissingChannelsAreRejectedWithoutChangingTheClock(bool supported, int offset)
    {
        var state = new DriverState { Rate = 176400, Supported = supported };
        Assert.ThrowsAny<Exception>(() => AsioDriverSession.Open(() => new TestDriver(state), 44100, offset, 2, _ => { }));
        Assert.Equal(new[] { "open", "dispose" }, state.Events);
    }

    [Fact]
    public void OutputTypeChangeDuringBufferCreationCannotUseAStaleConverter()
    {
        WpfTestHost.Run(() =>
        {
            var state = new DriverState { Rate = 44100, ChangeTypeOnBuffers = true };
            using var driver = new TestDriver(state);
            var source = new SilenceProvider(new WaveFormat(44100, 16, 2));
            Assert.Throws<InvalidOperationException>(() => new AsioPlaybackOutput(driver, source, 0, _ => { }));
            Assert.DoesNotContain("start", state.Events);
        });
    }

    [Fact]
    public void ChangedRateBeforeStartCannotDisplayRequestedRateAndSilentlyContinue()
    {
        WpfTestHost.Run(() =>
        {
            var state = new DriverState { Rate = 44100 };
            var driver = new TestDriver(state);
            using var output = new AsioPlaybackOutput(driver, new SilenceProvider(new WaveFormat(44100, 16, 2)), 0, _ => { });
            state.Rate = 48000;
            Assert.Throws<InvalidOperationException>(output.Play);
            Assert.DoesNotContain("start", state.Events);
            Assert.Equal(PlaybackState.Stopped, output.PlaybackState);
        });
    }

    [Fact]
    public void TransferDiagnosticsCountNonzeroPackedAudioWithoutLoggingSamples()
    {
        WpfTestHost.Run(() =>
        {
            var messages = new List<string>();
            var state = new DriverState { Rate = 44100 };
            using var stream = new WaveFileReader(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference16.wav"));
            using var source = new BufferedAudioSource(stream, new PlayerSettings());
            var output = new AsioPlaybackOutput(new TestDriver(state), source, 0, messages.Add);
            source.Start(); output.Play();
            source.WaitForData(TestDriver.Frames * source.WaveFormat.BlockAlign);
            // Retrieve the session without exposing any audio data to the diagnostic writer.
            state.Current!.Pump();
            output.Dispose();
            Assert.Contains(messages, m => m.Contains("callbacks=1") && m.Contains("nonzeroBuffers=1") && m.Contains("bytes=2048"));
        });
    }

    private static byte[] ReadAll(IWaveProvider source)
    {
        using var data = new MemoryStream();
        var buffer = new byte[257 * source.WaveFormat.BlockAlign];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) data.Write(buffer, 0, read);
        return data.ToArray();
    }

    private sealed class DriverState
    {
        public double Rate { get; set; }
        public bool IgnoreRate { get; init; }
        public bool Supported { get; init; } = true;
        public bool ChangeTypeOnBuffers { get; init; }
        public List<string> Events { get; } = [];
        public TestDriver? Current { get; set; }
    }

    private sealed class TestDriver : IAsioDriverSession
    {
        internal const int Frames = 512;
        private readonly DriverState _state;
        private IntPtr[] _buffers = [];
        private AsioFillBufferCallback? _fill;
        private bool _disposed;
        public TestDriver(DriverState state) { _state = state; state.Current = this; state.Events.Add("open"); }
        public double SampleRate => _state.Rate;
        public int OutputChannels => 2;
        public AsioSampleType GetOutputType(int channel) => _state.ChangeTypeOnBuffers && _buffers.Length > 0
            ? AsioSampleType.Int16LSB : AsioSampleType.Int32LSB;
        public bool SupportsRate(int rate) => _state.Supported;
        public void SetRate(int rate) { _state.Events.Add($"set {rate}"); if (!_state.IgnoreRate) _state.Rate = rate; }
        public int CreateBuffers(int channels, int offset)
        {
            Assert.Equal(2, channels); Assert.Equal(0, offset);
            _state.Events.Add("buffers");
            _buffers = Enumerable.Range(0, channels).Select(_ => Marshal.AllocHGlobal(Frames * 4)).ToArray();
            return Frames;
        }
        public AsioFillBufferCallback? FillBuffer { set => _fill = value; }
        public Action? ResetRequest { set { } }
        public void Start() => _state.Events.Add("start");
        public void Stop() => _state.Events.Add("stop");
        public byte[][] Pump()
        {
            Assert.NotNull(_fill);
            _fill([], _buffers);
            return _buffers.Select(pointer => { var data = new byte[Frames * 4]; Marshal.Copy(pointer, data, 0, data.Length); return data; }).ToArray();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _state.Events.Add("dispose");
            foreach (var pointer in _buffers) Marshal.FreeHGlobal(pointer);
        }
    }
}
