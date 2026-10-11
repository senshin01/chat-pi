using ChatPi.App.Audio;

namespace ChatPi.Windows.Tests;

public sealed class AudioDiagnosticsTests
{
    [Fact]
    public void LogRotatesToOnePreviousFileAndIncludesVersionAndCounters()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chatpi-audio-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "audio.log");
            File.WriteAllBytes(path, new byte[AudioDiagnostics.MaximumBytes]);
            File.WriteAllText(path + ".previous", "older");
            var log = new AudioDiagnostics(path);
            log.Write("callbacks=10 nonzeroBuffers=8");
            var text = File.ReadAllText(path);
            Assert.Contains($"v={typeof(AudioDiagnostics).Assembly.GetName().Version}", text);
            Assert.Contains("ASIO callbacks=10 nonzeroBuffers=8", text);
            Assert.Equal(AudioDiagnostics.MaximumBytes, new FileInfo(path + ".previous").Length);
            Assert.True(new FileInfo(path).Length < 1024);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void LockedLogDoesNotPreventPlaybackInitialization()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chatpi-audio-log-{Guid.NewGuid():N}.log");
        try
        {
            using (File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                new AudioDiagnostics(path).Write("started");
            Assert.Equal(0, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }
}
