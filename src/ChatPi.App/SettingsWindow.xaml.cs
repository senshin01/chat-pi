using System.Windows;
using System.Windows.Controls;
using ChatPi.App.Audio;
using ChatPi.App.Services;
using ChatPi.Core;

namespace ChatPi.App;

public partial class SettingsWindow : Window
{
    private readonly PlayerSettings _original;
    private readonly Action<PlayerSettings> _save;

    public SettingsWindow(PlayerSettings settings, Action<PlayerSettings> save)
    {
        _original = settings;
        _save = save;
        InitializeComponent();
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
            _ => "デバイスを排他的に使用します。対応しない形式はエラーを表示し、共有モードへ自動切り替えしません。音源のデジタル音量は変更しません。"
        };
    }

    private void OnSave(object sender, RoutedEventArgs args)
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
            var settings = _original with
            {
                OutputMode = Mode,
                WasapiDeviceId = DeviceBox.SelectedItem is AudioDevice device ? device.Id : _original.WasapiDeviceId,
                AsioDriverName = DriverBox.SelectedItem as string ?? _original.AsioDriverName,
                LatencyMilliseconds = latency,
                AsioChannelOffset = channel - 1
            };
            // An unavailable saved driver is not a valid current selection.
            if (Mode == OutputMode.Asio && DriverBox.SelectedItem is null)
                throw new ArgumentException("利用可能な ASIO ドライバーを選択してください。");
            settings.Validate();
            _save(settings);
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }
    private void OnRegister(object sender, RoutedEventArgs args)
    {
        try { FileAssociations.Register(); AssociationNotice.Text = "登録しました。Windows の「既定のアプリ」で Chat Pi を選択してください。"; }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }
    private void OnUnregister(object sender, RoutedEventArgs args)
    {
        try { FileAssociations.Unregister(); AssociationNotice.Text = "登録を解除しました。必要に応じて Windows で別の既定アプリを選択してください。"; }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }
}
