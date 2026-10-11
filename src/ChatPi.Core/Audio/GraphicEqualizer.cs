using System.Numerics;

namespace ChatPi.Core.Audio;

/// <summary>One-octave peaking sections, independent transposed-direct-form-II states per channel.</summary>
public sealed class GraphicEqualizer
{
    public static IReadOnlyList<double> Frequencies { get; } = Array.AsReadOnly(new double[]
        { 25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000 });
    private readonly Biquad[] _filters;
    private readonly double[] _z1;
    private readonly double[] _z2;
    private readonly int _channels;
    private readonly double _gain;
    public double AppliedPreampDb { get; }
    public IReadOnlyList<bool> ActiveBands { get; }

    public GraphicEqualizer(int sampleRate, int channels, DspSettings settings)
    {
        settings.Validate();
        _channels = channels;
        ActiveBands = Frequencies.Select(f => f < sampleRate * .49).ToArray();
        _filters = Frequencies.Select((f, i) => (f, i)).Where(b => settings.EqualizerEnabled && ActiveBands[b.i] && settings.BandGainsDb[b.i] != 0)
            .Select(b => Biquad.Peaking(b.f, sampleRate, settings.BandGainsDb[b.i])).ToArray();
        _z1 = new double[_filters.Length * channels];
        _z2 = new double[_filters.Length * channels];
        // Evaluate the complete cascade, including band overlap. This is an estimate,
        // not a bound on every transient; post-SRC overloads are monitored separately.
        var peakDb = 0.0;
        if (_filters.Length > 0 && settings.AutoHeadroom)
        {
            for (var i = 0; i <= 8192; i++)
            {
                var f = sampleRate * .5 * i / 8192;
                peakDb = Math.Max(peakDb, ResponseDb(f, sampleRate));
            }
            foreach (var f in Frequencies.Where(f => f < sampleRate * .5))
                peakDb = Math.Max(peakDb, ResponseDb(f, sampleRate));
        }
        var hasBoost = settings.EqualizerEnabled && settings.BandGainsDb.Any(g => g > 0);
        var margin = settings.AutoHeadroom && (hasBoost || settings.UpsamplingFactor > 1) ? 3 : 0;
        AppliedPreampDb = settings.Enabled ? settings.PreampDb - peakDb - margin : 0;
        _gain = Math.Pow(10, AppliedPreampDb / 20);
    }

    public double ResponseDb(double frequency, int sampleRate)
    {
        var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / sampleRate);
        var response = Complex.One;
        foreach (var b in _filters) response *= (b.B0 + b.B1 * z + b.B2 * z * z) / (1 + b.A1 * z + b.A2 * z * z);
        return 20 * Math.Log10(Math.Max(1e-30, response.Magnitude));
    }

    public void Process(Span<double> samples)
    {
        if (_filters.Length == 0 && _gain == 1) return;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i] * _gain;
            var channel = i % _channels;
            for (var band = 0; band < _filters.Length; band++)
            {
                var b = _filters[band];
                var state = band * _channels + channel;
                var output = b.B0 * value + _z1[state];
                _z1[state] = b.B1 * value - b.A1 * output + _z2[state];
                _z2[state] = b.B2 * value - b.A2 * output;
                // Avoid long-lived subnormal filter tails.
                if (Math.Abs(_z1[state]) < 1e-280) _z1[state] = 0;
                if (Math.Abs(_z2[state]) < 1e-280) _z2[state] = 0;
                value = output;
            }
            samples[i] = value;
        }
    }

    private readonly record struct Biquad(double B0, double B1, double B2, double A1, double A2)
    {
        public static Biquad Identity => new(1, 0, 0, 0, 0);
        public static Biquad Peaking(double f, int rate, double db)
        {
            if (db == 0) return Identity;
            var a = Math.Pow(10, db / 40);
            var w = 2 * Math.PI * f / rate;
            var alpha = Math.Sin(w) / (2 * Math.Sqrt(2));
            var a0 = 1 + alpha / a;
            return new((1 + alpha * a) / a0, -2 * Math.Cos(w) / a0, (1 - alpha * a) / a0,
                -2 * Math.Cos(w) / a0, (1 - alpha / a) / a0);
        }
    }
}
