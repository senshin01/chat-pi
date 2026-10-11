using System.Buffers.Binary;
using ChatPi.App.Audio;
using ChatPi.Core;
using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Windows.Tests;

public sealed class FormatDecoderTests
{
    [Theory]
    [InlineData("lossless16.flac", "reference16.wav")]
    [InlineData("lossless24.flac", "reference24.wav")]
    [InlineData("alac16.m4a", "reference16.wav")]
    [InlineData("alac24.m4a", "reference24.wav")]
    [InlineData("pcm16.aiff", "reference16.wav")]
    [InlineData("pcm24.aiff", "reference24.wav")]
    public void LosslessFormatsPreserveAllSamplesIncluding24BitPrecisionAndSeekExactly(string file, string reference)
    {
        using var original = new WaveFileReader(Fixture(reference));
        using var stream = new WindowsAudioSourceFactory().Open(Fixture(file));
        Assert.Equal(original.WaveFormat.SampleRate, stream.WaveFormat.SampleRate);
        Assert.Equal(original.WaveFormat.Channels, stream.WaveFormat.Channels);
        Assert.Equal(WaveFormatEncoding.Pcm, stream.WaveFormat.Encoding);
        Assert.True(stream.WaveFormat.BitsPerSample >= original.WaveFormat.BitsPerSample);
        var expected = ExpandIntegerSamples(ReadAll(original), original.WaveFormat.BitsPerSample, stream.WaveFormat.BitsPerSample);
        Assert.Equal(expected, ReadAll(stream));
        stream.CurrentTime = TimeSpan.FromMilliseconds(250);
        var position = (int)stream.Position;
        Assert.Equal(expected.AsSpan(position).ToArray(), ReadAll(stream));
        stream.Position = 0;
        Assert.Equal(expected, ReadAll(stream));
    }

    [Theory]
    [InlineData("aac.m4a", 44100, 2)]
    [InlineData("aac.aac", 44100, 2)]
    [InlineData("wma.wma", 44100, 2)]
    [InlineData("vorbis.ogg", 44100, 2)]
    [InlineData("vorbis-mono.oga", 48000, 1)]
    public void CompressedFormatsDecodeAtTheirNativeRateAndCanSeekAndRewind(string file, int rate, int channels)
    {
        using var stream = new WindowsAudioSourceFactory().Open(Fixture(file));
        Assert.Equal(rate, stream.WaveFormat.SampleRate);
        Assert.Equal(channels, stream.WaveFormat.Channels);
        Assert.InRange(stream.TotalTime.TotalSeconds, 0.4, 0.65);
        var data = ReadAll(stream);
        Assert.Contains(data, value => value != 0);
        Assert.InRange((double)data.Length / stream.WaveFormat.AverageBytesPerSecond, 0.4, 0.65);
        stream.CurrentTime = TimeSpan.FromMilliseconds(250);
        Assert.InRange(stream.CurrentTime.TotalMilliseconds, 249, 251);
        Assert.NotEmpty(ReadAll(stream));
        stream.Position = 0;
        Assert.Equal(data, ReadAll(stream));
    }

    [Theory]
    [InlineData("lossless24.flac")]
    [InlineData("alac24.m4a")]
    [InlineData("aac.m4a")]
    [InlineData("aac.aac")]
    [InlineData("wma.wma")]
    [InlineData("vorbis.ogg")]
    [InlineData("pcm24.aiff")]
    public void NewFormatsRepeatAndKeepPauseStateDuringSeek(string file)
    {
        var callbacks = new Queue<Action>();
        var outputs = new TestOutputs();
        using var player = new PlaybackController(new WindowsAudioSourceFactory(), outputs, callbacks.Enqueue);
        player.Open(Fixture(file), new PlayerSettings { RepeatEnabled = true });
        var rate = player.SourceFormat!.SampleRate;
        outputs.Last!.ReadToEnd();
        while (callbacks.TryDequeue(out var callback)) callback();
        Assert.Equal(PlayerState.Playing, player.State);
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.Equal(rate, player.OutputFormat!.SampleRate);
        player.Pause();
        player.Seek(TimeSpan.FromMilliseconds(250));
        Assert.Equal(PlayerState.Paused, player.State);
        player.Play();
        Assert.Equal(PlayerState.Playing, player.State);
        player.Stop();
        Assert.Equal(PlayerState.Stopped, player.State);
    }

    [Fact]
    public void UnsupportedDsdAndCorruptFilesAreRejectedWithoutKeepingTheFileOpen()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chatpi-formats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var extension in new[] { ".flac", ".m4a", ".aac", ".ogg", ".aiff", ".wma", ".dsf", ".dff" })
            {
                var path = Path.Combine(directory, "invalid" + extension);
                File.WriteAllText(path, "Invalid audio");
                Assert.ThrowsAny<Exception>(() => new WindowsAudioSourceFactory().Open(path));
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] ReadAll(WaveStream stream)
    {
        using var data = new MemoryStream();
        var count = stream.WaveFormat.BlockAlign * 257;
        var buffer = new byte[count + 10];
        int read;
        while ((read = stream.Read(buffer, 5, count)) > 0) data.Write(buffer, 5, read);
        return data.ToArray();
    }

    private static byte[] ExpandIntegerSamples(byte[] source, int sourceBits, int targetBits)
    {
        var result = new byte[source.Length / (sourceBits / 8) * (targetBits / 8)];
        for (int input = 0, output = 0; input < source.Length; input += sourceBits / 8, output += targetBits / 8)
        {
            var sample = sourceBits == 16 ? BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(input))
                : (source[input] | source[input + 1] << 8 | source[input + 2] << 16) << 8 >> 8;
            sample <<= targetBits - sourceBits;
            for (var b = 0; b < targetBits / 8; b++) result[output + b] = (byte)(sample >> (b * 8));
        }
        return result;
    }

    private sealed class TestOutputs : IPlaybackBackendFactory
    {
        public TestPlayer? Last { get; private set; }
        public PlaybackBackend Create(IWaveProvider source, PlayerSettings settings)
        {
            Last = new TestPlayer(source);
            return new PlaybackBackend(Last, source.WaveFormat, "Test output");
        }
    }

    private sealed class TestPlayer(IWaveProvider source) : IWavePlayer
    {
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat => source.WaveFormat;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider provider) => throw new NotSupportedException();
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Dispose() { }
        public void ReadToEnd()
        {
            var buffer = new byte[source.WaveFormat.BlockAlign * 1024];
            while (source.Read(buffer, 0, buffer.Length) > 0) { }
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }
    }
}
