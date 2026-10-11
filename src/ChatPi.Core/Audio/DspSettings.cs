namespace ChatPi.Core.Audio;

public sealed record DspSettings
{
    public bool EqualizerEnabled { get; init; }
    public double[] BandGainsDb { get; init; } = new double[15];
    public int UpsamplingFactor { get; init; } = 1;
    public bool AutoHeadroom { get; init; } = true;
    public double PreampDb { get; init; }
    public bool DitherEnabled { get; init; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Enabled => EqualizerEnabled || UpsamplingFactor != 1;

    public void Validate()
    {
        if (UpsamplingFactor is not (1 or 2 or 4)) throw new ArgumentException("アップサンプリングはオフ／2倍／4倍を選択してください。");
        if (BandGainsDb is null || BandGainsDb.Length != 15 || BandGainsDb.Any(g => !double.IsFinite(g) || g is < -12 or > 12))
            throw new ArgumentException("EQ は15バンド、各 −12〜＋12 dB にしてください。");
        if (!double.IsFinite(PreampDb) || PreampDb is < -24 or > 0) throw new ArgumentException("プリアンプは −24〜0 dB にしてください。");
    }

    public bool SameAs(DspSettings other) => EqualizerEnabled == other.EqualizerEnabled &&
        UpsamplingFactor == other.UpsamplingFactor && AutoHeadroom == other.AutoHeadroom &&
        PreampDb == other.PreampDb && DitherEnabled == other.DitherEnabled && BandGainsDb.SequenceEqual(other.BandGainsDb);
    public bool Equals(DspSettings? other) => other is not null && SameAs(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EqualizerEnabled); hash.Add(UpsamplingFactor); hash.Add(AutoHeadroom); hash.Add(PreampDb); hash.Add(DitherEnabled);
        foreach (var gain in BandGainsDb) hash.Add(gain);
        return hash.ToHashCode();
    }
}
