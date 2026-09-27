using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using PlainViewer.Core;
using System.Diagnostics;

// Run preparation in a separate process: dropping integrity is intentionally irreversible.
if (args.FirstOrDefault() == "--low-prepare")
{
    LowIntegrity.LowerCurrentProcess();
    if (!LowIntegrity.IsCurrentProcessLow()) return 3;
    try { OfficePackages.Prepare(args[1], args[2]); Console.WriteLine("LOW:PREPARED"); return 0; }
    catch (Exception ex) when (ex is IOException or DocumentException) { Console.WriteLine("LOW:REJECTED"); return 2; }
}

int passed = 0, failed = 0;
string root = Path.Combine(Path.GetTempPath(), "PlainViewerOfficeSafety-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string lowRoot = Path.Combine(LowIntegrity.Root, "office-safety-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(lowRoot);
string LowPrepare(string input, string output)
{
    string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in new[] { System.Reflection.Assembly.GetExecutingAssembly().Location, "--low-prepare", input, output }) start.ArgumentList.Add(argument);
    using var child = Process.Start(start) ?? throw new Exception("Could not start low-integrity test child");
    var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
    if (!child.WaitForExit(20000)) { child.Kill(true); throw new Exception("Low-integrity preparation timed out"); }
    if (child.ExitCode is not (0 or 2)) throw new Exception("Child failed: " + stderr.GetAwaiter().GetResult());
    return stdout.GetAwaiter().GetResult().Trim();
}
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Test(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); passed++; } catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; } }
void Reject(Action action) { try { action(); } catch (IOException) { return; } catch (DocumentException) { return; } throw new Exception("Expected rejection"); }
string Package(string relationships = "", bool invalidXml = false)
{
    string path = Path.Combine(root, Guid.NewGuid() + ".docx");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    void Part(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
    Part("word/document.xml", invalidXml ? "<broken>" : "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p></w:body></w:document>");
    if (relationships.Length > 0) Part("word/_rels/document.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" + relationships + "</Relationships>");
    return path;
}
string Output() => Path.Combine(root, Guid.NewGuid() + ".docx");
XDocument Relationships(string path) { using var zip = ZipFile.OpenRead(path); using var entry = zip.GetEntry("word/_rels/document.xml.rels")!.Open(); using var reader = ArchiveSafety.CreateXmlReader(entry); return XDocument.Load(reader); }
const string Image = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
const string Link = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
try
{
    Test("Existing output is not deleted or changed", () => { string source = Package(), output = Output(); File.WriteAllText(output, "existing user file"); Reject(() => OfficePackages.Prepare(source, output)); Check(File.ReadAllText(output) == "existing user file"); });
    Test("Source passed as output remains intact", () => { string source = Package(); var hash = SHA256.HashData(File.ReadAllBytes(source)); Reject(() => OfficePackages.Prepare(source, source)); Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))); });
    Test("Corrupt input cannot delete existing output", () => { string source = Package(invalidXml: true), output = Output(); File.WriteAllText(output, "keep me"); Reject(() => OfficePackages.Prepare(source, output)); Check(File.ReadAllText(output) == "keep me"); });
    Test("New incomplete output is removed on parse failure", () => { string source = Package(invalidXml: true), output = Output(); Reject(() => OfficePackages.Prepare(source, output)); Check(!File.Exists(output)); Check(File.Exists(source)); });
    Test("Explicit closing relationship preserves next internal sibling", () => { string source = Package($"<Relationship Id='outside' Type='{Image}' TargetMode='External' Target='https://example.invalid/a'></Relationship><Relationship Id='inside' Type='{Image}' Target='media/image.png'/>"); string output = Output(); OfficePackages.Prepare(source, output); var rows = Relationships(output).Root!.Elements().ToArray(); Check(rows.Length == 1 && (string?)rows[0].Attribute("Id") == "inside"); });
    Test("Last removed relationship preserves closing parent", () => { string source = Package($"<Relationship Id='outside' Type='{Image}' TargetMode='External' Target='https://example.invalid/a'></Relationship>"); string output = Output(); OfficePackages.Prepare(source, output); Check(!Relationships(output).Root!.Elements().Any()); });
    Test("Unsafe hyperlink schemes are removed", () => { string source = Package(string.Join("", new[] { "javascript:alert", "file:///C:/secret", @"\\server\share\file", "data:text/html,bad" }.Select((url, i) => $"<Relationship Id='r{i}' Type='{Link}' TargetMode='External' Target='{url}'/>"))); string output = Output(); OfficePackages.Prepare(source, output); Check(!Relationships(output).Root!.Elements().Any()); });
    Test("HTTP HTTPS and mailto hyperlinks remain", () => { string source = Package(string.Join("", new[] { "http://example.com", "https://example.com", "mailto:a@example.com" }.Select((url, i) => $"<Relationship Id='r{i}' Type='{Link}' TargetMode='External' Target='{url}'/>"))); string output = Output(); OfficePackages.Prepare(source, output); Check(Relationships(output).Root!.Elements().Count() == 3); });
    Test("Absolute remote target without External marker is removed", () => { string source = Package($"<Relationship Id='r' Type='{Image}' Target='https://example.invalid/a'/>"); string output = Output(); OfficePackages.Prepare(source, output); Check(!Relationships(output).Root!.Elements().Any()); });
    Test("Internal parent-relative relationship remains", () => { string source = Package($"<Relationship Id='r' Type='{Image}' Target='../media/image.png'/>"); string output = Output(); OfficePackages.Prepare(source, output); Check(Relationships(output).Root!.Elements().Count() == 1); });
    Test("Package-root relative relationship remains", () => { string source = Package($"<Relationship Id='r' Type='{Image}' Target='/word/media/image.png'/>"); string output = Output(); OfficePackages.Prepare(source, output); Check(Relationships(output).Root!.Elements().Count() == 1); });
    Test("Low-integrity preparation writes under LocalLow", () => { string source = Package(), output = Path.Combine(lowRoot, "prepared.docx"); var hash = SHA256.HashData(File.ReadAllBytes(source)); Check(LowPrepare(source, output) == "LOW:PREPARED"); using var zip = ZipFile.OpenRead(output); Check(zip.GetEntry("word/document.xml") is not null); Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))); });
    Test("Low-integrity collision preserves existing output", () => { string source = Package(), output = Path.Combine(lowRoot, "existing.docx"); File.WriteAllText(output, "preserve this"); Check(LowPrepare(source, output) == "LOW:REJECTED"); Check(File.ReadAllText(output) == "preserve this"); });
    Test("Low-integrity failure removes only its new output", () => { string source = Package(invalidXml: true), output = Path.Combine(lowRoot, "failed.docx"); Check(LowPrepare(source, output) == "LOW:REJECTED"); Check(!File.Exists(output) && File.Exists(source)); });
}
finally { Directory.Delete(root, true); Directory.Delete(lowRoot, true); }
Console.WriteLine($"Office safety regressions: {passed} passed, {failed} failed. No LibreOffice process or network request is needed for these tests.");
return failed == 0 ? 0 : 1;
