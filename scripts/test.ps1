param([switch]$Offline)
. "$PSScriptRoot\env.ps1"
$project = Join-Path $repoRoot 'tests\PlainViewer.Tests\PlainViewer.Tests.csproj'
$restoreArgs = @('restore', $project, '--configfile', (Join-Path $repoRoot 'NuGet.Config'))
if ($Offline) { $restoreArgs += @('--source', (Join-Path $repoRoot '.tools\feed')) }
& $Dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Test dependency restore failed.' }
& $Dotnet run --project $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
