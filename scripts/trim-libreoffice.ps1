# Prepares the unpacked LibreOffice for shipping inside Plain Viewer:
# - copies Microsoft's C++ runtime DLLs beside soffice.exe (an MSI install would put them in System32; a clean
#   Windows PC may not have them), and
# - removes what a read-only viewer never uses: interface translations, spelling and thesaurus data, Java
#   extensions (no Java is bundled), offline help, icon themes other than the Windows default, and folders
#   that only an MSI install uses.
# Kept on purpose: hyphenation patterns (they change line breaks), fonts, import/export filters, Python (bundled
# dictionary extensions register Python components) and every licence and readme file.
# Run by fetch-libreoffice.ps1. Safe to run again. -Backup moves removed files there instead of deleting them.
param(
  [string]$Path = (Join-Path (Split-Path $PSScriptRoot -Parent) '.tools\libreoffice-26.2.6'),
  [string]$Backup
)
$ErrorActionPreference = 'Stop'
$program = Join-Path $Path 'program'
if (-not (Test-Path -LiteralPath (Join-Path $program 'soffice.exe'))) { throw "No LibreOffice found at $Path" }
function Size([string]$folder) { [math]::Round(((Get-ChildItem -LiteralPath $folder -Recurse -File -Force | Measure-Object Length -Sum).Sum) / 1MB) }
$before = Size $Path

$runtime = Join-Path $Path 'System64'
if (Test-Path -LiteralPath $runtime) { Copy-Item (Join-Path $runtime '*.dll') $program -Force }
if (-not (Test-Path -LiteralPath (Join-Path $program 'vcruntime140.dll'))) { throw 'The C++ runtime DLLs are missing from LibreOffice''s program folder.' }

# LibreOffice loads its own fonts from share\fonts\truetype; the top-level Fonts folder is a copy meant for C:\Windows\Fonts.
$fonts = Join-Path $Path 'share\fonts\truetype'
if (Test-Path -LiteralPath (Join-Path $Path 'Fonts')) {
  New-Item -ItemType Directory -Force -Path $fonts | Out-Null
  Copy-Item (Join-Path $Path 'Fonts\*') $fonts -Force
}

$remove = [System.Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath (Join-Path $program 'resource') -Directory | Where-Object Name -ne 'common' | ForEach-Object { $remove.Add($_.FullName) }
Get-ChildItem -LiteralPath (Join-Path $Path 'share\extensions') -Directory -Filter 'dict-*' | ForEach-Object {
  Get-ChildItem -LiteralPath $_.FullName -File |
    Where-Object { ($_.Extension -in '.dic', '.aff' -and $_.Name -notlike 'hyph*') -or $_.Name -like 'th_*' -or $_.Name -like 'thes_*' } |
    ForEach-Object { $remove.Add($_.FullName) }
}
Get-ChildItem -LiteralPath (Join-Path $Path 'share\config') -File -Filter 'images_*.zip' | Where-Object Name -notlike 'images_colibre*' | ForEach-Object { $remove.Add($_.FullName) }
foreach ($item in 'share\extensions\nlpsolver', 'share\extensions\wiki-publisher', 'help', 'Fonts', 'System', 'System64') {
  $full = Join-Path $Path $item
  if (Test-Path -LiteralPath $full) { $remove.Add($full) }
}

foreach ($full in $remove) {
  if ($Backup) {
    $destination = Join-Path $Backup $full.Substring($Path.TrimEnd('\').Length + 1)
    New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
    Move-Item -LiteralPath $full -Destination $destination -Force
  }
  else { Remove-Item -LiteralPath $full -Recurse -Force }
}
Write-Output "LibreOffice trimmed: $before MB -> $(Size $Path) MB ($($remove.Count) items $(if ($Backup) { "moved to $Backup" } else { 'removed' }))"
