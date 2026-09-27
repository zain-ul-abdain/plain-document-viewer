using System.Diagnostics;
using System.Runtime.InteropServices;
namespace PlainViewer.App;

// Peak memory of the processes started by this one (WebView2's browser, renderer and GPU processes), for
// scripts/measure.ps1. Only processes still running are counted; the worker and LibreOffice report through their jobs.
internal static class ProcessTree
{
    public static long PeakBytesOfDescendants(int root)
    {
        var parents = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(0x2, 0);   // processes
        if (snapshot == new IntPtr(-1)) return 0;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        }
        finally { CloseHandle(snapshot); }
        long total = 0;
        foreach (var (id, _) in parents)
        {
            // Descendant of root: follow parents upward (bounded, in case of reused process ids).
            int current = id;
            for (int depth = 0; depth < 8 && parents.TryGetValue(current, out int parent) && parent != current; depth++)
            {
                if (parent != root) { current = parent; continue; }
                try { using var process = Process.GetProcessById(id); total += process.PeakWorkingSet64; } catch (ArgumentException) { } catch (InvalidOperationException) { }
                break;
            }
        }
        return total;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId; public IntPtr DefaultHeapId; public uint ModuleId, Threads, ParentProcessId; public int PriorityClassBase; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
