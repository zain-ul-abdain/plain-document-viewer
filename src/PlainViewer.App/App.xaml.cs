using System.Windows;
namespace PlainViewer.App;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--smoke-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                foreach (var file in e.Args.Skip(1))
                {
                    var preview = new MainWindow();
                    await preview.VerifyPreviewAsync(file);
                    Console.WriteLine("PASS native view and worker: " + System.IO.Path.GetFileName(file));
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
