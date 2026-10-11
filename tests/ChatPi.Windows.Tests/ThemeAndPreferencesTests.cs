using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatPi.App;
using ChatPi.App.Services;
using ChatPi.App.ViewModels;
using ChatPi.Core;
using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Windows.Tests;

public sealed class ThemeAndPreferencesTests
{
    [Fact]
    public void ThemesUpdateBothWindowsAndSavedPreferencesDoNotInterruptPlayback()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyOnUiThread(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyOnUiThread()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chatpi-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var app = new ChatPi.App.App();
        app.InitializeComponent();
        var window = new MainWindow();
        var settingsWindow = new SettingsWindow(new PlayerSettings(), _ => { });
        try
        {
            ThemeManager.Apply(app.Resources, PlayerTheme.Dark);
            var darkKeys = app.Resources.MergedDictionaries[0].Keys.Cast<string>().Order().ToArray();
            Assert.Equal(Color.FromRgb(0x11, 0x14, 0x1A), ((SolidColorBrush)window.Background).Color);
            ThemeManager.Apply(app.Resources, PlayerTheme.Light);
            Assert.Equal(darkKeys, app.Resources.MergedDictionaries[0].Keys.Cast<string>().Order().ToArray());
            Assert.Equal(Color.FromRgb(0xFA, 0xFB, 0xFA), ((SolidColorBrush)window.Background).Color);
            Assert.Equal(((SolidColorBrush)window.Background).Color, ((SolidColorBrush)settingsWindow.Background).Color);
            Assert.Equal(Color.FromRgb(0x18, 0x23, 0x1D), ((SolidColorBrush)window.Foreground).Color);
            var themeBox = (ComboBox)settingsWindow.FindName("ThemeBox");
            themeBox.ApplyTemplate();
            Assert.Equal("Dark", themeBox.SelectedValue);
            themeBox.SelectedValue = "Light";
            Assert.Equal("Light", themeBox.SelectedValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => ThemeManager.Apply(app.Resources, (PlayerTheme)99));

            var path = Path.Combine(directory, "track.wav");
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2))) writer.Write(new byte[176400], 0, 176400);
            var settingsPath = Path.Combine(directory, "settings.json");
            var store = new SettingsStore(settingsPath);
            var backend = new CountingBackend();
            using (var model = CreateModel(store, backend, app.Resources))
            {
                window.DataContext = model;
                var repeatButton = (Button)window.FindName("RepeatButton");
                model.Open(path);
                model.Seek(0.5);
                var output = backend.Last;
                var count = backend.Created;
                model.ToggleRepeat();
                Assert.True(model.RepeatEnabled);
                Assert.True(store.Load().RepeatEnabled);
                Assert.Equal("リピート: オン", model.RepeatLabel);
                Assert.Equal(((SolidColorBrush)app.FindResource("AccentBrush")).Color, ((SolidColorBrush)repeatButton.Background).Color);
                model.ApplySettings(model.Settings with { Theme = PlayerTheme.Light });
                Assert.True(model.IsPlaying);
                Assert.InRange(model.PositionSeconds, 0.499, 0.501);
                Assert.Equal(count, backend.Created);
                Assert.Same(output, backend.Last);
                Assert.False(output!.Disposed);
                model.TogglePlay();
                model.ApplySettings(model.Settings with { Theme = PlayerTheme.Dark });
                model.ToggleRepeat();
                Assert.False(model.IsPlaying);
                Assert.Equal("一時停止中", model.StatusText);
                Assert.Equal(count, backend.Created);
                Assert.False(store.Load().RepeatEnabled);
                Assert.Equal(((SolidColorBrush)app.FindResource("PanelBrush")).Color, ((SolidColorBrush)repeatButton.Background).Color);
                model.ToggleRepeat();
                model.ApplySettings(model.Settings with { Theme = PlayerTheme.Light });
            }
            using var reloaded = CreateModel(new SettingsStore(settingsPath), new CountingBackend(), app.Resources);
            Assert.True(reloaded.RepeatEnabled);
            Assert.Equal(PlayerTheme.Light, reloaded.Settings.Theme);
            Assert.Equal(Color.FromRgb(0xFA, 0xFB, 0xFA), ((SolidColorBrush)window.Background).Color);
        }
        finally
        {
            settingsWindow.Close();
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PlayerViewModel CreateModel(SettingsStore store, CountingBackend backend, ResourceDictionary resources) =>
        new(new PlaybackController(new WavSources(), backend, action => action()), store, theme => ThemeManager.Apply(resources, theme));

    private sealed class WavSources : IAudioSourceFactory
    {
        public WaveStream Open(string path) => new WaveFileReader(path);
    }

    private sealed class CountingBackend : IPlaybackBackendFactory
    {
        public int Created { get; private set; }
        public FakePlayer? Last { get; private set; }
        public PlaybackBackend Create(IWaveProvider source, PlayerSettings settings)
        {
            Created++;
            Last = new FakePlayer();
            Last.Init(source);
            return new PlaybackBackend(Last, source.WaveFormat, "Test output");
        }
    }

    private sealed class FakePlayer : IWavePlayer
    {
        public bool Disposed { get; private set; }
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat { get; private set; } = null!;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider provider) => OutputWaveFormat = provider.WaveFormat;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs()); }
        public void Dispose() => Disposed = true;
    }
}
