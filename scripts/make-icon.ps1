# Makes the app icon (src/PlainViewer.App/Assets/app.ico) and docs/images/logo-256.png from docs/images/logo.svg.
# The SVG is drawn by Microsoft Edge (or Google Chrome) in headless mode at 1024 pixels with a transparent background, then scaled down
# with GDI+ to each icon size; the icon holds 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel images (PNG-compressed).
# It also writes docs/images/store/logo-300.png, the 300 x 300 logo the Microsoft Store listing needs; -StoreLogoOnly
# writes only that file.
param([switch]$StoreLogoOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$svg = Join-Path $repo 'docs\images\logo.svg'
$edge = foreach ($name in 'msedge.exe', 'chrome.exe') { foreach ($root in 'HKLM:', 'HKCU:') {
  (Get-ItemProperty "$root\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\$name" -ErrorAction SilentlyContinue).'(default)' } }
$edge = $edge | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge or Google Chrome is needed to draw the SVG.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ("plainviewer-icon-" + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $work | Out-Null
try {
  $page = Join-Path $work 'logo.html'
  $svgText = [IO.File]::ReadAllText($svg) -replace 'width="256" height="256"', 'width="1024" height="1024"'
  [IO.File]::WriteAllText($page, "<!DOCTYPE html><html><head><style>html,body{margin:0;background:transparent;overflow:hidden}</style></head><body>$svgText</body></html>")
  $large = Join-Path $work 'logo-1024.png'
  Start-Process $edge -Wait -WindowStyle Hidden -ArgumentList @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
    "--user-data-dir=`"$work\edge`"", '--window-size=1024,1024', "--screenshot=`"$large`"", "`"file:///$($page.Replace('\', '/'))`"")
  if (-not (Test-Path $large)) { throw 'Edge did not draw the logo.' }

  Add-Type -AssemblyName System.Drawing
  $source = [Drawing.Image]::FromFile($large)
  function Scaled([int]$size) {
    $bitmap = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size); $g.Dispose()
    $stream = New-Object IO.MemoryStream; $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
    , $stream.ToArray()
  }
  try {
    $store = Join-Path $repo 'docs\images\store'; New-Item -ItemType Directory -Force $store | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $store 'logo-300.png'), (Scaled 300))
    Write-Output 'Wrote docs\images\store\logo-300.png'
    if ($StoreLogoOnly) { return }
    $sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
    $images = @($sizes | ForEach-Object { , (Scaled $_) })
    [IO.File]::WriteAllBytes((Join-Path $repo 'docs\images\logo-256.png'), $images[-1])
    # ICONDIR, one ICONDIRENTRY per size, then the PNG images.
    $icon = New-Object IO.MemoryStream; $writer = New-Object IO.BinaryWriter $icon
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
      $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
      $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
      $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
      $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
    $writer.Flush()
    [IO.File]::WriteAllBytes((Join-Path $repo 'src\PlainViewer.App\Assets\app.ico'), $icon.ToArray())
  }
  finally { $source.Dispose() }
  Write-Output "Wrote src\PlainViewer.App\Assets\app.ico and docs\images\logo-256.png"
}
finally { Start-Sleep -Milliseconds 500; [IO.Directory]::Delete($work, $true) }
