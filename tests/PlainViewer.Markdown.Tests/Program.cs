using PlainViewer.Core;
using PlainViewer.App;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

int failed = 0, passed = 0;
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Test(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); passed++; } catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; } }
List<ViewRun> Runs(string text) => MarkdownView.Parse(text).SelectMany(b => b.Runs).ToList();
string Text(string text) => string.Concat(Runs(text).Select(r => r.Text));
Test("HTML entities decode to visible text", () => Check(Text("A &amp; B &#x1F600; &lt;tag&gt;") == "A & B 😀 <tag>"));
Test("Decoded entities never become HTML", () => Check(Runs("&lt;script&gt;alert(1)&lt;/script&gt;").All(r => r.Link is null)));
Test("Soft break becomes a space", () => Check(Text("first\nsecond") == "first second"));
Test("Hard break remains a newline", () => Check(Text("first  \nsecond") == "first\nsecond"));
Test("Email autolink has allowed mailto target", () => Check(Runs("<person@example.com>").Single().Link == "mailto:person@example.com"));
Test("Combined emphasis preserves both styles", () => Check(Runs("***both***").Single(r => r.Text == "both") is { Bold: true, Italic: true }));
Test("Inline math is literal code with delimiters", () => Check(Runs("Value $x_1 + \\alpha$.").Any(r => r.Code && r.Text == "$x_1 + \\alpha$")));
Test("Display math preserves literal source", () => { var block = MarkdownView.Parse("$$\nx_1 + \\alpha\n$$").Single(); Check(block.Kind == "code" && block.Text == "$$\nx_1 + \\alpha\n$$"); });
Test("Currency is not lost", () => Check(Text("Price $5 and $10.") == "Price $5 and $10."));
Test("Code keeps entity spelling", () => Check(Runs("`&amp; $x$`").Single().Text == "&amp; $x$"));
Test("Math inside fenced code stays unchanged", () => Check(MarkdownView.Parse("```\n$x$ &amp;\n```").Single().Text == "$x$ &amp;"));
Test("Image descriptions preserve entities without reading targets", () => Check(Text("![A &amp; B](file:///C:/secret)") == "[Image: A & B]"));
Test("Entity-encoded unsafe link stays inert", () => Check(Runs("[bad](javascript&#58;alert)").All(r => r.Link is null)));
Test("Math offsets remain correct after front matter", () => Check(MarkdownView.Parse("---\ntitle: test\n---\n\nMath $x$.").SelectMany(b => b.Runs).Any(r => r.Code && r.Text == "$x$")));
Test("Ordered list start survives parsing", () => Check(MarkdownView.Parse("7. Seven\n8. Eight").Single().StartNumber == 7));
Test("Zero list start survives parsing", () => Check(MarkdownView.Parse("0. Zero\n1. One").Single().StartNumber == 0));
Test("Table alignments survive parsing", () => {
    var table = MarkdownView.Parse("| A | B | C |\n| :--- | :---: | ---: |\n| left | middle | right |").Single();
    foreach (var row in table.Children) Check(row.Children.Select(c => c.Alignment).SequenceEqual(new[] { "left", "center", "right" }));
});
void Native(Action action)
{
    Exception? failure = null;
    var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
    thread.SetApartmentState(ApartmentState.STA); thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(20))) throw new Exception("Native Markdown test timed out");
    if (failure is not null) throw failure;
}
RichTextBox Render(string markdown)
{
    var host = new RichTextBox { IsReadOnly = true, IsDocumentEnabled = true, AcceptsTab = false, Width = 400, Height = 280 };
    host.Document.Blocks.Clear();
    foreach (var block in MarkdownView.Parse(markdown)) host.Document.Blocks.Add(MarkdownRenderer.Render(block, 1, _ => throw new Exception("Unexpected link activation")));
    MarkdownRenderer.ResizeCode(host.Document, 400);
    host.Measure(new Size(400, 280)); host.Arrange(new Rect(0, 0, 400, 280)); host.UpdateLayout(); return host;
}
Test("Native ordered lists retain non-default and zero starts", () => Native(() => {
    foreach (var n in new[] { 7, 999999999 }) { var list = (System.Windows.Documents.List)MarkdownRenderer.Render(MarkdownView.Parse($"{n}. Item").Single(), 1, _ => { }); Check(list.StartIndex == n); }
    var zero = (System.Windows.Documents.List)MarkdownRenderer.Render(MarkdownView.Parse("0. Zero\n1. One").Single(), 1, _ => { });
    Check(zero.MarkerStyle == TextMarkerStyle.None && new TextRange(zero.ContentStart, zero.ContentEnd).Text.Contains("0. Zero") && new TextRange(zero.ContentStart, zero.ContentEnd).Text.Contains("1. One"));
}));
Test("Native table cells inherit requested alignment", () => Native(() => {
    var table = (Table)MarkdownRenderer.Render(MarkdownView.Parse("| A | B | C |\n| :--- | :---: | ---: |\n| left | middle | right |").Single(), 1, _ => { });
    foreach (TableRow row in table.RowGroups[0].Rows) Check(row.Cells.Cast<TableCell>().Select(c => c.TextAlignment).SequenceEqual(new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right }));
}));
Test("Wide code scrolls without wrapping and permits keyboard exit", () => Native(() => {
    var host = Render("```\n" + new string('x', 500) + "\n```");
    var code = (TextBox)((BlockUIContainer)host.Document.Blocks.FirstBlock).Child;
    Check(code.IsReadOnly && !code.AcceptsTab && code.TextWrapping == TextWrapping.NoWrap);
    Check(code.ExtentWidth > code.ViewportWidth && code.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);
    Check(System.Windows.Automation.AutomationProperties.GetName(code) == "Read-only code block");
    MarkdownRenderer.ResizeCode(host.Document, 300); Check(code.MaxWidth == 200);
}));
Test("Search includes code and prose in reading order", () => Native(() => {
    var host = Render("Hello before\n\n```\nHello code\n```\n\nHello after"); var matches = MarkdownSearch.Find(host, "Hello");
    Check(matches.Count == 3 && matches[0].Code is null && matches[1].Code is not null && matches[2].Code is null);
    matches[1].Select(); Check(matches[1].Code!.SelectedText == "Hello");
    matches[2].Select(); Check(host.Selection.Text == "Hello");
}));
Test("Copying whole rendered document includes code once", () => Native(() => {
    var host = Render("Before\n\n```\nUNIQUE_CODE\n```\n\nAfter"); host.SelectAll(); var copied = MarkdownSearch.SelectedText(host);
    Check(copied.Contains("Before") && copied.Contains("After") && copied.Split("UNIQUE_CODE").Length == 2);
    Check(copied.IndexOf("Before") < copied.IndexOf("UNIQUE_CODE") && copied.IndexOf("UNIQUE_CODE") < copied.IndexOf("After"));
}));
Test("Nested code is searchable and uses requested zoom", () => Native(() => {
    var host = Render("> ```\n> NestedToken\n> ```"); Check(MarkdownSearch.Find(host, "NestedToken").Count == 1);
    var block = (BlockUIContainer)MarkdownRenderer.Render(new ViewBlock { Kind = "code", Text = "safe" }, 2, _ => { }); Check(((TextBox)block.Child).FontSize == 32);
}));
Console.WriteLine($"Markdown regressions: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
