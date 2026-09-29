using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlainViewer.Core;
namespace PlainViewer.Worker;

// HEIC/HEIF photos (the format iPhones use). The browser page cannot decode them, so this worker (low integrity, under
// the job's memory and time limits) decodes them with Windows' own HEIF codec and writes a PNG to the work folder,
// which the app shows like any other picture. Nothing is bundled or downloaded: the codec comes from Microsoft's "HEIF
// Image Extensions" and "HEVC Video Extensions", which many PCs already have.
internal static class HeifPictures
{
    public const string Output = "picture.png";
    public const long PixelLimit = 50_000_000;                  // a 48-megapixel phone photo decodes within the worker's memory
    private static readonly Guid HeifContainer = new("e1e62521-6787-405b-a339-500715b5763f");
    public const string MissingCodec = "Windows cannot read HEIC photos on this PC yet. Install \"HEIF Image Extensions\" and \"HEVC Video Extensions\" from the Microsoft Store (Microsoft may charge a small fee for the second one), then open the photo again. Or save it as JPEG in the Photos app.";

    public static DocumentView Load(string path, string folder)
    {
        var picture = ImageFiles.Snapshot(path);                // size limits and change checks, as for other pictures
        if (picture.Format != "HEIF") throw new DocumentException("This picture is shown without conversion.");
        BitmapDecoder decoder;
        try { decoder = BitmapDecoder.Create(new MemoryStream(picture.Bytes, false), BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None); }
        catch (NotSupportedException) { throw new DocumentException(MissingCodec); }
        catch (FileFormatException) { throw Damaged(); }
        // Only Windows' HEIF decoder may read it, never another codec that claims the bytes.
        if (decoder.CodecInfo?.ContainerFormat != HeifContainer || decoder.Frames.Count == 0) throw Damaged();
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > PixelLimit)
            throw new DocumentException($"This photo is {frame.PixelWidth:N0} × {frame.PixelHeight:N0} pixels, which is more than this viewer can show safely.");
        BitmapSource source = frame;
        try { source = Orient(frame, Orientation(frame)); source.Freeze(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FileFormatException or NotSupportedException)
        { throw new DocumentException("Windows could not decode this HEIC photo. It may be damaged, or Windows may be missing \"HEVC Video Extensions\" from the Microsoft Store."); }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var file = new FileStream(Path.Combine(folder, Output), FileMode.CreateNew, FileAccess.Write))
        {
            try { encoder.Save(file); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FileFormatException or NotSupportedException)
            { throw new DocumentException("Windows could not decode this HEIC photo. It may be damaged, or Windows may be missing \"HEVC Video Extensions\" from the Microsoft Store."); }
        }
        return new DocumentView { Kind = "image", Encoding = "HEIC picture", Store = Output, Notice = "HEIC photos are converted by Windows' own decoder for viewing; the original is not changed." };
    }

    private static DocumentException Damaged() => new("This picture is damaged or incomplete, so it cannot be shown. Try another copy of the file.");

    // EXIF-style orientation (1 to 8) as the camera recorded it; 1 when missing.
    private static int Orientation(BitmapFrame frame)
    {
        try { return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value and >= 1 and <= 8 ? value : 1; }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException) { return 1; }
    }

    private static BitmapSource Orient(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1), 3 => new RotateTransform(180), 4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270), _ => null
        };
        return transform is null ? source : new TransformedBitmap(source, transform);
    }
}
