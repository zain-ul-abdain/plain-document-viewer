# Re-encodes PNGs with Windows GDI+ (System.Drawing), a producer independent of the viewer's decoders.
#   gdiplus.ps1 <input.png> <output> <jpeg|gif|tiff> [quality] [exif orientation]
#   gdiplus.ps1 "<page1.png>;<page2.png>;..." <output.tif> tiffpages      (one TIFF page per PNG)
param([string]$In, [string]$Out, [string]$Format, [int]$Quality = 85, [int]$Orientation = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
function Encoder($mime) { [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq $mime }
function Flag($value) {
  $parameters = New-Object Drawing.Imaging.EncoderParameters 1
  $parameters.Param[0] = New-Object Drawing.Imaging.EncoderParameter ([Drawing.Imaging.Encoder]::SaveFlag), ([long]$value)
  $parameters
}
if ($Format -eq 'tiffpages') {
  $pages = @($In.Split(';') | ForEach-Object { [Drawing.Image]::FromFile($_) })
  try {
    $pages[0].Save($Out, (Encoder 'image/tiff'), (Flag ([Drawing.Imaging.EncoderValue]::MultiFrame)))
    for ($i = 1; $i -lt $pages.Count; $i++) { $pages[0].SaveAdd($pages[$i], (Flag ([Drawing.Imaging.EncoderValue]::FrameDimensionPage))) }
    $pages[0].SaveAdd((Flag ([Drawing.Imaging.EncoderValue]::Flush)))
  }
  finally { $pages | ForEach-Object { $_.Dispose() } }
  return
}
$image = [Drawing.Image]::FromFile($In)
try {
  if ($Orientation -gt 0) {
    # EXIF orientation tag 0x0112 (SHORT). PropertyItem has no public constructor.
    $item = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Drawing.Imaging.PropertyItem])
    $item.Id = 0x0112; $item.Type = 3; $item.Len = 2; $item.Value = [BitConverter]::GetBytes([uint16]$Orientation)
    $image.SetPropertyItem($item)
  }
  $mime = @{ jpeg = 'image/jpeg'; gif = 'image/gif'; tiff = 'image/tiff' }[$Format]
  $parameters = New-Object Drawing.Imaging.EncoderParameters 1
  $parameters.Param[0] = New-Object Drawing.Imaging.EncoderParameter ([Drawing.Imaging.Encoder]::Quality), ([long]$Quality)
  $image.Save($Out, (Encoder $mime), $parameters)
}
finally { $image.Dispose() }
