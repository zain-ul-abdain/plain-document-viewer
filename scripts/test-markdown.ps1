param([switch]$Offline)
. "$PSScriptRoot\env.ps1"
$project = Join-Path $repoRoot 'tests\PlainViewer.Markdown.Tests\PlainViewer.Markdown.Tests.csproj'
$restoreArgs = @('restore', $project, '--configfile', (Join-Path $repoRoot 'NuGet.Config'))
if ($Offline) { $restoreArgs += @('--source', (Join-Path $repoRoot '.tools\feed')) }
& $Dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Markdown dependency restore failed.' }
& $Dotnet run --project $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Markdown regressions failed.' }
