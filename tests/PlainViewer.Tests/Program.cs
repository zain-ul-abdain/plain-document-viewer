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
T Throws<T>(Action action) where T : Exception { try { action(); } catch (T ex) { return ex; } throw new Exception("Expected " + typeof(T).Name); }
string root = Path.Combine(Path.GetTempPath(), "PlainViewerTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    Test("CSV multiline, escaped quotes and leading zeros", () => {
        var rows = Csv.Read(new StringReader("id,name,note\r\n001,\"Ali, A\",\"line1\nline2 \"\"quote\"\"\"\r\n"), ',').ToList();
        Check(rows.Count == 2 && rows[1][0] == "001" && rows[1][1] == "Ali, A" && rows[1][2] == "line1\nline2 \"quote\""); });
    Test("CSV malformed quotes rejected", () => Throws<DocumentException>(() => Csv.Read(new StringReader("a,\"unfinished"), ',').ToList()));
    Test("CSV empty and trailing fields", () => { Check(!Csv.Read(new StringReader(""), ',').Any()); Check(Csv.Read(new StringReader("a,"), ',').Single().SequenceEqual(new[] { "a", "" })); });
    Test("CSV delimiter ignores quoted separators", () => Check(Csv.DetectDelimiter("\"a,b\";c\n") == ';'));
    Test("Full disk recognised, other IO errors not", () => {
        Check(DiskSpace.IsFull(new IOException("full", unchecked((int)0x80070070))) && DiskSpace.IsFull(new IOException("full", unchecked((int)0x80070027))));
        Check(!DiskSpace.IsFull(new IOException("locked", unchecked((int)0x80070020))) && !DiskSpace.IsFull(new InvalidDataException("x"))); });
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
    // Row store: CSV and large-text rows on disk, written by the low-integrity worker and read by the app.
    string NewFolder(string name) { string folder = Path.Combine(root, name); Directory.CreateDirectory(folder); return folder; }
    Test("Row store keeps text, empty fields and ragged rows", () => {
        string folder = NewFolder("store-round-trip");
        using (var writer = new RowStoreWriter(folder, "rows")) { writer.Add(["001", "Ali, A", "line1\nline2"]); writer.Add([]); writer.Add(["", "سنڌي 中文 😀"]); writer.Complete(); }
        using var store = RowStore.Open(folder, "rows");
        var rows = store.Read(0, 3);
        Check(store.Count == 3 && store.Columns == 3 && rows[0][1] == "Ali, A" && rows[0][2] == "line1\nline2" && rows[1].Length == 0 && rows[2][1] == "سنڌي 中文 😀");
        Check(store.Read(2, 1)[0][0] == "");
        Throws<ArgumentOutOfRangeException>(() => store.Read(2, 2)); });
    Test("Damaged row stores are refused", () => {
        string folder = NewFolder("store-damaged");
        using (var writer = new RowStoreWriter(folder, "rows")) { writer.Add(["a", "b"]); writer.Add(["c"]); writer.Complete(); }
        string index = Path.Combine(folder, "rows.index"), data = Path.Combine(folder, "rows.rows");
        byte[] goodIndex = File.ReadAllBytes(index), goodData = File.ReadAllBytes(data);
        void Refused(Action damage) { File.WriteAllBytes(index, goodIndex); File.WriteAllBytes(data, goodData); damage(); Throws<InvalidDataException>(() => { using var store = RowStore.Open(folder, "rows"); store.Read(0, store.Count); }); }
        Refused(() => { var b = (byte[])goodIndex.Clone(); BitConverter.GetBytes(long.MaxValue).CopyTo(b, 24); File.WriteAllBytes(index, b); });   // offset past the end
        Refused(() => { var b = (byte[])goodIndex.Clone(); BitConverter.GetBytes(0L).CopyTo(b, 32); File.WriteAllBytes(index, b); });             // offsets going backwards
        Refused(() => File.WriteAllBytes(index, goodIndex[..^4]));                                                                            // truncated index
        Refused(() => { var b = (byte[])goodIndex.Clone(); b[0] = (byte)'X'; File.WriteAllBytes(index, b); });                              // wrong header
        Refused(() => { var d = (byte[])goodData.Clone(); d[1] = 100; File.WriteAllBytes(data, d); }); });                                  // field longer than its row
    Test("CSV rows stream to a row store and match the in-memory reader", () => {
        string folder = NewFolder("store-csv"), file = Path.Combine(FindCorpus(), "complex.csv");
        var stored = TextFiles.Load(file, "Auto", "Auto", folder); var memory = TextFiles.Load(file);
        using var store = RowStore.Open(folder, stored.Store);
        Check(stored.Kind == "csv" && stored.Store == "rows" && stored.RowCount == memory.Rows.Count && store.Read(0, store.Count).Zip(memory.Rows).All(pair => pair.First.SequenceEqual(pair.Second))); });
    Test("Large text streams to a row store, one row per line", () => {
        string folder = NewFolder("store-text"), file = Path.Combine(root, "big.txt");
        File.WriteAllText(file, "﻿first line\r\n" + string.Concat(Enumerable.Range(0, 400_000).Select(i => $"line {i:D6}\n")) + "last line");
        var view = TextFiles.Load(file, "Auto", "Auto", folder);
        using var store = RowStore.Open(folder, "rows");
        Check(view.Kind == "lines" && view.RowCount == 400_002 && store.Read(0, 1)[0][0] == "first line" && store.Read(400_001, 1)[0][0] == "last line"); });
    string largeCsv = Path.Combine(FindCorpus(), "generated", "csv-large-200mb.csv");
    if (File.Exists(largeCsv))
        Test("200 MB CSV fixture streams to a row store", () => {
            string folder = NewFolder("store-large");
            var view = TextFiles.Load(largeCsv, "Auto", "Auto", folder);
            using var store = RowStore.Open(folder, "rows");
            Check(view.RowCount > 4_000_000 && view.Columns == 6 && store.Read(view.RowCount - 1, 1)[0][5] == "Hello last row"); });
    else Console.WriteLine("SKIP 200 MB CSV fixture (run npm run generate:large in tests/corpus/generate)");
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

    // Large sheets: past 10,000 rows the reader streams every row to a row store (first rows stay in the view).
    string Workbook(string name, IEnumerable<string> rows, string after = "")
    {
        string path = Path.Combine(root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Part(string entry, string xml) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write(xml); }
        Part("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Part("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Big\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Part("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        Part("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + string.Concat(rows) + "</sheetData>" + after + "</worksheet>");
        return path;
    }
    string Cell(string reference, string text) => $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{text}</t></is></c>";
    Test("Workbook sheets past 10,000 rows stream to a row store", () => {
        var rows = Enumerable.Range(1, 12_000).Select(n => n == 11_000 ? $"<row r=\"{n}\" hidden=\"1\">{Cell($"A{n}", "hidden")}</row>"
            : n == 11_500 ? "" : $"<row r=\"{n}\">{Cell($"A{n}", $"r{n}")}{(n == 12_000 ? Cell($"C{n}", "Hello end") : "")}</row>");
        string file = Workbook("big.xlsx", rows, "<mergeCells count=\"1\"><mergeCell ref=\"A11990:B11991\"/></mergeCells>");
        string folder = NewFolder("store-sheet");
        var sheet = Spreadsheets.Load(file, culture, folder).Sheets[0];
        using var store = RowStore.Open(folder, sheet.Store);
        var last = store.Read(11_999, 1)[0];
        Check(sheet.Store == "sheet0" && sheet.RowCount == 12_000 && store.Count == 12_000 && sheet.Rows.Count == 10_000);
        Check(store.Read(9_999, 1)[0][1] == "r10000" && store.Read(11_499, 1)[0].Length == 1 && last[0] == "lll" && last[1] == "r12000" && last[3] == "Hello end");
        Check(sheet.HiddenRows.Contains(11_000) && sheet.Merges.Any(m => m.SequenceEqual(new[] { 11_989, 0, 11_990, 1 })));
        Check(string.IsNullOrEmpty(Spreadsheets.Load(file, culture).Sheets[0].Store));   // without a folder: the 10,000-row preview as before
    });
    Test("Rows of a large sheet out of order are refused", () => {
        var rows = Enumerable.Range(1, 10_002).Select(n => $"<row r=\"{(n == 10_002 ? 10_001 : n)}\">{Cell($"A{n}", "x")}</row>");
        Throws<DocumentException>(() => Spreadsheets.Load(Workbook("unordered.xlsx", rows), culture, NewFolder("store-unordered"))); });
    string largeXlsx = Path.Combine(corpus, "generated", "xlsx-large-500k-rows.xlsx");
    if (File.Exists(largeXlsx))
        Test("500,000-row workbook fixture streams to a row store", () => {
            string folder = NewFolder("store-large-xlsx");
            var sheet = Spreadsheets.Load(largeXlsx, culture, folder).Sheets[0];
            using var store = RowStore.Open(folder, sheet.Store);
            Check(sheet.RowCount == 500_001 && store.Read(500_000, 1)[0][6] == "Hello last row"); });
    else Console.WriteLine("SKIP 500,000-row workbook fixture (run npm run generate:large in tests/corpus/generate)");
    using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, "manifest.json")));
    var errorWords = new Dictionary<string, string[]> {
        ["damaged"] = ["damaged", "too large to open safely"], ["empty"] = ["empty"], ["password"] = ["password"],
        ["mismatch"] = ["not an Excel workbook", "older Excel file", "contents are not", "older Office file", "binary data"],
        ["unsupported"] = ["not supported", "cannot be opened in this version"], ["too-large"] = ["more than this viewer can"] };

    // Word and PowerPoint: OfficePackages.Prepare must refuse bad packages with the right message and write a
    // copy of good ones with no outside references (other than hyperlinks) and no content-fetching field codes.
    foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
    {
        string format = fixture.GetProperty("format").GetString()!;
        if (!OfficePackages.IsOfficeDocument("x." + format) || fixture.TryGetProperty("generated", out _)) continue;
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
                Check(view.Kind == (OfficePackages.IsWord(path) ? "word" : "slides"));
                using var zip = ZipFile.OpenRead(output);
                // The copy is always a plain document: no macro project, no template/show/macro-enabled main type.
                if (zip.Entries.Any(e => OfficePackages.IsMacroPart(e.FullName))) throw new Exception("The prepared copy still contains a macro project.");
                using (var types = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open()))
                    if (System.Text.RegularExpressions.Regex.IsMatch(types.ReadToEnd(), "macroEnabled|vbaProject|template\\.main|slideshow\\.main", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        throw new Exception("The prepared copy's content types still name a macro, template or show part.");
                if (expect.TryGetProperty("macrosRemoved", out _)) Check(view.Notice.Contains("macros", StringComparison.Ordinal) && view.Notice.Contains("never ran"));
                else Check(!view.Notice.Contains("macros"));
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
        if (!Spreadsheets.IsWorkbook("x." + format) || fixture.TryGetProperty("generated", out _)) continue;
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
                Check(expect.TryGetProperty("macrosRemoved", out _) == view.Notice.Contains("macros"));
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
    // Pictures and plain-text data files: every fixture in the manifest, with the originals left untouched.
    foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
    {
        string file = fixture.GetProperty("file").GetString()!;
        bool picture = ImageFiles.IsImage(file), data = TextFiles.PlainExtensions.Contains(Path.GetExtension(file)) && !file.EndsWith(".txt");
        if (!(picture || data) || fixture.TryGetProperty("generated", out _)) continue;
        var expect = fixture.GetProperty("expect");
        Test((picture ? "Picture fixture " : "Data fixture ") + file, () => {
            string path = Path.Combine(corpus, file.Replace('/', Path.DirectorySeparatorChar));
            byte[] before = SHA256.HashData(File.ReadAllBytes(path)); var siblings = Directory.GetFiles(Path.GetDirectoryName(path)!);
            // "stage: renderer" files pass the app's checks; the sandboxed renderer refuses them (smoke test).
            bool opensHere = expect.GetProperty("result").GetString() == "open" || expect.TryGetProperty("stage", out _);
            if (!opensHere)
            {
                string message = "";
                try { if (picture) ImageFiles.Snapshot(path); else TextFiles.Load(path); } catch (DocumentException ex) { message = ex.Message; }
                var words = errorWords[expect.GetProperty("error").GetString()!];
                if (!words.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase))) throw new Exception($"Expected a {string.Join("/", words)} message, got: '{message}'");
            }
            else if (picture)
            {
                var image = ImageFiles.Snapshot(path);
                if (expect.TryGetProperty("format", out var imageFormat) && image.Format != imageFormat.GetString()) throw new Exception($"Format: got {image.Format}");
                if (expect.TryGetProperty("width", out var width) && width.GetInt32() > 0) Check(image.Width == width.GetInt32() && image.Height == expect.GetProperty("height").GetInt32());
                if (expect.GetProperty("result").GetString() == "open" && image.Incomplete != expect.TryGetProperty("incomplete", out _)) throw new Exception($"Incomplete: got {image.Incomplete}");
            }
            else
            {
                var view = TextFiles.Load(path);
                Check(view.Kind == "text");
                foreach (var text in expect.GetProperty("text").EnumerateArray())
                    if (!view.Text.Contains(text.GetString()!)) throw new Exception($"Text '{text.GetString()}' not shown.");
            }
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))));
            Check(siblings.SequenceEqual(Directory.GetFiles(Path.GetDirectoryName(path)!)));
        });
    }
    Test("Picture formats are recognised by content, not by name", () => {
        byte[] ftyp(string brand) => [0, 0, 0, 24, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(brand), 0, 0, 0, 0, .. "mif1"u8, .. Encoding.ASCII.GetBytes(brand)];
        Check(ImageFiles.Identify(ftyp("avif"))?.Format == "AVIF");
        Check(ImageFiles.Identify(ftyp("heic")) is null);
        string heic = Path.Combine(root, "photo.jpg"); File.WriteAllBytes(heic, ftyp("heic"));
        Check(Throws<DocumentException>(() => ImageFiles.Snapshot(heic)).Message.Contains("HEIC"));
        Check(ImageFiles.Identify(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!-- note --><!DOCTYPE svg><svg xmlns=\"http://www.w3.org/2000/svg\"/>"))?.Format == "SVG");
        Check(ImageFiles.Identify(Encoding.UTF8.GetBytes("<html><body><svg></svg></body></html>")) is null);   // an HTML page is not an SVG
        string gz = Path.Combine(root, "drawing.svg"); File.WriteAllBytes(gz, [0x1f, 0x8b, 8, 0, 0, 0, 0, 0]);
        Check(Throws<DocumentException>(() => ImageFiles.Snapshot(gz)).Message.Contains("compressed"));
        Throws<DocumentException>(() => ImageFiles.Snapshot(@"\\nonexistent.invalid\share\photo.png"));
    });
    Test("Every supported type is registered by the installer, and nothing else", () => {
        string script = File.ReadAllText(Path.Combine(Path.GetDirectoryName(corpus)!, "..", "installer", "PlainViewer.iss"));
        var registered = System.Text.RegularExpressions.Regex.Matches(script, "#define Ext\\[\\d+\\] \"([a-z0-9]+)\"").Select(m => "." + m.Groups[1].Value).ToHashSet();
        var supported = Formats.All.ToHashSet();
        if (!registered.SetEquals(supported)) throw new Exception($"Installer only: {string.Join(" ", registered.Except(supported))}; app only: {string.Join(" ", supported.Except(registered))}");
        Check(Formats.OpenDialogFilter.StartsWith("All supported files|*.pdf;"));
    });
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
