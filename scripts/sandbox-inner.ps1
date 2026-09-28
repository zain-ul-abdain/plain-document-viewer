# Runs inside Windows Sandbox (started by sandbox-test.ps1). Writes C:\Test\results\results.txt and done.txt,
# then shuts the sandbox down. Everything here happens in the throwaway sandbox, not on the host PC.
$results = 'C:\Test\results'
$log = Join-Path $results 'results.txt'
$failures = [System.Collections.Generic.List[string]]::new()
function Log([string]$message) { Add-Content -LiteralPath $log -Value $message }
function Expect([bool]$condition, [string]$what) { if ($condition) { Log "PASS $what" } else { Log "FAIL $what"; $failures.Add($what) } }
function Run([string]$exe, [string[]]$arguments, [string]$output) {
  $quoted = @($arguments | ForEach-Object { '"' + $_ + '"' })
  $process = Start-Process -FilePath $exe -ArgumentList $quoted -Wait -PassThru -NoNewWindow -RedirectStandardOutput $output -RedirectStandardError "$output.err"
  return $process.ExitCode
}

function WebView2Version {
  # Per-machine or per-user Evergreen runtime (the registry keys Microsoft documents).
  $key = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
  @((Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\$key" -ErrorAction SilentlyContinue).pv, (Get-ItemProperty "HKCU:\Software\$key" -ErrorAction SilentlyContinue).pv) |
    Where-Object { $_ -and $_ -ne '0.0.0.0' } | Select-Object -First 1
}
function Install([string]$tasks, [string]$logName) {
  $start = Get-Date
  $process = Start-Process -FilePath $setup.FullName -Wait -PassThru -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', "/MERGETASKS=`"$tasks`"", "/LOG=`"$results\$logName`""
  return [pscustomobject]@{ ExitCode = $process.ExitCode; Seconds = [math]::Round(((Get-Date) - $start).TotalSeconds) }
}

try {
  $rules = 'Plain Viewer - block network - '
  Log "Clean machine: Windows $([Environment]::OSVersion.Version); C++ runtime in System32: $(Test-Path "$env:WINDIR\System32\vcruntime140.dll"); WebView2: $(WebView2Version); network adapters up: $(@(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up').Count)"

  # Local copies: the app refuses files it cannot treat as local, and mapped folders are read-only.
  $corpus = Join-Path $env:USERPROFILE 'corpus'
  Copy-Item -LiteralPath 'C:\Test\input\corpus' -Destination $corpus -Recurse
  $setup = Get-ChildItem 'C:\Test\input' -Filter 'PlainViewer-Setup-*.exe' | Select-Object -First 1
  # First without the bundled WebView2 Runtime, to check the app's message; the second install below adds it.
  $install = Install 'openwith,firewall,!webview2' 'install.log'
  Expect ($install.ExitCode -eq 0 -and -not (WebView2Version)) "install without the WebView2 option (exit $($install.ExitCode), $($install.Seconds) s)"
  $dir = Join-Path $env:LOCALAPPDATA 'Programs\Plain Viewer'
  $app = Join-Path $dir 'PlainViewer.exe'
  Expect (Test-Path -LiteralPath $app) 'app installed'
  Expect (Test-Path -LiteralPath "$env:USERPROFILE\AppData\LocalLow\PlainViewer\LibreOffice\profile\user\plainviewer-ready.txt") 'converter prepared during install'
  foreach ($name in 'document worker (out)', 'converter (out)', 'converter launcher (in)', 'converter scripting (out)') {
    & netsh advfirewall firewall show rule name="$rules$name" | Out-Null
    Expect ($LASTEXITCODE -eq 0) "firewall rule '$name'"
  }
  Expect (Test-Path 'HKCU:\Software\Classes\PlainViewer.docx') '"Open with" registered'

  # Without a WebView2 Runtime a PDF must be refused with a clear explanation (text formats are tested below).
  $code = Run $app @('--smoke-test', ('!' + (Join-Path $corpus 'pdf\simple.pdf'))) (Join-Path $results 'no-webview2.txt')
  $message = Get-Content (Join-Path $results 'no-webview2.txt') -Raw
  Expect ($code -eq 0 -and $message -match 'WebView2 Runtime') "without WebView2 a PDF is refused with a clear message"
  # Installing again is an upgrade over the existing installation; this time with the bundled WebView2 Runtime.
  $upgrade = Install 'openwith,firewall,webview2' 'upgrade.log'
  $version = WebView2Version
  $webViews = [bool]$version
  Expect ($upgrade.ExitCode -eq 0 -and $webViews) "install again over it (upgrade) with the bundled WebView2 Runtime (exit $($upgrade.ExitCode), $($upgrade.Seconds) s, runtime $version)"
  Expect (Test-Path -LiteralPath $app) 'app still installed after the upgrade'
  $native = 'simple.txt', 'complex.txt', 'simple.csv', 'complex.csv', 'simple.md', 'complex.markdown'
  $web = 'pdf\simple.pdf', 'pdf\complex.pdf', 'pdf\attack-javascript.pdf', 'pdf\attack-links.pdf', 'xlsx\simple.xlsx', 'xlsx\complex.xlsx',
    'docx\simple.docx', 'docx\complex-20-pages.docx', 'docx\attack-remote-image.docx', 'docx\attack-remote-template.docx', 'docx\attack-includepicture.docx',
    'pptx\simple.pptx', 'pptx\complex.pptx', 'pptx\attack-remote-image.pptx'
  $open = @(@($native) + $(if ($webViews) { @($web) } else { @() }) | ForEach-Object { Join-Path $corpus $_ })
  $refuse = @('pdf\zero-byte.pdf', 'pdf\not-a-pdf.pdf', 'xlsx\attack-xxe.xlsx', 'xlsx\attack-zip-bomb.xlsx', 'xlsx\password.xlsx', 'xlsx\macro.xlsm',
    'docx\attack-xxe.docx', 'docx\attack-zip-bomb.docx', 'docx\password.docx', 'docx\damaged-truncated.docx', 'docx\macro.docm',
    'pptx\attack-zip-bomb.pptx', 'pptx\password.pptx', 'pptx\macro.pptm') | ForEach-Object { '!' + (Join-Path $corpus $_) }
  $code = Run $app (@('--smoke-test') + $open + $refuse) (Join-Path $results 'smoke.txt')
  Get-Content (Join-Path $results 'smoke.txt') | ForEach-Object { Log "  $_" }
  Expect ($code -eq 0) "smoke test in the installed app: $($open.Count) opened, $($refuse.Count) refused (exit $code)"

  # Keyboard-only use (sends keystrokes, which is why it runs only here).
  # @(...) keeps it an array: splatting the bare string '-NoWeb' to powershell.exe makes the child exit with code 5.
  $webSwitch = @(if (-not $webViews) { '-NoWeb' })
  $output = (& powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\Test\input\keyboard-check.ps1' -App $app -Corpus $corpus -Log $log @webSwitch 2>&1 | Out-String).Trim()
  if ($output) { Log "  $output" }
  Expect ($LASTEXITCODE -eq 0) "keyboard: every toolbar control and the document are reachable with Tab, and Tab leaves the document (exit $LASTEXITCODE)"
  # High contrast (switches the sandbox's theme): images for review in results\high-contrast.
  Log (& powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\Test\input\high-contrast-capture.ps1' -App $app -Corpus $corpus -Out (Join-Path $results 'high-contrast') @webSwitch 2>&1 | Out-String).Trim()

  $uninstall = Start-Process -FilePath (Join-Path $dir 'unins000.exe') -Wait -PassThru -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES'
  Start-Sleep -Seconds 3
  Expect ($uninstall.ExitCode -eq 0) "uninstall (exit $($uninstall.ExitCode))"
  Expect (-not (Test-Path -LiteralPath $dir)) 'app folder removed'
  Expect (-not (Test-Path -LiteralPath "$env:LOCALAPPDATA\PlainViewer") -and -not (Test-Path -LiteralPath "$env:USERPROFILE\AppData\LocalLow\PlainViewer")) 'private data removed'
  & netsh advfirewall firewall show rule name="${rules}converter (out)" | Out-Null
  Expect ($LASTEXITCODE -ne 0) 'firewall rules removed'
  Expect (-not (Test-Path 'HKCU:\Software\Classes\PlainViewer.docx')) '"Open with" removed'
}
catch { Log "FAIL unexpected error: $($_.Exception.Message)"; $failures.Add('unexpected error') }
finally {
  Set-Content -LiteralPath (Join-Path $results 'done.txt') -Value $(if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' })
  shutdown.exe /s /t 5
}
