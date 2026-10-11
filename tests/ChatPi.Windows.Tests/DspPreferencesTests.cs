using System.Windows;
using System.Windows.Controls;
using ChatPi.App;
using ChatPi.App.Services;
using ChatPi.App.ViewModels;
using ChatPi.Core;
using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Windows.Tests;

public sealed class DspPreferencesTests
{
    [Fact]
    public void DspControlsSaveAndReloadAndPausedRateChangesRemainPaused() => WpfTestHost.Run(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chatpi-dsp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var backend = new TestBackend();
        using var model = new PlayerViewModel(new PlaybackController(new WavSources(), backend, action => action()), store,
            theme => ThemeManager.Apply(Application.Current.Resources, theme));
        var window = new MainWindow(model);
        var settings = new SettingsWindow(model.Settings, model.ApplySettings);
        var eq = new EqualizerWindow(new double[15], 8000, PlayerTheme.Dark, _ => { });
        try
        {
            Assert.Equal(15, eq.Bands.Count);
            Assert.False(eq.Bands[14].Active);
            eq.Bands[8].GainDb = 6;
            Assert.Equal(6, eq.Bands[8].GainDb);
            Assert.Equal("1k", eq.Bands[8].Label);
            Assert.Equal("1", ((ComboBox)settings.FindName("UpsamplingBox")).SelectedValue);
            Assert.False(((CheckBox)settings.FindName("EqualizerEnabledBox")).IsChecked);
            Assert.True(((CheckBox)settings.FindName("AutoHeadroomBox")).IsChecked);
            var path = Path.Combine(directory, "track.wav");
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 24, 2))) writer.Write(new byte[44100 * 6], 0, 44100 * 6);
            model.Open(path); model.Seek(.25); model.TogglePlay();
            var gains = new double[15]; gains[8] = 6;
            model.ApplySettings(model.Settings with { Dsp = new DspSettings { EqualizerEnabled = true, BandGainsDb = gains, UpsamplingFactor = 4, PreampDb = -1 } });
            Assert.False(model.IsPlaying); Assert.Equal("一時停止中", model.StatusText);
            Assert.Equal(.25, model.PositionSeconds);
            var saved = store.Load();
            Assert.True(saved.Dsp.EqualizerEnabled); Assert.Equal(4, saved.Dsp.UpsamplingFactor);
            Assert.Equal(6, saved.Dsp.BandGainsDb[8]); Assert.Equal(-1, saved.Dsp.PreampDb);
            model.TogglePlay();
            Assert.Equal(176400, backend.Last!.Source.WaveFormat.SampleRate);
            Assert.Contains("EQ オン", model.OutputText); Assert.Contains("SRC ×4", model.OutputText);
            Assert.Contains("プリアンプ -10.0 dB", model.DspDetailText);
            WpfTestHost.FlushBindings();
            Assert.Equal(model.DspDetailText, ((TextBlock)window.FindName("DspDetail")).Text);
            var output = backend.Last;
            var count = backend.Created;
            model.ApplySettings(model.Settings with { Dsp = model.Settings.Dsp with { BandGainsDb = new double[15] } });
            Assert.Same(output, backend.Last); Assert.Equal(count, backend.Created); Assert.False(output.Disposed);
            Assert.True(model.IsPlaying);
            model.Stop();
            using var reloaded = new PlayerViewModel(new PlaybackController(new WavSources(), new TestBackend(), action => action()), store, _ => { });
            Assert.True(reloaded.Settings.Dsp.EqualizerEnabled); Assert.Equal(4, reloaded.Settings.Dsp.UpsamplingFactor);
        }
        finally { eq.Close(); settings.Close(); window.Close(); Directory.Delete(directory, true); }
    });

    private sealed class WavSources : IAudioSourceFactory { public WaveStream Open(string path) => new WaveFileReader(path); }
    private sealed class TestBackend : IPlaybackBackendFactory
    {
        public int Created { get; private set; }
        public TestPlayer? Last { get; private set; }
        public PlaybackBackend Create(IWaveProvider source, PlayerSettings settings)
        {
            Created++; Last = new TestPlayer(source); return new(Last, source.WaveFormat, "Test output");
        }
    }
    private sealed class TestPlayer(IWaveProvider source) : IWavePlayer
    {
        public IWaveProvider Source => source;
        public bool Disposed { get; private set; }
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat => source.WaveFormat;
        public float Volume { get; set; } = 1;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider waveProvider) => throw new NotSupportedException();
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new()); }
        public void Dispose() => Disposed = true;
    }
}
