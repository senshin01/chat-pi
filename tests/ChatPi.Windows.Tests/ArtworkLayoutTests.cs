using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatPi.App;
using ChatPi.App.Services;
using ChatPi.Core;

namespace ChatPi.Windows.Tests;

public sealed class ArtworkLayoutTests
{
    [Theory]
    [InlineData(320, 180, PlayerTheme.Dark)]
    [InlineData(180, 320, PlayerTheme.Dark)]
    [InlineData(180, 180, PlayerTheme.Dark)]
    [InlineData(320, 180, PlayerTheme.Light)]
    [InlineData(180, 320, PlayerTheme.Light)]
    [InlineData(180, 180, PlayerTheme.Light)]
    public void WideTallAndSquareArtworkRenderTheCenterWithoutStretching(int width, int height, PlayerTheme theme) => WpfTestHost.Run(() =>
    {
        var window = new MainWindow();
        try
        {
            ThemeManager.Apply(Application.Current.Resources, theme);
            var image = CreateArtwork(width, height);
            window.DataContext = new { Artwork = image, HasArtwork = true, ShowPlaceholder = false };
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000;
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Show();
            WpfTestHost.FlushBindings(); window.UpdateLayout();
            var artwork = (FrameworkElement)window.FindName("ArtworkDisplay");
            var rendered = new RenderTargetBitmap(180, 180, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(artwork);
            var pixels = new byte[180 * 180 * 4];
            rendered.CopyPixels(pixels, 180 * 4, 0);
            // The colored margins of a wide/tall source must be cropped equally.
            // A left/top crop, a fit with letterboxing, or distorted scaling fails.
            foreach (var (x, y) in new[] { (5, 5), (175, 5), (5, 175), (175, 175) })
                Assert.Equal(new byte[] { 0, 180, 30, 255 }, pixels.AsSpan((y * 180 + x) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels.AsSpan((90 * 180 + 90) * 4, 4).ToArray());
            Directory.CreateDirectory("TestResults/UI");
            Save(image, $"TestResults/UI/artwork-source-{width}x{height}.png");
            Save(rendered, $"TestResults/UI/artwork-centered-{width}x{height}-{theme}.png");
        }
        finally { window.Close(); }
    });

    private static BitmapSource CreateArtwork(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var side = Math.Min(width, height);
        var left = (width - side) / 2; var top = (height - side) / 2;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i + 3] = 255;
            if (x < left || y < top) pixels[i + 2] = 255;
            else if (x >= left + side || y >= top + side) pixels[i] = 255;
            else if (Math.Abs(x - width / 2) < 2 || Math.Abs(y - height / 2) < 2)
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
            else { pixels[i + 1] = 180; pixels[i + 2] = 30; }
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
