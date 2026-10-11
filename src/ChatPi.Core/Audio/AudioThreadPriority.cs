using System.Runtime.InteropServices;

namespace ChatPi.Core.Audio;

/// <summary>Best-effort Windows multimedia scheduling on the producer's owning thread.</summary>
internal sealed class AudioThreadPriority : IDisposable
{
    private readonly IntPtr _handle;
    public AudioThreadPriority()
    {
        if (!OperatingSystem.IsWindows()) return;
        uint index = 0;
        _handle = Register("Pro Audio", ref index);
        if (_handle != IntPtr.Zero) _ = SetPriority(_handle, 1); // AVRT_PRIORITY_HIGH
    }
    public void Dispose() { if (_handle != IntPtr.Zero) _ = Revert(_handle); }
    [DllImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr Register(string task, ref uint index);
    [DllImport("avrt.dll", EntryPoint = "AvSetMmThreadPriority")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriority(IntPtr handle, int priority);
    [DllImport("avrt.dll", EntryPoint = "AvRevertMmThreadCharacteristics")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Revert(IntPtr handle);
}
