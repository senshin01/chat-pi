namespace ChatPi.Core;

public static class AudioFiles
{
    public static bool IsSupported(string path) =>
        Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase);

    public static string ValidatePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsSupported(fullPath))
            throw new NotSupportedException("初期版で再生できるのは MP3 と WAV です。");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("音楽ファイルが見つかりません。", fullPath);
        return fullPath;
    }

    // Multiple shell arguments never create a queue or playlist: use the first file.
    public static string? FirstFileArgument(IEnumerable<string> arguments) =>
        arguments.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
}
