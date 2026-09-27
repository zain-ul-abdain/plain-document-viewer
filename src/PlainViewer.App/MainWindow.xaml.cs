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
    private double zoom = 1;
    private string lastQuery = "";
    private int matchIndex = -1;
    private WindowState savedState;
    private double openSeconds;
    public MainWindow()
    {
        InitializeComponent(); PreviewKeyDown += WindowKeyDown;
        Closed += (_, _) => { loading?.Cancel(); };
        PdfPane.StateChanged += ShowPdfStatus;
        PdfPane.FindResult += (current, total, finished) => { if (finished) Status.Text = total == 0 ? "No matches." : $"Match {Math.Max(current, 1)} of {total}."; };
        PdfPane.LinkRequested += address =>
        {
            if (LinkPolicy.CanOpen(address)) OpenLink(address);
            else Status.Text = "This link was not opened because it is not a web or email address.";
        };
        PdfPane.AskPassword = AskPdfPassword;
    }
    private static string Choice(ComboBox box) => ((ComboBoxItem)box.SelectedItem).Content.ToString()!;
    private static bool IsPdf(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    internal async Task VerifyPreviewAsync(string path)
    {
        if (IsPdf(path))
        {
            // WebView2 needs a real window handle, so the smoke test shows the window off-screen.
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; ShowActivated = false; ShowInTaskbar = false; Show();
        }
        currentPath = path; await LoadCurrent();
        if (document is null) throw new InvalidOperationException(Status.Text);
        if (document.Kind == "pdf")
        {
            if (PdfPane.Pages < 1) throw new InvalidOperationException("PDF reported no pages.");
            var (_, total) = await PdfFind("Hello");
            if (total < 1) throw new InvalidOperationException("PDF text search found no match for 'Hello'.");
            ZoomBy(1); ZoomBy(0);
            if (PdfPane.BlockedRequests != 0) throw new InvalidOperationException($"PDF view attempted {PdfPane.BlockedRequests} blocked request(s).");
            return;
        }
        Measure(new Size(1100, 760)); Arrange(new Rect(0, 0, 1100, 760)); UpdateLayout();
        if (document.Kind == "markdown" && MarkdownDisplay.Document.Blocks.Count == 0) throw new InvalidOperationException("No Markdown blocks rendered.");
        if (document.Kind == "csv" && CsvGrid.Items.Count != document.Rows.Count) throw new InvalidOperationException("CSV row count mismatch.");
        if (document.Kind == "text" && TextView.Text != document.Text) throw new InvalidOperationException("Text view mismatch.");
        FindBox.Text = "Hello"; Find(false);
        if (document.Kind == "markdown" && MarkdownDisplay.Selection.Text != "Hello") throw new InvalidOperationException("Rendered Markdown search selected the wrong text.");
        ChangeZoom(1.2);
        if (document.Kind == "markdown") { SourceToggle.IsChecked = true; if (TextView.Text != document.Text) throw new InvalidOperationException("Source view mismatch."); }
    }
    private void OpenClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open a document — development preview", Filter = "Preview formats|*.pdf;*.txt;*.csv;*.md;*.markdown", CheckFileExists = true };
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
        try
        {
            var loaded = IsPdf(currentPath) ? await LoadPdf(currentPath, operation.Token)
                : await WorkerClient.Load(currentPath, Choice(EncodingChoice), Choice(DelimiterChoice), operation.Token);
            if (loading != operation) return;
            document = loaded; Title = Path.GetFileName(currentPath) + " · Plain Viewer preview";
            openSeconds = stopwatch.Elapsed.TotalSeconds;
            Display(); Status.Text = $"Read only · {document.Encoding} · Opened in {openSeconds:F2}s. {document.Notice}";
            if (document.Kind == "pdf") ShowPdfStatus();
        }
        catch (OperationCanceledException) { if (loading == operation) Status.Text = "Opening cancelled. Choose a file to try again."; }
        catch (Exception ex)
        {
            if (loading != operation) return;
            Status.Text = ex is DocumentException ? ex.Message : "The document could not open. Check the local SDK and worker build, then try again.";
        }
        finally
        {
            if (loading == operation) { CancelButton.IsEnabled = false; Progress.Visibility = Visibility.Collapsed; }
            operation.Dispose(); if (loading == operation) loading = null;
        }
    }
    private void Display()
    {
        if (document is null) return;
        Welcome.Visibility = Visibility.Collapsed; TextView.Visibility = MarkdownDisplay.Visibility = CsvGrid.Visibility = Visibility.Collapsed;
        SourceToggle.Visibility = document.Kind == "markdown" ? Visibility.Visible : Visibility.Collapsed;
        bool pdf = document.Kind == "pdf";
        PdfPane.Visibility = pdf ? Visibility.Visible : Visibility.Collapsed;
        PageControls.Visibility = pdf ? Visibility.Visible : Visibility.Collapsed;
        EncodingChoice.IsEnabled = !pdf;
        if (pdf) { DelimiterChoice.IsEnabled = false; lastQuery = ""; matchIndex = -1; PdfPane.FocusDocument(); return; }
        DelimiterChoice.IsEnabled = document.Kind == "csv";
        if (document.Kind == "csv")
        {
            CsvGrid.Columns.Clear(); int count = document.Rows.Count == 0 ? 0 : document.Rows.Max(row => row.Length);
            for (int i = 0; i < count; i++) CsvGrid.Columns.Add(new DataGridTextColumn { Header = ColumnName(i), Binding = new Binding($"[{i}]") { FallbackValue = "" }, Width = 140 });
            CsvGrid.ItemsSource = document.Rows.Select(row => Enumerable.Range(0, count).Select(i => i < row.Length ? row[i] : "").ToArray()).ToList();
            CsvGrid.Visibility = Visibility.Visible;
        }
        else if (document.Kind == "markdown" && SourceToggle.IsChecked != true)
        {
            var flow = new FlowDocument { PagePadding = new Thickness(18), FontFamily = new FontFamily("Segoe UI"), FontSize = 16 * zoom };
            foreach (var block in document.Blocks) flow.Blocks.Add(Render(block));
            MarkdownDisplay.Document = flow; MarkdownDisplay.Visibility = Visibility.Visible;
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
        if (document.Kind == "pdf") { Status.Text = "Searching…"; PdfPane.Find(FindBox.Text, previous); lastQuery = FindBox.Text; return; }
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
        if (document?.Kind == "pdf") { PdfPane.Zoom(direction > 0 ? "in" : direction < 0 ? "out" : 1.0); return; }
        ChangeZoom(direction == 0 ? 1 : zoom + 0.1 * direction);
    }
    private void ZoomIn(object s, RoutedEventArgs e) => ZoomBy(1);
    private void ZoomOut(object s, RoutedEventArgs e) => ZoomBy(-1);
    private void ResetZoom(object s, RoutedEventArgs e) => ZoomBy(0);
    private void FitWidth(object s, RoutedEventArgs e) => PdfPane.Zoom("page-width");
    private void FitPage(object s, RoutedEventArgs e) => PdfPane.Zoom("page-fit");
    private void PageBoxKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (int.TryParse(PageBox.Text.Trim(), out int page) && page >= 1 && page <= PdfPane.Pages) { PdfPane.GoToPage(page); PdfPane.FocusDocument(); }
        else Status.Text = $"Enter a page number from 1 to {PdfPane.Pages}.";
        e.Handled = true;
    }
    private async Task<DocumentView> LoadPdf(string path, CancellationToken cancellation)
    {
        var data = await Task.Run(() => PdfFiles.Snapshot(path), cancellation);
        // Show the PDF area before loading so PDF.js can measure the page width.
        Welcome.Visibility = TextView.Visibility = MarkdownDisplay.Visibility = CsvGrid.Visibility = Visibility.Collapsed;
        PdfPane.Visibility = Visibility.Visible;
        await PdfPane.Load(data, IsDarkTheme(), cancellation);
        return new DocumentView { Kind = "pdf", Encoding = "PDF" };
    }
    private void ShowPdfStatus()
    {
        if (document?.Kind != "pdf") return;
        PageCount.Text = $"of {PdfPane.Pages}";
        if (!PageBox.IsKeyboardFocused) PageBox.Text = PdfPane.Page.ToString();
        ZoomButton.Content = $"{PdfPane.Scale:P0}";
        Status.Text = $"Read only · PDF · Page {PdfPane.Page} of {PdfPane.Pages} · Opened in {openSeconds:F2}s";
    }
    private async Task<(int Current, int Total)> PdfFind(string query)
    {
        var result = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(int current, int total, bool finished) { if (finished) result.TrySetResult((current, total)); }
        PdfPane.FindResult += Handler;
        try { PdfPane.Find(query, false); return await result.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { PdfPane.FindResult -= Handler; }
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
    private void OptionsChanged(object s, SelectionChangedEventArgs e) { if (IsLoaded && currentPath is not null && document?.Kind != "pdf") _ = LoadCurrent(); }
    private void CancelClicked(object s, RoutedEventArgs e) => loading?.Cancel();
    // WPF still marks runtime Fluent theme switching experimental in this SDK.
#pragma warning disable WPF0001
    private void ThemeChanged(object s, SelectionChangedEventArgs e)
    {
        if (ThemeChoice is null) return;
        ThemeMode = Choice(ThemeChoice) switch { "Dark" => ThemeMode.Dark, "Light" => ThemeMode.Light, _ => ThemeMode.System };
        if (document?.Kind == "pdf") PdfPane.SetTheme(IsDarkTheme());
    }
#pragma warning restore WPF0001
    private void NumberRow(object s, DataGridRowEventArgs e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    private void FileDropped(object s, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var file in files) OpenPath(file); }
    private void AboutClicked(object s, RoutedEventArgs e) => MessageBox.Show(this, "Plain Viewer — development preview\n\nRead-only PDF, text, CSV and Markdown. Word, Excel and PowerPoint rendering are pending.\n\nPDF uses PDF.js (Apache-2.0) inside Microsoft Edge WebView2. Markdown uses Markdig (BSD-2-Clause). See THIRD-PARTY-NOTICES.md.\n\nThe parser worker has resource limits but is not yet a low-privilege security sandbox.", "About Plain Viewer");
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
        else if (e.Key == Key.F11) { if (WindowStyle == WindowStyle.None) { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = savedState; } else { savedState = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; } }
        else return;
        e.Handled = true;
    }
}
