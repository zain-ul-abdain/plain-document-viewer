using System.Diagnostics;
using System.IO;
namespace PlainViewer.App;

// Whether the installer's optional Windows Firewall rules are present (installer/PlainViewer.iss, task "firewall").
// Reading rules needs no administrator rights; the exit code of "netsh ... show rule" does not depend on the language.
internal static class NetworkBlock
{
    private const string Prefix = "Plain Viewer - block network - ";

    public static bool IsOn() => RuleExists("document worker (out)") && RuleExists("converter (out)");

    private static bool RuleExists(string name)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "advfirewall", "firewall", "show", "rule", "name=" + Prefix + name }) start.ArgumentList.Add(argument);
            using var netsh = Process.Start(start)!;
            netsh.StandardOutput.ReadToEnd();
            return netsh.WaitForExit(5000) && netsh.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
}
