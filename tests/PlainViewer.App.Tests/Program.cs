using System.IO;
using System.Text.Json;
using System.Windows;
using PlainViewer.App;

// Tests of the app's own code that run without opening a window, against the real app assembly. The settings file is
// redirected to a temporary folder, so the user's own settings are never read or changed.
int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; }
}
void Check(bool value, string what) { if (!value) throw new Exception(what); }

string root = Path.Combine(Path.GetTempPath(), "PlainViewerAppTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string settings = Path.Combine(root, "PlainViewer", "settings.json");
AppSettings.FilePath = settings;
AppSettings Fresh() { AppSettings.Reload(); return AppSettings.Current; }

Test("Settings: no file gives the defaults", () =>
{
    var current = Fresh();
    Check(current.Theme == "System" && current.Left is null && !current.Maximized && !current.Thumbnails, "not the defaults");
});
Test("Settings: a saved file is read back", () =>
{
    Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
    File.WriteAllText(settings, """{"Theme":"Dark","Left":10,"Top":20,"Width":900,"Height":700,"Maximized":true,"Thumbnails":true}""");
    var current = Fresh();
    Check(current.Theme == "Dark" && current.Left == 10 && current.Width == 900 && current.Maximized && current.Thumbnails, "values not read");
});
Test("Settings: a damaged file gives the defaults", () =>
{
    File.WriteAllText(settings, "{ not json");
    Check(Fresh().Theme == "System", "damaged file not ignored");
});
Test("Settings: nothing is written unless saving is enabled (test and measurement modes)", () =>
{
    File.Delete(settings);
    AppSettings.Enabled = false;
    Fresh().Theme = "Light";
    AppSettings.Save();
    Check(!File.Exists(settings), "file written while disabled");
});
Test("Settings: saving writes the file in one step and leaves no temporary file", () =>
{
    AppSettings.Enabled = true;
    try
    {
        var current = Fresh();
        current.Theme = "Light"; current.Width = 1000; current.Thumbnails = true;
        AppSettings.Save();
        var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settings))!;
        Check(saved.Theme == "Light" && saved.Width == 1000 && saved.Thumbnails, "values not saved");
        Check(!File.Exists(settings + ".tmp"), "temporary file left");
        Check(!File.ReadAllText(settings).Contains(root, StringComparison.OrdinalIgnoreCase), "a path was saved");
    }
    finally { AppSettings.Enabled = false; }
});
Test("Settings: saved window bounds are used only while enough of the window is on a screen", () =>
{
    var onScreen = new AppSettings { Left = SystemParameters.VirtualScreenLeft + 10, Top = SystemParameters.VirtualScreenTop + 10, Width = 200, Height = 100 };
    Check(onScreen.Bounds(600, 400) is { Width: 600, Height: 400 }, "bounds on screen not used (or the minimum size not applied)");
    var offScreen = new AppSettings { Left = SystemParameters.VirtualScreenLeft - 100_000, Top = 0, Width = 800, Height = 600 };
    Check(offScreen.Bounds(600, 400) is null, "bounds off every screen used");
    Check(new AppSettings { Left = 0, Top = 0, Width = 800 }.Bounds(600, 400) is null, "incomplete bounds used");
    Check(new AppSettings { Left = double.NaN, Top = 0, Width = 800, Height = 600 }.Bounds(600, 400) is null, "invalid bounds used");
});

try { Directory.Delete(root, true); } catch (IOException) { }
Console.WriteLine($"App tests: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
