using NAudio.Wave.Asio;

namespace ChatPi.App.Audio;

internal interface IAsioDriverSession : IDisposable
{
    double SampleRate { get; }
    int OutputChannels { get; }
    AsioSampleType GetOutputType(int channel);
    bool SupportsRate(int rate);
    void SetRate(int rate);
    int CreateBuffers(int channels, int offset);
    AsioFillBufferCallback? FillBuffer { set; }
    Action? ResetRequest { set; }
    void Start();
    void Stop();
}

internal sealed class AsioDriverSession : IAsioDriverSession
{
    private readonly AsioDriverExt _driver;
    private bool _hasBuffers;
    private bool _disposed;

    public AsioDriverSession(string name)
    {
        var basic = AsioDriver.GetAsioDriverByName(name);
        try { _driver = new AsioDriverExt(basic); }
        catch { basic.ReleaseComAsioDriver(); throw; }
    }

    // Read the driver itself, rather than displaying the requested rate or cached capabilities.
    public double SampleRate => _driver.Driver.GetSampleRate();
    public int OutputChannels
    {
        get { _driver.Driver.GetChannels(out _, out var outputs); return outputs; }
    }
    public AsioSampleType GetOutputType(int channel) => _driver.Driver.GetChannelInfo(channel, false).type;
    public bool SupportsRate(int rate) => _driver.IsSampleRateSupported(rate);
    public void SetRate(int rate) => _driver.SetSampleRate(rate);
    public int CreateBuffers(int channels, int offset)
    {
        var frames = _driver.CreateBuffers(channels, 0, useMaxBufferSize: false);
        _hasBuffers = true;
        _driver.SetChannelOffset(offset, 0);
        return frames;
    }
    public AsioFillBufferCallback? FillBuffer { set => _driver.FillBufferCallback = value; }
    public Action? ResetRequest { set => _driver.ResetRequestCallback = value; }
    public void Start() => _driver.Start();
    public void Stop() => _driver.Stop();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _driver.FillBufferCallback = null;
        _driver.ResetRequestCallback = null;
        // A rate-setting session has no buffers to dispose. Release its COM instance directly.
        if (_hasBuffers) _driver.ReleaseDriver();
        else _driver.Driver.ReleaseComAsioDriver();
    }

    public static IAsioDriverSession Open(Func<IAsioDriverSession> create, int rate, int offset, int channels,
        Action<string> diagnostic)
    {
        IAsioDriverSession? driver = null;
        try
        {
            driver = create();
            var previousRate = driver.SampleRate;
            diagnostic($"open rate={previousRate} requested={rate}");
            Validate(driver, rate, offset, channels);
            if (previousRate != rate)
            {
                driver.SetRate(rate);
                VerifyRate(driver, rate);
                // Some USB drivers retain a stream configured for the previous clock.
                // Select the new clock without buffers, release, then open the playback session.
                driver.Dispose();
                driver = null;
                driver = create();
                diagnostic($"reopened after rate change; rate={driver.SampleRate}");
                Validate(driver, rate, offset, channels);
                // Drivers which restore a default rate on init still get the requested rate.
                if (driver.SampleRate != rate) driver.SetRate(rate);
            }
            VerifyRate(driver, rate);
            return driver;
        }
        catch
        {
            driver?.Dispose();
            throw;
        }
    }

    private static void Validate(IAsioDriverSession driver, int rate, int offset, int channels)
    {
        if (!driver.SupportsRate(rate))
            throw new NotSupportedException($"ASIO ドライバーは {rate:N0} Hz に対応していません。アップサンプリングの倍率を下げるか、別のドライバーを選択してください。");
        if (offset < 0 || offset + channels > driver.OutputChannels)
            throw new InvalidOperationException("指定した ASIO 出力チャンネルがありません。設定の開始チャンネルを確認してください。");
    }

    internal static void VerifyRate(IAsioDriverSession driver, int rate)
    {
        var actual = driver.SampleRate;
        if (actual != rate)
            throw new InvalidOperationException($"ASIO の出力レートを確認できません。要求 {rate:N0} Hz / 実際 {actual:N0} Hz。ドライバーの設定と機器の接続を確認してください。");
    }
}
