using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using PlainViewer.Core;

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
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"Results: {passed} passed, {failed} failed. No UI, Office fidelity, network instrumentation or release performance checks were run.");
return failed == 0 ? 0 : 1;
