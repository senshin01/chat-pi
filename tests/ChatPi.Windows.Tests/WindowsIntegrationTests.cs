using ChatPi.App.Audio;
using ChatPi.Core.Audio;
using NAudio.Wave;

namespace ChatPi.Windows.Tests;

public sealed class WindowsIntegrationTests
{
    [Fact]
    public void RealMp3DecodesToNonSilentPcmAtItsOriginalRate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.mp3");
        using var stream = new WindowsAudioSourceFactory().Open(path);
        var provider = NativePcmPipeline.Prepare(stream);
        Assert.Equal(44100, provider.WaveFormat.SampleRate);
        Assert.Equal(2, provider.WaveFormat.Channels);
        using var decoded = new MemoryStream();
        var buffer = new byte[provider.WaveFormat.BlockAlign * 1024];
        int count;
        while ((count = provider.Read(buffer, 0, buffer.Length)) != 0) decoded.Write(buffer, 0, count);
        Assert.InRange(decoded.Length, 44100 * 4 / 2, 44100 * 4);
        Assert.Contains(decoded.ToArray(), value => value != 0);
        stream.CurrentTime = TimeSpan.FromMilliseconds(300);
        Assert.InRange(stream.CurrentTime.TotalMilliseconds, 299, 301);
        Assert.True(provider.Read(buffer, 0, buffer.Length) > 0);
        stream.CurrentTime = TimeSpan.Zero;
        Assert.True(provider.Read(buffer, 0, buffer.Length) > 0);
    }

}
