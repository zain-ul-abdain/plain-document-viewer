# Takes README and Store screenshots: opens each file in the development build (Light theme unless -Theme Dark,
# thumbnails on, 1280 x 820 unless -Width and -Height say otherwise; the Store needs at least 1366 x 768) and
# saves the whole window with PrintWindow, which also captures the WebView2 content. The window appears on screen for
# a few seconds. The viewer's settings file is backed up and restored.
#   screenshot.ps1 -File <document> -Out <png>   (repeat the script for each picture)
param([Parameter(Mandatory)][string]$File, [Parameter(Mandatory)][string]$Out, [int]$Width = 1280, [int]$Height = 820, [int]$Wait = 6, [ValidateSet('Light', 'Dark')][string]$Theme = 'Light')
. "$PSScriptRoot\env.ps1"
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Capture {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
# Window sizes in real pixels on scaled displays (otherwise the two size functions below disagree).
[Capture]::SetProcessDPIAware() | Out-Null
$settings = Join-Path $env:LOCALAPPDATA 'PlainViewer\settings.json'
$backup = if (Test-Path $settings) { [IO.File]::ReadAllText($settings) } else { $null }
New-Item -ItemType Directory -Force (Split-Path $settings) | Out-Null
[IO.File]::WriteAllText($settings, "{""Theme"":""$Theme"",""Thumbnails"":true,""Maximized"":false}")
$dll = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
$process = Start-Process $Dotnet -ArgumentList @("`"$dll`"", "`"$((Resolve-Path $File).Path)`"") -PassThru
try {
  for ($i = 0; $i -lt 60 -and $process.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 500; $process.Refresh() }
  $window = $process.MainWindowHandle
  [Capture]::SetWindowPos($window, [IntPtr]::Zero, 40, 40, $Width, $Height, 0x0040) | Out-Null
  # The title shows the file name once the document has opened (Word and PowerPoint files are converted first).
  $name = [IO.Path]::GetFileName($File)
  for ($i = 0; $i -lt 180 -and -not $process.MainWindowTitle.StartsWith($name); $i++) { Start-Sleep -Milliseconds 500; $process.Refresh() }
  if (-not $process.MainWindowTitle.StartsWith($name)) { throw "$name did not open within 90 seconds." }
  Start-Sleep -Seconds $Wait
  # The visible frame (without the invisible resize border Windows adds around it).
  $frame = New-Object Capture+RECT
  [Capture]::DwmGetWindowAttribute($window, 9, [ref]$frame, 16) | Out-Null
  $outer = New-Object Capture+RECT; [Capture]::GetWindowRect($window, [ref]$outer) | Out-Null
  $full = New-Object Drawing.Bitmap ($outer.Right - $outer.Left), ($outer.Bottom - $outer.Top)
  $g = [Drawing.Graphics]::FromImage($full); $hdc = $g.GetHdc()
  [Capture]::PrintWindow($window, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  $crop = New-Object Drawing.Rectangle ($frame.Left - $outer.Left), ($frame.Top - $outer.Top), ($frame.Right - $frame.Left), ($frame.Bottom - $frame.Top)
  $image = $full.Clone($crop, $full.PixelFormat); $full.Dispose()
  New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
  $image.Save($Out, [Drawing.Imaging.ImageFormat]::Png); $image.Dispose()
  Write-Output "Saved $Out"
}
finally {
  if (-not $process.HasExited) { [Capture]::PostMessage($process.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; $process.WaitForExit(10000) | Out-Null }
  if ($backup -ne $null) { [IO.File]::WriteAllText($settings, $backup) } else { [IO.File]::Delete($settings) }
}
