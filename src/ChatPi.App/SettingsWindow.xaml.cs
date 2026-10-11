using System.Windows;
using System.Windows.Controls;
using ChatPi.App.Audio;
using ChatPi.App.Services;
using ChatPi.Core;
using ChatPi.Core.Audio;

namespace ChatPi.App;

public partial class SettingsWindow : Window
{
    private readonly PlayerSettings _original;
    private readonly Action<PlayerSettings> _save;
    private readonly int _sampleRate;
    private double[] _bandGains;

    public SettingsWindow(PlayerSettings settings, Action<PlayerSettings> save, int sampleRate = 44100)
    {
        _original = settings;
        _save = save;
        _sampleRate = sampleRate;
        _bandGains = (double[])settings.Dsp.BandGainsDb.Clone();
        InitializeComponent();
        UpsamplingBox.SelectedValue = settings.Dsp.UpsamplingFactor.ToString();
        EqualizerEnabledBox.IsChecked = settings.Dsp.EqualizerEnabled;
        AutoHeadroomBox.IsChecked = settings.Dsp.AutoHeadroom;
        DitherBox.IsChecked = settings.Dsp.DitherEnabled;
        PreampBox.Text = settings.Dsp.PreampDb.ToString(System.Globalization.CultureInfo.CurrentCulture);
        ReadAheadBox.Text = settings.ReadAheadMilliseconds.ToString();
        ThemeBox.SelectedValue = settings.Theme.ToString();
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowTheme(this, settings.Theme);
        var notices = new List<string>();
        try
        {
            var devices = WindowsAudioBackendFactory.GetWasapiDevices();
            DeviceBox.ItemsSource = devices;
            DeviceBox.SelectedItem = devices.FirstOrDefault(device => device.Id == settings.WasapiDeviceId);
            if (DeviceBox.SelectedItem is null) notices.Add("保存済みの WASAPI デバイスがありません。接続するか、別のデバイスを選択してください。");
        }
        catch (Exception exception) { notices.Add($"WASAPI デバイスを取得できません: {exception.Message}"); }
        try
        {
            var drivers = WindowsAudioBackendFactory.GetAsioDrivers();
            DriverBox.ItemsSource = drivers;
            DriverBox.SelectedItem = drivers.FirstOrDefault(driver => driver == settings.AsioDriverName);
            if (drivers.Length == 0) notices.Add("ASIO ドライバーは未インストールです。WASAPI はそのまま使えます。");
            else if (settings.AsioDriverName is not null && DriverBox.SelectedItem is null) notices.Add("保存済みの ASIO ドライバーがありません。選び直してください。");
        }
        catch (Exception exception) { notices.Add($"ASIO ドライバーを取得できません: {exception.Message}"); }
        DeviceNotice.Text = string.Join("\n", notices);
        LatencyBox.Text = settings.LatencyMilliseconds.ToString();
        ChannelBox.Text = (settings.AsioChannelOffset + 1).ToString();
        ModeBox.SelectedValue = settings.OutputMode.ToString();
        UpdateMode();
    }

    private OutputMode Mode => Enum.TryParse<OutputMode>(ModeBox.SelectedValue as string, out var mode) ? mode : OutputMode.WasapiExclusive;
    private void OnModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (WasapiPanel is not null && AsioPanel is not null) UpdateMode();
    }
    private void UpdateMode()
    {
        WasapiPanel.Visibility = Mode == OutputMode.Asio ? Visibility.Collapsed : Visibility.Visible;
        AsioPanel.Visibility = Mode == OutputMode.Asio ? Visibility.Visible : Visibility.Collapsed;
        ModeHelp.Text = Mode switch
        {
            OutputMode.Asio => "メーカーの ASIO ドライバーへ直接出力します。対応しないサンプルレートでは停止します。ASIO の占有・共有の挙動はドライバーによります。",
            OutputMode.WasapiShared => "Windows ミキサーを通して再生します。デバイス設定に応じて Windows が形式を変換する場合があります。",
            _ => "デバイスを排他的に使用します。対応しない形式はエラーを表示し、共有モードへ自動切り替えしません。"
        };
    }

    private void OnSave(object sender, RoutedEventArgs args)
    {
        if (SaveSettings()) DialogResult = true;
    }
    private void OnApply(object sender, RoutedEventArgs args) => SaveSettings();
    private void OnEqualizer(object sender, RoutedEventArgs args)
    {
        new EqualizerWindow(_bandGains, _sampleRate, _original.Theme, gains => _bandGains = gains) { Owner = this }.ShowDialog();
    }
    private bool SaveSettings()
    {
        try
        {
            var latency = _original.LatencyMilliseconds;
            var channel = _original.AsioChannelOffset + 1;
            if (Mode != OutputMode.Asio)
            {
                if (!int.TryParse(LatencyBox.Text, out latency)) throw new ArgumentException("バッファは整数で入力してください。");
                if (DeviceBox.SelectedItem is null) throw new ArgumentException("再生デバイスを選択してください。");
            }
            else if (!int.TryParse(ChannelBox.Text, out channel)) throw new ArgumentException("出力開始チャンネルは整数で入力してください。");
            if (!double.TryParse(PreampBox.Text, out var preamp)) throw new ArgumentException("プリアンプは数値で入力してください。");
            if (!int.TryParse(ReadAheadBox.Text, out var readAhead)) throw new ArgumentException("先読みは整数で入力してください。");
            var settings = _original with
            {
                OutputMode = Mode,
                Theme = Enum.Parse<PlayerTheme>((string)ThemeBox.SelectedValue),
                WasapiDeviceId = DeviceBox.SelectedItem is AudioDevice device ? device.Id : _original.WasapiDeviceId,
                AsioDriverName = DriverBox.SelectedItem as string ?? _original.AsioDriverName,
                LatencyMilliseconds = latency,
                AsioChannelOffset = channel - 1,
                ReadAheadMilliseconds = readAhead,
                Dsp = new DspSettings
                {
                    EqualizerEnabled = EqualizerEnabledBox.IsChecked == true,
                    BandGainsDb = (double[])_bandGains.Clone(),
                    UpsamplingFactor = int.Parse((string)UpsamplingBox.SelectedValue),
                    AutoHeadroom = AutoHeadroomBox.IsChecked == true,
                    PreampDb = preamp,
                    DitherEnabled = DitherBox.IsChecked == true
                }
            };
            // An unavailable saved driver is not a valid current selection.
            if (Mode == OutputMode.Asio && DriverBox.SelectedItem is null)
                throw new ArgumentException("利用可能な ASIO ドライバーを選択してください。");
            settings.Validate();
            _save(settings);
            ErrorText.Text = "適用しました。";
            return true;
        }
        catch (Exception exception) { ErrorText.Text = exception.Message; return false; }
    }
    private void OnRegister(object sender, RoutedEventArgs args)
    {
        try { FileAssociations.Register(); AssociationNotice.Text = "登録しました。形式別アイコンを使うには、Windows の「既定のアプリ」で各形式に Chat Pi を選び直してください。"; }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }
    private void OnUnregister(object sender, RoutedEventArgs args)
    {
        try { FileAssociations.Unregister(); AssociationNotice.Text = "登録を解除しました。必要に応じて Windows で別の既定アプリを選択してください。"; }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }
}
