using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatPi.Core;
using ChatPi.Core.Audio;

namespace ChatPi.App.ViewModels;

public sealed class PlayerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PlaybackController _player;
    private readonly SettingsStore _store;
    private readonly Action<PlayerTheme> _applyTheme;
    private TrackMetadata? _track;
    private string? _lastPath;
    private ImageSource? _artwork;
    private string? _error;
    private double _seekSeconds;

    public PlayerViewModel(PlaybackController player, SettingsStore store, Action<PlayerTheme> applyTheme)
    {
        _player = player;
        _store = store;
        _applyTheme = applyTheme;
        try { Settings = store.Load(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidDataException)
        {
            Settings = new PlayerSettings();
            _error = $"設定を読み込めませんでした。既定設定を使用します。{exception.Message}";
        }
        _player.RepeatEnabled = Settings.RepeatEnabled;
        _applyTheme(Settings.Theme);
        _player.Changed += OnPlayerChanged;
        _player.Failed += OnPlayerFailed;
    }

    public PlayerSettings Settings { get; private set; }
    public string Title => _track?.Title ?? "音楽を、ただ再生する。";
    public string Artist => _track?.Artist ?? AudioFiles.FormatSummary;
    public string Album => _track?.Album ?? "ファイルを開くか、この画面にドロップ";
    public ImageSource? Artwork => _artwork;
    public bool HasArtwork => _artwork is not null;
    public bool ShowPlaceholder => _artwork is null;
    public bool HasTrack => _lastPath is not null;
    public bool CanSeek => _player.State != PlayerState.Empty;
    public string PlayLabel => _player.State == PlayerState.Playing ? "一時停止" : "再生";
    public bool IsPlaying => _player.State == PlayerState.Playing;
    public bool IsNotPlaying => !IsPlaying;
    public bool RepeatEnabled => Settings.RepeatEnabled;
    public string RepeatLabel => RepeatEnabled ? "リピート: オン" : "リピート: オフ";
    public string PositionText => FormatTime(TimeSpan.FromSeconds(SeekPositionSeconds));
    public string DurationText => FormatTime(_player.Duration);
    public double PositionSeconds => _player.Position.TotalSeconds;
    public bool IsSeeking { get; private set; }
    public double SeekPositionSeconds
    {
        get => IsSeeking ? _seekSeconds : PositionSeconds;
        set
        {
            if (!IsSeeking) return;
            _seekSeconds = Math.Clamp(value, 0, _player.Duration.TotalSeconds);
            OnPropertyChanged(nameof(SeekPositionSeconds));
            OnPropertyChanged(nameof(PositionText));
        }
    }
    public double DurationSeconds => Math.Max(1, _player.Duration.TotalSeconds);
    public string? Error => _error;
    public bool HasError => !string.IsNullOrEmpty(_error);
    public string OutputModeText => Settings.OutputMode switch
    {
        OutputMode.Asio => "ASIO",
        OutputMode.WasapiShared => "WASAPI · 共有",
        _ => "WASAPI · 排他"
    };
    public string FormatText => _player.SourceFormat is { } format
        ? $"{Path.GetExtension(_lastPath)?.TrimStart('.').ToUpperInvariant()}  ·  {format.SampleRate / 1000.0:0.###} kHz  ·  {format.BitsPerSample} bit  ·  {(format.Channels == 1 ? "MONO" : "STEREO")}"
        : "元のサンプルレートで再生";
    public string DeviceText => _player.DeviceName ?? "設定から出力デバイスを選択できます";
    public string OutputText => _player.OutputFormat is { } format
        ? $"出力 {format.SampleRate / 1000.0:0.###} kHz / {format.BitsPerSample} bit" +
          (Settings.OutputMode == OutputMode.WasapiShared ? " · Windows ミキサー経由" : "") +
          (Settings.Dsp.Enabled ? $" · EQ {(Settings.Dsp.EqualizerEnabled ? "オン" : "オフ")} · SRC {(Settings.Dsp.UpsamplingFactor == 1 ? "オフ" : $"×{Settings.Dsp.UpsamplingFactor}")}" : " · DSP なし")
        : "";
    public int SourceSampleRate => _player.SourceFormat?.SampleRate ?? 44100;
    public string DspDetailText => _player.OutputFormat is null ? "" :
        $"プリアンプ {_player.AppliedPreampDb:0.0} dB · 先読み {_player.ReadAheadMilliseconds} ms" +
        (Settings.Dsp.UpsamplingFactor > 1 ? $" · フィルター {(Settings.Dsp.Filter == UpsamplingFilter.Standard ? "標準" : "スーパースロー")}" : "") +
        (_player.ClippedSamples > 0 ? $" · ピーク超過 {_player.ClippedSamples:N0}" : "") +
        (_player.BufferUnderruns > 0 ? $" · バッファ不足 {_player.BufferUnderruns:N0}" : "");
    public string StatusText => _player.State switch
    {
        PlayerState.Playing => "再生中",
        PlayerState.Paused => "一時停止中",
        PlayerState.Completed => "再生終了",
        PlayerState.Stopped => "停止中",
        _ => "READY"
    };

    public void Open(string path)
    {
        Try(() =>
        {
            path = AudioFiles.ValidatePath(path);
            IsSeeking = false;
            _lastPath = path;
            _track = TrackMetadata.Read(path);
            _artwork = LoadArtwork(_track.Artwork);
            NotifyAll();
            _player.Open(path, Settings);
        });
    }

    public void TogglePlay() => Try(() =>
    {
        if (_player.State == PlayerState.Playing) _player.Pause();
        else if (_player.State == PlayerState.Empty && _lastPath is not null) _player.Open(_lastPath, Settings);
        else _player.Play();
    });

    public void Stop() { IsSeeking = false; Try(_player.Stop); }
    public void ToggleRepeat() => Try(() => ApplySettings(Settings with { RepeatEnabled = !RepeatEnabled }));
    public void Seek(double seconds) => Try(() => _player.Seek(TimeSpan.FromSeconds(seconds)));

    public void BeginSeek()
    {
        if (IsSeeking || !CanSeek) return;
        _seekSeconds = PositionSeconds;
        IsSeeking = true;
    }

    public void CommitSeek()
    {
        if (!IsSeeking) return;
        var seconds = _seekSeconds;
        IsSeeking = false;
        Seek(seconds);
    }

    public void CancelSeek()
    {
        IsSeeking = false;
        NotifyAll();
    }

    public void ApplySettings(PlayerSettings settings)
    {
        settings.Validate();
        var deviceChanged = (settings with { RepeatEnabled = Settings.RepeatEnabled, Theme = Settings.Theme, Dsp = Settings.Dsp }) != Settings;
        var dspChanged = !settings.Dsp.SameAs(Settings.Dsp);
        var outputChanged = deviceChanged || dspChanged;
        var themeChanged = settings.Theme != Settings.Theme;
        _store.Save(settings);
        Settings = settings;
        _player.RepeatEnabled = settings.RepeatEnabled;
        if (themeChanged) _applyTheme(settings.Theme);
        var position = _player.Position;
        var wasPlaying = IsPlaying;
        var wasPaused = _player.State == PlayerState.Paused;
        if (dspChanged && !deviceChanged && _player.TryUpdateDsp(settings)) outputChanged = false;
        if (outputChanged && _lastPath is not null)
        {
            Try(() =>
            {
                _player.Open(_lastPath, settings, autoPlay: false, startPaused: wasPaused);
                _player.Seek(position);
                if (wasPlaying) _player.Play();
            });
        }
        else NotifyAll();
    }

    public void SetError(string message) { _error = message; NotifyAll(); }
    public void RefreshProgress()
    {
        OnPropertyChanged(nameof(DspDetailText));
        OnPropertyChanged(nameof(PositionSeconds));
        if (!IsSeeking)
        {
            OnPropertyChanged(nameof(SeekPositionSeconds));
            OnPropertyChanged(nameof(PositionText));
        }
    }

    private void Try(Action action)
    {
        try { _error = null; action(); }
        catch (Exception exception) { _error = exception.Message; }
        NotifyAll();
    }

    private static ImageSource? LoadArtwork(byte[]? bytes)
    {
        if (bytes is null) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 512;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException or ArgumentException or FormatException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1
        ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
        : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private void OnPlayerChanged(object? sender, EventArgs args) => NotifyAll();
    private void OnPlayerFailed(object? sender, Exception exception) => SetError(exception.Message);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose()
    {
        _player.Changed -= OnPlayerChanged;
        _player.Failed -= OnPlayerFailed;
        _player.Dispose();
    }
}
