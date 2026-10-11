using System.Reflection;
using System.Runtime.InteropServices;

namespace ChatPi.Core.Audio;

/// <summary>libsoxr 0.1.3, 24-bit quality, linear phase, double input/output/calculation.</summary>
public sealed class SoxrResampler : IDisposable
{
    private IntPtr _handle;
    private readonly int _channels;
    static SoxrResampler() => NativeLibrary.SetDllImportResolver(typeof(SoxrResampler).Assembly, Resolve);
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path) => name == "chatpi_soxr"
        ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "soxr.dll" : "libsoxr.so")) : IntPtr.Zero;

    public SoxrResampler(int inputRate, int factor, int channels)
    {
        if (factor is not (2 or 4)) throw new ArgumentOutOfRangeException(nameof(factor));
        _channels = channels;
        var io = IoSpec(1, 1); // SOXR_FLOAT64_I
        var quality = QualitySpec(5, 16); // SOXR_24_BITQ | SOXR_DOUBLE_PRECISION
        var runtime = RuntimeSpec(1);
        runtime.MinDft = 8;
        runtime.LargeDft = 10; // Avoid large FFT batches for small stereo playback blocks.
        _handle = Create(inputRate, checked(inputRate * factor), (uint)channels, out var error, ref io, ref quality, ref runtime);
        if (error != IntPtr.Zero) { Dispose(); Check(error); }
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("アップサンプリングを初期化できません。");
    }

    public int Process(double[] input, int offsetFrames, int frames, double[] output, out int consumed, bool end = false)
    {
        var inPin = GCHandle.Alloc(input, GCHandleType.Pinned);
        var outPin = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            Check(ProcessNative(_handle, end ? IntPtr.Zero : inPin.AddrOfPinnedObject() + offsetFrames * _channels * sizeof(double),
                (nuint)frames, out var read, outPin.AddrOfPinnedObject(), (nuint)(output.Length / _channels), out var written));
            consumed = checked((int)read);
            return checked((int)written);
        }
        finally { inPin.Free(); outPin.Free(); }
    }
    public string Engine => Marshal.PtrToStringAnsi(EngineNative(_handle)) ?? "unknown";
    public double PendingOutputFrames => Delay(_handle);
    private static void Check(IntPtr error)
    {
        if (error != IntPtr.Zero) throw new InvalidOperationException($"アップサンプリング: {Marshal.PtrToStringAnsi(error)}");
    }
    public void Dispose() { if (_handle != IntPtr.Zero) { Delete(_handle); _handle = IntPtr.Zero; } }

    // unsigned long is 32-bit on Windows and 64-bit on Linux. The final ulong
    // occupies the same aligned struct slot on x64; only low flag bits are used.
    [StructLayout(LayoutKind.Sequential)]
    private struct Io { public int Input, Output; public double Scale; public IntPtr Reserved; public ulong Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Quality { public double Precision, Phase, Passband, Stopband; public IntPtr Reserved; public ulong Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Runtime { public uint MinDft, LargeDft, CoefSize, Threads; public IntPtr Reserved; public ulong Flags; }
    [DllImport("chatpi_soxr", EntryPoint = "soxr_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Create(double input, double output, uint channels, out IntPtr error, ref Io io, ref Quality quality, ref Runtime runtime);
    [DllImport("chatpi_soxr", EntryPoint = "soxr_io_spec", CallingConvention = CallingConvention.Cdecl)] private static extern Io IoSpec(int input, int output);
    [DllImport("chatpi_soxr", EntryPoint = "soxr_quality_spec", CallingConvention = CallingConvention.Cdecl)] private static extern Quality QualitySpec(nuint recipe, nuint flags);
    [DllImport("chatpi_soxr", EntryPoint = "soxr_runtime_spec", CallingConvention = CallingConvention.Cdecl)] private static extern Runtime RuntimeSpec(uint threads);
    [DllImport("chatpi_soxr", EntryPoint = "soxr_process", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ProcessNative(IntPtr handle, IntPtr input, nuint frames, out nuint read, IntPtr output, nuint capacity, out nuint written);
    [DllImport("chatpi_soxr", EntryPoint = "soxr_delete", CallingConvention = CallingConvention.Cdecl)] private static extern void Delete(IntPtr handle);
    [DllImport("chatpi_soxr", EntryPoint = "soxr_delay", CallingConvention = CallingConvention.Cdecl)] private static extern double Delay(IntPtr handle);
    [DllImport("chatpi_soxr", EntryPoint = "soxr_engine", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr EngineNative(IntPtr handle);
}
