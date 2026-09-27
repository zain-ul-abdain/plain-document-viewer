$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localHost = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localHost) { $script:Dotnet = $localHost } else { $script:Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $script:Dotnet -Parent
$env:DOTNET_HOST_PATH = $script:Dotnet
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
# Keep build-tool settings local; never read or modify the user's NuGet settings.
$env:APPDATA = Join-Path $repoRoot '.tools\appdata'
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
