using System.Diagnostics;
using ChatPi.Core.Audio;
using NAudio.Wave;
using Xunit.Abstractions;

namespace ChatPi.Core.Tests;

public sealed class SuperSlowTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(2, 1)] [InlineData(4, 1)] [InlineData(2, 2)] [InlineData(4, 2)]
    public void ArbitraryInputAndOutputBlocksPreserveEveryFrameAndChannel(int factor, int channels)
    {
        foreach (var frames in new[] { 0, 1, 2, 257, 997 })
        {
            var input = Enumerable.Range(0, frames).SelectMany(i => Enumerable.Range(0, channels)
                .Select(ch => ch == 0 ? Math.Sin(i * .71) : .5 * Math.Cos(i * .37))).ToArray();
            var whole = Resample(input, factor, channels, Math.Max(1, frames), Math.Max(1, frames * factor));
            var fragmented = Resample(input, factor, channels, 7, 3);
            Assert.Equal(frames * factor * channels, fragmented.Length);
            Assert.Equal(whole, fragmented);
            for (var frame = 0; frame < frames; frame++)
                for (var ch = 0; ch < channels; ch++)
                {
                    Assert.Equal(input[frame * channels + ch], fragmented[frame * factor * channels + ch]);
                    var next = input[Math.Min(frame + 1, frames - 1) * channels + ch];
                    for (var phase = 0; phase < factor; phase++)
                        Assert.InRange(fragmented[(frame * factor + phase) * channels + ch],
                            Math.Min(input[frame * channels + ch], next), Math.Max(input[frame * channels + ch], next));
                }
            if (frames > 0)
                for (var phase = 0; phase < factor; phase++)
                    Assert.Equal(input.AsSpan(input.Length - channels).ToArray(), fragmented.AsSpan(fragmented.Length - (phase + 1) * channels, channels).ToArray());
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(4)]
    public void ImpulseHasOnlyPositiveTriangularSupportAndStepNeverRings(int factor)
    {
        var impulse = new double[64 * 2]; impulse[32 * 2] = 1;
        var output = Resample(impulse, factor, 2, 1, 1);
        var peak = 32 * factor;
        for (var i = 0; i < output.Length / 2; i++)
        {
            Assert.Equal(Math.Max(0, 1 - Math.Abs(i - peak) / (double)factor), output[i * 2]);
            Assert.Equal(0, output[i * 2 + 1]);
        }
        Assert.Equal(factor, output.Sum());
        var step = Enumerable.Range(0, 64).Select(i => i < 32 ? 0.0 : 1).ToArray();
        var response = Resample(step, factor, 1, 1, 1);
        Assert.All(response, value => Assert.InRange(value, 0, 1));
        Assert.True(response.Zip(response.Skip(1), (a, b) => a <= b).All(x => x));
        var dc = Resample(Enumerable.Repeat(-.5, 64).ToArray(), factor, 1, 7, 3);
        Assert.All(dc, value => Assert.Equal(-.5, value));
        log.WriteLine($"x{factor}: impulse support {2 * factor - 1} output samples, no negative lobes; step overshoot 0");
    }

    [Theory]
    [InlineData(2)] [InlineData(4)]
    public void OnlyOneInputFrameOfLookaheadIsRequiredAndDrainIsExact(int factor)
    {
        using var resampler = new SuperSlowResampler(factor, 1);
        var output = new double[factor];
        Assert.Equal(0, resampler.Process([.25], 0, 1, output, out var first)); Assert.Equal(1, first);
        Assert.Equal(factor, resampler.Process([.75], 0, 1, output, out var second)); Assert.Equal(1, second);
        Assert.Equal(.25, output[0]);
        Assert.Equal(factor, resampler.Process([], 0, 0, output, out _, end: true));
        Assert.All(output, value => Assert.Equal(.75, value));
        Assert.Equal(0, resampler.Process([], 0, 0, output, out _, end: true));
        Assert.Throws<InvalidOperationException>(() => resampler.Process([1.0], 0, 1, output, out _));
    }

    [Theory]
    [InlineData(44100, 2, 1000)] [InlineData(44100, 4, 1000)]
    [InlineData(44100, 2, 10000)] [InlineData(44100, 4, 10000)]
    [InlineData(44100, 2, 20000)] [InlineData(44100, 4, 20000)]
    [InlineData(48000, 2, 22000)] [InlineData(48000, 4, 22000)]
    public void RolloffAndFirstImageMatchFirstOrderHoldResponse(int rate, int factor, double frequency)
    {
        var input = Enumerable.Range(0, rate).Select(i => .25 * Math.Sin(2 * Math.PI * frequency * i / rate)).ToArray();
        var timer = Stopwatch.StartNew();
        var output = Resample(input, factor, 1, 257, 1024);
        timer.Stop();
        var middle = output.Skip(output.Length / 4).Take(output.Length / 2).ToArray();
        var gain = Amplitude(middle, frequency, rate * factor) / .25;
        var image = Amplitude(middle, rate - frequency, rate * factor) / .25;
        var expected = Response(frequency);
        var expectedImage = Response(rate - frequency);
        Assert.InRange(gain, expected - 1e-10, expected + 1e-10);
        Assert.InRange(image, expectedImage - 1e-10, expectedImage + 1e-10);
        Assert.Equal(rate * factor, output.Length);
        log.WriteLine($"{rate} x{factor}, {frequency} Hz: amplitude {20 * Math.Log10(gain):F5} dB, first image {20 * Math.Log10(image / gain):F5} dBc; 1s mono render {timer.Elapsed.TotalMilliseconds:F2} ms");

        double Response(double f) => Math.Pow(Math.Sin(Math.PI * f / rate) / (factor * Math.Sin(Math.PI * f / (rate * factor))), 2);
    }

    [Fact]
    public void SteadyProcessingDoesNotAllocateAndInvalidBuffersAreRejected()
    {
        using var resampler = new SuperSlowResampler(4, 2);
        var input = new double[512]; var output = new double[2048];
        // Warm all streaming paths and runtime initialization before measuring.
        // Capture the second counter before invoking any test assertion helpers.
        for (var i = 0; i < 100; i++) resampler.Process(input, 0, 256, output, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) resampler.Process(input, 0, 256, output, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Throws<ArgumentOutOfRangeException>(() => resampler.Process(input, -1, 256, output, out _));
        Assert.Throws<ArgumentException>(() => resampler.Process(input, 1, 256, output, out _));
        Assert.Throws<ArgumentException>(() => resampler.Process(input, 0, 256, new double[1], out _));
        Assert.Throws<ArgumentException>(() => resampler.Process(input, 0, 256, output, out _, end: true));
        resampler.Dispose();
        Assert.Throws<ObjectDisposedException>(() => resampler.Process(input, 0, 256, output, out _));
    }

    [Theory]
    [InlineData(2)] [InlineData(4)]
    public void BufferedSuperSlowPreservesSeekTimelineAndIntegerSamples(int factor)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference24.wav");
        var settings = new PlayerSettings { Dsp = new DspSettings { Filter = UpsamplingFilter.SuperSlow, UpsamplingFactor = factor, AutoHeadroom = false, DitherEnabled = false } };
        using var stream = new WaveFileReader(path);
        var original = new byte[stream.Length]; stream.ReadExactly(original);
        var decoded = new double[original.Length / 3]; DspSampleConverter.Decode(original, decoded, stream.WaveFormat);
        var samples = Resample(decoded, factor, 2, 257, 3);
        var expected = new byte[samples.Length * 4];
        new DspSampleConverter(new WaveFormat(96000 * factor, 32, 2), 32, false).Convert(samples, expected);
        foreach (var start in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(250) })
        {
            stream.CurrentTime = start;
            using var source = new BufferedAudioSource(stream, settings);
            source.Start();
            Assert.Equal(start, source.Position);
            var actual = Render(source);
            Assert.Equal(expected.AsSpan((int)(start.TotalSeconds * source.WaveFormat.SampleRate) * source.WaveFormat.BlockAlign).ToArray(), actual);
            Assert.Equal(.5, source.Position.TotalSeconds);
            Assert.Equal(0, source.Underruns); Assert.Equal(0, source.ClippedSamples);
        }
    }

    [Fact]
    public void FilterIsPersistedDefaultsToStandardAndDoesNotEnableDspByItself()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<PlayerSettings>("{\"Dsp\":{\"UpsamplingFactor\":4}}");
        Assert.Equal(UpsamplingFilter.Standard, old!.Dsp.Filter);
        var selected = old with { Dsp = old.Dsp with { Filter = UpsamplingFilter.SuperSlow } };
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<PlayerSettings>(System.Text.Json.JsonSerializer.Serialize(selected));
        Assert.Equal(selected, reloaded);
        Assert.NotEqual(old, selected);
        Assert.False(new DspSettings { Filter = UpsamplingFilter.SuperSlow }.Enabled);
        Assert.Throws<ArgumentException>(() => (selected.Dsp with { Filter = (UpsamplingFilter)123 }).Validate());
    }

    [Fact]
    public void SelectingFilterWithSrcOffPreservesNativeBytesAndActiveSrcRequiresRestart()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference24.wav");
        using var stream = new WaveFileReader(path);
        var original = new byte[stream.Length]; stream.ReadExactly(original); stream.Position = 0;
        using (var source = new BufferedAudioSource(stream, new PlayerSettings { Dsp = new DspSettings { Filter = UpsamplingFilter.SuperSlow, PreampDb = -5 } }))
        {
            source.Start(); Assert.Equal(original, Render(source)); Assert.Equal(0, source.AppliedPreampDb);
            Assert.True(source.TryUpdateEqualizer(new DspSettings()));
        }
        stream.Position = 0;
        var settings = new DspSettings { Filter = UpsamplingFilter.SuperSlow, UpsamplingFactor = 4, EqualizerEnabled = true };
        using var active = new BufferedAudioSource(stream, new PlayerSettings { Dsp = settings });
        active.Start();
        Assert.False(active.TryUpdateEqualizer(settings with { Filter = UpsamplingFilter.Standard }));
        var gains = new double[15]; gains[8] = 6;
        Assert.True(active.TryUpdateEqualizer(settings with { BandGainsDb = gains, PreampDb = -1 }));
        Assert.NotEmpty(Render(active));
        Assert.InRange(active.AppliedPreampDb, -10.02, -9.98);
        Assert.Equal(0, active.ClippedSamples);
    }

    private static byte[] Render(BufferedAudioSource source)
    {
        using var result = new MemoryStream();
        var buffer = new byte[257 * source.WaveFormat.BlockAlign];
        while (true)
        {
            source.WaitForData(buffer.Length);
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }

    private static double[] Resample(double[] input, int factor, int channels, int inputBlock, int outputBlock)
    {
        using var resampler = new SuperSlowResampler(factor, channels);
        var output = new double[outputBlock * channels];
        var result = new List<double>();
        var position = 0;
        while (position < input.Length / channels)
        {
            var frames = Math.Min(inputBlock, input.Length / channels - position);
            var usedInBlock = 0;
            // Nonzero offset and sentinels exercise frame offsets independently of chunk size.
            var block = new double[(frames + 4) * channels];
            input.AsSpan(position * channels, frames * channels).CopyTo(block.AsSpan(2 * channels));
            while (usedInBlock < frames)
            {
                var count = resampler.Process(block, 2 + usedInBlock, frames - usedInBlock, output, out var used);
                Assert.True(count > 0 || used > 0);
                result.AddRange(output.Take(count * channels)); usedInBlock += used;
            }
            position += frames;
        }
        int written;
        while ((written = resampler.Process([], 0, 0, output, out _, end: true)) > 0)
            result.AddRange(output.Take(written * channels));
        return result.ToArray();
    }

    private static double Amplitude(double[] signal, double frequency, int rate)
    {
        double re = 0, im = 0;
        for (var i = 0; i < signal.Length; i++) { var phase = 2 * Math.PI * frequency * i / rate; re += signal[i] * Math.Cos(phase); im += signal[i] * Math.Sin(phase); }
        return 2 * Math.Sqrt(re * re + im * im) / signal.Length;
    }
}
