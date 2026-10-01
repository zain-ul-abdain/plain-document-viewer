# Worker failure and recovery tests: crash, half an answer, output flood, hang (time limit), cancellation, memory and
# process limits, then the real worker opening a file. Builds the worker first, because the last tests start it.
param([switch]$Offline)
. "$PSScriptRoot\env.ps1"
$project = Join-Path $repoRoot 'tests\PlainViewer.Worker.Tests\PlainViewer.Worker.Tests.csproj'
$worker = Join-Path $repoRoot 'src\PlainViewer.Worker\PlainViewer.Worker.csproj'
foreach ($item in $project, $worker) {
  $restoreArgs = @('restore', $item, '--configfile', (Join-Path $repoRoot 'NuGet.Config'))
  if ($Offline) { $restoreArgs += @('--source', (Join-Path $repoRoot '.tools\feed')) }
  & $Dotnet @restoreArgs
  if ($LASTEXITCODE -ne 0) { throw 'Worker test restore failed.' }
}
& $Dotnet build $worker -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Worker build failed.' }
& $Dotnet run --project $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Worker failure tests failed.' }
