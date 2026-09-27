param([switch]$Offline)
. "$PSScriptRoot\env.ps1"
$project = Join-Path $repoRoot 'src\PlainViewer.App\PlainViewer.App.csproj'
$restoreArgs = @('restore', $project, '--configfile', (Join-Path $repoRoot 'NuGet.Config'))
if ($Offline) { $restoreArgs += @('--source', (Join-Path $repoRoot '.tools\feed')) }
& $Dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
& $Dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
