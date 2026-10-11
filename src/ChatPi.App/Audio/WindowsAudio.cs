using System.Runtime.InteropServices;
using ChatPi.Core;
using ChatPi.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace ChatPi.App.Audio;

public sealed record AudioDevice(string? Id, string Name);

public sealed class WindowsAudioSourceFactory : IAudioSourceFactory
{
    public WaveStream Open(string path)
    {
        path = AudioFiles.ValidatePath(path);
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".wav" => new WaveFileReader(path),
                ".mp3" => new Mp3FileReader(path),
                ".aiff" or ".aif" => new AiffPcmWaveStream(path),
                ".ogg" or ".oga" => new VorbisWaveStream(path),
                ".flac" or ".m4a" or ".aac" or ".wma" => new NativeMediaFoundationWaveStream(path),
                _ => throw new NotSupportedException("未対応の音声形式です。")
            };
        }
        catch (COMException exception)
        {
            throw new NotSupportedException("Windows の音声デコーダーでこのファイルを開けません。破損・DRM・未対応コーデックを確認してください。Windows N では Media Feature Pack が必要です。", exception);
        }
    }
}

public sealed class WindowsAudioBackendFactory : IPlaybackBackendFactory
{
    public static IReadOnlyList<AudioDevice> GetWasapiDevices()
    {
        var devices = new List<AudioDevice> { new(null, "Windows の既定の出力デバイス") };
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in endpoints)
        {
            using (device) devices.Add(new(device.ID, device.FriendlyName));
        }
        return devices;
    }

    public static string[] GetAsioDrivers() => AsioOut.GetDriverNames();

    public PlaybackBackend Create(IWaveProvider source, PlayerSettings settings) => settings.OutputMode == OutputMode.Asio
        ? CreateAsio(source, settings) : CreateWasapi(source, settings);

    private static PlaybackBackend CreateWasapi(IWaveProvider source, PlayerSettings settings)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        try
        {
            device = string.IsNullOrEmpty(settings.WasapiDeviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(settings.WasapiDeviceId);
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException("出力デバイスが見つかりません。設定で接続中のデバイスを選び直してください。", exception);
        }

        WasapiOut? player = null;
        try
        {
            var exclusive = settings.OutputMode == OutputMode.WasapiExclusive;
            if (device.State != DeviceState.Active)
                throw new InvalidOperationException("選択した出力デバイスは接続されていません。");
            if (exclusive)
            {
                // Reject unsupported formats before NAudio can choose a resampling fallback.
                var client = device.AudioClient;
                if (!client.IsFormatSupported(AudioClientShareMode.Exclusive, source.WaveFormat))
                {
                    var equivalent = DeviceFormats.AsExtensible(source.WaveFormat);
                    if (!client.IsFormatSupported(AudioClientShareMode.Exclusive, equivalent))
                        throw new NotSupportedException($"デバイスは {source.WaveFormat.SampleRate:N0} Hz / {source.WaveFormat.BitsPerSample} bit を排他モードで再生できません。別のデバイス／ASIO、または WASAPI 共有モードを選択してください。初期版では自動リサンプリングを行いません。");
                    source = new FormatOverrideProvider(source, equivalent);
                }
            }

            player = new WasapiOut(device, exclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared,
                useEventSync: true, settings.LatencyMilliseconds);
            player.Init(source);
            if (exclusive && !DeviceFormats.AsStandard(player.OutputWaveFormat).Equals(DeviceFormats.AsStandard(source.WaveFormat)))
                throw new InvalidOperationException("デバイスの形式が初期化時に変更されたため、再生を中止しました。");
            var outputFormat = exclusive ? player.OutputWaveFormat : device.AudioClient.MixFormat;
            return new PlaybackBackend(player, outputFormat, device.FriendlyName, device);
        }
        catch (COMException exception)
        {
            player?.Dispose();
            device.Dispose();
            throw new InvalidOperationException("音声デバイスを開けません。ほかのアプリの排他再生、デバイスの接続、Windows の「排他モードを許可する」設定を確認してください。", exception);
        }
        catch
        {
            player?.Dispose();
            device.Dispose();
            throw;
        }
    }

    private static PlaybackBackend CreateAsio(IWaveProvider source, PlayerSettings settings)
    {
        if (!GetAsioDrivers().Contains(settings.AsioDriverName, StringComparer.Ordinal))
            throw new InvalidOperationException("選択した ASIO ドライバーがありません。メーカー提供の 64 bit ASIO ドライバーをインストールし、設定で選択してください。");
        AsioDriverExt? driver = null;
        try
        {
            var basicDriver = AsioDriver.GetAsioDriverByName(settings.AsioDriverName!);
            try { driver = new AsioDriverExt(basicDriver); }
            catch { basicDriver.ReleaseComAsioDriver(); throw; }
            if (!driver.IsSampleRateSupported(source.WaveFormat.SampleRate))
                throw new NotSupportedException($"ASIO ドライバーは {source.WaveFormat.SampleRate:N0} Hz に対応していません。初期版では自動リサンプリングを行いません。");
            if (settings.AsioChannelOffset + source.WaveFormat.Channels > driver.Capabilities.NbOutputChannels)
                throw new InvalidOperationException("指定した ASIO 出力チャンネルがありません。設定の開始チャンネルを確認してください。");
            if (driver.Capabilities.SampleRate != source.WaveFormat.SampleRate)
                driver.SetSampleRate(source.WaveFormat.SampleRate);
            var player = new AsioPlaybackOutput(driver, source, settings.AsioChannelOffset);
            return new PlaybackBackend(player, player.OutputWaveFormat, settings.AsioDriverName!);
        }
        catch
        {
            driver?.ReleaseDriver();
            throw;
        }
    }
}
