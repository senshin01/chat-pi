using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatPi.Core;

public enum OutputMode { WasapiExclusive, WasapiShared, Asio }

public sealed record PlayerSettings
{
    public OutputMode OutputMode { get; init; } = OutputMode.WasapiExclusive;
    public string? WasapiDeviceId { get; init; }
    public string? AsioDriverName { get; init; }
    public int LatencyMilliseconds { get; init; } = 100;
    public int AsioChannelOffset { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(OutputMode))
            throw new ArgumentException("出力モードが不正です。");
        if (LatencyMilliseconds is < 20 or > 500)
            throw new ArgumentException("WASAPI バッファは 20〜500 ms にしてください。");
        if (AsioChannelOffset is < 0 or > 126)
            throw new ArgumentException("ASIO の開始チャンネルは 1〜127 にしてください。");
        if (OutputMode == OutputMode.Asio && string.IsNullOrWhiteSpace(AsioDriverName))
            throw new ArgumentException("ASIO ドライバーを選択してください。");
    }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PlayerSettings Load()
    {
        if (!File.Exists(path)) return new PlayerSettings();
        var settings = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("設定ファイルが空です。");
        settings.Validate();
        return settings;
    }

    public void Save(PlayerSettings settings)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
