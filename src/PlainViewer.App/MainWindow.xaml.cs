using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PlainViewer.Core;
namespace PlainViewer.App;

public partial class MainWindow : Window
{
    private DocumentView? document;
    private string? currentPath;
    private CancellationTokenSource? loading;
    // CSV and large text: rows on disk in a work folder written by the worker; deleted when replaced or closed.
    private RowStore? rowStore;
    private Dictionary<int, RowStore> sheetStores = [];   // large spreadsheet sheets, by sheet position
    private string? rowStoreFolder;
    private StoreSearch? storeSearch;
    private int storeMatch = -1;
    private double zoom = 1;
    private string lastQuery = "";
    private int matchIndex = -1;
    private WindowState savedState;
    private double openSeconds;
    public MainWindow()
    {
        InitializeComponent(); PreviewKeyDown += WindowKeyDown;
        Closed += (_, _) => { loading?.Cancel(); ReplaceRowStore(null, null, null); };
        // The status line is a live region: screen readers announce loading, errors and search results as they change.
        var statusText = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        EventHandler announce = (_, _) => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(Status)
            ?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        statusText.AddValueChanged(Status, announce);
        Closed += (_, _) => statusText.RemoveValueChanged(Status, announce);
        WebPane.StateChanged += ShowWebStatus;
        WebPane.FindResult += (current, total, finished) => { if (finished) Status.Text = total == 0 ? "No matches." : $"Match {Math.Max(current, 1)} of {total}."; };
        WebPane.LinkRequested += address =>
        {
            if (LinkPolicy.CanOpen(address)) OpenLink(address);
            else Status.Text = "This link was not opened because it is not a web or email address.";
        };
        WebPane.AskPassword = AskPdfPassword;
    }
    private static string Choice(ComboBox box) => ((ComboBoxItem)box.SelectedItem).Content.ToString()!;
    private static bool IsPdf(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    private static bool IsWorkbook(string path) => Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xltx" or ".xltm" or ".xlsb";
    private static bool IsOffice(string path) => OfficePackages.IsOfficeDocument(path);
    private static bool UsesWebPane(string path) => IsPdf(path) || IsWorkbook(path) || IsOffice(path);
    private bool InWebPane => document?.Kind is "pdf" or "sheet" or "word" or "slides";
    private bool Paginated => document?.Kind is "pdf" or "word" or "slides";
    internal async Task<string> VerifyRefusedAsync(string path)
    {
        if (UsesWebPane(path))
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; ShowActivated = false; ShowInTaskbar = false; Show(); }
        currentPath = path; await LoadCurrent();
        if (document is not null) throw new InvalidOperationException($"Expected {Path.GetFileName(path)} to be refused, but it opened.");
        if (string.IsNullOrWhiteSpace(Status.Text) || Status.Text.StartsWith("The document could not open", StringComparison.Ordinal))
            throw new InvalidOperationException($"{Path.GetFileName(path)} was refused without a specific message: {Status.Text}");
        if (WebPane.BlockedRequests != 0) throw new InvalidOperationException($"The document view attempted {WebPane.BlockedRequests} blocked request(s).");
        return Status.Text;
    }
    internal async Task VerifyPreviewAsync(string path)
    {
        if (UsesWebPane(path) || Environment.GetEnvironmentVariable("PLAINVIEWER_CAPTURE_DIR") is { Length: > 0 })
        {
            // WebView2 needs a real window handle (and captures a drawn window), so the smoke test shows it off-screen.
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; ShowActivated = false; ShowInTaskbar = false; Show();
        }
        currentPath = path; await LoadCurrent();
        if (document is null) throw new InvalidOperationException(Status.Text);
        if (InWebPane)
        {
            if (WebPane.Pages < 1) throw new InvalidOperationException("The document reported no pages or sheets.");
            var (_, total) = await PdfFind("Hello");
            if (total < 1) throw new InvalidOperationException("Search found no match for 'Hello'.");
            ZoomBy(1); ZoomBy(0);
            if (Environment.GetEnvironmentVariable("PLAINVIEWER_CAPTURE_DIR") is { Length: > 0 } captures)
            {
                // Optional visual evidence for manual review: one PNG per document (and per sheet).
                Directory.CreateDirectory(captures); await Task.Delay(800);
                await WebPane.Capture(Path.Combine(captures, Path.GetFileName(path) + ".png"));
                for (int sheet = 2; document.Kind == "sheet" && sheet <= WebPane.Pages; sheet++)
                { WebPane.ChangeSheet(1); await Task.Delay(500); await WebPane.Capture(Path.Combine(captures, $"{Path.GetFileName(path)}.sheet{sheet}.png")); }
            }
            if (document.Kind == "sheet" && WebPane.Pages > 1) { WebPane.ChangeSheet(1); WebPane.ChangeSheet(-1); }
            if (WebPane.BlockedRequests != 0) throw new InvalidOperationException($"The document view attempted {WebPane.BlockedRequests} blocked request(s).");
            return;
        }
        Measure(new Size(1100, 760)); Arrange(new Rect(0, 0, 1100, 760)); UpdateLayout();
        if (document.Kind == "markdown" && MarkdownDisplay.Document.Blocks.Count == 0) throw new InvalidOperationException("No Markdown blocks rendered.");
        if (document.Kind is "csv" or "lines" && CsvGrid.Items.Count != (rowStore is null ? document.Rows.Count : document.RowCount)) throw new InvalidOperationException("Row count mismatch.");
        if (document.Kind == "text" && TextView.Text != document.Text) throw new InvalidOperationException("Text view mismatch.");
        FindBox.Text = "Hello"; Find(false);
        if (storeSearch is not null)
        {
            while (!storeSearch.Done) await Task.Delay(50);
            if (storeSearch.Error is not null) throw new InvalidOperationException(storeSearch.Error);
        }
        if (Environment.GetEnvironmentVariable("PLAINVIEWER_CAPTURE_DIR") is { Length: > 0 } captureFolder)
        {
            // Optional visual evidence for manual review, as for the web views.
            UpdateLayout(); Directory.CreateDirectory(captureFolder);
            // Drawn over the theme's window colour: the Fluent window itself has a transparent backdrop.
            var layer = new DrawingVisual();
            using (var context = layer.RenderOpen())
            {
                context.DrawRectangle(IsDarkTheme() ? Brushes.Black : Brushes.White, null, new Rect(0, 0, 1100, 760));
                context.DrawRectangle(new VisualBrush(this), null, new Rect(0, 0, ActualWidth, ActualHeight));
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1100, 760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(layer);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(captureFolder, Path.GetFileName(path) + ".png")); png.Save(file);
        }
        if (document.Kind == "markdown" && MarkdownDisplay.Selection.Text != "Hello") throw new InvalidOperationException("Rendered Markdown search selected the wrong text.");
        ChangeZoom(1.2);
        if (document.Kind == "markdown") { SourceToggle.IsChecked = true; if (TextView.Text != document.Text) throw new InvalidOperationException("Source view mismatch."); }
    }
    // Timing mode for scripts/measure.ps1: shows the window off-screen, opens the file, and reports milliseconds from
    // process start until the window is drawn, and from the start of opening until the first content is drawn
    // (for PDF, Word and PowerPoint: the first page; for workbooks: the first sheet), plus peak memory in MB.
    internal async Task<Dictionary<string, object?>> MeasureAsync(string? path, DateTime processStart)
    {
        var result = new Dictionary<string, object?>();
        WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; ShowActivated = false; ShowInTaskbar = false;
        var drawn = new TaskCompletionSource(); ContentRendered += (_, _) => drawn.TrySetResult();
        Show(); await drawn.Task;
        result["windowReadyMs"] = Math.Round((DateTime.Now - processStart).TotalMilliseconds);
        if (path is not null)
        {
            var firstPage = new TaskCompletionSource();
            WebPane.Rendered += () => firstPage.TrySetResult();
            var clock = Stopwatch.StartNew();
            currentPath = path; await LoadCurrent();
            if (document is null) { result["error"] = Status.Text; return result; }
            if (InWebPane) { if (await Task.WhenAny(firstPage.Task, Task.Delay(120_000)) != firstPage.Task) result["error"] = "No page was drawn within two minutes."; }
            else await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);   // after layout and drawing
            result["firstContentMs"] = Math.Round(clock.Elapsed.TotalMilliseconds);
        }
        const double MB = 1024 * 1024;
        result["appPeakMb"] = Math.Round(Process.GetCurrentProcess().PeakWorkingSet64 / MB);
        result["workerPeakMb"] = Math.Round(WorkerJob.LastPeakBytes / MB);
        result["converterPeakMb"] = Math.Round(OfficeConverter.LastPeakBytes / MB);
        result["webViewPeakMb"] = Math.Round(ProcessTree.PeakBytesOfDescendants(Environment.ProcessId) / MB);
        return result;
    }
    private void OpenClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open a document — development preview", Filter = "Preview formats|*.pdf;*.docx;*.xlsx;*.pptx;*.txt;*.csv;*.md;*.markdown", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) OpenPath(dialog.FileName);
    }
    public void OpenPath(string path)
    {
        if (currentPath is not null) { var next = new MainWindow(); next.Show(); next.OpenPath(path); return; }
        currentPath = path; _ = LoadCurrent();
    }
    private async Task LoadCurrent()
    {
        if (currentPath is null) return;
        loading?.Cancel(); var operation = new CancellationTokenSource(); loading = operation;
        CancelButton.IsEnabled = true; Progress.Visibility = Visibility.Visible; Status.Text = "Opening document…";
        var stopwatch = Stopwatch.StartNew();
        string? work = null; RowStore? opened = null; var openedSheets = new Dictionary<int, RowStore>();
        try
        {
            DocumentView loaded;
            if (IsPdf(currentPath)) loaded = await LoadPdf(currentPath, operation.Token);
            else if (IsOffice(currentPath)) loaded = await LoadOffice(currentPath, operation.Token);
            else
            {
                work = OfficeConverter.NewWorkFolder();
                loaded = await WorkerClient.Load(currentPath, Choice(EncodingChoice), Choice(DelimiterChoice), work, operation.Token);
                // Store names and counts come from the worker, so only the names it may use are accepted, and each
                // store must hold exactly the rows the worker reported.
                if (loaded.Store.Length > 0) opened = loaded.Store == "rows" ? RowStore.Open(work, "rows") : throw new InvalidDataException("Unexpected worker output.");
                if (opened is not null && opened.Count != loaded.RowCount) throw new InvalidDataException("Unexpected worker output.");
                for (int i = 0; i < loaded.Sheets.Count; i++)
                {
                    if (loaded.Sheets[i].Store is not { Length: > 0 } name) continue;
                    var sheetStore = openedSheets[i] = name == $"sheet{i}" ? RowStore.Open(work, name) : throw new InvalidDataException("Unexpected worker output.");
                    if (sheetStore.Count != loaded.Sheets[i].RowCount) throw new InvalidDataException("Unexpected worker output.");
                }
            }
            if (loading != operation) return;
            bool stores = opened is not null || openedSheets.Count > 0;
            ReplaceRowStore(opened, openedSheets, stores ? work : null);
            if (stores) { opened = null; openedSheets = []; work = null; }
            if (loaded.Kind == "sheet") await LoadSheets(loaded, operation.Token);
            if (loading != operation) return;
            document = loaded; Title = Path.GetFileName(currentPath) + " · Plain Viewer preview";
            openSeconds = stopwatch.Elapsed.TotalSeconds;
            string size = rowStore is null ? "" : $"{document.RowCount:N0} {(document.Kind == "lines" ? "lines" : "rows")} · ";
            Display(); Status.Text = $"Read only · {document.Encoding} · {size}Opened in {openSeconds:F2}s. {document.Notice}";
            if (InWebPane) ShowWebStatus();
        }
        catch (OperationCanceledException) { if (loading == operation) Status.Text = "Opening cancelled. Choose a file to try again."; }
        catch (Exception ex)
        {
            if (loading != operation) return;
            Status.Text = ex is DocumentException ? ex.Message : "The document could not open. Check the local SDK and worker build, then try again.";
        }
        finally
        {
            opened?.Dispose(); foreach (var store in openedSheets.Values) store.Dispose();
            if (work is not null) OfficeConverter.Delete(work);
            if (loading == operation) { CancelButton.IsEnabled = false; Progress.Visibility = Visibility.Collapsed; }
            operation.Dispose(); if (loading == operation) loading = null;
        }
    }
    private void ReplaceRowStore(RowStore? store, Dictionary<int, RowStore>? sheets, string? folder)
    {
        storeSearch?.Dispose(); storeSearch = null; storeMatch = -1;
        CsvGrid.ItemsSource = null;
        var previous = sheetStores; sheetStores = sheets ?? [];
        WebPane.Data = sheetStores.Count > 0 ? SheetRequest : null;
        rowStore?.Dispose(); foreach (var old in previous.Values) old.Dispose();
        if (rowStoreFolder is not null) OfficeConverter.Delete(rowStoreFolder);
        rowStore = store; rowStoreFolder = folder;
    }

    // Answers the spreadsheet page's requests for rows and searches of large sheets. Runs on a background thread.
    private byte[]? SheetRequest(string path, System.Collections.Specialized.NameValueCollection query)
    {
        if (!int.TryParse(query["sheet"], out int sheet) || !sheetStores.TryGetValue(sheet, out var store)) return null;
        if (path == "/rows")
        {
            int start = int.Parse(query["start"] ?? ""), count = int.Parse(query["count"] ?? "");
            if (count is < 0 or > 2000 || start < 0 || start > store.Count - count) return null;
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { start, rows = store.Read(start, count) });
        }
        string needle = query["q"] ?? "";
        if (needle.Length is 0 or > 1000) return null;
        var hits = new List<int[]>(); int total = 0;
        for (int start = 0; start < store.Count; start += 4096)
        {
            var rows = store.Read(start, Math.Min(4096, store.Count - start));
            for (int i = 0; i < rows.Length; i++)
                for (int c = 1; c < rows[i].Length; c++)                         // field 0 is the row's alignment
                    if (rows[i][c].Contains(needle, StringComparison.OrdinalIgnoreCase)) { total++; if (hits.Count < 10_000) hits.Add([start + i, c - 1]); }
        }
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { total, hits });
    }
    private void Display()
    {
        if (document is null) return;
        Welcome.Visibility = Visibility.Collapsed; TextView.Visibility = MarkdownDisplay.Visibility = CsvGrid.Visibility = Visibility.Collapsed;
        SourceToggle.Visibility = document.Kind == "markdown" ? Visibility.Visible : Visibility.Collapsed;
        bool web = InWebPane;
        WebPane.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
        PageControls.Visibility = Paginated ? Visibility.Visible : Visibility.Collapsed;
        PageLabel.Text = document.Kind == "slides" ? "Slide" : "Page";
        System.Windows.Automation.AutomationProperties.SetName(PageBox, document.Kind == "slides" ? "Go to slide number" : "Go to page number");
        EncodingChoice.IsEnabled = !web;
        if (web) { DelimiterChoice.IsEnabled = false; lastQuery = ""; matchIndex = -1; WebPane.FocusDocument(); return; }
        DelimiterChoice.IsEnabled = document.Kind == "csv";
        if (document.Kind is "csv" or "lines")
        {
            // Large plain text shows one line per row, with line numbers as row headers.
            bool lines = document.Kind == "lines";
            CsvGrid.Columns.Clear();
            int count = rowStore is not null ? document.Columns : document.Rows.Count == 0 ? 0 : document.Rows.Max(row => row.Length);
            for (int i = 0; i < count; i++)
                CsvGrid.Columns.Add(new DataGridTextColumn { Header = lines ? "Text" : ColumnName(i), Binding = new Binding($"[{i}]") { FallbackValue = "" }, Width = lines ? DataGridLength.Auto : 140 });
            CsvGrid.HeadersVisibility = lines ? DataGridHeadersVisibility.Row : DataGridHeadersVisibility.All;
            if (lines) CsvGrid.FontFamily = new FontFamily("Consolas"); else CsvGrid.ClearValue(FontFamilyProperty);
            System.Windows.Automation.AutomationProperties.SetName(CsvGrid, lines ? "Read-only text, one line per row" : "Read-only CSV grid");
            CsvGrid.ItemsSource = rowStore is not null ? new StoreRows(rowStore)
                : document.Rows.Select(row => Enumerable.Range(0, count).Select(i => i < row.Length ? row[i] : "").ToArray()).ToList();
            CsvGrid.Visibility = Visibility.Visible;
        }
        else if (document.Kind == "markdown" && SourceToggle.IsChecked != true)
        {
            // Refill the existing document rather than replacing it: a screen reader that queried the window while the
            // file was loading (as Narrator does) keeps reading the original document object, which would stay empty.
            var flow = MarkdownDisplay.Document;
            flow.Blocks.Clear(); flow.PagePadding = new Thickness(18); flow.FontFamily = new FontFamily("Segoe UI"); flow.FontSize = 16 * zoom;
            foreach (var block in document.Blocks) flow.Blocks.Add(Render(block));
            MarkdownDisplay.Visibility = Visibility.Visible;
        }
        else { TextView.Text = document.Text; TextView.Visibility = Visibility.Visible; }
        lastQuery = ""; matchIndex = -1; ApplyZoom();
    }
    private Block Render(ViewBlock model)
    {
        if (model.Kind == "table")
        {
            var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); table.RowGroups.Add(group);
            foreach (var row in model.Children)
            {
                var targetRow = new TableRow(); group.Rows.Add(targetRow);
                foreach (var cell in row.Children)
                {
                    var target = new TableCell { Padding = new Thickness(8), BorderThickness = new Thickness(0.5), BorderBrush = SystemColors.GrayTextBrush };
                    foreach (var child in cell.Children) target.Blocks.Add(Render(child));
                    targetRow.Cells.Add(target);
                }
            }
            return table;
        }
        if (model.Kind is "list" or "ordered")
        {
            var list = new System.Windows.Documents.List { MarkerStyle = model.Kind == "ordered" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };
            foreach (var item in model.Children) { var target = new ListItem(); foreach (var child in item.Children) target.Blocks.Add(Render(child)); list.ListItems.Add(target); }
            return list;
        }
        if (model.Children.Count > 0)
        {
            var section = new Section { Margin = model.Kind == "quote" ? new Thickness(20, 6, 0, 6) : new Thickness(0) };
            foreach (var child in model.Children) section.Blocks.Add(Render(child)); return section;
        }
        var paragraph = new Paragraph { Margin = new Thickness(0, 4, 0, 10) };
        if (model.Kind == "heading") { paragraph.FontSize = (32 - Math.Min(model.Level, 6) * 2) * zoom; paragraph.FontWeight = FontWeights.SemiBold; }
        if (model.Kind == "code") { paragraph.FontFamily = new FontFamily("Consolas"); paragraph.Inlines.Add(new Run(model.Text)); return paragraph; }
        if (model.Kind == "rule") { paragraph.Inlines.Add(new Run("────────────────────────")); return paragraph; }
        foreach (var item in model.Runs)
        {
            var run = new Run(item.Text) { FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal };
            if (item.Code) run.FontFamily = new FontFamily("Consolas");
            if (item.Link is { } address && LinkPolicy.CanOpen(address))
            {
                var link = new Hyperlink(run) { ToolTip = address };
                link.Click += (_, _) => OpenLink(address); paragraph.Inlines.Add(link);
            }
            else paragraph.Inlines.Add(run);
        }
        return paragraph;
    }
    private void OpenLink(string address)
    {
        if (!LinkPolicy.CanOpen(address)) return;
        if (MessageBox.Show(this, "Open this address in your default application?\n\n" + address, "Open link", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch { Status.Text = "The link could not open. Check your default browser or email application."; }
    }
    private void Find(bool previous)
    {
        if (document is null || FindBox.Text.Length == 0) return;
        if (InWebPane) { Status.Text = "Searching…"; WebPane.Find(FindBox.Text, previous); lastQuery = FindBox.Text; return; }
        if (rowStore is not null) { FindInStore(FindBox.Text, previous); return; }
        string query = FindBox.Text; var hits = new List<int>();
        if (document.Kind == "csv")
        {
            var rows = (List<string[]>)CsvGrid.ItemsSource;
            for (int i = 0; i < rows.Count; i++) if (rows[i].Any(cell => cell.Contains(query, StringComparison.OrdinalIgnoreCase))) hits.Add(i);
            if (lastQuery != query) matchIndex = previous ? 0 : -1;
            if (hits.Count > 0) { matchIndex = (matchIndex + (previous ? -1 : 1) + hits.Count) % hits.Count; CsvGrid.SelectedItem = rows[hits[matchIndex]]; CsvGrid.ScrollIntoView(CsvGrid.SelectedItem); }
            Status.Text = hits.Count == 0 ? "No matches in loaded rows." : $"Matching row {matchIndex + 1} of {hits.Count} in loaded rows. {document.Notice}";
        }
        else
        {
            bool rendered = MarkdownDisplay.Visibility == Visibility.Visible;
            string text = rendered ? new TextRange(MarkdownDisplay.Document.ContentStart, MarkdownDisplay.Document.ContentEnd).Text : TextView.Text;
            for (int start = 0; start <= text.Length - query.Length;)
            { int found = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase); if (found < 0) break; hits.Add(found); start = found + query.Length; }
            if (lastQuery != query) matchIndex = previous ? 0 : -1;
            if (hits.Count > 0)
            {
                matchIndex = (matchIndex + (previous ? -1 : 1) + hits.Count) % hits.Count;
                if (!rendered) { TextView.Focus(); TextView.Select(hits[matchIndex], query.Length); TextView.ScrollToLine(TextView.GetLineIndexFromCharacterIndex(hits[matchIndex])); }
                else
                {
                    var start = PointerAtTextOffset(MarkdownDisplay.Document, hits[matchIndex]); var end = PointerAtTextOffset(MarkdownDisplay.Document, hits[matchIndex] + query.Length);
                    MarkdownDisplay.Focus(); MarkdownDisplay.Selection.Select(start, end); start.Paragraph?.BringIntoView();
                }
            }
            Status.Text = hits.Count == 0 ? "No matches." : $"Match {matchIndex + 1} of {hits.Count}.";
        }
        lastQuery = query;
    }
    // CSV and large text search on a background thread: the first match shows as soon as it is found, progress
    // appears in the status line, and Cancel or a new search stops it.
    private void FindInStore(string query, bool previous)
    {
        if (storeSearch is null || storeSearch.Query != query || storeSearch.Error is not null)
        {
            storeSearch?.Dispose(); storeMatch = -1; lastQuery = query;
            storeSearch = new StoreSearch(rowStore!, query, search => Dispatcher.InvokeAsync(() => SearchChanged(search, previous)));
            CancelButton.IsEnabled = true; Status.Text = "Searching…";
            return;
        }
        ShowStoreMatch(previous);
    }
    private void SearchChanged(StoreSearch search, bool previous)
    {
        if (search != storeSearch) return;
        if (storeMatch < 0 && search.Count > 0) ShowStoreMatch(previous); else ShowSearchStatus();
        if (search.Done && loading is null) CancelButton.IsEnabled = false;
    }
    private void ShowStoreMatch(bool previous)
    {
        var search = storeSearch!; int count = search.Count;
        if (count > 0 && CsvGrid.ItemsSource is StoreRows rows)
        {
            storeMatch = storeMatch < 0 ? (previous && search.Done ? count - 1 : 0) : (storeMatch + (previous ? -1 : 1) + count) % count;
            var row = rows.Row(search.Hit(storeMatch));
            CsvGrid.SelectedItem = row; CsvGrid.ScrollIntoView(row);
        }
        ShowSearchStatus();
    }
    private void ShowSearchStatus()
    {
        var search = storeSearch!; int count = search.Count; string unit = document?.Kind == "lines" ? "line" : "row";
        Status.Text = search.Error ?? (search.Done
            ? count == 0 ? "No matches." : $"Matching {unit} {storeMatch + 1} of {count:N0}.{(search.Truncated ? " The search stopped at 1,000,000 matches." : "")}"
            : $"Searching… {search.Progress:P0} · {count:N0} matching {unit}s so far{(storeMatch >= 0 ? $", showing {storeMatch + 1}" : "")}. Cancel stops the search.");
    }
    private static TextPointer PointerAtTextOffset(FlowDocument flow, int offset)
    {
        // Binary-search WPF symbol offsets rather than constructing a prefix per character.
        var start = flow.ContentStart;
        int low = 0, high = start.GetOffsetToPosition(flow.ContentEnd);
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            var pointer = start.GetPositionAtOffset(middle)!;
            if (new TextRange(start, pointer).Text.Length < offset) low = middle + 1; else high = middle;
        }
        return start.GetPositionAtOffset(low)!.GetInsertionPosition(LogicalDirection.Forward);
    }
    private static string ColumnName(int index) { string name = ""; for (int value = index + 1; value > 0; value = (value - 1) / 26) name = (char)('A' + (value - 1) % 26) + name; return name; }
    private void ApplyZoom() { TextView.FontSize = 16 * zoom; CsvGrid.FontSize = 14 * zoom; MarkdownDisplay.FontSize = 16 * zoom; if (document?.Kind == "markdown") MarkdownDisplay.Document.FontSize = 16 * zoom; ZoomButton.Content = $"{zoom:P0}"; }
    private void ChangeZoom(double value) { zoom = Math.Clamp(value, 0.5, 3); if (document?.Kind == "markdown") Display(); else ApplyZoom(); }
    private void ZoomBy(int direction)
    {
        if (InWebPane) { WebPane.Zoom(direction > 0 ? "in" : direction < 0 ? "out" : 1.0); return; }
        ChangeZoom(direction == 0 ? 1 : zoom + 0.1 * direction);
    }
    private void ZoomIn(object s, RoutedEventArgs e) => ZoomBy(1);
    private void ZoomOut(object s, RoutedEventArgs e) => ZoomBy(-1);
    private void ResetZoom(object s, RoutedEventArgs e) => ZoomBy(0);
    private void FitWidth(object s, RoutedEventArgs e) => WebPane.Zoom("page-width");
    private void FitPage(object s, RoutedEventArgs e) => WebPane.Zoom("page-fit");
    private void PreviousPage(object s, RoutedEventArgs e) => WebPane.Step(-1);
    private void NextPage(object s, RoutedEventArgs e) => WebPane.Step(1);
    private async Task<DocumentView> LoadOffice(string path, CancellationToken cancellation)
    {
        var (prepared, pdf) = await OfficeConverter.Convert(path, cancellation, () => Status.Text = "Preparing the document for viewing…");
        Welcome.Visibility = TextView.Visibility = MarkdownDisplay.Visibility = CsvGrid.Visibility = Visibility.Collapsed;
        WebPane.Visibility = Visibility.Visible;
        bool slides = prepared.Kind == "slides";
        await WebPane.LoadPdf(pdf, IsDarkTheme(), cancellation, slides);
        if (slides) prepared.Notice = (prepared.Notice + " Slides are shown as still pictures: animations, transitions, audio and video do not play.").Trim();
        return prepared;
    }
    private void PageBoxKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (int.TryParse(PageBox.Text.Trim(), out int page) && page >= 1 && page <= WebPane.Pages) { WebPane.GoToPage(page); WebPane.FocusDocument(); }
        else Status.Text = $"Enter a page number from 1 to {WebPane.Pages}.";
        e.Handled = true;
    }
    private async Task<DocumentView> LoadPdf(string path, CancellationToken cancellation)
    {
        var data = await Task.Run(() => PdfFiles.Snapshot(path), cancellation);
        // Show the PDF area before loading so PDF.js can measure the page width.
        Welcome.Visibility = TextView.Visibility = MarkdownDisplay.Visibility = CsvGrid.Visibility = Visibility.Collapsed;
        WebPane.Visibility = Visibility.Visible;
        await WebPane.LoadPdf(data, IsDarkTheme(), cancellation);
        return new DocumentView { Kind = "pdf", Encoding = "PDF" };
    }
    private async Task LoadSheets(DocumentView view, CancellationToken cancellation)
    {
        // The worker has already turned the workbook into display text; the page only lays it out.
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { sheets = view.Sheets },
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Welcome.Visibility = TextView.Visibility = MarkdownDisplay.Visibility = CsvGrid.Visibility = Visibility.Collapsed;
        WebPane.Visibility = Visibility.Visible;
        await WebPane.LoadSheets(json, IsDarkTheme(), cancellation);
    }
    private void ShowWebStatus()
    {
        if (document is null || !InWebPane) return;
        ZoomButton.Content = $"{WebPane.Scale:P0}";
        if (Paginated)
        {
            string unit = document.Kind == "slides" ? "Slide" : "Page";
            string type = document.Kind switch { "word" => "Word document", "slides" => "PowerPoint presentation", _ => "PDF" };
            PageCount.Text = $"of {WebPane.Pages}";
            if (!PageBox.IsKeyboardFocused) PageBox.Text = WebPane.Page.ToString();
            Status.Text = $"Read only · {type} · {unit} {WebPane.Page} of {WebPane.Pages} · Opened in {openSeconds:F2}s. {document.Notice}";
        }
        else Status.Text = $"Read only · Excel workbook · Sheet {WebPane.Page} of {WebPane.Pages}: {WebPane.SheetName} · Opened in {openSeconds:F2}s. {document.Notice}";
    }
    private async Task<(int Current, int Total)> PdfFind(string query)
    {
        var result = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(int current, int total, bool finished) { if (finished) result.TrySetResult((current, total)); }
        WebPane.FindResult += Handler;
        try { WebPane.Find(query, false); return await result.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { WebPane.FindResult -= Handler; }
    }
    private string? AskPdfPassword(bool incorrect)
    {
        var box = new PasswordBox { Margin = new Thickness(0, 10, 0, 14), MinWidth = 280 };
        System.Windows.Automation.AutomationProperties.SetName(box, "PDF password");
        var ok = new Button { Content = "Open", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = incorrect ? "That password is not correct. Try again." : "This PDF is protected. Enter its password to view it.", TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
        panel.Children.Add(box); panel.Children.Add(buttons);
        var dialog = new Window { Title = "Password required", Owner = IsVisible ? this : null, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => box.Focus();
        // The password goes straight to PDF.js; it is never stored or logged.
        return dialog.ShowDialog() == true ? box.Password : null;
    }
    private bool IsDarkTheme()
    {
        string choice = ThemeChoice.SelectedItem is ComboBoxItem ? Choice(ThemeChoice) : "System";
        if (choice != "System") return choice == "Dark";
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
    private void NextMatch(object s, RoutedEventArgs e) => Find(false);
    private void PreviousMatch(object s, RoutedEventArgs e) => Find(true);
    private void FindKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Find(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); }
    private void ViewChanged(object s, RoutedEventArgs e) => Display();
    private void OptionsChanged(object s, SelectionChangedEventArgs e) { if (IsLoaded && currentPath is not null && !InWebPane) _ = LoadCurrent(); }
    private void CancelClicked(object s, RoutedEventArgs e)
    {
        loading?.Cancel();
        if (storeSearch is { Done: false }) { storeSearch.Dispose(); storeSearch = null; storeMatch = -1; Status.Text = "Search cancelled."; CancelButton.IsEnabled = loading is not null; }
    }
    // WPF still marks runtime Fluent theme switching experimental in this SDK.
#pragma warning disable WPF0001
    private void ThemeChanged(object s, SelectionChangedEventArgs e)
    {
        if (ThemeChoice is null) return;
        ThemeMode = Choice(ThemeChoice) switch { "Dark" => ThemeMode.Dark, "Light" => ThemeMode.Light, _ => ThemeMode.System };
        if (InWebPane) WebPane.SetTheme(IsDarkTheme());
    }
#pragma warning restore WPF0001
    private void NumberRow(object s, DataGridRowEventArgs e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    private void FileDropped(object s, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var file in files) OpenPath(file); }
    private static string Version => typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
    private void AboutClicked(object s, RoutedEventArgs e) => MessageBox.Show(this, $"Plain Viewer {Version} — development preview\n\nRead-only PDF, Word (.docx), Excel (.xlsx), PowerPoint (.pptx), text, CSV and Markdown.\n\nPDF uses PDF.js (Apache-2.0) inside Microsoft Edge WebView2. Word and PowerPoint files are converted to PDF by LibreOffice (MPL-2.0). Excel number formats use ExcelNumberFormat (MIT). Markdown uses Markdig (BSD-2-Clause). See THIRD-PARTY-NOTICES.md.\n\n{SafetyNote()}", "About Plain Viewer");

    private static string SafetyNote() =>
        "Files are read by separate processes that run at low integrity with memory and time limits: they cannot change your files or other programs. PDF pages are drawn inside WebView2's own sandbox.\n\n" +
        (NetworkBlock.IsOn()
            ? "Network block: on. Windows Firewall blocks the document reader and converter from the network."
            : "Network block: off. To turn it on, run the Plain Viewer installer again and allow the administrator prompt.");
    private void WindowKeyDown(object s, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.O) OpenClicked(s, e);
        else if (ctrl && e.Key == Key.F) FindBox.Focus();
        else if (ctrl && e.Key == Key.W) Close();
        else if (ctrl && (e.Key == Key.Add || e.Key == Key.OemPlus)) ZoomBy(1);
        else if (ctrl && (e.Key == Key.Subtract || e.Key == Key.OemMinus)) ZoomBy(-1);
        else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) ZoomBy(0);
        else if (e.Key == Key.F3) Find(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        else if (ctrl && e.Key is Key.PageUp or Key.PageDown && document?.Kind == "sheet") WebPane.ChangeSheet(e.Key == Key.PageDown ? 1 : -1);
        else if (e.Key == Key.F11) { if (WindowStyle == WindowStyle.None) { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = savedState; } else { savedState = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; } }
        else return;
        e.Handled = true;
    }
}
