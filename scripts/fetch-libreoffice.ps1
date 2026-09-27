# Downloads the pinned LibreOffice release, verifies it against the SHA-256 published by The Document Foundation,
# and unpacks it (administrative extract, no system-wide install) into .tools\libreoffice-<version>.
# LibreOffice converts Word and PowerPoint files to PDF for display. It is MPL-2.0 licensed; see THIRD-PARTY-NOTICES.md.
param([string]$Version = '26.2.6')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$target = Join-Path $repoRoot ".tools\libreoffice-$Version"
if (Test-Path (Join-Path $target 'program\soffice.exe')) { Write-Output "LibreOffice $Version is already at $target"; return }
$downloads = Join-Path $repoRoot '.tools\downloads'
New-Item -ItemType Directory -Force -Path $downloads | Out-Null
$name = "LibreOffice_${Version}_Win_x86-64.msi"
$msi = Join-Path $downloads $name
$url = "https://download.documentfoundation.org/libreoffice/stable/$Version/win/x86_64/$name"

# Node's fetch is used because Windows PowerShell's web client fails TLS in some sandboxed accounts.
$download = @'
const fs = require("fs"), crypto = require("crypto");
(async () => {
  const [url, file] = process.argv.slice(1);
  const expected = (await (await fetch(url + ".sha256", { redirect: "error" })).text()).trim().split(/\s+/)[0];
  if (!/^[0-9a-f]{64}$/.test(expected)) throw new Error("no checksum from download.documentfoundation.org");
  const response = await fetch(url);
  if (!response.ok) throw new Error("HTTP " + response.status);
  const hash = crypto.createHash("sha256"), out = fs.createWriteStream(file + ".part");
  for await (const chunk of response.body) { hash.update(chunk); out.write(chunk); }
  await new Promise(resolve => out.end(resolve));
  const actual = hash.digest("hex");
  if (actual !== expected) { fs.unlinkSync(file + ".part"); throw new Error(`checksum mismatch: expected ${expected}, got ${actual}`); }
  fs.renameSync(file + ".part", file);
  console.log("Verified SHA-256 " + actual);
})().catch(e => { console.error(e.message); process.exit(1); });
'@
& node -e $download $url $msi
if ($LASTEXITCODE -ne 0) { throw 'Download or checksum verification failed.' }

$process = Start-Process msiexec.exe -ArgumentList @('/a', "`"$msi`"", '/qn', "TARGETDIR=`"$target`"") -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Unpacking failed (msiexec exit code $($process.ExitCode))." }
Remove-Item $msi -Force
Remove-Item (Join-Path $target $name) -Force -ErrorAction SilentlyContinue

# The unpacked Fonts folder is meant for C:\Windows\Fonts; LibreOffice also loads fonts from share\fonts\truetype.
$fonts = Join-Path $target 'share\fonts\truetype'
New-Item -ItemType Directory -Force -Path $fonts | Out-Null
Copy-Item (Join-Path $target 'Fonts\*') $fonts -Force
Write-Output "LibreOffice $Version unpacked to $target"
