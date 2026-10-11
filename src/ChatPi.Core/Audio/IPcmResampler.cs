namespace ChatPi.Core.Audio;

public interface IPcmResampler : IDisposable
{
    // Frame counts, not sample counts. After input ends, call with end=true
    // and no input until it returns zero to drain all remaining output.
    int Process(double[] input, int offsetFrames, int frames, double[] output, out int consumed, bool end = false);
}

public static class PcmResamplers
{
    public static IPcmResampler Create(int inputRate, int factor, int channels, UpsamplingFilter filter) => filter switch
    {
        UpsamplingFilter.Standard => new SoxrResampler(inputRate, factor, channels),
        UpsamplingFilter.SuperSlow => new SuperSlowResampler(factor, channels),
        _ => throw new ArgumentOutOfRangeException(nameof(filter))
    };
}
