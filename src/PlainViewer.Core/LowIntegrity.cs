using System.ComponentModel;
using System.Runtime.InteropServices;
namespace PlainViewer.Core;

// Windows mandatory integrity control for the processes that read documents (the worker and LibreOffice).
// A low-integrity process cannot write to the user's files or registry and cannot open other programs to change
// or inject into them. It can still read files, and it still has network access unless the optional firewall
// rules are installed (DECISIONS.md, gate 1). Anything it writes must be under Root, which Windows labels low.
public static class LowIntegrity
{
    private const string LowLabel = "S-1-16-4096";
    private const int TokenIntegrityLevel = 25;
    private const uint TokenAssignPrimary = 0x1, TokenDuplicate = 0x2, TokenQuery = 0x8, TokenAdjustDefault = 0x80;

    // %USERPROFILE%\AppData\LocalLow\PlainViewer
    public static string Root => Path.Combine(KnownFolder(new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16")), "PlainViewer");

    // Drops the calling process to low integrity. Cannot be undone; the worker calls it before reading any document.
    public static void LowerCurrentProcess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustDefault | TokenQuery, out var token)) throw new Win32Exception();
        try { SetLow(token); }
        finally { CloseHandle(token); }
        if (!IsCurrentProcessLow()) throw new InvalidOperationException("The process is still above low integrity.");
    }

    // A copy of the caller's token at low integrity, for CreateProcessAsUser. The caller closes it.
    public static IntPtr CreateToken()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenDuplicate | TokenAdjustDefault | TokenQuery | TokenAssignPrimary, out var own)) throw new Win32Exception();
        try
        {
            if (!DuplicateTokenEx(own, 0, IntPtr.Zero, 2 /* impersonation level */, 1 /* primary token */, out var low)) throw new Win32Exception();
            try { SetLow(low); return low; }
            catch { CloseHandle(low); throw; }
        }
        finally { CloseHandle(own); }
    }

    public static bool IsCurrentProcessLow()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token)) throw new Win32Exception();
        try
        {
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out uint size);
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) throw new Win32Exception();
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                int last = Marshal.ReadByte(GetSidSubAuthorityCount(sid)) - 1;
                return (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)last)) <= 0x1000;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }

    private static void SetLow(IntPtr token)
    {
        if (!ConvertStringSidToSid(LowLabel, out var sid)) throw new Win32Exception();
        try
        {
            var label = new MandatoryLabel { Sid = sid, Attributes = 0x20 /* SE_GROUP_INTEGRITY */ };
            if (!SetTokenInformation(token, TokenIntegrityLevel, ref label, (uint)(Marshal.SizeOf<MandatoryLabel>() + GetLengthSid(sid)))) throw new Win32Exception();
        }
        finally { LocalFree(sid); }
    }

    private static string KnownFolder(Guid id)
    {
        int result = SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var path);
        try { if (result != 0) Marshal.ThrowExceptionForHR(result); return Marshal.PtrToStringUni(path)!; }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct MandatoryLabel { public IntPtr Sid; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int level, int type, out IntPtr duplicate);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref MandatoryLabel info, uint length);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, uint length, out uint returned);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSidToSid(string sid, out IntPtr result);
    [DllImport("advapi32.dll")] private static extern uint GetLengthSid(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
