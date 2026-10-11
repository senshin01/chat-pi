using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using ChatPi.Core;

namespace ChatPi.App.Services;

internal static class FileAssociations
{
    private const string LegacyProgId = "ChatPi.AudioFile";
    private const string CapabilitiesPath = @"Software\ChatPi\Capabilities";
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void Register()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルの場所を取得できません。");
        RegisterForExecutable(Registry.CurrentUser, executable);
        NotifyShell();
        // Windows owns the protected UserChoice key. Let the user choose their defaults.
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }

    internal static string ProgIdFor(string extension) => $"{LegacyProgId}{extension.ToUpperInvariant()}";

    internal static string LabelFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".aif" or ".aiff" => "AIFF",
        ".ogg" or ".oga" => "OGG",
        _ => extension.TrimStart('.').ToUpperInvariant()
    };

    internal static string IconPathFor(string executable, string extension) =>
        Path.Combine(Path.GetDirectoryName(executable)!, "Assets", "FileIcons", $"{LabelFor(extension)}.ico");

    // Accepting a registry root lets integration tests use isolated temporary keys.
    internal static void RegisterForExecutable(RegistryKey userRoot, string executable)
    {
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("ChatPi.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("発行した ChatPi.exe を起動してから登録してください。");
        foreach (var extension in AudioFiles.Extensions)
        {
            var iconPath = IconPathFor(executable, extension);
            if (!File.Exists(iconPath))
                throw new FileNotFoundException("形式別アイコンが見つかりません。ZIP をすべて展開してから登録してください。", iconPath);
        }

        // Keep the previous ProgID functional until Windows UserChoice is reassigned.
        RegisterProgram(userRoot, LegacyProgId, "Chat Pi audio file", $"\"{executable}\",0", executable);

        using var capabilities = userRoot.CreateSubKey(CapabilitiesPath);
        capabilities.SetValue("ApplicationName", "Chat Pi");
        capabilities.SetValue("ApplicationDescription", "WASAPI / ASIO music player");
        capabilities.SetValue("ApplicationIcon", $"\"{executable}\",0");
        using (var associations = capabilities.CreateSubKey("FileAssociations"))
        {
            foreach (var extension in AudioFiles.Extensions)
            {
                var progId = ProgIdFor(extension);
                RegisterProgram(userRoot, progId, $"Chat Pi {LabelFor(extension)} audio file",
                    $"\"{IconPathFor(executable, extension)}\",0", executable);
                associations.SetValue(extension, progId);
                using var openWith = userRoot.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
                openWith.DeleteValue(LegacyProgId, throwOnMissingValue: false);
            }
        }
        using var registered = userRoot.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue("Chat Pi", CapabilitiesPath);
    }

    public static void Unregister()
    {
        UnregisterFrom(Registry.CurrentUser);
        NotifyShell();
    }

    internal static void UnregisterFrom(RegistryKey userRoot)
    {
        userRoot.DeleteSubKeyTree($@"Software\Classes\{LegacyProgId}", throwOnMissingSubKey: false);
        userRoot.DeleteSubKeyTree(@"Software\ChatPi", throwOnMissingSubKey: false);
        using (var registered = userRoot.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            registered?.DeleteValue("Chat Pi", throwOnMissingValue: false);
        foreach (var extension in AudioFiles.Extensions)
        {
            var progId = ProgIdFor(extension);
            userRoot.DeleteSubKeyTree($@"Software\Classes\{progId}", throwOnMissingSubKey: false);
            using var openWith = userRoot.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(progId, throwOnMissingValue: false);
            openWith?.DeleteValue(LegacyProgId, throwOnMissingValue: false);
        }
    }

    private static void RegisterProgram(RegistryKey userRoot, string progId, string description, string iconReference, string executable)
    {
        using var program = userRoot.CreateSubKey($@"Software\Classes\{progId}");
        program.SetValue("", description);
        using (var icon = program.CreateSubKey("DefaultIcon")) icon.SetValue("", iconReference);
        using (var command = program.CreateSubKey(@"shell\open\command")) command.SetValue("", $"\"{executable}\" \"%1\"");
        using var application = program.CreateSubKey("Application");
        application.SetValue("ApplicationName", "Chat Pi");
        application.SetValue("ApplicationIcon", $"\"{executable}\",0");
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
}
