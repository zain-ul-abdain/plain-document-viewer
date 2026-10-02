# Tests the MSIX package (scripts\package-msix.ps1 -TestSign) on a clean, throwaway Windows 11 in Windows Sandbox with
# networking off: install, open every kind of file through the packaged app, check its data folders and "Open with"
# entries, remove it (msix-sandbox-inner.ps1). The local test certificate is trusted inside the sandbox only. Needs the
# Windows Sandbox feature and about 8 GB free on the system drive. Results: artifacts\msix-sandbox-test\<run>\results.
param([int]$TimeoutMinutes = 30)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$package = Get-ChildItem (Join-Path $repoRoot 'artifacts\msix') -Filter 'PlainViewer-*-x64.msix' | Sort-Object LastWriteTime | Select-Object -Last 1
$certificate = Join-Path $repoRoot 'artifacts\msix\test-certificate.cer'
$webView2 = Join-Path $repoRoot '.tools\webview2\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
if (-not $package -or -not (Test-Path -LiteralPath $certificate)) { throw 'No test-signed package: run scripts\package-msix.ps1 -TestSign first.' }
if (-not (Test-Path -LiteralPath $webView2)) { throw 'The WebView2 offline installer is missing: see docs/RELEASING.md, one-time setup step 6.' }
$sandbox = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
if (-not (Test-Path -LiteralPath $sandbox)) { throw 'Windows Sandbox is not enabled.' }
$free = (Get-PSDrive ($env:SystemDrive.TrimEnd(':'))).Free
if ($free -lt 8GB) { throw "Only $([math]::Round($free / 1GB, 1)) GB free on $env:SystemDrive; the sandbox needs about 8 GB." }

$runs = Join-Path $repoRoot 'artifacts\msix-sandbox-test'
Get-ChildItem -LiteralPath $runs -Directory -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
$work = Join-Path $runs (Get-Date -Format 'yyyyMMdd-HHmmss')
$inputs = New-Item -ItemType Directory -Force -Path (Join-Path $work 'input')
$results = New-Item -ItemType Directory -Force -Path (Join-Path $work 'results')
Copy-Item -LiteralPath $package.FullName, $certificate, $webView2, (Join-Path $PSScriptRoot 'msix-sandbox-inner.ps1') -Destination $inputs
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
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Test\input\msix-sandbox-inner.ps1</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8

Start-Process -FilePath $sandbox -ArgumentList "`"$config`""
$done = Join-Path $results.FullName 'done.txt'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path -LiteralPath $done) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }
Get-Content -LiteralPath (Join-Path $results.FullName 'results.txt') -ErrorAction SilentlyContinue
if (-not (Test-Path -LiteralPath $done)) {
  Get-Process -Name 'WindowsSandboxRemoteSession', 'WindowsSandboxServer', 'WindowsSandboxClient', 'WindowsSandbox' -ErrorAction SilentlyContinue | Stop-Process -Force
  throw "The sandbox test did not finish within $TimeoutMinutes minutes; see $($results.FullName)."
}
if ((Get-Content -LiteralPath $done -Raw).Trim() -ne 'PASS') { throw 'The MSIX sandbox test failed; see the results above.' }
Write-Output "MSIX sandbox test passed. Results: $($results.FullName)"
# robocopy's exit code (1 = files copied) must not become this script's.
exit 0
