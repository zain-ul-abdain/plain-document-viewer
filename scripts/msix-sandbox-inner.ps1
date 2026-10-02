# Runs inside Windows Sandbox (started by msix-sandbox-test.ps1): trusts the local test certificate (in the sandbox
# only), installs the WebView2 Runtime and the MSIX package, opens files of every kind through the packaged app,
# checks where its private data went and the "Open with" entries, removes the package and checks what is left.
# Writes C:\Test\results\results.txt and done.txt, then shuts the sandbox down.
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

try {
  $build = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue
  Log "Clean machine: build $($build.CurrentBuild).$($build.UBR)"
  $corpus = Join-Path $env:USERPROFILE 'corpus'
  Copy-Item -LiteralPath 'C:\Test\input\corpus' -Destination $corpus -Recurse
  $package = Get-ChildItem 'C:\Test\input' -Filter '*.msix' | Select-Object -First 1

  # The sandbox (not the host PC) trusts the local test certificate. Store packages are signed by Microsoft instead.
  Import-Certificate -FilePath 'C:\Test\input\test-certificate.cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
  # Windows Sandbox has no WebView2 Runtime; Windows 11 PCs have it. Microsoft's offline installer, as the EXE installer bundles.
  $webView2 = Start-Process 'C:\Test\input\MicrosoftEdgeWebView2RuntimeInstallerX64.exe' -ArgumentList '/silent', '/install' -Wait -PassThru
  Expect ($webView2.ExitCode -eq 0) "WebView2 Runtime installed for the test (exit $($webView2.ExitCode))"

  $start = Get-Date
  try { Add-AppxPackage -Path $package.FullName -ErrorAction Stop; $added = $true } catch { Log "  $($_.Exception.Message)"; $added = $false }
  $installed = Get-AppxPackage -Name 'PlainViewer.LocalTest'
  Expect ($added -and $installed) "MSIX installed ($([math]::Round(((Get-Date) - $start).TotalSeconds)) s): $($installed.PackageFullName)"
  $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\PlainViewer.exe'
  Expect (Test-Path -LiteralPath $alias) 'command-line alias PlainViewer.exe present'
  $openWith = @('docx', 'xlsx', 'pdf', 'png', 'md') | Where-Object {
    $names = (Get-Item "Registry::HKEY_CURRENT_USER\Software\Classes\.$_\OpenWithProgids" -ErrorAction SilentlyContinue).Property
    -not ($names | Where-Object { $_ -like 'AppX*' }) }
  Expect ($openWith.Count -eq 0) "'Open with' entries registered (checked .docx .xlsx .pdf .png .md)$(if ($openWith) { '; missing: ' + ($openWith -join ' ') })"

  # Every kind of view, through the packaged app (package identity, files in the read-only package folder).
  $files = 'complex.txt', 'complex.csv', 'complex.markdown', 'pdf\complex.pdf', 'xlsx\complex.xlsx', 'xlsx\conditional.xlsx', 'xlsx\drawings.xlsx',
    'docx\complex-20-pages.docx', 'pptx\complex.pptx', 'odt\complex.odt', 'xls\styles.xls', 'ods\styles-libreoffice.ods', 'images\complex.png', 'tiff\scan-3-pages.tiff',
    'docx\attack-remote-template.docx'
  $refused = 'docx\password.docx', 'xlsx\attack-zip-bomb.xlsx'
  $arguments = @('--smoke-test') + @($files | ForEach-Object { Join-Path $corpus $_ }) + @($refused | ForEach-Object { '!' + (Join-Path $corpus $_) })
  $start = Get-Date
  $code = Run $alias $arguments (Join-Path $results 'smoke.txt')
  $smoke = Get-Content (Join-Path $results 'smoke.txt') -ErrorAction SilentlyContinue
  $opened = @($smoke | Select-String '^PASS native').Count; $declined = @($smoke | Select-String '^PASS refused').Count
  Expect ($code -eq 0 -and $opened -eq $files.Count -and $declined -eq $refused.Count) "packaged app: $opened of $($files.Count) opened, $declined of $($refused.Count) refused (exit $code, $([math]::Round(((Get-Date) - $start).TotalSeconds)) s)"
  $smoke | ForEach-Object { Log "  $_" }

  # Where the private data went: the real LocalLow folder, or the package's own copy (MSIX redirects some AppData writes).
  $family = $installed.PackageFamilyName
  $real = Join-Path $env:USERPROFILE 'AppData\LocalLow\PlainViewer'
  $private = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalCache"
  foreach ($place in $real, $private, (Join-Path $env:LOCALAPPDATA 'PlainViewer')) {
    $items = @(Get-ChildItem -LiteralPath $place -Recurse -File -ErrorAction SilentlyContinue)
    Log ("  data in {0}: {1} files, {2:N1} MB{3}" -f $place, $items.Count, (($items | Measure-Object Length -Sum).Sum / 1MB),
      $(if ($items | Where-Object Name -eq 'plainviewer-ready.txt') { ' (converter profile ready)' } else { '' }))
  }
  Get-ChildItem -LiteralPath $private -Directory -Recurse -Depth 2 -ErrorAction SilentlyContinue | ForEach-Object { Log "    $($_.FullName.Substring($private.Length))" }

  Remove-AppxPackage -Package $installed.PackageFullName
  Expect (-not (Get-AppxPackage -Name 'PlainViewer.LocalTest')) 'MSIX removed'
  Expect (-not (Test-Path -LiteralPath $alias)) 'alias removed'
  $left = @(Get-ChildItem -LiteralPath $real -Recurse -File -ErrorAction SilentlyContinue)
  Log ("  left in {0} after removal: {1} files, {2:N1} MB" -f $real, $left.Count, (($left | Measure-Object Length -Sum).Sum / 1MB))
  Log "  left in the package folder after removal: $(Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA "Packages\$family"))"
}
catch { Log "FAIL unexpected error: $($_.Exception.Message)"; $failures.Add('unexpected error') }
finally {
  Set-Content -LiteralPath (Join-Path $results 'done.txt') -Value $(if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' })
  Stop-Computer -Force
}
