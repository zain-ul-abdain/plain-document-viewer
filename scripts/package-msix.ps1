# Makes the MSIX package for the Microsoft Store (DECISIONS.md D15) from the published app (scripts\package.ps1
# -Stage Publish) and the trimmed LibreOffice: artifacts\msix\PlainViewer-<version>-x64.msix. The Store signs packages
# it accepts, so a Store upload is not signed here: pass the identity Partner Center shows under Product identity
# (-IdentityName, -Publisher, -PublisherDisplayName). Without them the package gets a local-test identity, and
# -TestSign signs it with a self-signed certificate made for local testing only (never for distribution); its public
# part (test-certificate.cer) must be trusted on the test PC, which scripts\msix-sandbox-test.ps1 does inside Windows
# Sandbox only. Tools: Microsoft's SDK build tools (makeappx, signtool) in .tools\winsdk-buildtools-<version>.
param(
  [string]$IdentityName = 'PlainViewer.LocalTest',
  [string]$Publisher = 'CN=Plain Viewer Local Test',
  [string]$PublisherDisplayName = 'Plain Viewer (local test)',
  [string]$LibreOfficeVersion = '26.2.6',
  [switch]$TestSign)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $repoRoot 'artifacts\publish\win-x64'
$libreOffice = Join-Path $repoRoot ".tools\libreoffice-$LibreOfficeVersion"
$output = Join-Path $repoRoot 'artifacts\msix'
$layout = Join-Path $output 'layout'
if (-not (Test-Path -LiteralPath (Join-Path $publish 'PlainViewer.exe'))) { throw 'Nothing is published yet: run scripts\package.ps1 -Stage Publish first.' }
if (-not (Test-Path -LiteralPath (Join-Path $libreOffice 'program\soffice.exe'))) { throw 'LibreOffice is missing: run scripts\fetch-libreoffice.ps1.' }
$bin = Get-ChildItem (Join-Path $repoRoot '.tools') -Directory -Filter 'winsdk-buildtools-*' | Sort-Object Name -Descending |
  ForEach-Object { Get-ChildItem (Join-Path $_.FullName 'bin') -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64' } } |
  Where-Object { Test-Path (Join-Path $_ 'makeappx.exe') } | Select-Object -First 1
if (-not $bin) { throw 'The SDK build tools are missing: see docs/RELEASING.md (Microsoft Store package).' }
foreach ($tool in 'makeappx.exe', 'signtool.exe') {
  $signature = Get-AuthenticodeSignature (Join-Path $bin $tool)
  if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "$tool is not validly signed by Microsoft." }
}

# Store versions have four parts and the last must be 0.
[xml]$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
$version = (@($props.Project.PropertyGroup | ForEach-Object { $_.Version }) | Where-Object { $_ } | Select-Object -First 1) + '.0'

# Layout: the installed layout (app beside a libreoffice folder), the manifest and the tile pictures.
if (Test-Path -LiteralPath $layout) { Remove-Item -LiteralPath $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout, (Join-Path $layout 'Assets') | Out-Null
& robocopy $publish $layout /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Copying the app failed.' }
& robocopy $libreOffice (Join-Path $layout 'libreoffice') /E /XD __pycache__ /XF *.pyc /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Copying LibreOffice failed.' }
Add-Type -AssemblyName System.Drawing
$logo = [System.Drawing.Image]::FromFile((Join-Path $repoRoot 'docs\images\logo-256.png'))
try {
  foreach ($picture in @(@('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150), @('StoreLogo.png', 50))) {
    $bitmap = New-Object System.Drawing.Bitmap $picture[1], $picture[1]
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($logo, 0, 0, $picture[1], $picture[1]); $graphics.Dispose()
    $bitmap.Save((Join-Path $layout "Assets\$($picture[0])"), [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
  }
}
finally { $logo.Dispose() }
$escape = { param($text) [System.Security.SecurityElement]::Escape($text) }
(Get-Content (Join-Path $repoRoot 'installer\msix\AppxManifest.xml') -Raw).
  Replace('{IdentityName}', (& $escape $IdentityName)).Replace('{Publisher}', (& $escape $Publisher)).
  Replace('{PublisherDisplayName}', (& $escape $PublisherDisplayName)).Replace('{Version}', $version) |
  Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Encoding utf8

$package = Join-Path $output "PlainViewer-$version-x64.msix"
& (Join-Path $bin 'makeappx.exe') pack /d $layout /p $package /o /h SHA256 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx could not make the package.' }

if ($TestSign) {
  # A throwaway certificate for local testing: made, exported and removed from the certificate store at once.
  $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Publisher -CertStoreLocation Cert:\CurrentUser\My `
    -KeyUsage DigitalSignature -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddDays(30)
  $password = ConvertTo-SecureString ([Guid]::NewGuid().ToString('N')) -AsPlainText -Force
  $pfx = Join-Path $output 'test-certificate.pfx'
  try {
    Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password $password | Out-Null
    Export-Certificate -Cert $certificate -FilePath (Join-Path $output 'test-certificate.cer') | Out-Null
  }
  finally { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" }
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($password))
  & (Join-Path $bin 'signtool.exe') sign /fd SHA256 /f $pfx /p $plain $package | Out-Null
  $signed = $LASTEXITCODE
  Remove-Item -LiteralPath $pfx
  if ($signed -ne 0) { throw 'signtool could not sign the test package.' }
}
$item = Get-Item -LiteralPath $package
Write-Output ("{0} ({1:N0} MB){2}" -f $item.FullName, ($item.Length / 1MB), $(if ($TestSign) { ', signed with a local test certificate' } else { ', unsigned (for Store upload)' }))
