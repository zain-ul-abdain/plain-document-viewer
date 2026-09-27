namespace PlainViewer.Core;

// Reads a PDF into a private in-memory snapshot. Nothing here parses the PDF: PDF.js parses it inside
// WebView2's sandboxed renderer process. The original is opened with shared access and never written.
public static class PdfFiles
{
    public const long SizeLimit = 512L * 1024 * 1024;

    public static byte[] Snapshot(string path)
    {
        TextFiles.ValidateLocalPath(path);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new DocumentException("Only files ending in .pdf open in the PDF view.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        var modified = File.GetLastWriteTimeUtc(path);
        if (length == 0)
            throw new DocumentException("This PDF file is empty (0 bytes). It may not have finished downloading or copying. Get a complete copy and try again.");
        if (length > SizeLimit)
            throw new DocumentException("This PDF is larger than 512 MB, which is more than this viewer can open safely.");
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        if (stream.Length != length || File.GetLastWriteTimeUtc(path) != modified)
            throw new DocumentException("The file changed while it was being opened. Wait until it has finished saving, then open it again.");
        if (!HasPdfHeader(bytes))
            throw new DocumentException("This file is named .pdf, but its contents are not a PDF. Open it with an application for its actual format.");
        return bytes;
    }

    // PDF readers accept a header within the first 1024 bytes.
    public static bool HasPdfHeader(ReadOnlySpan<byte> bytes) => bytes[..Math.Min(bytes.Length, 1024)].IndexOf("%PDF-"u8) >= 0;
}
