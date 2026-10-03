# Measures how much of Plain Viewer's own code (the app, the core library and the worker) the automated tests run:
# the core, Markdown, Office safety, worker and app test programs and the smoke test, each under Microsoft's dotnet-coverage
# (a development tool in .tools\dotnet-coverage; licence and set-up in docs/RELEASING.md, "Code coverage"). Telemetry
# is switched off. Writes artifacts\coverage\coverage.cobertura.xml and summary.txt (line coverage per part and file).
# The page scripts (sheet.js, viewer.js and the other WebView2 pages) are JavaScript and are not measured.
param([switch]$SkipBuild, [switch]$SkipSmoke)
. "$PSScriptRoot\env.ps1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$tool = Join-Path $repoRoot '.tools\dotnet-coverage\dotnet-coverage.exe'
if (-not (Test-Path -LiteralPath $tool)) { throw 'dotnet-coverage is missing: see docs/RELEASING.md, "Code coverage".' }
$out = Join-Path $repoRoot 'artifacts\coverage'
if (Test-Path -LiteralPath $out) { Get-ChildItem -LiteralPath $out -File | ForEach-Object { $_.Delete() } }
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $SkipBuild) { & "$PSScriptRoot\build.ps1" -Offline; if ($LASTEXITCODE -ne 0) { throw 'Build failed.' } }

# Only Plain Viewer's own programs and library, not the tests or third-party libraries.
$settings = Join-Path $out 'settings.xml'
@'
<Configuration><CodeCoverage>
  <ModulePaths><Include><ModulePath>.*\\PlainViewer(\.Core|\.Worker)?\.dll$</ModulePath></Include></ModulePaths>
  <CollectFromChildProcesses>True</CollectFromChildProcesses>
</CodeCoverage></Configuration>
'@ | Set-Content -LiteralPath $settings -Encoding utf8

$runs = [ordered]@{
  core = @($Dotnet, (Join-Path $repoRoot 'tests\PlainViewer.Tests\bin\Release\net10.0\PlainViewer.Tests.dll'))
  markdown = @($Dotnet, (Join-Path $repoRoot 'tests\PlainViewer.Markdown.Tests\bin\Release\net10.0-windows\PlainViewer.Markdown.Tests.dll'))
  officesafety = @($Dotnet, (Join-Path $repoRoot 'tests\PlainViewer.OfficeSafety.Tests\bin\Release\net10.0\PlainViewer.OfficeSafety.Tests.dll'))
  worker = @($Dotnet, (Join-Path $repoRoot 'tests\PlainViewer.Worker.Tests\bin\Release\net10.0\PlainViewer.Worker.Tests.dll'))
  app = @($Dotnet, (Join-Path $repoRoot 'tests\PlainViewer.App.Tests\bin\Release\net10.0-windows\PlainViewer.App.Tests.dll'))
}
if (-not $SkipSmoke) { $runs.smoke = @('powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'smoke-test.ps1')) }
$files = @()
foreach ($name in $runs.Keys) {
  $command = $runs[$name]
  if ($command.Count -eq 2 -and -not (Test-Path -LiteralPath $command[1])) { throw "Not built: $($command[1])" }
  $file = Join-Path $out "$name.cobertura.xml"
  Write-Output "Measuring $name..."
  & $tool collect --settings $settings --output $file --output-format cobertura -- @command | Out-Null
  if ($LASTEXITCODE -ne 0) { Write-Warning "$name exited with $LASTEXITCODE (coverage is still recorded)" }
  if (Test-Path -LiteralPath $file) { $files += $file } else { Write-Warning "No coverage recorded for $name" }
}
$merged = Join-Path $out 'coverage.cobertura.xml'
& $tool merge --output $merged --output-format cobertura @files | Out-Null
if (-not (Test-Path -LiteralPath $merged)) { throw 'Merging the coverage files failed.' }

# Line coverage per part (assembly) and per source file, from the merged report.
[xml]$report = Get-Content -LiteralPath $merged -Raw
$rows = foreach ($package in $report.coverage.packages.package) {
  # Code the compiler generates (under obj) is not Plain Viewer's own.
  foreach ($group in ($package.classes.class | Where-Object { $_.filename -notmatch '\\obj\\' } | Group-Object filename)) {
    $lines = @($group.Group | ForEach-Object { $_.lines.line }) | Group-Object number | ForEach-Object { [pscustomobject]@{ Hit = [bool]($_.Group | Where-Object { [long]$_.hits -gt 0 }) } }
    [pscustomobject]@{ Part = $package.name; File = ($group.Name -replace [regex]::Escape($repoRoot + '\'), ''); Lines = $lines.Count; Covered = @($lines | Where-Object Hit).Count }
  }
}
$summary = @()
$total = ($rows | Measure-Object Lines -Sum).Sum; $hit = ($rows | Measure-Object Covered -Sum).Sum
$summary += ('Total: {0:P1} of {1:N0} lines' -f ($hit / [math]::Max(1, $total)), $total)
$summary += ''
foreach ($part in ($rows | Group-Object Part | Sort-Object Name)) {
  $lines = ($part.Group | Measure-Object Lines -Sum).Sum; $covered = ($part.Group | Measure-Object Covered -Sum).Sum
  $summary += ('{0,-22} {1,7:P1}  {2,6:N0} lines' -f $part.Name, ($covered / [math]::Max(1, $lines)), $lines)
}
$summary += ''
$summary += 'Files, least covered first (files of 20 lines or more):'
$summary += $rows | Where-Object Lines -ge 20 | Sort-Object { $_.Covered / $_.Lines }, File |
  ForEach-Object { '{0,7:P1}  {1,5:N0} lines  {2}' -f ($_.Covered / $_.Lines), $_.Lines, $_.File }
$summary | Set-Content -LiteralPath (Join-Path $out 'summary.txt') -Encoding utf8
$summary
