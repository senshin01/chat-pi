using System.ComponentModel;
using System.Windows;
using ChatPi.App.Services;
using ChatPi.Core;
using ChatPi.Core.Audio;

namespace ChatPi.App;

public partial class EqualizerWindow : Window
{
    private readonly Action<double[]> _accept;
    public IReadOnlyList<EqualizerBand> Bands { get; }
    public EqualizerWindow(double[] gains, int sampleRate, PlayerTheme theme, Action<double[]> accept)
    {
        _accept = accept;
        InitializeComponent();
        Bands = GraphicEqualizer.Frequencies.Select((f, i) => new EqualizerBand(f, gains[i], f < sampleRate * .49)).ToArray();
        BandsControl.ItemsSource = Bands;
        RateNotice.Text = $"−12〜＋12 dB · 左右共通 · {sampleRate / 1000.0:0.###} kHz の音源で使えない帯域は無効表示。設定画面で適用／保存すると反映します。";
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowTheme(this, theme);
    }
    private void OnReset(object sender, RoutedEventArgs args) { foreach (var band in Bands) band.GainDb = 0; }
    private void OnAccept(object sender, RoutedEventArgs args) { _accept(Bands.Select(b => b.GainDb).ToArray()); DialogResult = true; }
}

public sealed class EqualizerBand(double frequency, double gain, bool active) : INotifyPropertyChanged
{
    private double _gain = gain;
    public string Label => frequency >= 1000 ? $"{frequency / 1000:0.#}k" : frequency.ToString("0");
    public bool Active => active;
    public double Opacity => Active ? 1 : .4;
    public double GainDb
    {
        get => _gain;
        set { _gain = Math.Round(Math.Clamp(value, -12, 12) * 2) / 2; PropertyChanged?.Invoke(this, new(nameof(GainDb))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
