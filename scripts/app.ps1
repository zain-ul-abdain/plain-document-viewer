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
