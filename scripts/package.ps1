# Builds the offline installer artifacts\installer\PlainViewer-Setup-<version>-x64.exe (see docs/RELEASING.md).
# 1. Runs the test scripts (skip with -SkipTests).
# 2. Publishes the app and its worker with .NET included (x64, no .NET install needed on the user's PC).
# 3. Packs them with the trimmed LibreOffice using Inno Setup, and writes the installer's SHA-256 beside it.
# The version comes from Directory.Build.props. Needs .tools\dotnet, .tools\feed with the .NET runtime packs,
# .tools\libreoffice-<version> (scripts\fetch-libreoffice.ps1) and .tools\innosetup-7.1.0.
param([switch]$SkipTests, [string]$LibreOfficeVersion = '26.2.6')
. "$PSScriptRoot\env.ps1"
$version = ([xml](Get-Content -Raw (Join-Path $repoRoot 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Version in Directory.Build.props must look like 1.2.3 (found '$version')." }
$libreOffice = Join-Path $repoRoot ".tools\libreoffice-$LibreOfficeVersion"
$iscc = Join-Path $repoRoot '.tools\innosetup-7.1.0\ISCC.exe'
$feed = Join-Path $repoRoot '.tools\feed'
$publish = Join-Path $repoRoot 'artifacts\publish\win-x64'
$output = Join-Path $repoRoot 'artifacts\installer'
if (-not (Test-Path -LiteralPath (Join-Path $libreOffice 'program\soffice.exe'))) { throw 'LibreOffice is missing: run scripts\fetch-libreoffice.ps1.' }
if (Test-Path -LiteralPath (Join-Path $libreOffice 'System64')) { throw 'LibreOffice is not trimmed: run scripts\trim-libreoffice.ps1.' }
if (-not (Test-Path -LiteralPath $iscc)) { throw "Inno Setup 7 is missing: expected $iscc" }

if (-not $SkipTests) {
  & "$PSScriptRoot\build.ps1" -Offline
  & "$PSScriptRoot\test.ps1" -Offline
  & "$PSScriptRoot\test-markdown.ps1" -Offline
  & "$PSScriptRoot\test-office-safety.ps1" -Offline
  & "$PSScriptRoot\smoke-test.ps1"
  & "$PSScriptRoot\security-smoke.ps1"
}

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
# The worker is published last so its own settings files are the ones that remain.
foreach ($project in 'src\PlainViewer.App\PlainViewer.App.csproj', 'src\PlainViewer.Worker\PlainViewer.Worker.csproj') {
  & $Dotnet publish (Join-Path $repoRoot $project) -c Release -r win-x64 --self-contained true -o $publish --source $feed `
    -p:SatelliteResourceLanguages=en -p:DebugType=none -p:DebugSymbols=false -p:DisableTransitiveFrameworkReferenceDownloads=true
  if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}
# The installed app carries the .NET runtime, so it also carries the runtime's licence and third-party notices.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$licenses = Join-Path $publish 'licenses\dotnet'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
$runtimePack = Get-ChildItem -LiteralPath $feed -Filter 'microsoft.netcore.app.runtime.win-x64.*.nupkg' | Sort-Object Name | Select-Object -Last 1
$zip = [IO.Compression.ZipFile]::OpenRead($runtimePack.FullName)
try { foreach ($name in 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') { [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($name), (Join-Path $licenses $name), $true) } }
finally { $zip.Dispose() }
foreach ($file in 'PlainViewer.exe', 'PlainViewer.Worker.exe', 'PlainViewer.Worker.dll', 'coreclr.dll', 'Assets\prewarm\prewarm-word.docx', 'THIRD-PARTY-NOTICES.md') {
  if (-not (Test-Path -LiteralPath (Join-Path $publish $file))) { throw "Published output is missing $file." }
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
& $iscc /Qp "/DAppVersion=$version" "/DPublishDir=$publish" "/DLibreOfficeDir=$libreOffice" "/DOutputDir=$output" (Join-Path $repoRoot 'installer\PlainViewer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup could not build the installer.' }
$setup = Join-Path $output "PlainViewer-Setup-$version-x64.exe"
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$setup.sha256" -Value "$hash  $(Split-Path $setup -Leaf)" -Encoding ascii
Write-Output "Installer: $setup ($([math]::Round((Get-Item -LiteralPath $setup).Length / 1MB)) MB)"
Write-Output "SHA-256: $hash"
Write-Output 'Not code-signed: Windows SmartScreen warns about an unknown publisher until it is.'
