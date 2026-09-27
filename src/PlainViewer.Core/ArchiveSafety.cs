using System.IO.Compression;
using System.Xml;
namespace PlainViewer.Core;

// Foundation for Office ingestion; no Office format is enabled by these helpers.
public static class ArchiveSafety
{
    public static XmlReader CreateXmlReader(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024,
        MaxCharactersFromEntities = 1024, CloseInput = false });
    public static void Validate(ZipArchive archive, long maximumBytes = 128 * 1024 * 1024, int maximumEntries = 10000, long maximumRatio = 200)
    {
        if (archive.Entries.Count > maximumEntries) throw new DocumentException("This file is damaged or exceeds safe archive limits.");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > maximumBytes - total || entry.Length / Math.Max(1, entry.CompressedLength) > maximumRatio ||
                entry.FullName.Replace('\\', '/').Split('/').Any(s => s == "..") || entry.FullName.StartsWith('/') || entry.FullName.Contains(':'))
                throw new DocumentException("This file is damaged or exceeds safe archive limits.");
            total += entry.Length;
        }
    }
}
