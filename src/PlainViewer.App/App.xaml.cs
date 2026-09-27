using System.Windows;
namespace PlainViewer.App;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Remove private work folders left behind if the app or a conversion was ended abruptly.
        _ = Task.Run(() => { try { OfficeConverter.CleanLeftovers(); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { } });
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
    }
}
