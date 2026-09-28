# Tests the installer on a clean, throwaway Windows 11 (Windows Sandbox) with networking switched off:
# install with "Open with" and firewall rules, open every smoke fixture, refuse the hostile/broken ones, uninstall,
# and check that nothing is left; also keyboard-only use and high-contrast captures. Needs the Windows Sandbox feature
# (Containers-DisposableClientVM) and about 3 GB free on the system drive (the sandbox's disk lives there). Windows
# Sandbox has no WebView2 Runtime: the test first installs without it (checking the app's message), then installs
# again, as an upgrade, with the runtime bundled in the installer.
# The sandbox closes itself when done; results are printed and kept in artifacts\sandbox-test\<run>\results.
param([string]$Installer, [int]$TimeoutMinutes = 30)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $Installer) {
  $Installer = Get-ChildItem (Join-Path $repoRoot 'artifacts\installer') -Filter 'PlainViewer-Setup-*-x64.exe' |
    Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
}
if (-not $Installer -or -not (Test-Path -LiteralPath $Installer)) { throw 'No installer found: run scripts\package.ps1 first.' }
$sandbox = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
$free = (Get-PSDrive ($env:SystemDrive.TrimEnd(':'))).Free
if ($free -lt 3GB) { throw "Only $([math]::Round($free / 1GB, 1)) GB free on $env:SystemDrive; the sandbox needs about 3 GB (setup fails part-way otherwise)." }
if (-not (Test-Path -LiteralPath $sandbox)) {
  throw 'Windows Sandbox is not enabled. As administrator: Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All, then restart.'
}

# One folder per run: Windows keeps a closed sandbox's shared folders locked for a while.
$runs = Join-Path $repoRoot 'artifacts\sandbox-test'
Get-ChildItem -LiteralPath $runs -Directory -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
$work = Join-Path $runs (Get-Date -Format 'yyyyMMdd-HHmmss')
$inputs = New-Item -ItemType Directory -Force -Path (Join-Path $work 'input')
$results = New-Item -ItemType Directory -Force -Path (Join-Path $work 'results')
Copy-Item -LiteralPath $Installer -Destination $inputs
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sandbox-inner.ps1'), (Join-Path $PSScriptRoot 'keyboard-check.ps1'), (Join-Path $PSScriptRoot 'high-contrast-capture.ps1') -Destination $inputs
# The small fixtures only: not the large generated files or the generator's packages.
& robocopy (Join-Path $repoRoot 'tests\corpus') (Join-Path $inputs 'corpus') /E /XD generated node_modules generate /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Copying the test files failed.' }


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
if (-not (Test-Path -LiteralPath $done)) {
  # Close the throwaway sandbox this script started; nothing in it is kept.
  Get-Process -Name 'WindowsSandboxRemoteSession', 'WindowsSandboxServer', 'WindowsSandboxClient', 'WindowsSandbox' -ErrorAction SilentlyContinue | Stop-Process -Force
  throw "The sandbox test did not finish within $TimeoutMinutes minutes; see $($results.FullName)."
}
if ((Get-Content -LiteralPath $done -Raw).Trim() -ne 'PASS') { throw 'The sandbox test failed; see the results above.' }
Write-Output "Sandbox test passed. Results: $($results.FullName)"
exit 0 # otherwise robocopy's exit code 1 ("files copied") would be reported
