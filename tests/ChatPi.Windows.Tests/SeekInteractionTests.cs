using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using ChatPi.App;
using ChatPi.App.Services;
using ChatPi.App.ViewModels;
using ChatPi.Core;
using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Windows.Tests;

public sealed class SeekInteractionTests
{
    [Fact]
    public void SliderDragClickAndKeyboardSeekWithoutSnappingBackOrResumingPausedAudio() =>
        WpfTestHost.Run(VerifyOnUiThread);

    private static void VerifyOnUiThread()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chatpi-seek-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "track.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2)))
            writer.Write(new byte[1764000], 0, 1764000);
        var backend = new CountingBackend();
        var model = new PlayerViewModel(new PlaybackController(new WavSources(), backend, action => action()),
            new SettingsStore(Path.Combine(directory, "settings.json")), theme => ThemeManager.Apply(Application.Current.Resources, theme));
        var window = new MainWindow(model);
        try
        {
            model.Open(path);
            model.Seek(1);
            window.Measure(new Size(650, 540));
            window.Arrange(new Rect(0, 0, 650, 540));
            window.UpdateLayout();
            WpfTestHost.FlushBindings();
            var slider = (Slider)window.FindName("ProgressSlider");
            slider.ApplyTemplate();
            var thumb = ((Track)slider.Template.FindName("PART_Track", slider)).Thumb;
            var initialOutput = backend.Last!;
            var created = backend.Created;

            StartDrag(thumb);
            SetValue(slider, 7);
            model.RefreshProgress();
            model.SetError("A state notification while dragging");
            WpfTestHost.FlushBindings();
            Assert.Equal(7, slider.Value);
            Assert.Equal("0:07", model.PositionText);
            Assert.Equal(1, model.PositionSeconds);
            Assert.Equal(created, backend.Created);
            EndDrag(thumb);
            WpfTestHost.FlushBindings();
            Assert.Equal(7, model.PositionSeconds);
            Assert.Equal(7, slider.Value);
            Assert.True(model.IsPlaying);
            Assert.True(initialOutput.Disposed);
            Assert.Equal(created + 1, backend.Created);

            // Cancelled drags restore the current position without reopening the output.
            StartDrag(thumb);
            SetValue(slider, 2);
            EndDrag(thumb, canceled: true);
            WpfTestHost.FlushBindings();
            Assert.Equal(7, slider.Value);
            Assert.Equal(7, model.PositionSeconds);
            Assert.Equal(created + 1, backend.Created);

            // A track click has no Thumb.DragCompleted event; commit after its Value update.
            slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent,
                Handled = true
            });
            slider.SetCurrentValue(RangeBase.ValueProperty, 3d);
            WpfTestHost.FlushBindings();
            Assert.Equal(3, model.PositionSeconds);
            Assert.True(model.IsPlaying);
            Assert.Equal(created + 2, backend.Created);

            // KeyDown edits the preview; KeyUp commits once, including auto-repeat keys.
            using var source = new HwndSource(new HwndSourceParameters("ChatPi seek test") { Width = 1, Height = 1 });
            RaiseKey(slider, source, Keyboard.PreviewKeyDownEvent, Key.Right);
            SetValue(slider, 5);
            model.RefreshProgress();
            Assert.Equal(3, model.PositionSeconds);
            RaiseKey(slider, source, Keyboard.PreviewKeyUpEvent, Key.Right);
            Assert.Equal(5, model.PositionSeconds);
            Assert.Equal(created + 3, backend.Created);

            model.TogglePlay();
            StartDrag(thumb);
            SetValue(slider, 4.5);
            EndDrag(thumb);
            WpfTestHost.FlushBindings();
            Assert.Equal(4.5, model.PositionSeconds);
            Assert.False(model.IsPlaying);
            Assert.Equal("一時停止中", model.StatusText);
            model.TogglePlay();
            Assert.True(model.IsPlaying);
            Assert.Equal(4.5, model.PositionSeconds);

            // A stop or a new file during a gesture must cancel the pending seek.
            StartDrag(thumb);
            SetValue(slider, 8);
            model.Stop();
            EndDrag(thumb);
            Assert.Equal(0, model.PositionSeconds);
            Assert.Equal("停止中", model.StatusText);
            StartDrag(thumb);
            SetValue(slider, 6);
            model.Open(path);
            EndDrag(thumb);
            Assert.Equal(0, model.PositionSeconds);
            Assert.True(model.IsPlaying);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void StartDrag(Thumb thumb) => thumb.RaiseEvent(new DragStartedEventArgs(0, 0)
    { RoutedEvent = Thumb.DragStartedEvent, Handled = true });

    private static void EndDrag(Thumb thumb, bool canceled = false) => thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled)
    { RoutedEvent = Thumb.DragCompletedEvent, Handled = true });

    private static void SetValue(Slider slider, double value)
    {
        slider.SetCurrentValue(RangeBase.ValueProperty, value);
        WpfTestHost.FlushBindings();
    }

    private static void RaiseKey(Slider slider, HwndSource source, RoutedEvent routedEvent, Key key) =>
        slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = routedEvent });

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
