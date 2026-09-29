using System.Buffers.Binary;
using System.Text;
namespace PlainViewer.Core;

// Reads a picture into a private in-memory snapshot and identifies its format from its bytes. Nothing here decodes
// pixels: the picture is decoded inside WebView2's sandboxed renderer process. SVG is shown only as a picture (an
// <img>), where scripts never run and nothing outside the file is loaded. The original is never written.
public static class ImageFiles
{
    public const long SizeLimit = 100L * 1024 * 1024;
    public const long PixelLimit = 200_000_000;   // about 14,000 × 14,000; larger pictures would exhaust memory
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".ico", ".webp", ".avif", ".svg"];
    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public sealed record Picture(byte[] Bytes, string ContentType, string Format, int Width, int Height)
    {
        // Cut short (for example an interrupted download): the renderer shows what it can, and the viewer says so.
        public bool Incomplete => !IsComplete(Bytes, Format);
    }

    // Each format's own end marker or length fields.
    public static bool IsComplete(ReadOnlySpan<byte> s, string format)
    {
        switch (format)
        {
            case "PNG": return s[Math.Max(0, s.Length - 64)..].IndexOf("IEND"u8) >= 0;
            case "JPEG": return s[Math.Max(0, s.Length - 4096)..].IndexOf(new byte[] { 0xff, 0xd9 }) >= 0;
            case "GIF": return s[Math.Max(0, s.Length - 16)..].IndexOf((byte)0x3b) >= 0;
            case "WebP": return 8L + BinaryPrimitives.ReadUInt32LittleEndian(s[4..]) <= s.Length;
            case "BMP": { uint size = BinaryPrimitives.ReadUInt32LittleEndian(s[2..]); return size == 0 || size <= s.Length; }
            case "icon":
                {
                    int count = BinaryPrimitives.ReadUInt16LittleEndian(s[4..]);
                    for (int i = 0; i < count; i++)
                    {
                        int entry = 6 + i * 16;
                        if (entry + 16 > s.Length) return false;
                        if ((long)BinaryPrimitives.ReadUInt32LittleEndian(s[(entry + 12)..]) + BinaryPrimitives.ReadUInt32LittleEndian(s[(entry + 8)..]) > s.Length) return false;
                    }
                    return true;
                }
            case "AVIF":
                {
                    long offset = 0;
                    while (offset + 8 <= s.Length)
                    {
                        long size = BinaryPrimitives.ReadUInt32BigEndian(s[(int)offset..]);
                        if (size == 1 && offset + 16 <= s.Length) size = (long)BinaryPrimitives.ReadUInt64BigEndian(s[(int)(offset + 8)..]);
                        else if (size == 0) return true;   // the last box runs to the end of the file
                        if (size < 8) return false;
                        offset += size;
                    }
                    return offset == s.Length;
                }
            default: return true;
        }
    }

    public static Picture Snapshot(string path)
    {
        TextFiles.ValidateLocalPath(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!IsImage(path)) throw new DocumentException($"{extension} files do not open in the picture view.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        var modified = File.GetLastWriteTimeUtc(path);
        if (length == 0)
            throw new DocumentException("This picture is empty (0 bytes). It may not have finished downloading or copying. Get a complete copy and try again.");
        if (length > SizeLimit)
            throw new DocumentException("This picture is larger than 100 MB, which is more than this viewer can open safely.");
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        if (stream.Length != length || File.GetLastWriteTimeUtc(path) != modified)
            throw new DocumentException("The file changed while it was being opened. Wait until it has finished saving, then open it again.");
        var picture = Identify(bytes) ?? throw Unrecognised(bytes, extension);
        if ((long)picture.Width * picture.Height > PixelLimit)
            throw new DocumentException($"This picture is {picture.Width:N0} × {picture.Height:N0} pixels, which is more than this viewer can show safely.");
        return picture;
    }

    // Format, content type and (where the header states them) dimensions; null if the bytes are not a supported picture.
    public static Picture? Identify(byte[] b)
    {
        ReadOnlySpan<byte> s = b;
        if (s.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return s.Length >= 24 ? new(b, "image/png", "PNG", (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(s[16..])), (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(s[20..]))) : Damaged();
        if (s.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) { var (w, h) = JpegSize(s); return new(b, "image/jpeg", "JPEG", w, h); }
        if (s.StartsWith("GIF87a"u8) || s.StartsWith("GIF89a"u8))
            return s.Length >= 10 ? new(b, "image/gif", "GIF", BinaryPrimitives.ReadUInt16LittleEndian(s[6..]), BinaryPrimitives.ReadUInt16LittleEndian(s[8..])) : Damaged();
        if (s.StartsWith("BM"u8))
            return s.Length >= 26 ? new(b, "image/bmp", "BMP", Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(s[18..])), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(s[22..]))) : Damaged();
        if (s.Length >= 6 && s.StartsWith(new byte[] { 0, 0, 1, 0 }) && BinaryPrimitives.ReadUInt16LittleEndian(s[4..]) > 0)
            return new(b, "image/x-icon", "icon", 256, 256);
        if (s.Length >= 16 && s.StartsWith("RIFF"u8) && s[8..12].SequenceEqual("WEBP"u8)) { var (w, h) = WebPSize(s); return new(b, "image/webp", "WebP", w, h); }
        if (Brands(s) is { } brands && (brands.Contains("avif") || brands.Contains("avis"))) return new(b, "image/avif", "AVIF", 0, 0);
        if (IsSvg(s)) return new(b, "image/svg+xml", "SVG", 0, 0);
        return null;
    }

    private static Picture Damaged() => throw new DocumentException("This picture is damaged or incomplete, so it cannot be shown. Try another copy of the file.");

    private static DocumentException Unrecognised(byte[] b, string extension)
    {
        ReadOnlySpan<byte> s = b;
        if (Brands(s) is { } brands && brands.Any(x => x is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1"))
            return new("This is a HEIC/HEIF photo (the format iPhones use). It cannot be opened in this version. Save it as JPEG or PNG in the Photos app to view it here.");
        if (s.StartsWith("II*\0"u8) || s.StartsWith("MM\0*"u8))
            return new($"This is a TIFF picture, but its name ends in {extension}. Rename it to end in .tif to view it.");
        if (s.StartsWith(new byte[] { 0x1f, 0x8b }))
            return new("This is a compressed file (for example a compressed SVG), which cannot be opened as a picture. Unpack it first.");
        return new($"This file is named {extension}, but its contents are not a picture this viewer can show. Open it with an application for its actual format.");
    }

    // ISO base media "ftyp" brands (AVIF and HEIC share this container).
    private static List<string>? Brands(ReadOnlySpan<byte> s)
    {
        if (s.Length < 16 || !s[4..8].SequenceEqual("ftyp"u8)) return null;
        int size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(s), (uint)Math.Min(s.Length, 4096));
        var brands = new List<string> { Encoding.ASCII.GetString(s[8..12]) };
        for (int i = 16; i + 4 <= size; i += 4) brands.Add(Encoding.ASCII.GetString(s.Slice(i, 4)));
        return brands;
    }

    // Start-of-frame dimensions; (0, 0) if the header ends first (the renderer then reports the problem).
    private static (int, int) JpegSize(ReadOnlySpan<byte> s)
    {
        int i = 2;
        while (i + 9 < s.Length)
        {
            if (s[i] != 0xff) { i++; continue; }
            byte marker = s[i + 1];
            if (marker is 0xd8 or 0x01 or (>= 0xd0 and <= 0xd7) or 0xff) { i += marker == 0xff ? 1 : 2; continue; }
            int length = BinaryPrimitives.ReadUInt16BigEndian(s[(i + 2)..]);
            if (marker is >= 0xc0 and <= 0xcf and not 0xc4 and not 0xc8 and not 0xcc)
                return (BinaryPrimitives.ReadUInt16BigEndian(s[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(s[(i + 5)..]));
            if (length < 2) break;
            i += 2 + length;
        }
        return (0, 0);
    }

    private static (int, int) WebPSize(ReadOnlySpan<byte> s)
    {
        if (s.Length >= 30 && s[12..16].SequenceEqual("VP8X"u8))
            return (1 + (s[24] | s[25] << 8 | s[26] << 16), 1 + (s[27] | s[28] << 8 | s[29] << 16));
        if (s.Length >= 30 && s[12..16].SequenceEqual("VP8 "u8))
            return (BinaryPrimitives.ReadUInt16LittleEndian(s[26..]) & 0x3fff, BinaryPrimitives.ReadUInt16LittleEndian(s[28..]) & 0x3fff);
        if (s.Length >= 25 && s[12..16].SequenceEqual("VP8L"u8))
        {
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(s[21..]);
            return ((int)(bits & 0x3fff) + 1, (int)((bits >> 14) & 0x3fff) + 1);
        }
        return (0, 0);
    }

    // SVG is XML text whose root element is <svg>. Binary data, or text that never reaches an <svg> element, is not.
    private static bool IsSvg(ReadOnlySpan<byte> s)
    {
        var head = s[..Math.Min(s.Length, 65536)];
        if (head.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) head = head[3..];
        if (head.IndexOf((byte)0) >= 0) return false;
        string text = Encoding.UTF8.GetString(head);
        int start = 0;
        while (true)
        {
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            if (start >= text.Length) return false;
            if (string.CompareOrdinal(text, start, "<svg", 0, 4) == 0) return true;
            int end = text.StartsWith("<!--", start) ? text.IndexOf("-->", start, StringComparison.Ordinal) + 3
                : text.StartsWith("<!DOCTYPE", start) ? DoctypeEnd(text, start)
                : text.StartsWith("<?", start) || text.StartsWith("<!", start) ? text.IndexOf('>', start) + 1 : 0;
            if (end <= start) return false;   // anything other than a declaration, comment or doctype before <svg>
            start = end;
        }
    }

    // A doctype may carry an internal subset in [...] whose declarations contain '>'.
    private static int DoctypeEnd(string text, int start)
    {
        int close = text.IndexOf('>', start), open = text.IndexOf('[', start);
        if (open < 0 || (close >= 0 && close < open)) return close + 1;
        int end = text.IndexOf("]", open, StringComparison.Ordinal);
        return end < 0 ? 0 : text.IndexOf('>', end) + 1;
    }
}

internal static class StringStart
{
    public static bool StartsWith(this string text, string value, int index) => string.CompareOrdinal(text, index, value, 0, value.Length) == 0;
}
