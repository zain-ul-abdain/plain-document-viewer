param([string]$File)
. "$PSScriptRoot\env.ps1"
$app = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
if (-not (Test-Path $app)) { throw 'Run scripts/build.ps1 first.' }
if ($File) { & $Dotnet $app $File } else { & $Dotnet $app }
