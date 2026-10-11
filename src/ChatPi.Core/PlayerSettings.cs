using System.Text.Json;
using System.Text.Json.Serialization;
using ChatPi.Core.Audio;

namespace ChatPi.Core;

public enum OutputMode { WasapiExclusive, WasapiShared, Asio }
public enum PlayerTheme { Dark, Light }

public sealed record PlayerSettings
{
    public OutputMode OutputMode { get; init; } = OutputMode.WasapiExclusive;
    public string? WasapiDeviceId { get; init; }
    public string? AsioDriverName { get; init; }
    public int LatencyMilliseconds { get; init; } = 100;
    public int AsioChannelOffset { get; init; }
    public bool RepeatEnabled { get; init; }
    public PlayerTheme Theme { get; init; } = PlayerTheme.Dark;
    public DspSettings Dsp { get; init; } = new();
    public int ReadAheadMilliseconds { get; init; } = 40;

    public void Validate()
    {
        if (Dsp is null) throw new ArgumentException("音声処理の設定が不正です。");
        Dsp.Validate();
        if (ReadAheadMilliseconds is < 20 or > 200) throw new ArgumentException("先読みは 20〜200 ms にしてください。");
        if (!Enum.IsDefined(OutputMode))
            throw new ArgumentException("出力モードが不正です。");
        if (!Enum.IsDefined(Theme))
            throw new ArgumentException("テーマが不正です。");
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
