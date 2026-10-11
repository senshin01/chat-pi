using System.Diagnostics;
using ChatPi.Core.Audio;
using Xunit.Abstractions;

namespace ChatPi.Core.Tests;

public sealed class UltraHighTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(44100, 2)] [InlineData(44100, 4)]
    [InlineData(48000, 2)] [InlineData(48000, 4)]
    [InlineData(96000, 4)] [InlineData(192000, 4)]
    public void SmallStereoBlocksHaveBoundedLookaheadAndDrainExactly(int rate, int factor)
    {
        using var src = Assert.IsType<SoxrResampler>(PcmResamplers.Create(rate, factor, 2, UpsamplingFilter.UltraHigh));
        Assert.Equal(32, src.PrecisionBits);
        var input = new double[rate * 2];
        var output = new double[256 * factor * 2];
        var consumed = 0;
        var total = 0;
        var firstOutputInputFrames = 0;
        double largestPending = 0;
        var timer = Stopwatch.StartNew();
        while (consumed < rate)
        {
            var written = src.Process(input, consumed, Math.Min(256, rate - consumed), output, out var used);
            Assert.True(used > 0 || written > 0);
            consumed += used;
            total += written;
            if (written > 0 && firstOutputInputFrames == 0) firstOutputInputFrames = consumed;
            largestPending = Math.Max(largestPending, src.PendingOutputFrames);
        }
        int count;
        while ((count = src.Process(input, 0, 0, output, out _, end: true)) > 0) total += count;
        timer.Stop();
        Assert.Equal(rate * factor, total);
        Assert.Equal(0, src.PendingOutputFrames);
        // Bound signal lookahead rather than wall-clock execution time, which varies
        // with runner load. Detect an accidental return to libsoxr's large FFT default.
        Assert.InRange(firstOutputInputFrames, 1, rate / 20);
        Assert.InRange(largestPending / (rate * factor), 0, .05);
        log.WriteLine($"UltraHigh {rate} x{factor}, stereo: first output after {firstOutputInputFrames * 1000.0 / rate:F3} ms of input; max pending {largestPending * 1000 / (rate * factor):F3} ms; 1s audio processed in {timer.Elapsed.TotalMilliseconds:F3} ms ({src.Engine})");
    }
}
