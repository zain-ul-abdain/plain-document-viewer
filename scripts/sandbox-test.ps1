# Tests the installer on a clean, throwaway Windows 11 (Windows Sandbox) with networking switched off:
# install with "Open with" and firewall rules, open every smoke fixture, refuse the hostile/broken ones, uninstall,
# and check that nothing is left. Needs the Windows Sandbox feature (Containers-DisposableClientVM).
# The sandbox closes itself when done; results are printed and kept in artifacts\sandbox-test\results.
param([string]$Installer, [int]$TimeoutMinutes = 20)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $Installer) {
  $Installer = Get-ChildItem (Join-Path $repoRoot 'artifacts\installer') -Filter 'PlainViewer-Setup-*-x64.exe' |
    Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $Installer -or -not (Test-Path -LiteralPath $Installer)) { throw 'No installer found: run scripts\package.ps1 first.' }
$sandbox = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
if (-not (Test-Path -LiteralPath $sandbox)) {
  throw 'Windows Sandbox is not enabled. As administrator: Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All, then restart.'
}

$work = Join-Path $repoRoot 'artifacts\sandbox-test'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
$inputs = New-Item -ItemType Directory -Force -Path (Join-Path $work 'input')
$results = New-Item -ItemType Directory -Force -Path (Join-Path $work 'results')
Copy-Item -LiteralPath $Installer -Destination $inputs
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sandbox-inner.ps1') -Destination $inputs
Copy-Item -LiteralPath (Join-Path $repoRoot 'tests\corpus') -Destination (Join-Path $inputs 'corpus') -Recurse

$config = Join-Path $work 'test.wsb'
@"
<Configuration>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$($inputs.FullName)</HostFolder><SandboxFolder>C:\Test\input</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$($results.FullName)</HostFolder><SandboxFolder>C:\Test\results</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Test\input\sandbox-inner.ps1</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8

Start-Process -FilePath $sandbox -ArgumentList "`"$config`""
$done = Join-Path $results.FullName 'done.txt'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path -LiteralPath $done) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }
Get-Content -LiteralPath (Join-Path $results.FullName 'results.txt') -ErrorAction SilentlyContinue
if (-not (Test-Path -LiteralPath $done)) { throw "The sandbox test did not finish within $TimeoutMinutes minutes; see $($results.FullName)." }
if ((Get-Content -LiteralPath $done -Raw).Trim() -ne 'PASS') { throw 'The sandbox test failed; see the results above.' }
