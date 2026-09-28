namespace PlainViewer.Core;

// A full disk shows up as an IOException from whichever write fails first (work folder, row store, converter output).
// Both the worker and the app turn it into this message instead of a generic or misleading one.
public static class DiskSpace
{
    public const string Message = "There is not enough free disk space to open this file. Free some space on the drive that holds your user folder (usually C:), then open the file again.";

    // ERROR_HANDLE_DISK_FULL (39) and ERROR_DISK_FULL (112), as the HRESULTs .NET gives them.
    public static bool IsFull(Exception ex) => ex is IOException { HResult: unchecked((int)0x80070027) or unchecked((int)0x80070070) };
}
