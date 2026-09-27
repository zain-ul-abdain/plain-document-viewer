# Switches Windows to a high-contrast theme, captures each view of the app for review, then switches back.
# CHANGES A SYSTEM SETTING, so sandbox-inner.ps1 runs it inside Windows Sandbox only.
# Usage: high-contrast-capture.ps1 -App <PlainViewer.exe> -Corpus <tests\corpus folder> -Out <folder for PNG files>
param([Parameter(Mandatory)][string]$App, [Parameter(Mandatory)][string]$Corpus, [Parameter(Mandatory)][string]$Out, [switch]$NoWeb)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class HighContrast {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  struct Settings { public int Size; public int Flags; public string Scheme; }
  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern bool SystemParametersInfo(int action, int parameter, ref Settings value, int update);
  // SPI_SETHIGHCONTRAST, HCF_HIGHCONTRASTON, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
  public static bool Set(bool on, string scheme) {
    var s = new Settings { Size = Marshal.SizeOf(typeof(Settings)), Flags = on ? 1 : 0, Scheme = scheme };
    return SystemParametersInfo(0x43, s.Size, ref s, 3);
  }
}
'@
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$on = [HighContrast]::Set($true, 'High Contrast Black')
Start-Sleep -Seconds 3
try {
  $env:PLAINVIEWER_CAPTURE_DIR = $Out
  $samples = @('complex.markdown', 'complex.csv', 'simple.txt') + $(if ($NoWeb) { @() } else { @('pdf\complex.pdf', 'xlsx\complex.xlsx') })
  $files = $samples | ForEach-Object { '"' + (Join-Path $Corpus $_) + '"' }
  $process = Start-Process -FilePath $App -ArgumentList (@('--smoke-test') + $files) -Wait -PassThru -NoNewWindow
  Write-Output "High contrast switched on: $on; views captured with exit code $($process.ExitCode): $(@(Get-ChildItem $Out -Filter *.png).Count) images"
}
finally { $null = [HighContrast]::Set($false, $null) }
