namespace ChatPi.Core;

public static class AudioFiles
{
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[]
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".aiff", ".aif", ".ogg", ".oga", ".wma" });
    public const string FormatSummary = "MP3 / WAV / FLAC / ALAC / AAC / AIFF / Ogg Vorbis / WMA";
    public static string FileDialogFilter => $"音楽ファイル|{string.Join(';', Extensions.Select(extension => $"*{extension}"))}";

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string ValidatePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsSupported(fullPath))
            throw new NotSupportedException($"対応形式は {FormatSummary} です。DSD は未対応です。");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("音楽ファイルが見つかりません。", fullPath);
        return fullPath;
    }

    // Multiple shell arguments never create a queue or playlist: use the first file.
    public static string? FirstFileArgument(IEnumerable<string> arguments) =>
        arguments.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
}
