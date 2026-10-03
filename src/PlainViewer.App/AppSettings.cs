using System.IO;
using System.Text.Json;
using System.Windows;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PlainViewer.App.Tests")]
namespace PlainViewer.App;

// The viewer's own preferences: theme choice and the last window size, position and state. Stored in
// %LOCALAPPDATA%\PlainViewer\settings.json (removed on uninstall). Never holds document names or contents.
// Only interactive windows use it (App enables it); test and measurement modes keep the defaults.
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Maximized { get; set; }
    public bool Thumbnails { get; set; }           // page thumbnails beside PDF and Word documents

    public static bool Enabled { get; set; }
    // Settable, and Reload, for tests only (tests/PlainViewer.App.Tests).
    internal static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlainViewer", "settings.json");
    private static AppSettings? current;
    internal static void Reload() => current = null;

    public static AppSettings Current => current ??= Load();

    private static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public static void Save()
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current));
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // preferences are optional
    }

    // Saved bounds, if at least part of the window (100 × 50) would be on a screen that is still connected.
    public Rect? Bounds(double minWidth, double minHeight)
    {
        if (Left is not { } left || Top is not { } top || Width is not { } width || Height is not { } height) return null;
        if (!double.IsFinite(left + top + width + height)) return null;
        var bounds = new Rect(left, top, Math.Max(width, minWidth), Math.Max(height, minHeight));
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(bounds, screen);
        return visible.IsEmpty || visible.Width < 100 || visible.Height < 50 ? null : bounds;
    }
}
