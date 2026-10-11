using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ChatPi.Core;

namespace ChatPi.App.Services;

public static class ThemeManager
{
    public static void Apply(ResourceDictionary resources, PlayerTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/ChatPi;component/Themes/{theme}.xaml", UriKind.Relative)
        };
        // Keep styles and unrelated resources; replace only the active palette.
        var previous = resources.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("/Themes/", StringComparison.Ordinal) == true);
        if (previous is null) resources.MergedDictionaries.Insert(0, palette);
        else resources.MergedDictionaries[resources.MergedDictionaries.IndexOf(previous)] = palette;
    }

    public static void ApplyWindowTheme(Window window, PlayerTheme theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = theme == PlayerTheme.Dark ? 1 : 0;
        // Windows 10 / 11 own the caption; older builds use attribute 19.
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
