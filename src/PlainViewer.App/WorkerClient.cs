using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PlainViewer.Core;
namespace PlainViewer.App;

internal static class WorkerClient
{
    public static Task<DocumentView> Load(string path, string encoding, string delimiter, CancellationToken cancellation) =>
        Run([path, encoding, delimiter], cancellation);

    // Validates a Word/PowerPoint package in the worker and writes a sanitised copy to `output` for conversion.
    public static Task<DocumentView> PrepareOffice(string path, string output, CancellationToken cancellation) =>
        Run([path, "--prepare-office", output], cancellation);

    private static async Task<DocumentView> Run(string[] arguments, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        // Installed builds carry .NET with them and ship the worker as an .exe beside the app. Development builds run
        // the worker DLL with the local .NET host that scripts/env.ps1 sets.
        string published = Path.Combine(AppContext.BaseDirectory, "PlainViewer.Worker.exe");
        if (File.Exists(published) && File.Exists(Path.ChangeExtension(published, ".dll"))) start.FileName = published;
        else
        {
            start.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "worker", "PlainViewer.Worker.dll"));
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new DocumentException("The document worker could not start. Rebuild the application and try again.");
        try
        {
            using var job = new WorkerJob(process);
            await process.StandardInput.WriteLineAsync("START"); process.StandardInput.Close();
            var errorDrain = process.StandardError.ReadToEndAsync(timeout.Token);
            var text = new StringBuilder(); char[] buffer = new char[8192]; int count;
            while ((count = await process.StandardOutput.ReadAsync(buffer, timeout.Token)) != 0)
            { if (text.Length + count > 32 * 1024 * 1024) throw new DocumentException("This document exceeds the preview display limit."); text.Append(buffer, 0, count); }
            await process.WaitForExitAsync(timeout.Token); await errorDrain;
            var response = JsonSerializer.Deserialize<WorkerResponse>(text.ToString());
            if (response?.Error is { } error) throw new DocumentException(error);
            return response?.Document ?? throw new DocumentException("The document worker stopped before finishing. Try a smaller file.");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new DocumentException("Opening took longer than 20 seconds. Try a smaller file."); }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}

// Resource limits. Privileges are reduced by the worker itself, which drops to low integrity before reading (Program.cs).
internal sealed class WorkerJob : IDisposable
{
    private readonly IntPtr handle;
    public WorkerJob(Process process)
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = 0x2000 | 0x100 | 0x8; // kill on close, process memory, active process count
        limits.Basic.ActiveProcessLimit = 1; limits.ProcessMemoryLimit = (UIntPtr)(256UL * 1024 * 1024);
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(handle, process.Handle))
        { int error = Marshal.GetLastWin32Error(); Dispose(); throw new System.ComponentModel.Win32Exception(error); }
    }
    public void Dispose() { if (handle != IntPtr.Zero) CloseHandle(handle); }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr Minimum, Maximum; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int info, ref ExtendedLimits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
