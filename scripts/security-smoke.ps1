# Opens hostile fixtures in the real app while a local listener records any network request they cause.
# Fails if a fixture opens that should be refused, is refused without a clear message, or causes a request.
# -App tests an installed or published PlainViewer.exe instead of the development build.
param([string]$App)
if ($App) { $ErrorActionPreference = 'Stop'; $repoRoot = Split-Path $PSScriptRoot -Parent } else { . "$PSScriptRoot\env.ps1" }
. "$PSScriptRoot\app.ps1"
$node = (Get-Command node -ErrorAction Stop).Source
$log = Join-Path ([IO.Path]::GetTempPath()) "plainviewer-requests-$PID.jsonl"
$corpus = Join-Path $repoRoot 'tests\corpus'

# Hostile or broken files that should still open safely, and files that must be refused with a clear message.
$open = @('pdf\attack-javascript.pdf', 'pdf\attack-links.pdf', 'xlsx\complex.xlsx',
  'docx\attack-remote-image.docx', 'docx\attack-unc-image.docx', 'docx\attack-remote-template.docx', 'docx\attack-includepicture.docx',
  'pptx\attack-remote-image.pptx', 'docx\complex-20-pages.docx', 'pptx\complex.pptx',
  'xlsx\macro.xlsm', 'docx\macro.docm', 'pptx\macro.pptm', 'images\attack-svg-active.svg', 'data\attack-xxe.xml',
  'images\damaged-truncated.webp', 'images\damaged-truncated.jpg', 'images\damaged-truncated.png', 'images\attack-svg-xxe.svg',
  'odt\attack-external.odt', 'ods\attack-external.ods', 'odp\attack-remote-image.odp', 'rtf\attack.rtf',
  'doc\attack-remote-image.doc', 'doc\attack-unc-image.doc', 'ppt\attack-remote-image.ppt')
$refuse = @('pdf\zero-byte.pdf', 'pdf\not-a-pdf.pdf', 'xlsx\attack-xxe.xlsx', 'xlsx\attack-zip-bomb.xlsx', 'xlsx\password.xlsx',
  'xlsx\old-format-renamed.xlsx', 'xlsx\damaged-truncated.xlsx', 'xlsx\zero-byte.xlsx', 'xlsx\not-a-workbook.xlsx',
  'docx\attack-xxe.docx', 'docx\attack-zip-bomb.docx', 'docx\password.docx', 'docx\old-format-renamed.docx', 'docx\damaged-truncated.docx',
  'docx\zero-byte.docx', 'docx\not-a-document.docx',
  'pptx\attack-xxe.pptx', 'pptx\attack-zip-bomb.pptx', 'pptx\password.pptx',
  'images\zero-byte.png', 'images\damaged-header.png', 'images\not-a-picture.jpg', 'images\tiff-named.png',
  'images\attack-pixel-bomb.png', 'data\binary-named.json',
  'odt\password.odt', 'odt\zero-byte.odt', 'odt\not-a-document.odt', 'odt\damaged-truncated.odt', 'odt\docx-named.odt', 'ods\zero-byte.ods',
  'rtf\zero-byte.rtf', 'tiff\zero-byte.tif', 'doc\zero-byte.doc', 'doc\xls-named.doc')
# Switch off the converter's extra dead-proxy layer so the listener observes LibreOffice directly.
$env:PLAINVIEWER_LO_PROXY_OFF = '1'
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

  $code = Invoke-PlainViewer $App (@('--smoke-test') + $arguments)
  if ($code -ne 0) { throw 'Security smoke test failed: see the messages above.' }
}
finally { Stop-Process -Id $listener.Id -Force -ErrorAction SilentlyContinue }

$requests = @(Get-Content $log | Where-Object { $_ -and $_ -notmatch 'harness-canary' })
$webClient = (Get-Service WebClient -ErrorAction SilentlyContinue).Status
Write-Output "WebClient (WebDAV) service: $(if ($webClient) { $webClient } else { 'not installed' }). UNC fixtures reach the listener only when it runs; SMB on port 445 is not observed."
if ($requests.Count -gt 0) { $requests | ForEach-Object { Write-Output "REQUEST $_" }; throw "Hostile fixtures caused $($requests.Count) network request(s)." }
Write-Output "No network requests recorded while opening $($open.Count + $refuse.Count) hostile or broken fixtures."
