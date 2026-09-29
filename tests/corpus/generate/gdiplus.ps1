# Re-encodes a PNG with Windows GDI+ (System.Drawing), a producer independent of the viewer's decoders.
#   gdiplus.ps1 <input.png> <output> <jpeg|gif|tiff> [quality] [exif orientation]
param([string]$In, [string]$Out, [string]$Format, [int]$Quality = 85, [int]$Orientation = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$image = [Drawing.Image]::FromFile($In)
try {
  if ($Orientation -gt 0) {
    # EXIF orientation tag 0x0112 (SHORT). PropertyItem has no public constructor.
    $item = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Drawing.Imaging.PropertyItem])
    $item.Id = 0x0112; $item.Type = 3; $item.Len = 2; $item.Value = [BitConverter]::GetBytes([uint16]$Orientation)
    $image.SetPropertyItem($item)
  }
  $mime = @{ jpeg = 'image/jpeg'; gif = 'image/gif'; tiff = 'image/tiff' }[$Format]
  $codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq $mime
  $parameters = New-Object Drawing.Imaging.EncoderParameters 1
  $parameters.Param[0] = New-Object Drawing.Imaging.EncoderParameter ([Drawing.Imaging.Encoder]::Quality), ([long]$Quality)
  $image.Save($Out, $codec, $parameters)
}
finally { $image.Dispose() }
