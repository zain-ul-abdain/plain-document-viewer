using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PlainViewer.Tests")]
namespace PlainViewer.Core;

// Opens a document for reading and checks the file that was actually opened, not only its path. TextFiles.ValidateLocalPath
// checks the path first (no network or device paths, no links, nothing that is offline); a folder in the path could
// still be replaced by a link between that check and the open. So the file is opened without following a link in its
// last part and without downloading a cloud placeholder, and the open handle must then be a regular, local, available
// file. Change checks use the same handle. Windows still follows a link in a folder of the path while opening, so a
// link created in that moment is caught here but may already have been followed; creating such links needs
// administrator rights or Developer Mode.
public static class LocalFiles
{
    public const string ChangedMessage = "The file changed while it was being opened. Wait until it has finished saving, then open it again.";
    private const string NotLocal = "This file or folder is a link or is not available offline. Copy it to a regular local folder first.";

    public const string MovedMessage = "The file was moved or deleted. Choose it again from its current location.";
    public const string DeniedMessage = "The file cannot be read. Check its permissions or copy it to a local folder.";
    public const string LockedMessage = "The file is open in another program that does not let others read it. Close it there, then open it again.";

    // The same plain messages wherever a document is opened (by the worker or, for PDFs and pictures, by the app).
    public static FileStream OpenRead(string path)
    {
        try
        {
            TextFiles.ValidateLocalPath(path);
            return OpenChecked(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new DocumentException(MovedMessage); }
        catch (UnauthorizedAccessException) { throw new DocumentException(DeniedMessage); }
    }

    // The open and the handle checks alone (tests call this to show they work without the path check before them).
    internal static FileStream OpenChecked(string path)
    {
        // FILE_FLAG_OPEN_REPARSE_POINT: a link is opened itself, not followed (and then refused below).
        // FILE_FLAG_OPEN_NO_RECALL: a cloud placeholder is not downloaded by opening it.
        const uint Read = 0x80000000, ShareAll = 0x7, OpenExisting = 3, ReparsePoint = 0x00200000, NoRecall = 0x00100000, Sequential = 0x08000000;
        var handle = CreateFile(path, Read, ShareAll, IntPtr.Zero, OpenExisting, ReparsePoint | NoRecall | Sequential, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw error switch
            {
                2 or 3 => new FileNotFoundException("The file was not found.", path),
                5 => new UnauthorizedAccessException(),
                32 or 33 => new DocumentException(LockedMessage),
                _ => new IOException(new Win32Exception(error).Message)
            };
        }
        try
        {
            Check(handle);
            return new FileStream(handle, FileAccess.Read, 1 << 16);
        }
        catch { handle.Dispose(); throw; }
    }

    // The opened file must be an ordinary file (not a link, folder or offline file) on a local drive.
    private static void Check(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out AttributeTagInfo info, (uint)Marshal.SizeOf<AttributeTagInfo>()))   // FileAttributeTagInfo
            throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        const uint Directory = 0x10, Reparse = 0x400, Offline = 0x1000, RecallOnOpen = 0x40000, RecallOnDataAccess = 0x400000;
        if ((info.Attributes & (Directory | Reparse | Offline | RecallOnOpen | RecallOnDataAccess)) != 0) throw new DocumentException(NotLocal);
        // The opened file's own path: "\\?\C:\..." for a drive letter; anything else (a network share, a volume without a
        // drive letter) is refused.
        var buffer = new StringBuilder(1024);
        uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new DocumentException(NotLocal);
        string final = buffer.ToString();
        if (!final.StartsWith(@"\\?\", StringComparison.Ordinal) || final.Length < 7 || final[5] != ':' || !char.IsAsciiLetter(final[4]))
            throw new DocumentException(NotLocal);
        if (new DriveInfo(final.Substring(4, 3)).DriveType is DriveType.Network or DriveType.NoRootDirectory)
            throw new DocumentException("Copy this file to a local drive before opening it.");
    }

    // The file's size and last change, from the open handle (the path might name a different file by now).
    public static (long Length, DateTime Modified) Stamp(FileStream stream) => (RandomAccess.GetLength(stream.SafeFileHandle), File.GetLastWriteTimeUtc(stream.SafeFileHandle));

    public static void ThrowIfChanged(FileStream stream, (long Length, DateTime Modified) before)
    {
        if (Stamp(stream) != before) throw new DocumentException(ChangedMessage);
    }

    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public uint Attributes, ReparseTag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out AttributeTagInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
