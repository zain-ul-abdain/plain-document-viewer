using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PlainViewer.Core;
namespace PlainViewer.App;

// Hosts PDF.js in a locked-down WebView2. PDF.js parses the PDF inside WebView2's sandboxed renderer
// process; this class only supplies the bytes and relays messages. Every network request is refused.
internal sealed class PdfView : Border
{
    private const string AppHost = "app.plainviewer.invalid";
    private const string DocumentHost = "doc.plainviewer.invalid";
    private static Task<CoreWebView2Environment>? environment;
    private readonly WebView2 web = new();
    private byte[]? bytes;
    private TaskCompletionSource<int>? opening;

    public int Page { get; private set; }
    public int Pages { get; private set; }
    public double Scale { get; private set; } = 1;
    public int BlockedRequests { get; private set; }
    public event Action? StateChanged;
    public event Action<int, int, bool>? FindResult;          // current, total, finished
    public event Action<string>? LinkRequested;
    public Func<bool, string?>? AskPassword;                  // argument: previous password was wrong

    public PdfView()
    {
        Child = web;
        AutomationProperties.SetName(this, "PDF document");
        AutomationProperties.SetName(web, "PDF pages");
    }

    private static Task<CoreWebView2Environment> SharedEnvironment() => environment ??= CreateEnvironment();

    private static Task<CoreWebView2Environment> CreateEnvironment()
    {
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlainViewer", "WebView2");
        // Background services off; crash reports stay local. Document requests are blocked separately below.
        var options = new CoreWebView2EnvironmentOptions(
            "--disable-background-networking --disable-component-update --disable-domain-reliability --disable-sync --no-pings")
        { IsCustomCrashReportingEnabled = true };
        return CoreWebView2Environment.CreateAsync(null, data, options);
    }

    private async Task EnsureReady()
    {
        if (web.CoreWebView2 is not null) return;
        await web.EnsureCoreWebView2Async(await SharedEnvironment());
        var core = web.CoreWebView2 ?? throw new DocumentException("The PDF view could not start. Check that Microsoft Edge WebView2 Runtime is installed.");
        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsReputationCheckingRequired = false;
        settings.IsWebMessageEnabled = true;

        core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "Assets", "pdf"), CoreWebView2HostResourceAccessKind.Deny);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnResourceRequested;
        core.NavigationStarting += (_, e) => { if (!IsViewerPage(e.Uri)) e.Cancel = true; };
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.NewWindowRequested += (_, e) => e.Handled = true;          // no window is created
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.WebMessageReceived += OnMessage;
        core.ProcessFailed += (_, _) => opening?.TrySetException(new DocumentException("The PDF view stopped unexpectedly. Open the file again."));
    }

    private static bool IsViewerPage(string uri) => uri.StartsWith($"https://{AppHost}/viewer.html", StringComparison.Ordinal);

    private void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) { Block(e); return; }
        if (uri.Scheme == Uri.UriSchemeHttps && uri.Host == AppHost) return;   // served from the app's own folder
        if (uri.Scheme == Uri.UriSchemeHttps && uri.Host == DocumentHost && uri.AbsolutePath == "/document.pdf" && bytes is not null)
        {
            e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes, false), 200, "OK",
                $"Content-Type: application/pdf\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: https://{AppHost}");
            return;
        }
        Block(e);
    }

    private void Block(CoreWebView2WebResourceRequestedEventArgs e)
    {
        BlockedRequests++;
        e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
    }

    public async Task<int> Load(byte[] data, bool dark, CancellationToken cancellation)
    {
        bytes = data;
        await EnsureReady();
        opening = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => opening.TrySetCanceled(cancellation));
        web.CoreWebView2.Navigate($"https://{AppHost}/viewer.html?theme={(dark ? "dark" : "light")}");
        try { return await opening.Task; }
        catch { web.CoreWebView2?.Stop(); throw; }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith($"https://{AppHost}/", StringComparison.Ordinal)) return;
        using var json = JsonDocument.Parse(e.WebMessageAsJson);
        var message = json.RootElement;
        string type = message.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        switch (type)
        {
            case "loaded":
                Pages = message.GetProperty("pages").GetInt32(); Page = 1;
                opening?.TrySetResult(Pages);
                break;
            case "state":
                Page = message.GetProperty("page").GetInt32(); Pages = message.GetProperty("pages").GetInt32();
                Scale = message.GetProperty("scale").GetDouble();
                StateChanged?.Invoke();
                break;
            case "find":
                FindResult?.Invoke(message.GetProperty("current").GetInt32(), message.GetProperty("total").GetInt32(), message.GetProperty("done").GetBoolean());
                break;
            case "link":
                LinkRequested?.Invoke(message.GetProperty("href").GetString() ?? "");
                break;
            case "password":
                string? password = AskPassword?.Invoke(message.GetProperty("incorrect").GetBoolean());
                Post(password is null ? new { type = "password-cancel" } : new { type = "password", value = password });
                break;
            case "error":
                opening?.TrySetException(new DocumentException(ErrorText(message.GetProperty("kind").GetString())));
                break;
        }
    }

    private static string ErrorText(string? kind) => kind switch
    {
        "password-cancelled" => "This PDF is password protected. Open it again and enter its password to view it.",
        "unavailable" => "The PDF could not be passed to the viewer. Open the file again.",
        _ => "This PDF is damaged or incomplete, so it cannot be shown. Try another copy of the file."
    };

    private void Post(object message)
    {
        if (web.CoreWebView2 is not null) web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    public void Find(string query, bool previous) => Post(new { type = "find", query, previous });
    public void Zoom(object value) => Post(new { type = "zoom", value });
    public void GoToPage(int number) => Post(new { type = "page", number });
    public void SetTheme(bool dark) => Post(new { type = "theme", dark });
    public void FocusDocument() { web.Focus(); Post(new { type = "focus" }); }
}
