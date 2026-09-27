# Measures start-up and first-content times and peak memory against the targets in docs/SPECIFICATION.md.
# Each file is opened in a new app process $Runs times. The first run of each file is reported on its own: it is the
# closest to a cold open that is possible without restarting Windows (the file and program may still be cached).
# Needs the large fixtures (npm run generate:large in tests/corpus/generate). -App measures an installed PlainViewer.exe.
# Results: a table here and artifacts\measure\results.json.
param([int]$Runs = 4, [string]$App)
if ($App) { $ErrorActionPreference = 'Stop'; $repoRoot = Split-Path $PSScriptRoot -Parent } else { . "$PSScriptRoot\env.ps1" }
$corpus = Join-Path $repoRoot 'tests\corpus'
$scenarios = [ordered]@{
  'Main window only'        = @{ file = $null; target = 'window 2 s' }
  'Text (small)'            = @{ file = 'simple.txt' }
  'PDF, 5 pages'            = @{ file = 'pdf\complex.pdf'; target = 'first page 1 s'; limit = 1000 }
  'PDF, 500 pages'          = @{ file = 'generated\pdf-large-500-pages.pdf'; target = 'opens' }
  'Word, 20 pages'          = @{ file = 'docx\complex-20-pages.docx'; target = 'first page 5 s cold'; limit = 5000 }
  'PowerPoint, 12 slides'   = @{ file = 'pptx\complex.pptx' }
  'Excel (small)'           = @{ file = 'xlsx\complex.xlsx' }
  'Excel, 500,000 rows'     = @{ file = 'generated\xlsx-large-500k-rows.xlsx'; target = 'opens' }
  'CSV, 200 MB'             = @{ file = 'generated\csv-large-200mb.csv'; target = 'opens' }
  'Text, 100 MB'            = @{ file = 'generated\text-large-100mb.txt' }
}

function Measure-Once([string]$file) {
  $out = New-TemporaryFile
  try {
    $arguments = @('--measure') + @(if ($file) { '"' + $file + '"' })
    if ($App) { $process = Start-Process -FilePath $App -ArgumentList $arguments -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out.FullName }
    else {
      $dll = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
      $process = Start-Process -FilePath $Dotnet -ArgumentList (@("`"$dll`"") + $arguments) -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out.FullName
    }
    $line = Get-Content -LiteralPath $out.FullName | Where-Object { $_ -like '{*' } | Select-Object -Last 1
    if (-not $line) { return [pscustomobject]@{ error = "no result (exit code $($process.ExitCode))" } }
    return $line | ConvertFrom-Json
  }
  finally { Remove-Item -LiteralPath $out.FullName -Force -ErrorAction SilentlyContinue }
}
function Median([double[]]$values) { if (-not $values) { return $null }; $sorted = $values | Sort-Object; $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)] }

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$system = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$disk = Get-PhysicalDisk -ErrorAction SilentlyContinue | Where-Object DeviceId -eq ((Get-Partition -DriveLetter C -ErrorAction SilentlyContinue).DiskNumber) | Select-Object -First 1
$hardware = [ordered]@{
  cpu = "$($cpu.Name.Trim()) ($($cpu.NumberOfCores) cores, $($cpu.NumberOfLogicalProcessors) threads)"
  ramGb = [math]::Round($system.TotalPhysicalMemory / 1GB)
  storage = if ($disk) { "$($disk.FriendlyName) ($($disk.MediaType), $($disk.BusType))" } else { 'unknown' }
  windows = "$($os.Caption) $($os.Version)"
  build = if ($App) { "installed: $App" } else { 'development build through the local .NET host' }
}
Write-Output ("Machine: {0}; {1} GB RAM; {2}; {3}" -f $hardware.cpu, $hardware.ramGb, $hardware.storage, $hardware.windows)

$results = [ordered]@{ date = (Get-Date).ToString('s'); hardware = $hardware; runs = $Runs; scenarios = [ordered]@{} }
$table = foreach ($name in $scenarios.Keys) {
  $scenario = $scenarios[$name]
  $file = if ($scenario.file) { Join-Path $corpus $scenario.file } else { $null }
  if ($file -and -not (Test-Path -LiteralPath $file)) { Write-Output "SKIP $name (missing $($scenario.file))"; continue }
  $measured = @(1..$Runs | ForEach-Object { Measure-Once $file })
  $errors = @($measured | Where-Object { $_.PSObject.Properties['error'] } | ForEach-Object error)
  $first = $measured[0]; $warm = @($measured | Select-Object -Skip 1)
  $key = if ($file) { 'firstContentMs' } else { 'windowReadyMs' }
  $row = [ordered]@{
    scenario = $name
    firstRunMs = $first.$key
    warmMedianMs = Median ($warm | ForEach-Object { $_.$key })
    windowMedianMs = Median ($measured | ForEach-Object { $_.windowReadyMs })
    appPeakMb = ($measured | Measure-Object appPeakMb -Maximum).Maximum
    workerPeakMb = ($measured | Measure-Object workerPeakMb -Maximum).Maximum
    converterPeakMb = ($measured | Measure-Object converterPeakMb -Maximum).Maximum
    webViewPeakMb = ($measured | Measure-Object webViewPeakMb -Maximum).Maximum
    target = $scenario.target
    met = if ($errors) { 'error' } elseif ($scenario.limit) { if ($first.$key -le $scenario.limit) { 'yes' } else { 'no' } } elseif (-not $file) { if ($first.windowReadyMs -le 2000) { 'yes' } else { 'no' } } else { '' }
    errors = $errors
  }
  $results.scenarios[$name] = $row
  [pscustomobject]$row
}
$table | Format-Table scenario, firstRunMs, warmMedianMs, windowMedianMs, appPeakMb, workerPeakMb, converterPeakMb, webViewPeakMb, target, met -AutoSize | Out-String -Width 220 | Write-Output
Write-Output 'Times: first content from the start of opening (window only: from process start). Memory: peak working set in MB; WebView2 is the sum over its processes (shared pages are counted more than once).'
$folder = New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot 'artifacts\measure')
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder.FullName 'results.json') -Encoding utf8
if (@($table | Where-Object { $_.met -in 'no', 'error' }).Count) { Write-Output 'Some targets were missed or failed; see the table.' }
