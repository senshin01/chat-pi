using System.Globalization;

namespace ChatPi.App.Audio;

/// <summary>Bounded local metadata only. Never called by the real-time audio callback.</summary>
internal sealed class AudioDiagnostics(string path)
{
    internal const long MaximumBytes = 1024 * 1024;
    internal static AudioDiagnostics Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatPi", "audio.log"));

    public void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            if (File.Exists(path) && new FileInfo(path).Length >= MaximumBytes)
                File.Move(path, path + ".previous", overwrite: true);
            var version = typeof(AudioDiagnostics).Assembly.GetName().Version;
            File.AppendAllText(path, string.Create(CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} v={version} ASIO {message}{Environment.NewLine}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A locked/unwritable log must never prevent playback.
        }
    }
}
