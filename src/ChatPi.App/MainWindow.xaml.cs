using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ChatPi.App.Audio;
using ChatPi.App.Services;
using ChatPi.App.ViewModels;
using ChatPi.Core;
using ChatPi.Core.Audio;
using Microsoft.Win32;

namespace ChatPi.App;

public partial class MainWindow : Window
{
    private readonly PlayerViewModel _model;
    private readonly DispatcherTimer _timer;
    private bool _dragging;
    private bool _keyboardSeeking;
    private long _seekGesture;

    public MainWindow() : this(null) { }

    internal MainWindow(PlayerViewModel? model)
    {
        InitializeComponent();
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatPi", "settings.json");
        var player = new PlaybackController(new WindowsAudioSourceFactory(), new WindowsAudioBackendFactory(),
            action => Dispatcher.BeginInvoke(action));
        _model = model ?? new PlayerViewModel(player, new SettingsStore(settingsPath), theme =>
        {
            ThemeManager.Apply(Application.Current.Resources, theme);
            ThemeManager.ApplyWindowTheme(this, theme);
        });
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowTheme(this, _model.Settings.Theme);
        DataContext = _model;
        // Start before Slider handles a track click. Thumb handles mouse release itself,
        // so listen to its routed completion event even when already handled.
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnSeekStart), handledEventsToo: true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnSeekEnd), handledEventsToo: true);
        ProgressSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnSeekDragStarted), handledEventsToo: true);
        ProgressSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSeekDragCompleted), handledEventsToo: true);
        ProgressSlider.LostKeyboardFocus += (_, _) =>
        {
            if (_keyboardSeeking && !ProgressSlider.IsKeyboardFocusWithin)
            {
                _keyboardSeeking = false;
                _model.CommitSeek();
            }
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => _model.RefreshProgress();
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); _model.Dispose(); };
    }

    public void OpenFile(string path) => _model.Open(path);
    private void OnOpen(object? sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = AudioFiles.FileDialogFilter, CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) OpenFile(dialog.FileName);
    }
    private void OnTogglePlay(object? sender, RoutedEventArgs args) => _model.TogglePlay();
    private void OnStop(object? sender, RoutedEventArgs args) => _model.Stop();
    private void OnToggleRepeat(object? sender, RoutedEventArgs args) => _model.ToggleRepeat();
    private void OnSettings(object? sender, RoutedEventArgs args)
    {
        var dialog = new SettingsWindow(_model.Settings, _model.ApplySettings, _model.SourceSampleRate) { Owner = this };
        dialog.ShowDialog();
    }
    private void OnDragOver(object sender, DragEventArgs args)
    {
        var files = args.Data.GetData(DataFormats.FileDrop) as string[];
        args.Effects = files?.FirstOrDefault() is { } path && AudioFiles.IsSupported(path) ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs args)
    {
        if (args.Data.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault() is { } path)
            OpenFile(path);
        args.Handled = true;
    }
    private void OnSeekStart(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || args.OriginalSource is not System.Windows.Media.Visual visual ||
            (visual != ProgressSlider && !ProgressSlider.IsAncestorOf(visual))) return;
        _model.BeginSeek();
        var gesture = ++_seekGesture;
        // A track click changes Value during this input event without starting a drag.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (gesture == _seekGesture && !_dragging) _model.CommitSeek();
        });
    }
    private void OnSeekEnd(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton == MouseButton.Left && !_dragging) _model.CommitSeek();
    }
    private void OnSeekDragStarted(object sender, DragStartedEventArgs args)
    {
        _dragging = true;
        _model.BeginSeek();
    }
    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs args)
    {
        _dragging = false;
        _seekGesture++;
        if (args.Canceled) _model.CancelSeek();
        else _model.CommitSeek();
    }
    private void OnSeekKeyDown(object sender, KeyEventArgs args)
    {
        if (IsSeekKey(args.Key)) { _keyboardSeeking = true; _model.BeginSeek(); }
    }
    private void OnSeekKeyUp(object sender, KeyEventArgs args)
    {
        if (IsSeekKey(args.Key)) { _keyboardSeeking = false; _model.CommitSeek(); }
    }
    private static bool IsSeekKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
    private void OnWindowKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { OnOpen(this, new RoutedEventArgs()); args.Handled = true; }
        else if (args.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && args.OriginalSource is not (Button or TextBox))
        { _model.TogglePlay(); args.Handled = true; }
        else if (args.Key == Key.Escape) { _model.Stop(); args.Handled = true; }
    }
}
