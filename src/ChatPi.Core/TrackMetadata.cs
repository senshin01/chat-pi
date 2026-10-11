namespace ChatPi.Core;

public sealed record TrackMetadata(string Title, string Artist, string Album, byte[]? Artwork)
{
    private const int MaximumArtworkBytes = 20 * 1024 * 1024;

    public static TrackMetadata Read(string path)
    {
        var fallback = new TrackMetadata(Path.GetFileNameWithoutExtension(path), "アーティスト不明", "アルバム不明", null);
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var picture = tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                ?? tag.Pictures.FirstOrDefault();
            var artwork = picture?.Data.Count is > 0 and <= MaximumArtworkBytes
                ? picture.Data.Data : null;
            return new TrackMetadata(
                string.IsNullOrWhiteSpace(tag.Title) ? fallback.Title : tag.Title,
                tag.Performers.Length == 0 ? fallback.Artist : string.Join(" / ", tag.Performers),
                string.IsNullOrWhiteSpace(tag.Album) ? fallback.Album : tag.Album,
                artwork);
        }
        catch (Exception exception) when (exception is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or ArgumentException)
        {
            // Bad or absent tags must not prevent decoding playable audio.
            return fallback;
        }
    }
}
