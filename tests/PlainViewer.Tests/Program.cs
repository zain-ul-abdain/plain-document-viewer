using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using PlainViewer.Core;

// Child mode for the low-integrity test: drop to low integrity, then report which folders Windows lets it write to.
if (args is ["--low-integrity-probe", var mediumFolder, var lowFolder])
{
    LowIntegrity.LowerCurrentProcess();
    Console.WriteLine($"{CanWrite(mediumFolder)} {CanWrite(lowFolder)}");
    return 0;
}

int passed = 0, failed = 0;
void Test(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); passed++; } catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; } }
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
string root = Path.Combine(Path.GetTempPath(), "PlainViewerTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    Test("CSV multiline, escaped quotes and leading zeros", () => {
        var rows = Csv.Read(new StringReader("id,name,note\r\n001,\"Ali, A\",\"line1\nline2 \"\"quote\"\"\"\r\n"), ',').ToList();
        Check(rows.Count == 2 && rows[1][0] == "001" && rows[1][1] == "Ali, A" && rows[1][2] == "line1\nline2 \"quote\""); });
    Test("CSV malformed quotes rejected", () => Throws<DocumentException>(() => Csv.Read(new StringReader("a,\"unfinished"), ',').ToList()));
    Test("CSV empty and trailing fields", () => { Check(!Csv.Read(new StringReader(""), ',').Any()); Check(Csv.Read(new StringReader("a,"), ',').Single().SequenceEqual(new[] { "a", "" })); });
    Test("CSV delimiter ignores quoted separators", () => Check(Csv.DetectDelimiter("\"a,b\";c\n") == ';'));
    Test("CSV column resource limit", () => Throws<DocumentException>(() => Csv.Read(new StringReader(new string(',', 600)), ',').ToList()));
    Test("UTF BOM and Windows-1252", () => { Check(TextFiles.Detect([255, 254, 65, 0]).Encoding.CodePage == 1200); Check(TextFiles.Detect([0x93, 65, 0x94]).Encoding.CodePage == 1252); });
    Test("UTF-16 heuristic", () => Check(TextFiles.Detect([65, 0, 66, 0, 67, 0]).Encoding.CodePage == 1200));
    Test("UTF-8 partial sample", () => Check(TextFiles.Detect([65, 0xe2, 0x82]).Encoding.CodePage == 65001));
    Test("Unsafe link schemes remain inert", () => { foreach (var link in new[] { "javascript:alert(1)", "file:///c:/secret", @"\\server\share", "data:text/html,test", "relative.md" }) Check(!LinkPolicy.CanOpen(link)); Check(LinkPolicy.CanOpen("https://example.com")); });
    Test("Markdown literal HTML, no image references in display data", () => {
        var blocks = MarkdownView.Parse("# Hello\n\n<script>alert(1)</script>\n\n![private](file:///c:/secret.png)\n\n[bad](javascript:alert)\n");
        string json = System.Text.Json.JsonSerializer.Serialize(blocks); Check(blocks[0].Kind == "heading"); Check(json.Contains("script")); Check(!json.Contains("secret.png")); Check(!json.Contains("\"Link\":\"javascript")); });
    Test("Markdown tables and tasks", () => { var blocks = MarkdownView.Parse("| A | B |\n|---|---|\n| 1 | 2 |\n\n- [x] Done\n"); Check(blocks.Any(b => b.Kind == "table")); Check(blocks.Any(b => b.Kind == "list")); });
    Test("Front matter retained as code", () => Check(MarkdownView.Parse("---\nname: demo\n---\n# Hello")[0].Kind == "code"));
    Test("Mermaid stays code", () => Check(MarkdownView.Parse("```mermaid\ngraph TD; A-->B\n```")[0].Kind == "code"));
    Test("DTD and XXE prohibited", () => { using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///C:/secret'>]><a>&x;</a>")); using var reader = ArchiveSafety.CreateXmlReader(stream); Throws<XmlException>(() => { while (reader.Read()) { } }); });
    Test("Archive expansion limit", () => {
        using var stream = new MemoryStream(); using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, true)) { using var entry = writer.CreateEntry("large").Open(); entry.Write(new byte[1024 * 1024]); }
        stream.Position = 0; using var archive = new ZipArchive(stream, ZipArchiveMode.Read); Throws<DocumentException>(() => ArchiveSafety.Validate(archive, maximumBytes: 1000)); });
    Test("Archive traversal rejected", () => { using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) zip.CreateEntry("../escape"); stream.Position = 0; using var archive = new ZipArchive(stream); Throws<DocumentException>(() => ArchiveSafety.Validate(archive)); });
    Test("Network path rejected before access", () => Throws<DocumentException>(() => TextFiles.ValidateLocalPath(@"\\nonexistent.invalid\share\secret.md")));
    Test("Original unchanged and no adjacent files", () => {
        var path = Path.Combine(root, "sample.txt"); File.WriteAllText(path, "مرحبا • سنڌي • اردو • 中文", new UTF8Encoding(false));
        byte[] before = SHA256.HashData(File.ReadAllBytes(path)); var names = Directory.GetFiles(root);
        var loaded = TextFiles.Load(path); Check(loaded.Text.Contains("中文")); Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))); Check(names.SequenceEqual(Directory.GetFiles(root))); });
    Test("Source can remain open for writes", () => { string path = Path.Combine(root, "shared.txt"); File.WriteAllText(path, "readable"); using var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete); Check(TextFiles.Load(path).Text == "readable"); });
    Test("Wrong extension content rejected", () => { string path = Path.Combine(root, "fake.txt"); File.WriteAllText(path, "%PDF-1.7"); Throws<DocumentException>(() => TextFiles.Load(path)); });
    Test("Empty text is valid", () => { string path = Path.Combine(root, "empty.txt"); File.WriteAllText(path, ""); Check(TextFiles.Load(path).Text == ""); });
    Test("CSV preview explicitly reports truncation", () => { string path = Path.Combine(root, "large.csv"); File.WriteAllLines(path, Enumerable.Range(0, 1500).Select(i => i + ",001")); var view = TextFiles.Load(path); Check(view.Rows.Count == 1000 && view.Notice.Contains("1,000")); });
    // The worker and LibreOffice run at low integrity: they must not be able to write the user's folders.
    Test("Low integrity blocks writes to the user's folders but not to its own", () => {
        string medium = Path.Combine(root, "medium"), low = Path.Combine(LowIntegrity.Root, "Temp", "probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(medium); Directory.CreateDirectory(low);
        try
        {
            string self = Environment.ProcessPath!;
            var start = new System.Diagnostics.ProcessStartInfo(self) { RedirectStandardOutput = true, UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);
            foreach (var argument in new[] { "--low-integrity-probe", medium, low }) start.ArgumentList.Add(argument);
            using var probe = System.Diagnostics.Process.Start(start)!;
            string result = probe.StandardOutput.ReadToEnd().Trim(); probe.WaitForExit();
            Check(result == "False True" && !File.Exists(Path.Combine(medium, "probe.txt")) && File.Exists(Path.Combine(low, "probe.txt")));
        }
        finally { Directory.Delete(low, true); }
    });
    // PDF snapshot. PDF.js parses inside WebView2; these cover the host-side checks only.
    Test("PDF snapshot copies bytes and leaves the original unchanged", () => {
        string path = Path.Combine(root, "doc.pdf"); File.WriteAllBytes(path, Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        byte[] before = SHA256.HashData(File.ReadAllBytes(path)); var names = Directory.GetFiles(root);
        Check(PdfFiles.Snapshot(path).AsSpan().SequenceEqual(File.ReadAllBytes(path)));
        Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))); Check(names.SequenceEqual(Directory.GetFiles(root))); });
    Test("PDF header accepted after leading bytes, rejected after 1024", () => {
        Check(PdfFiles.HasPdfHeader(Encoding.ASCII.GetBytes(new string(' ', 500) + "%PDF-1.4")));
        Check(!PdfFiles.HasPdfHeader(Encoding.ASCII.GetBytes(new string(' ', 1100) + "%PDF-1.4"))); });
    Test("PDF empty and mislabelled files give clear errors", () => {
        string empty = Path.Combine(root, "empty.pdf"); File.WriteAllBytes(empty, []);
        string text = Path.Combine(root, "text.pdf"); File.WriteAllText(text, "not a pdf");
        try { PdfFiles.Snapshot(empty); throw new Exception("no error"); } catch (DocumentException ex) { Check(ex.Message.Contains("empty")); }
        try { PdfFiles.Snapshot(text); throw new Exception("no error"); } catch (DocumentException ex) { Check(ex.Message.Contains("not a PDF")); } });
    Test("PDF can be read while another program has it open for writing", () => {
        string path = Path.Combine(root, "open.pdf"); File.WriteAllBytes(path, Encoding.ASCII.GetBytes("%PDF-1.7\n"));
        using var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        Check(PdfFiles.Snapshot(path).Length == 9); });
    Test("PDF network path rejected before access", () => Throws<DocumentException>(() => PdfFiles.Snapshot(@"\\nonexistent.invalid\share\secret.pdf")));

    // Spreadsheets: every xlsx/xlsm fixture in tests/corpus/manifest.json is checked against its expected result.
    string corpus = FindCorpus();
    var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
    using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, "manifest.json")));
    var errorWords = new Dictionary<string, string[]> {
        ["damaged"] = ["damaged", "too large to open safely"], ["empty"] = ["empty"], ["password"] = ["password"],
        ["mismatch"] = ["not an Excel workbook", "older Excel file", "contents are not", "older Office file"], ["unsupported"] = ["not supported", "contains macros"] };

    // Word and PowerPoint: OfficePackages.Prepare must refuse bad packages with the right message and write a
    // copy of good ones with no outside references (other than hyperlinks) and no content-fetching field codes.
    foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
    {
        string format = fixture.GetProperty("format").GetString()!;
        if (format is not ("docx" or "pptx" or "docm" or "pptm") || fixture.TryGetProperty("generated", out _)) continue;
        string file = fixture.GetProperty("file").GetString()!;
        var expect = fixture.GetProperty("expect");
        Test("Office preparation " + file, () => {
            string path = Path.Combine(corpus, file.Replace('/', Path.DirectorySeparatorChar));
            string output = Path.Combine(root, Guid.NewGuid().ToString("N"), "document" + Path.GetExtension(path));
            byte[] before = SHA256.HashData(File.ReadAllBytes(path)); var siblings = Directory.GetFiles(Path.GetDirectoryName(path)!);
            if (expect.GetProperty("result").GetString() == "error")
            {
                string message = "";
                try { OfficePackages.Prepare(path, output); } catch (DocumentException ex) { message = ex.Message; }
                var words = errorWords[expect.GetProperty("error").GetString()!];
                if (!words.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase))) throw new Exception($"Expected a {string.Join("/", words)} message, got: '{message}'");
                Check(!File.Exists(output));
            }
            else
            {
                var view = OfficePackages.Prepare(path, output);
                Check(view.Kind == (format == "docx" ? "word" : "slides"));
                using var zip = ZipFile.OpenRead(output);
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
                {
                    using var reader = new StreamReader(entry.Open()); string xml = reader.ReadToEnd();
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xml, "<Relationship [^>]*TargetMode=\"External\"[^>]*>"))
                        if (!m.Value.Contains("/hyperlink\"")) throw new Exception($"{entry.FullName} still has an outside reference: {m.Value}");
                    if (System.Text.RegularExpressions.Regex.IsMatch(xml, "INCLUDEPICTURE|INCLUDETEXT|DDEAUTO", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        throw new Exception($"{entry.FullName} still has a content-fetching field.");
                }
                if (fixture.GetProperty("category").GetString() == "attack") Check(view.Notice.Contains("removed"));
            }
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))));
            Check(siblings.SequenceEqual(Directory.GetFiles(Path.GetDirectoryName(path)!)));
        });
    }
    foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
    {
        string format = fixture.GetProperty("format").GetString()!;
        if (format is not ("xlsx" or "xlsm") || fixture.TryGetProperty("generated", out _)) continue;
        string file = fixture.GetProperty("file").GetString()!;
        var expect = fixture.GetProperty("expect");
        Test("Spreadsheet fixture " + file, () => {
            string path = Path.Combine(corpus, file.Replace('/', Path.DirectorySeparatorChar));
            byte[] before = SHA256.HashData(File.ReadAllBytes(path)); var siblings = Directory.GetFiles(Path.GetDirectoryName(path)!);
            if (expect.GetProperty("result").GetString() == "error")
            {
                string message = "";
                try { Spreadsheets.Load(path, culture); } catch (DocumentException ex) { message = ex.Message; }
                var words = errorWords[expect.GetProperty("error").GetString()!];
                if (!words.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase))) throw new Exception($"Expected a {string.Join("/", words)} message, got: '{message}'");
            }
            else
            {
                var view = Spreadsheets.Load(path, culture);
                var names = view.Sheets.Select(s => s.Name).ToArray();
                var expected = expect.GetProperty("sheets").EnumerateArray().Select(e => e.GetString()!).ToArray();
                if (!names.SequenceEqual(expected)) throw new Exception($"Sheets: got {string.Join(",", names)}");
                if (expect.TryGetProperty("cells", out var cells))
                    foreach (var cell in cells.EnumerateArray())
                    {
                        var sheet = view.Sheets.Single(s => s.Name == cell.GetProperty("sheet").GetString());
                        Check(Spreadsheets.TryCell(cell.GetProperty("ref").GetString()!, out int row, out int column));
                        string actual = row - 1 < sheet.Rows.Count && column < sheet.Rows[row - 1].Length ? sheet.Rows[row - 1][column] : "(missing)";
                        if (actual != cell.GetProperty("text").GetString()) throw new Exception($"{sheet.Name}!{cell.GetProperty("ref").GetString()}: got '{actual}', expected '{cell.GetProperty("text").GetString()}'");
                    }
                if (expect.TryGetProperty("notice", out var notice)) Check(view.Sheets[0].Notice == notice.GetString());
                if (expect.TryGetProperty("hiddenSheetsNotShown", out var hidden))
                    Check(hidden.EnumerateArray().All(h => !names.Contains(h.GetString())) && !view.Sheets.SelectMany(s => s.Rows).SelectMany(r => r).Any(t => t.Contains("hidden value")));
                if (expect.TryGetProperty("frozen", out var frozen))
                {
                    var sheet = view.Sheets.Single(s => s.Name == frozen.GetProperty("sheet").GetString());
                    Check(sheet.FrozenRows == frozen.GetProperty("rows").GetInt32() && sheet.FrozenColumns == frozen.GetProperty("columns").GetInt32());
                }
                if (expect.TryGetProperty("merges", out var merges))
                    foreach (var merge in merges.EnumerateArray())
                    {
                        Check(Spreadsheets.TryRange(merge.GetProperty("range").GetString()!, out var range));
                        Check(view.Sheets.Single(s => s.Name == merge.GetProperty("sheet").GetString()).Merges.Any(m => m.SequenceEqual(range)));
                    }
                if (expect.TryGetProperty("rightToLeft", out var rtl))
                    Check(rtl.EnumerateArray().All(n => view.Sheets.Single(s => s.Name == n.GetString()).RightToLeft));
            }
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))));
            Check(siblings.SequenceEqual(Directory.GetFiles(Path.GetDirectoryName(path)!)));
        });
    }
    Test("Spreadsheet complex fixture reports hidden sheets and the missing formula result", () => {
        var view = Spreadsheets.Load(Path.Combine(corpus, "xlsx", "complex.xlsx"), culture);
        Check(view.Notice.Contains("2 hidden sheets") && view.Notice.Contains("1 formula cell has no saved result")); });
    Test("Spreadsheet cell references", () => {
        Check(Spreadsheets.TryCell("A1", out int r, out int c) && r == 1 && c == 0);
        Check(Spreadsheets.TryCell("AB12", out r, out c) && r == 12 && c == 27);
        Check(!Spreadsheets.TryCell("12", out _, out _) && !Spreadsheets.TryCell("ABCD1", out _, out _)); });
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"Results: {passed} passed, {failed} failed. No UI, Office fidelity, network instrumentation or release performance checks were run.");
return failed == 0 ? 0 : 1;

static bool CanWrite(string folder)
{
    try { File.WriteAllText(Path.Combine(folder, "probe.txt"), "probe"); return true; }
    catch (UnauthorizedAccessException) { return false; }
}

static string FindCorpus()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "tests", "corpus", "manifest.json"))) return Path.Combine(dir.FullName, "tests", "corpus");
    throw new DirectoryNotFoundException("tests/corpus/manifest.json not found above the test output folder.");
}
