using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using ChatPi.Core;

namespace ChatPi.App.Services;

internal static class FileAssociations
{
    private const string ProgId = "ChatPi.AudioFile";
    private const string CapabilitiesPath = @"Software\ChatPi\Capabilities";
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void Register()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルの場所を取得できません。");
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("ChatPi.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("発行した ChatPi.exe を起動してから登録してください。");

        using var program = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
        program.SetValue("", "Chat Pi audio file");
        using (var icon = program.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{executable}\",0");
        using (var command = program.CreateSubKey(@"shell\open\command")) command.SetValue("", $"\"{executable}\" \"%1\"");

        using var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath);
        capabilities.SetValue("ApplicationName", "Chat Pi");
        capabilities.SetValue("ApplicationDescription", "WASAPI / ASIO music player");
        using (var associations = capabilities.CreateSubKey("FileAssociations"))
        {
            foreach (var extension in AudioFiles.Extensions)
            {
                associations.SetValue(extension, ProgId);
                using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
                openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        using var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue("Chat Pi", CapabilitiesPath);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        // Windows owns the protected UserChoice key. Let the user choose their defaults.
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\ChatPi", throwOnMissingSubKey: false);
        using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            registered?.DeleteValue("Chat Pi", throwOnMissingValue: false);
        foreach (var extension in AudioFiles.Extensions)
        {
            using var openWith = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
        }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
