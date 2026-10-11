using System.Buffers.Binary;
using System.Diagnostics;
using ChatPi.Core.Audio;
using NAudio.Wave;
using Xunit.Abstractions;

namespace ChatPi.Core.Tests;

public sealed class DspTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(0, 12)] [InlineData(1, -12)] [InlineData(2, 6)] [InlineData(3, -6)]
    [InlineData(4, 12)] [InlineData(5, -12)] [InlineData(6, 6)] [InlineData(7, -6)]
    [InlineData(8, 12)] [InlineData(9, -12)] [InlineData(10, 6)] [InlineData(11, -6)]
    [InlineData(12, 12)] [InlineData(13, -12)] [InlineData(14, 6)]
    public void EveryBandProducesTheRequestedGainAtItsCenterWithoutStereoCrosstalk(int band, double gain)
    {
        const int rate = 44100;
        var gains = new double[15]; gains[band] = gain;
        var eq = new GraphicEqualizer(rate, 2, new DspSettings { EqualizerEnabled = true, AutoHeadroom = false, BandGainsDb = gains });
        var frequency = GraphicEqualizer.Frequencies[band];
        var signal = Enumerable.Range(0, rate * 2).SelectMany(i => new[] { .01 * Math.Sin(2 * Math.PI * frequency * i / rate), 0.0 }).ToArray();
        eq.Process(signal);
        var left = signal.Where((_, i) => i % 2 == 0).Skip(rate).ToArray();
        var measured = 20 * Math.Log10(Amplitude(left, frequency, rate) / .01);
        Assert.InRange(measured, gain - .03, gain + .03);
        Assert.All(signal.Where((_, i) => i % 2 == 1), x => Assert.Equal(0, x));
    }

    [Fact]
    public void CombinedBandBoostAndSrcMarginAreIncludedInReportedPreamp()
    {
        var gains = new double[15]; gains[7] = gains[8] = gains[9] = 12;
        var eq = new GraphicEqualizer(44100, 2, new DspSettings { EqualizerEnabled = true, UpsamplingFactor = 4, PreampDb = -2, BandGainsDb = gains });
        var peak = Enumerable.Range(0, 10000).Max(i => eq.ResponseDb(i * 2.205, 44100));
        Assert.True(peak > 12);
        Assert.InRange(eq.AppliedPreampDb, -peak - 5.02, -peak - 4.98);
        var flat = new GraphicEqualizer(44100, 2, new DspSettings());
        Assert.Equal(0, flat.AppliedPreampDb);
        Assert.Equal(-3, new GraphicEqualizer(44100, 2, new DspSettings { UpsamplingFactor = 2 }).AppliedPreampDb);
    }

    [Fact]
    public void LowRateAudioBypassesInvalidBandsAndRemainsFinite()
    {
        var eq = new GraphicEqualizer(8000, 2, new DspSettings { EqualizerEnabled = true, BandGainsDb = Enumerable.Repeat(12.0, 15).ToArray() });
        Assert.False(eq.ActiveBands[11]); Assert.False(eq.ActiveBands[14]);
        var samples = Enumerable.Repeat(.1, 16000).ToArray();
        eq.Process(samples);
        Assert.All(samples, x => Assert.True(double.IsFinite(x)));
    }

    [Theory]
    [InlineData(44100, 2, 10000)] [InlineData(44100, 4, 10000)] [InlineData(48000, 2, 10000)] [InlineData(48000, 4, 10000)]
    [InlineData(96000, 4, 10000)] [InlineData(44100, 4, 1000)] [InlineData(44100, 2, 20000)] [InlineData(48000, 4, 22000)]
    public void SoxrPreservesFrequencyDurationAndRejectsUpsamplingImages(int rate, int factor, double frequency)
    {
        var input = Enumerable.Range(0, rate).Select(i => .25 * Math.Sin(2 * Math.PI * frequency * i / rate)).ToArray();
        var timer = Stopwatch.StartNew();
        var output = Resample(input, rate, factor);
        timer.Stop();
        Assert.Equal(rate * factor, output.Length);
        var middle = output.Skip(output.Length / 4).Take(output.Length / 2).ToArray();
        var fundamental = Amplitude(middle, frequency, rate * factor);
        var image = Amplitude(middle, rate - frequency, rate * factor);
        var errorDb = 20 * Math.Log10(fundamental / .25);
        var imageDb = 20 * Math.Log10(Math.Max(1e-30, image) / fundamental);
        log.WriteLine($"{rate} x{factor}, {frequency} Hz: passband {errorDb:F7} dB, image {imageDb:F2} dB, 1s mono in {timer.Elapsed.TotalMilliseconds:F2} ms");
        Assert.InRange(errorDb, -.001, .001);
        Assert.True(imageDb < -120, $"Image rejection: {imageDb} dB");
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(257)] [InlineData(997)]
    public void SoxrFlushesShortTracksExactlyAndKeepsChannelsIndependent(int frames)
    {
        using var src = new SoxrResampler(44100, 4, 2);
        Assert.Contains("64", src.Engine);
        var input = Enumerable.Range(0, frames).SelectMany(i => new[] { i == frames / 2 ? .5 : 0, 0.0 }).ToArray();
        var output = new double[256 * 2];
        var result = new List<double>();
        var consumed = 0;
        while (consumed < frames)
        {
            var read = src.Process(input, consumed, frames - consumed, output, out var used);
            consumed += used; result.AddRange(output.Take(read * 2));
        }
        int written;
        while ((written = src.Process(input, 0, 0, output, out _, end: true)) > 0) result.AddRange(output.Take(written * 2));
        Assert.Equal(frames * 4 * 2, result.Count);
        Assert.All(result.Where((_, i) => i % 2 == 1), x => Assert.Equal(0, x));
        Assert.Contains(result, x => x > .1);
    }

    [Fact]
    public void QuantizationUsesEffectiveDeviceBitsAndDitherIsUnbiased()
    {
        var format = new WaveFormat(192000, 32, 2);
        var converter = new DspSampleConverter(format, 24, false);
        var bytes = new byte[20];
        converter.Convert(new[] { 0.0, .5, -.5, 2, -2 }, bytes);
        Assert.Equal(0x40000000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(-0x40000000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(0x7fffff00, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)));
        Assert.Equal(int.MinValue, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(2, converter.ClippedSamples);
        Assert.Equal(2, converter.Peak);
        var quantizer = new DspSampleConverter(new WaveFormat(44100, 16, 1), 16, true);
        var noise = new byte[200000];
        quantizer.Convert(new double[100000], noise);
        var values = Enumerable.Range(0, 100000).Select(i => (double)BinaryPrimitives.ReadInt16LittleEndian(noise.AsSpan(i * 2))).ToArray();
        Assert.InRange(values.Average(), -.01, .01);
        Assert.InRange(values.Average(v => v * v), .23, .27);
        Assert.All(values, x => Assert.InRange(x, -1, 1));
        Assert.Throws<InvalidDataException>(() => converter.Convert(new[] { double.NaN }, bytes));
    }

    [Theory]
    [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(4, true)]
    public void BufferedPipelineRetainsTimelineAndCanRestartAfterSeekAndRepeat(int factor, bool eq)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference24.wav");
        using var stream = new WaveFileReader(path);
        var settings = new PlayerSettings { Dsp = new DspSettings { EqualizerEnabled = eq, UpsamplingFactor = factor, DitherEnabled = false } };
        using (var source = new BufferedAudioSource(stream, settings))
        {
            source.Start();
            Assert.Equal(TimeSpan.Zero, source.Position);
            Assert.True(stream.Position > 0);
            var data = Render(source);
            Assert.Equal(stream.Length / stream.WaveFormat.BlockAlign * factor * source.WaveFormat.BlockAlign, data.Length);
            Assert.True(source.EndOfStream);
            Assert.Equal(0, source.Underruns);
            if (!settings.Dsp.Enabled)
            {
                stream.Position = 0;
                var original = new byte[stream.Length]; stream.ReadExactly(original);
                Assert.Equal(original, data);
            }
        }
        stream.CurrentTime = TimeSpan.FromMilliseconds(250);
        using (var source = new BufferedAudioSource(stream, settings))
        {
            source.Start();
            Assert.Equal(.25, source.Position.TotalSeconds);
            var data = Render(source);
            Assert.Equal((int)(.25 * source.WaveFormat.SampleRate) * source.WaveFormat.BlockAlign, data.Length);
            Assert.Equal(.5, source.Position.TotalSeconds);
        }
        stream.Position = 0;
        using var repeated = new BufferedAudioSource(stream, settings);
        repeated.Start(); Assert.NotEmpty(Render(repeated));
    }

    [Fact]
    public async Task CallbackDoesNotReadOrWaitForDecoderAndStarvationDoesNotEndPlayback()
    {
        using var stream = new BlockingStream();
        using var source = new BufferedAudioSource(stream, new PlayerSettings { ReadAheadMilliseconds = 20 });
        source.Start();
        Assert.True(stream.Blocked.Wait(TimeSpan.FromSeconds(2)));
        var buffer = new byte[256 * source.WaveFormat.BlockAlign];
        Assert.Equal(buffer.Length, source.Read(buffer, 0, buffer.Length));
        var position = source.Position;
        try
        {
            var callback = Task.Run(() => source.Read(buffer, 0, buffer.Length));
            Assert.Equal(buffer.Length, await callback.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.All(buffer, x => Assert.Equal(0, x));
            Assert.Equal(position, source.Position);
            Assert.False(source.EndOfStream);
            Assert.Equal(1, source.Underruns);
            Assert.NotEqual(Environment.CurrentManagedThreadId, stream.ReadThread);
        }
        finally { stream.Release.Set(); }
    }

    [Fact]
    public void DspSettingsRoundTripAndOldSettingsDefaultToNativePlayback()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<PlayerSettings>("{}");
        Assert.False(old!.Dsp.Enabled); Assert.Equal(40, old.ReadAheadMilliseconds);
        var settings = new PlayerSettings { Dsp = new DspSettings { EqualizerEnabled = true, UpsamplingFactor = 4, PreampDb = -2, BandGainsDb = Enumerable.Range(0, 15).Select(i => i - 7.0).ToArray() } };
        var loaded = System.Text.Json.JsonSerializer.Deserialize<PlayerSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
        Assert.True(settings.Dsp.SameAs(loaded!.Dsp)); loaded.Validate();
        Assert.Throws<ArgumentException>(() => (settings with { Dsp = settings.Dsp with { BandGainsDb = [double.NaN] } }).Validate());
        Assert.Throws<ArgumentException>(() => (settings with { Dsp = settings.Dsp with { UpsamplingFactor = 3 } }).Validate());
    }

    [Fact]
    public void EqualizerUpdatesWithoutRestartAndAllBandProcessingStaysStableAtHighOutputRate()
    {
        using var stream = new WaveFileReader(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference24.wav"));
        var original = new DspSettings { EqualizerEnabled = true, UpsamplingFactor = 4 };
        using var source = new BufferedAudioSource(stream, new PlayerSettings { Dsp = original });
        var timer = Stopwatch.StartNew();
        source.Start();
        var prepared = timer.Elapsed.TotalMilliseconds;
        Assert.True(source.TryUpdateEqualizer(original with { BandGainsDb = Enumerable.Repeat(12.0, 15).ToArray(), PreampDb = -1 }));
        var data = Render(source);
        log.WriteLine($"96 kHz stereo, x4 and 15 boosted bands: startup {prepared:F2} ms, 0.5s offline render {timer.Elapsed.TotalMilliseconds:F2} ms, buffer {source.BufferMilliseconds} ms");
        Assert.True(source.AppliedPreampDb < -16);
        Assert.Equal(0, source.ClippedSamples);
        Assert.Equal(0, source.Underruns);
        Assert.NotEmpty(data);
        Assert.False(source.TryUpdateEqualizer(original with { UpsamplingFactor = 2 }));
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
    private static double[] Resample(double[] input, int rate, int factor)
    {
        using var src = new SoxrResampler(rate, factor, 1);
        var result = new List<double>();
        var buffer = new double[1024];
        var offset = 0;
        while (offset < input.Length)
        {
            var written = src.Process(input, offset, Math.Min(257, input.Length - offset), buffer, out var used);
            result.AddRange(buffer.Take(written)); offset += used;
        }
        int count;
        while ((count = src.Process(input, 0, 0, buffer, out _, end: true)) > 0) result.AddRange(buffer.Take(count));
        return result.ToArray();
    }
    private static double Amplitude(double[] signal, double frequency, int rate)
    {
        double re = 0, im = 0;
        for (var i = 0; i < signal.Length; i++) { var angle = 2 * Math.PI * frequency * i / rate; re += signal[i] * Math.Cos(angle); im += signal[i] * Math.Sin(angle); }
        return 2 * Math.Sqrt(re * re + im * im) / signal.Length;
    }
    private sealed class BlockingStream : WaveStream
    {
        public ManualResetEventSlim Blocked { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public int ReadThread { get; private set; }
        public override WaveFormat WaveFormat { get; } = new(8000, 16, 2);
        public override long Length => 100000;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadThread = Environment.CurrentManagedThreadId;
            if (Position > 0) { Blocked.Set(); Release.Wait(); return 0; }
            Array.Fill(buffer, (byte)42, offset, count); Position += count; return count;
        }
        protected override void Dispose(bool disposing) { if (disposing) { Blocked.Dispose(); Release.Dispose(); } base.Dispose(disposing); }
    }
}
