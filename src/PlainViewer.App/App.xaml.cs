using System.Windows;
namespace PlainViewer.App;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Remove private work folders left behind if the app or a conversion was ended abruptly.
        _ = Task.Run(() => { try { OfficeConverter.CleanLeftovers(); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { } });
        // Run by the installer: builds the Word/PowerPoint converter's profile so the first open is fast. Exit code 0 = ready.
        if (e.Args.FirstOrDefault() == "--prepare-converter")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await PrewarmConverter() ? 0 : 1);
            return;
        }
        if (e.Args.FirstOrDefault() == "--smoke-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                foreach (var file in e.Args.Skip(1))
                {
                    var preview = new MainWindow();
                    // A leading "!" means the file must be refused with a clear message (damaged, hostile, protected...).
                    if (file.StartsWith('!'))
                    {
                        string message = await preview.VerifyRefusedAsync(file[1..]);
                        Console.WriteLine($"PASS refused {System.IO.Path.GetFileName(file[1..])}: {message}");
                    }
                    else
                    {
                        await preview.VerifyPreviewAsync(file);
                        Console.WriteLine("PASS native view and worker: " + System.IO.Path.GetFileName(file));
                    }
                    preview.Close();
                }
                Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Shutdown(1); }
            return;
        }
        var window = new MainWindow(); window.Show();
        if (e.Args.Length > 0) window.OpenPath(e.Args[0]);
        // Normally already done by the installer; repeats only if the profile is missing or LibreOffice changed.
        _ = PrewarmConverter();
    }

    private static async Task<bool> PrewarmConverter()
    {
        try { return await OfficeConverter.Prewarm(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlainViewer.Core.DocumentException) { return false; }
    }
}
