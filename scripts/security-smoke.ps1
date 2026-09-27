# Opens hostile fixtures in the real app while a local listener records any network request they cause.
# Fails if a fixture opens that should be refused, is refused without a clear message, or causes a request.
. "$PSScriptRoot\env.ps1"
$app = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
if (-not (Test-Path $app)) { throw 'Run scripts/build.ps1 first.' }
$node = (Get-Command node -ErrorAction Stop).Source
$log = Join-Path ([IO.Path]::GetTempPath()) "plainviewer-requests-$PID.jsonl"
$corpus = Join-Path $repoRoot 'tests\corpus'

# Hostile or broken files that should still open safely, and files that must be refused with a clear message.
$open = @('pdf\attack-javascript.pdf', 'pdf\attack-links.pdf', 'xlsx\complex.xlsx')
$refuse = @('pdf\zero-byte.pdf', 'pdf\not-a-pdf.pdf', 'xlsx\attack-xxe.xlsx', 'xlsx\attack-zip-bomb.xlsx', 'xlsx\password.xlsx',
  'xlsx\old-format-renamed.xlsx', 'xlsx\damaged-truncated.xlsx', 'xlsx\zero-byte.xlsx', 'xlsx\not-a-workbook.xlsx', 'xlsx\macro.xlsm')
$arguments = @($open | ForEach-Object { Join-Path $corpus $_ }) + @($refuse | ForEach-Object { '!' + (Join-Path $corpus $_) })

$listener = Start-Process -FilePath $node -ArgumentList @("`"$repoRoot\tests\harness\request-listener.mjs`"", "`"$log`"") -PassThru -WindowStyle Hidden
try {
  # Prove the listener records requests before trusting an empty log.
  $ready = $false
  for ($i = 0; $i -lt 50 -and -not $ready; $i++) {
    try { Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:47831/harness-canary' -TimeoutSec 2 | Out-Null } catch { }
    $ready = (Test-Path $log) -and ((Get-Content $log -Raw) -match 'harness-canary')
    if (-not $ready) { Start-Sleep -Milliseconds 100 }
  }
  if (-not $ready) { throw 'The request listener did not start, so the network check cannot be trusted.' }

  & $Dotnet $app --smoke-test @arguments
  if ($LASTEXITCODE -ne 0) { throw 'Security smoke test failed: see the messages above.' }
}
finally { Stop-Process -Id $listener.Id -Force -ErrorAction SilentlyContinue }

$requests = @(Get-Content $log | Where-Object { $_ -and $_ -notmatch 'harness-canary' })
$webClient = (Get-Service WebClient -ErrorAction SilentlyContinue).Status
Write-Output "WebClient (WebDAV) service: $(if ($webClient) { $webClient } else { 'not installed' }). UNC fixtures reach the listener only when it runs; SMB on port 445 is not observed."
if ($requests.Count -gt 0) { $requests | ForEach-Object { Write-Output "REQUEST $_" }; throw "Hostile fixtures caused $($requests.Count) network request(s)." }
Write-Output "No network requests recorded while opening $($open.Count + $refuse.Count) hostile or broken fixtures."
