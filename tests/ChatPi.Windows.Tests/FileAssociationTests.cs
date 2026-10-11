using ChatPi.App.Services;
using ChatPi.Core;
using Microsoft.Win32;
using System.Windows.Media.Imaging;

namespace ChatPi.Windows.Tests;

public sealed class FileAssociationTests : IDisposable
{
    private readonly string _keyPath = $@"Software\ChatPi.Tests\{Guid.NewGuid():N}";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Chat Pi アイコン {Guid.NewGuid():N}");
    private readonly RegistryKey _root;
    private string Executable => Path.Combine(_directory, "ChatPi.exe");

    public FileAssociationTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_keyPath);
        Directory.CreateDirectory(Path.Combine(_directory, "Assets", "FileIcons"));
        File.WriteAllBytes(Executable, [0]);
        foreach (var icon in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "FileIcons"), "*.ico"))
            File.Copy(icon, Path.Combine(_directory, "Assets", "FileIcons", Path.GetFileName(icon)));
    }

    [Fact]
    public void RegistrationMigratesToDistinctTypesWhileKeepingExistingDefaultsAndLegacyLaunchWorking()
    {
        SeedExistingDefaults();
        FileAssociations.RegisterForExecutable(_root, Executable);
        // Re-registration updates existing registrations without producing more types.
        FileAssociations.RegisterForExecutable(_root, Executable);
        using var capabilities = _root.OpenSubKey(@"Software\ChatPi\Capabilities\FileAssociations")!;
        var progIds = new HashSet<string>();
        var iconPaths = new HashSet<string>();
        foreach (var extension in AudioFiles.Extensions)
        {
            var progId = Assert.IsType<string>(capabilities.GetValue(extension));
            Assert.True(progIds.Add(progId));
            using var program = _root.OpenSubKey($@"Software\Classes\{progId}")!;
            using var command = program.OpenSubKey(@"shell\open\command")!;
            Assert.Equal($"\"{Executable}\" \"%1\"", command.GetValue(""));
            using var icon = program.OpenSubKey("DefaultIcon")!;
            var reference = Assert.IsType<string>(icon.GetValue(""));
            Assert.StartsWith("\"", reference);
            Assert.EndsWith("\",0", reference);
            var path = reference[1..^3];
            Assert.True(File.Exists(path), path);
            iconPaths.Add(path);
            using var openWith = _root.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids")!;
            Assert.NotNull(openWith.GetValue(progId));
            Assert.Null(openWith.GetValue("ChatPi.AudioFile"));
        }
        Assert.Equal(8, iconPaths.Count); // AIF/AIFF and OGA/OGG share their badges.
        using var legacy = _root.OpenSubKey(@"Software\Classes\ChatPi.AudioFile\shell\open\command")!;
        Assert.Equal($"\"{Executable}\" \"%1\"", legacy.GetValue(""));
        AssertExistingDefaultsAreUntouched();
    }

    [Fact]
    public void UnregisterRemovesOldAndNewTypesAndPreservesOtherApplications()
    {
        SeedExistingDefaults();
        FileAssociations.RegisterForExecutable(_root, Executable);
        // A leftover OpenWith entry from an earlier installation is also removed.
        using (var openWith = _root.OpenSubKey(@"Software\Classes\.mp3\OpenWithProgids", writable: true)!)
            openWith.SetValue("ChatPi.AudioFile", Array.Empty<byte>(), RegistryValueKind.None);
        FileAssociations.UnregisterFrom(_root);
        FileAssociations.UnregisterFrom(_root);
        Assert.Null(_root.OpenSubKey(@"Software\Classes\ChatPi.AudioFile"));
        Assert.Null(_root.OpenSubKey(@"Software\ChatPi"));
        using var registered = _root.OpenSubKey(@"Software\RegisteredApplications")!;
        Assert.Null(registered.GetValue("Chat Pi"));
        Assert.Equal("OtherCapabilities", registered.GetValue("Other Player"));
        foreach (var extension in AudioFiles.Extensions)
        {
            var progId = FileAssociations.ProgIdFor(extension);
            Assert.Null(_root.OpenSubKey($@"Software\Classes\{progId}"));
            using var openWith = _root.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids")!;
            Assert.Null(openWith.GetValue(progId));
            Assert.Null(openWith.GetValue("ChatPi.AudioFile"));
            Assert.NotNull(openWith.GetValue("Other.AudioFile"));
        }
        AssertExistingDefaultsAreUntouched();
    }

    [Fact]
    public void MissingIconFailsBeforeAnyRegistryValuesAreWritten()
    {
        File.Delete(Path.Combine(_directory, "Assets", "FileIcons", "WMA.ico"));
        Assert.Throws<FileNotFoundException>(() => FileAssociations.RegisterForExecutable(_root, Executable));
        Assert.Empty(_root.GetSubKeyNames());
    }

    [Fact]
    public void ShippedIconsDecodeAtExplorerAndHighDpiSizes()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "FileIcons"), "*.ico"))
        {
            using var input = File.OpenRead(path);
            var decoder = new IconBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, decoder.Frames.Select(frame => frame.PixelWidth).Order());
            Assert.All(decoder.Frames, frame => Assert.Equal(frame.PixelWidth, frame.PixelHeight));
        }
    }

    private void SeedExistingDefaults()
    {
        using var registered = _root.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue("Other Player", "OtherCapabilities");
        foreach (var extension in AudioFiles.Extensions)
        {
            using var fileType = _root.CreateSubKey($@"Software\Classes\{extension}");
            fileType.SetValue("", "Other.AudioFile");
            using var openWith = fileType.CreateSubKey("OpenWithProgids");
            openWith.SetValue("ChatPi.AudioFile", Array.Empty<byte>(), RegistryValueKind.None);
            openWith.SetValue("Other.AudioFile", Array.Empty<byte>(), RegistryValueKind.None);
            using var userChoice = _root.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");
            userChoice.SetValue("ProgId", "ChatPi.AudioFile");
            userChoice.SetValue("Hash", "ExistingProtectedChoice");
        }
    }

    private void AssertExistingDefaultsAreUntouched()
    {
        foreach (var extension in AudioFiles.Extensions)
        {
            using var fileType = _root.OpenSubKey($@"Software\Classes\{extension}")!;
            Assert.Equal("Other.AudioFile", fileType.GetValue(""));
            using var userChoice = _root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice")!;
            Assert.Equal("ChatPi.AudioFile", userChoice.GetValue("ProgId"));
            Assert.Equal("ExistingProtectedChoice", userChoice.GetValue("Hash"));
        }
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        Directory.Delete(_directory, recursive: true);
    }
}
