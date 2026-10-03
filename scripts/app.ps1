# Runs Plain Viewer and returns its exit code, for the smoke scripts.
# Without -App: the development build through the local .NET host (env.ps1 must be loaded).
# With -App: an installed or published PlainViewer.exe, run as a user would; its console output is captured.
function Invoke-PlainViewer([string]$App, [string[]]$Arguments) {
  if (-not $App) {
    $dll = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw 'Run scripts/build.ps1 first.' }
    & $Dotnet $dll @Arguments | Out-Host
    return $LASTEXITCODE
  }
  if (-not (Test-Path -LiteralPath $App)) { throw "PlainViewer.exe not found at $App" }
  $out = New-TemporaryFile; $err = New-TemporaryFile
  try {
    $quoted = @($Arguments | ForEach-Object { '"' + $_ + '"' })
    $process = Start-Process -FilePath $App -ArgumentList $quoted -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out.FullName -RedirectStandardError $err.FullName
    Get-Content -LiteralPath $out.FullName, $err.FullName | Out-Host
    return $process.ExitCode
  }
  finally { Remove-Item -LiteralPath $out.FullName, $err.FullName -Force -ErrorAction SilentlyContinue }
}

# As Invoke-PlainViewer, but returns the exit code and the output (standard output and errors) instead of showing it.
function Invoke-PlainViewerOutput([string]$App, [string[]]$Arguments) {
  if (-not $App) {
    $dll = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
    $output = & $Dotnet $dll @Arguments 2>&1 | Out-String
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
  }
  $out = New-TemporaryFile; $err = New-TemporaryFile
  try {
    $quoted = @($Arguments | ForEach-Object { '"' + $_ + '"' })
    $process = Start-Process -FilePath $App -ArgumentList $quoted -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out.FullName -RedirectStandardError $err.FullName
    return [pscustomobject]@{ Code = $process.ExitCode; Output = (Get-Content -LiteralPath $out.FullName, $err.FullName -Raw | Out-String) }
  }
  finally { Remove-Item -LiteralPath $out.FullName, $err.FullName -Force -ErrorAction SilentlyContinue }
}
