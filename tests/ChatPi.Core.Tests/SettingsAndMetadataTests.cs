using NAudio.Wave;

namespace ChatPi.Core.Tests;

public sealed class SettingsAndMetadataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"chatpi-settings-{Guid.NewGuid():N}");
    public SettingsAndMetadataTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void SettingsRoundTripAndAtomicOverwritePreserveDeviceSelection()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);
        Assert.Equal(OutputMode.WasapiExclusive, store.Load().OutputMode);
        var settings = new PlayerSettings { OutputMode = OutputMode.Asio, AsioDriverName = "Example ASIO", AsioChannelOffset = 2, WasapiDeviceId = "device-id", RepeatEnabled = true, Theme = PlayerTheme.Light };
        store.Save(settings);
        Assert.Equal(settings, store.Load());
        store.Save(settings with { LatencyMilliseconds = 200 });
        Assert.Equal(200, store.Load().LatencyMilliseconds);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void InvalidSettingsCannotOverwriteLastWorkingSettings()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        store.Save(new PlayerSettings());
        Assert.Throws<ArgumentException>(() => store.Save(new PlayerSettings { LatencyMilliseconds = 0 }));
        Assert.Equal(100, store.Load().LatencyMilliseconds);
        Assert.Throws<ArgumentException>(() => store.Save(new PlayerSettings { OutputMode = OutputMode.Asio }));
        Assert.Throws<ArgumentException>(() => store.Save(new PlayerSettings { Theme = (PlayerTheme)99 }));
        Assert.Equal(PlayerTheme.Dark, store.Load().Theme);
    }

    [Fact]
    public void ExistingSettingsLoadWithRepeatOffAndDarkThemeWithoutLosingAsioSelection()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """
            { "OutputMode": "Asio", "AsioDriverName": "Existing ASIO", "AsioChannelOffset": 2, "LatencyMilliseconds": 100 }
            """);
        var store = new SettingsStore(path);
        var settings = store.Load();
        Assert.False(settings.RepeatEnabled);
        Assert.Equal(PlayerTheme.Dark, settings.Theme);
        Assert.Equal("Existing ASIO", settings.AsioDriverName);
        store.Save(settings with { Theme = PlayerTheme.Light, RepeatEnabled = true });
        var reloaded = new SettingsStore(path).Load();
        Assert.True(reloaded.RepeatEnabled);
        Assert.Equal(PlayerTheme.Light, reloaded.Theme);
        Assert.Equal(settings.OutputMode, reloaded.OutputMode);
        Assert.Equal(settings.AsioDriverName, reloaded.AsioDriverName);
        Assert.Equal(settings.AsioChannelOffset, reloaded.AsioChannelOffset);
    }

    [Fact]
    public void TaglessWavFallsBackToFilename()
    {
        var path = Path.Combine(_directory, "タイトル 日本語.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2))) writer.Write(new byte[400], 0, 400);
        var metadata = TrackMetadata.Read(path);
        Assert.Equal("タイトル 日本語", metadata.Title);
        Assert.Equal("アーティスト不明", metadata.Artist);
        Assert.Null(metadata.Artwork);
    }

    [Fact]
    public void EmbeddedTitleArtistAlbumAndArtworkAreRead()
    {
        var path = Path.Combine(_directory, "tagged.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2))) writer.Write(new byte[400], 0, 400);
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = "Song";
            file.Tag.Performers = ["Artist"];
            file.Tag.Album = "Album";
            file.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(picture)) { Type = TagLib.PictureType.FrontCover }];
            file.Save();
        }
        var metadata = TrackMetadata.Read(path);
        Assert.Equal("Song", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Album", metadata.Album);
        Assert.Equal(picture, metadata.Artwork);
    }

    [Theory]
    [InlineData("SONG.MP3", true)]
    [InlineData("音楽.WaV", true)]
    [InlineData("song.flac", false)]
    [InlineData("song.mp3.exe", false)]
    public void FileSupportUsesExactCaseInsensitiveExtensions(string name, bool expected) =>
        Assert.Equal(expected, AudioFiles.IsSupported(name));

    [Fact]
    public void ShellLaunchSelectsOneFileWithoutAPlaylist() =>
        Assert.Equal("a.wav", AudioFiles.FirstFileArgument(["", "a.wav", "b.mp3"]));

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
