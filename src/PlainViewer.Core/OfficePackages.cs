using System.IO.Compression;
using System.Text;
using System.Xml;
namespace PlainViewer.Core;

// Prepares a Word or PowerPoint package for conversion. Runs in the worker, before LibreOffice sees the file:
// refuses encrypted, macro-enabled, mislabelled and malformed packages, then writes a private copy with every
// reference to outside content removed (pictures, templates or objects stored elsewhere) and content-fetching field
// codes blanked. Web and email hyperlinks are kept; the viewer asks before opening them.
public static class OfficePackages
{
    public const long SizeLimit = 256L * 1024 * 1024;
    private const long PartByteLimit = 512L * 1024 * 1024;
    private static readonly string[] FetchingFields = ["INCLUDEPICTURE", "INCLUDETEXT", "LINK", "DDE", "DDEAUTO", "IMPORT", "DATABASE"];

    public static bool IsOfficeDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".pptx" or ".docm" or ".pptm" or ".dotx" or ".dotm" or ".potx" or ".potm" or ".ppsx" or ".ppsm";

    public static DocumentView Prepare(string path, string output)
    {
        TextFiles.ValidateLocalPath(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".docx" or ".pptx"))
            throw new DocumentException($"{extension} files are not supported yet. Save the file as .docx or .pptx, without macros, to view it here.");
        bool word = extension == ".docx";
        string kind = word ? "Word document" : "PowerPoint presentation";

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        var modified = File.GetLastWriteTimeUtc(path);
        if (length == 0) throw new DocumentException($"This {kind} is empty (0 bytes). It may not have finished downloading or copying. Get a complete copy and try again.");
        if (length > SizeLimit) throw new DocumentException($"This {kind} is larger than 256 MB, which is more than this viewer can open safely.");
        byte[] head = new byte[Math.Min(length, 65536)];
        stream.ReadExactly(head); stream.Position = 0;
        if (head.AsSpan().StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }))
            throw new DocumentException(head.AsSpan().IndexOf(Encoding.Unicode.GetBytes("EncryptionInfo")) >= 0
                ? $"This {kind} is protected with a password. Password-protected Office files cannot be opened in this version. Remove the password in Office, or ask the sender for an unprotected copy."
                : $"This looks like an older Office file ({(word ? ".doc" : ".ppt")}) saved with a {extension} name. Older formats are not supported yet.");
        if (!head.AsSpan().StartsWith("PK\u0003\u0004"u8))
            throw new DocumentException($"This file is named {extension}, but its contents are not a {kind}. Open it with an application for its actual format.");

        int removed = 0;
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            ArchiveSafety.Validate(zip, maximumBytes: 2L * 1024 * 1024 * 1024, maximumEntries: 10000, maximumRatio: 500);
            if (zip.GetEntry(word ? "word/document.xml" : "ppt/presentation.xml") is null)
                throw new DocumentException($"This file is named {extension}, but its contents are not a {kind}. Open it with an application for its actual format.");
            if (zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("vbaData.xml", StringComparison.OrdinalIgnoreCase)))
                throw new DocumentException($"This {kind} contains macros. Files with macros are not supported yet, and macros would never run here. Save it without macros to view it.");

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var target = new ZipArchive(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None), ZipArchiveMode.Create);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var input = new LimitedStream(entry.Open(), PartByteLimit);
                using var destination = copy.Open();
                if (entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) removed += CopyXml(input, destination, Mode.Relationships);
                else if (word && entry.FullName.StartsWith("word/", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) removed += CopyXml(input, destination, Mode.WordFields);
                else if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) CopyXml(input, destination, Mode.Validate);
                else input.CopyTo(destination);
            }
        }
        catch (InvalidDataException) { Discard(output); throw Damaged(kind); }
        catch (XmlException) { Discard(output); throw Damaged(kind); }
        catch { Discard(output); throw; }
        if (File.GetLastWriteTimeUtc(path) != modified || new FileInfo(path).Length != length)
        { Discard(output); throw new DocumentException("The file changed while it was being opened. Wait until it has finished saving, then open it again."); }
        return new DocumentView
        {
            Kind = word ? "word" : "slides",
            Encoding = word ? "Word document" : "PowerPoint presentation",
            Notice = removed > 0 ? $"{removed} reference{(removed == 1 ? "" : "s")} to content stored outside this file {(removed == 1 ? "was" : "were")} removed before display, so linked pictures or templates are not shown." : ""
        };
    }

    private static DocumentException Damaged(string kind) => new($"This {kind} is damaged or incomplete, so it cannot be shown. Try another copy of the file.");
    private static void Discard(string output) { try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { } }

    private enum Mode { Validate, Relationships, WordFields }

    // Streams one XML part through a reader with DTDs prohibited and writes it back, dropping outside references
    // (Relationships) or blanking content-fetching field codes (WordFields). Returns how many items were removed.
    private static int CopyXml(Stream input, Stream output, Mode mode)
    {
        int removed = 0;
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 1024, IgnoreComments = true, CloseInput = false });
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        bool inInstruction = false;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.XmlDeclaration:
                    writer.WriteStartDocument(reader.GetAttribute("standalone") == "yes");
                    break;
                case XmlNodeType.Element:
                    if (mode == Mode.Relationships && reader.LocalName == "Relationship" && reader.GetAttribute("TargetMode") == "External"
                        && !(reader.GetAttribute("Type") ?? "").EndsWith("/hyperlink", StringComparison.Ordinal))
                    {
                        removed++;
                        if (!reader.IsEmptyElement) reader.Skip();
                        continue;
                    }
                    bool empty = reader.IsEmptyElement;
                    writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            string value = reader.Value;
                            if (mode == Mode.WordFields && reader.LocalName == "instr" && IsFetchingField(value)) { value = ""; removed++; }
                            writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, value);
                        } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                    if (empty) writer.WriteEndElement();
                    else if (mode == Mode.WordFields && reader.LocalName == "instrText") inInstruction = true;
                    break;
                case XmlNodeType.EndElement:
                    if (reader.LocalName == "instrText") inInstruction = false;
                    writer.WriteFullEndElement();
                    break;
                case XmlNodeType.Text:
                    if (inInstruction && IsFetchingField(reader.Value)) { removed++; writer.WriteString(""); }
                    else writer.WriteString(reader.Value);
                    break;
                case XmlNodeType.CDATA: writer.WriteCData(reader.Value); break;
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace: writer.WriteWhitespace(reader.Value); break;
                case XmlNodeType.ProcessingInstruction: writer.WriteProcessingInstruction(reader.Name, reader.Value); break;
            }
        }
        return removed;
    }

    private static bool IsFetchingField(string instruction)
    {
        var first = instruction.TrimStart().Split([' ', '\t', '\\', '"'], 2)[0];
        return FetchingFields.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            read += n;
            if (read > limit) throw new DocumentException("This file is damaged or too large to open safely. Try another copy of the file.");
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
