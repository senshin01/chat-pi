using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Core.Tests;

public sealed class PortableDecoderTests
{
    [Theory]
    [InlineData("pcm8.aif", "reference8.wav")]
    [InlineData("pcm16.aiff", "reference16.wav")]
    [InlineData("pcm24.aiff", "reference24.wav")]
    public void AiffPreservesAllPcmSamplesWithOffsetReadsAndSeeking(string input, string reference)
    {
        using var expected = new WaveFileReader(Fixture(reference));
        using var stream = new AiffPcmWaveStream(Fixture(input));
        Assert.Equal(expected.WaveFormat, stream.WaveFormat);
        Assert.Equal(expected.Length, stream.Length);
        var original = ReadAll(expected);
        Assert.Equal(original, ReadAll(stream));
        stream.CurrentTime = TimeSpan.FromMilliseconds(250);
        Assert.Equal(original.AsSpan((int)stream.Position).ToArray(), ReadAll(stream));
        stream.Position = 0;
        Assert.Equal(original, ReadAll(stream));
    }

    [Theory]
    [InlineData("vorbis.ogg", 44100, 2)]
    [InlineData("vorbis-mono.oga", 48000, 1)]
    [InlineData("vorbis-seek.ogg", 44100, 1)]
    public void VorbisDecodesAtNativeRateAndSeeksToTheSameSamples(string input, int rate, int channels)
    {
        using var stream = new VorbisWaveStream(Fixture(input));
        Assert.Equal(rate, stream.WaveFormat.SampleRate);
        Assert.Equal(channels, stream.WaveFormat.Channels);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, stream.WaveFormat.Encoding);
        var original = ReadAll(stream);
        Assert.Equal(stream.Length, original.Length);
        Assert.Contains(original, value => value != 0);
        // Seek backwards, forwards, within the final Ogg page and to exact EOF.
        foreach (var fraction in new[] { 0.5, 0.1, 0.95, 0.0, 0.999, 1.0 })
        {
            stream.Position = (long)(stream.Length * fraction);
            var position = (int)stream.Position;
            Assert.Equal(original.AsSpan(position).ToArray(), ReadAll(stream));
        }
    }

    [Fact]
    public void InvalidOrCompressedAiffReleasesItsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chatpi-invalid-{Guid.NewGuid():N}.aiff");
        try
        {
            var bytes = File.ReadAllBytes(Fixture("pcm16.aiff"));
            File.WriteAllBytes(path, bytes[..^10]);
            Assert.Throws<InvalidDataException>(() => new AiffPcmWaveStream(path));
            "AIFC"u8.CopyTo(bytes.AsSpan(8));
            File.WriteAllBytes(path, bytes);
            Assert.Throws<NotSupportedException>(() => new AiffPcmWaveStream(path));
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally { File.Delete(path); }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] ReadAll(WaveStream stream)
    {
        using var output = new MemoryStream();
        var count = stream.WaveFormat.BlockAlign * 257;
        var buffer = Enumerable.Repeat((byte)0xA5, count + 14).ToArray();
        int read;
        while ((read = stream.Read(buffer, 7, count)) > 0)
        {
            Assert.All(buffer.Take(7), value => Assert.Equal(0xA5, value));
            Assert.All(buffer.Skip(count + 7), value => Assert.Equal(0xA5, value));
            output.Write(buffer, 7, read);
        }
        return output.ToArray();
    }
}
