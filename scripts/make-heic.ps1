# Makes the HEIC test photos (tests/corpus/heic) with Windows' own HEIF encoder, which needs Microsoft's "HEIF Image
# Extensions" and "HEVC Video Extensions". The viewer decodes HEIC with the same Windows codec, so these fixtures prove
# its safety checks and behaviour, not decoding fidelity. They are kept in git; existing files are only replaced with
# -Force. Afterwards run `node generate.mjs` in tests/corpus/generate to record them in manifest.json.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$corpus = Join-Path $repoRoot 'tests\corpus'
$folder = Join-Path $corpus 'heic'
New-Item -ItemType Directory -Force $folder | Out-Null
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Runtime.WindowsRuntime
$null = [Windows.Graphics.Imaging.BitmapEncoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$extensions = [System.WindowsRuntimeSystemExtensions].GetMethods()
$operation = $extensions | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' }
$action = $extensions | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' }
function Wait-Operation($op, [Type]$type) { $task = $operation.MakeGenericMethod($type).Invoke($null, @($op)); $task.Wait(); $task.Result }
function Wait-Action($op) { $action.Invoke($null, @($op)).Wait() }

# PNG bytes to HEIC bytes; orientation (1-8) is stored as metadata, as phones do.
function ConvertTo-Heic([byte[]]$png, [int]$orientation = 1) {
  $in = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
  $writer = [Windows.Storage.Streams.DataWriter]::new($in); $writer.WriteBytes($png)
  $null = Wait-Operation ($writer.StoreAsync()) ([uint32]); $null = $writer.DetachStream(); $in.Seek(0)
  $decoder = Wait-Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($in)) ([Windows.Graphics.Imaging.BitmapDecoder])
  $bitmap = Wait-Operation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
  $out = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
  $encoder = Wait-Operation ([Windows.Graphics.Imaging.BitmapEncoder]::CreateAsync([Windows.Graphics.Imaging.BitmapEncoder]::HeifEncoderId, $out)) ([Windows.Graphics.Imaging.BitmapEncoder])
  $encoder.SetSoftwareBitmap($bitmap)
  if ($orientation -ne 1) {
    $properties = [Collections.Generic.Dictionary[string, Windows.Graphics.Imaging.BitmapTypedValue]]::new()
    $properties['System.Photo.Orientation'] = [Windows.Graphics.Imaging.BitmapTypedValue]::new([uint16]$orientation, [Windows.Foundation.PropertyType]::UInt16)
    Wait-Action ($encoder.BitmapProperties.SetPropertiesAsync($properties))
  }
  Wait-Action ($encoder.FlushAsync())
  $out.Seek(0); $reader = [Windows.Storage.Streams.DataReader]::new($out)
  $null = Wait-Operation ($reader.LoadAsync([uint32]$out.Size)) ([uint32])
  $bytes = New-Object byte[] $out.Size; $reader.ReadBytes($bytes); $bytes
}

# A drawn test picture (gradient, a label and an arrow pointing up, so rotation is visible) as PNG bytes.
function New-Png([int]$width, [int]$height, [string]$label, [int]$turn = 0) {
  $visual = [System.Windows.Media.DrawingVisual]::new()
  $context = $visual.RenderOpen()
  $gradient = [System.Windows.Media.LinearGradientBrush]::new([System.Windows.Media.Color]::FromRgb(40, 110, 200), [System.Windows.Media.Color]::FromRgb(240, 180, 60), 45)
  $context.DrawRectangle($gradient, $null, [System.Windows.Rect]::new(0, 0, $width, $height))
  $size = $height / 10
  $text = [System.Windows.Media.FormattedText]::new($label, [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', [System.Windows.Media.Typeface]::new('Segoe UI'), $size, [System.Windows.Media.Brushes]::White, 1.0)
  $context.DrawText($text, [System.Windows.Point]::new($width * 0.05, $height * 0.75))
  $arrow = [System.Windows.Media.StreamGeometry]::new(); $g = $arrow.Open()
  $g.BeginFigure([System.Windows.Point]::new($width / 2, $height * 0.1), $true, $true)
  $g.LineTo([System.Windows.Point]::new($width * 0.6, $height * 0.35), $true, $false); $g.LineTo([System.Windows.Point]::new($width * 0.4, $height * 0.35), $true, $false); $g.Close()
  $context.DrawGeometry([System.Windows.Media.Brushes]::White, $null, $arrow); $context.Close()
  $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $bitmap.Render($visual)
  $source = if ($turn) { [System.Windows.Media.Imaging.TransformedBitmap]::new($bitmap, [System.Windows.Media.RotateTransform]::new($turn)) } else { $bitmap }
  $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new(); $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($source))
  $stream = [IO.MemoryStream]::new(); $encoder.Save($stream); $stream.ToArray()
}

$jobs = @(
  @('simple.heic', { ConvertTo-Heic (New-Png 640 480 'Hello HEIC') }),
  # Drawn upright (portrait), stored turned a quarter anticlockwise with orientation 6, as a phone held upright saves it.
  @('complex-rotated.heic', { ConvertTo-Heic (New-Png 600 800 'Upright' 270) 6 }),
  @('large-12mp.heic', { ConvertTo-Heic (New-Png 4032 3024 'Large photo') }))
foreach ($job in $jobs) {
  $target = Join-Path $folder $job[0]
  if ((Test-Path $target) -and -not $Force) { Write-Output "kept    heic\$($job[0])"; continue }
  [IO.File]::WriteAllBytes($target, [byte[]](& $job[1]))
  Write-Output "made    heic\$($job[0])"
}
