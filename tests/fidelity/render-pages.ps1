# Draws PDF pages to PNG with Windows' own PDF renderer (Windows.Data.Pdf), which is independent of both LibreOffice
# and PDF.js. Needs Windows PowerShell 5.1 (it uses the WinRT bridge of .NET Framework). Dot-source this file.
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$script:AsTaskOperation = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
  $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
$script:AsTaskAction = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
  $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1

function Wait-Operation($operation, [Type]$type) {
  $task = $script:AsTaskOperation.MakeGenericMethod($type).Invoke($null, @($operation)); $task.Wait(); $task.Result
}

# Returns the page count and writes pages 1..$Count (or all) as "<prefix>-<n>.png", $Width pixels wide.
function Export-PdfPages([string]$Pdf, [string]$Prefix, [int]$Count = [int]::MaxValue, [int]$Width = 600) {
  $file = Wait-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])
  $document = Wait-Operation ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
  for ($i = 0; $i -lt [Math]::Min($Count, $document.PageCount); $i++) {
    $page = $document.GetPage([uint32]$i)
    $stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
    $options = [Windows.Data.Pdf.PdfPageRenderOptions]::new(); $options.DestinationWidth = [uint32]$Width
    $script:AsTaskAction.Invoke($null, @($page.RenderToStreamAsync($stream, $options))).Wait()
    $source = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
    $output = [System.IO.File]::Create("$Prefix-$($i + 1).png")
    try { $source.CopyTo($output) } finally { $output.Dispose(); $source.Dispose(); $page.Dispose() }
  }
  return [int]$document.PageCount
}
