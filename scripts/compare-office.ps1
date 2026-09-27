# Compares Plain Viewer's Word and PowerPoint conversion with PDFs that Microsoft Office made from the same files.
# Pairs: tests/fidelity/pairs.json (public documents; -Fetch downloads them into tests/fidelity/reference and checks
# each SHA-256). Per pair: page counts; how much of the Office PDF's text lands on the same page of Plain Viewer's PDF,
# and anywhere in it; and a rough visual similarity of the first pages, drawn by Windows' own PDF renderer (independent
# of LibreOffice and PDF.js). Side-by-side images go to artifacts\compare for review; results.json holds the numbers.
# Needs Windows PowerShell 5.1, Node.js, and "npm install --omit=optional" in tests/fidelity. -App uses an installed exe.
param([switch]$Fetch, [int]$Pages = 2, [string]$App, [string[]]$Only)
if ($App) { $ErrorActionPreference = 'Stop'; $repoRoot = Split-Path $PSScriptRoot -Parent } else { . "$PSScriptRoot\env.ps1" }
$fidelity = Join-Path $repoRoot 'tests\fidelity'
$reference = Join-Path $fidelity 'reference'
. (Join-Path $fidelity 'render-pages.ps1')
Add-Type -AssemblyName System.Drawing
$pairs = @(Get-Content -Raw -LiteralPath (Join-Path $fidelity 'pairs.json') | ConvertFrom-Json | ForEach-Object { $_ })   # unroll (PowerShell 5.1)
if ($Only) { $pairs = @($pairs | Where-Object id -in $Only) }

if ($Fetch) {
  New-Item -ItemType Directory -Force -Path $reference | Out-Null
  $download = @'
const fs = require("fs"), crypto = require("crypto");
(async () => {
  const [url, file, expected] = process.argv.slice(1);
  const response = await fetch(url);
  if (!response.ok) throw new Error(`HTTP ${response.status} for ${url}`);
  const data = Buffer.from(await response.arrayBuffer());
  const actual = crypto.createHash("sha256").update(data).digest("hex");
  if (actual !== expected) throw new Error(`checksum mismatch for ${url}: the publisher may have changed the file`);
  fs.writeFileSync(file, data);
})().catch(e => { console.error(e.message); process.exit(1); });
'@
  foreach ($pair in $pairs) {
    foreach ($part in @(@($pair.officeUrl, $pair.officeFile, $pair.officeSha256), @($pair.pdfUrl, $pair.pdfFile, $pair.pdfSha256))) {
      $target = Join-Path $reference $part[1]
      if (Test-Path -LiteralPath $target) { continue }
      & node -e $download $part[0] $target $part[2]
      if ($LASTEXITCODE -ne 0) { Write-Output "SKIP $($pair.id): download failed" }
    }
  }
}

function Words([string]$text) {
  # Chinese, Japanese and Korean characters count one by one; other scripts by runs of letters and digits.
  $counts = @{}
  foreach ($match in [regex]::Matches($text.ToLowerInvariant(), '\p{IsCJKUnifiedIdeographs}|[\p{L}\p{N}]+')) { $counts[$match.Value] = 1 + [int]$counts[$match.Value] }
  return $counts
}
function Overlap($expected, $actual) {
  $total = 0; $found = 0
  foreach ($word in $expected.Keys) { $total += $expected[$word]; $found += [Math]::Min($expected[$word], [int]$actual[$word]) }
  if ($total -eq 0) { return $null }
  return $found / $total
}
function PdfText([string]$pdf) {
  $ErrorActionPreference = 'Continue'   # stderr from node is not an error here; the exit code is checked
  $json = & node (Join-Path $fidelity 'pdf-text.mjs') $pdf 2>$null
  if ($LASTEXITCODE -ne 0) { throw "Could not read the text of $pdf" }
  return $json | ConvertFrom-Json
}
function Similarity([string]$first, [string]$second) {
  # Both pages shrunk to 60 pixels wide in grey; 1 = identical, lower = more different. Shifted text lowers it a lot.
  $a = [System.Drawing.Bitmap]::FromFile($first); $b = [System.Drawing.Bitmap]::FromFile($second)
  try {
    $w = 60; $h = [int](60 * $a.Height / $a.Width)
    $small = foreach ($image in $a, $b) { $bitmap = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bitmap); $g.DrawImage($image, 0, 0, $w, $h); $g.Dispose(); $bitmap }
    $difference = 0.0
    for ($y = 0; $y -lt $h; $y++) { for ($x = 0; $x -lt $w; $x++) {
      $p = $small[0].GetPixel($x, $y); $q = $small[1].GetPixel($x, $y)
      $difference += [Math]::Abs(($p.R + $p.G + $p.B) - ($q.R + $q.G + $q.B)) / 3.0 } }
    $small | ForEach-Object { $_.Dispose() }
    return 1 - $difference / ($w * $h * 255)
  }
  finally { $a.Dispose(); $b.Dispose() }
}
function SideBySide([string]$office, [string]$ours, [string]$target) {
  $a = [System.Drawing.Image]::FromFile($office); $b = [System.Drawing.Image]::FromFile($ours)
  try {
    $canvas = New-Object System.Drawing.Bitmap ($a.Width + $b.Width + 20), ([Math]::Max($a.Height, $b.Height) + 30)
    $g = [System.Drawing.Graphics]::FromImage($canvas); $g.Clear([System.Drawing.Color]::Gray)
    $font = New-Object System.Drawing.Font 'Segoe UI', 11
    $g.DrawString('Microsoft Office', $font, [System.Drawing.Brushes]::White, 4, 5); $g.DrawString('Plain Viewer (LibreOffice)', $font, [System.Drawing.Brushes]::White, $a.Width + 24, 5)
    $g.DrawImage($a, 0, 30, $a.Width, $a.Height); $g.DrawImage($b, $a.Width + 20, 30, $b.Width, $b.Height)
    $g.Dispose(); $canvas.Save($target, [System.Drawing.Imaging.ImageFormat]::Png); $canvas.Dispose()
  }
  finally { $a.Dispose(); $b.Dispose() }
}

$out = Join-Path $repoRoot 'artifacts\compare'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null
$results = foreach ($pair in $pairs) {
  $office = Join-Path $reference $pair.officeFile; $officePdf = Join-Path $reference $pair.pdfFile
  if (-not (Test-Path -LiteralPath $office) -or -not (Test-Path -LiteralPath $officePdf)) { Write-Output "SKIP $($pair.id): files missing (use -Fetch)"; continue }
  $ours = Join-Path $out "$($pair.id).pdf"
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $process = if ($App) { Start-Process -FilePath $App -ArgumentList '--export-pdf', "`"$office`"", "`"$ours`"" -Wait -PassThru -NoNewWindow }
    else { Start-Process -FilePath $Dotnet -ArgumentList "`"$(Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll')`"", '--export-pdf', "`"$office`"", "`"$ours`"" -Wait -PassThru -NoNewWindow }
  $seconds = [Math]::Round($clock.Elapsed.TotalSeconds, 1)
  if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ours)) { [pscustomobject]@{ id = $pair.id; kind = $pair.kind; error = 'conversion failed' }; continue }
  $expected = PdfText $officePdf; $actual = PdfText $ours
  $samePage = @(for ($i = 0; $i -lt [Math]::Min($expected.pages, $actual.pages); $i++) {
    $words = Words $expected.text[$i]
    if ($words.Count -ge 5) { Overlap $words (Words $actual.text[$i]) } })
  $anywhere = Overlap (Words ($expected.text -join ' ')) (Words ($actual.text -join ' '))
  $similar = @()
  $count = [Math]::Min($Pages, [Math]::Min($expected.pages, $actual.pages))
  if ($count -gt 0) {
    $null = Export-PdfPages $officePdf (Join-Path $out "$($pair.id)-office") $count 600
    $null = Export-PdfPages $ours (Join-Path $out "$($pair.id)-ours") $count 600
    for ($n = 1; $n -le $count; $n++) {
      $similar += Similarity (Join-Path $out "$($pair.id)-office-$n.png") (Join-Path $out "$($pair.id)-ours-$n.png")
      SideBySide (Join-Path $out "$($pair.id)-office-$n.png") (Join-Path $out "$($pair.id)-ours-$n.png") (Join-Path $out "$($pair.id)-page$n.png")
      Remove-Item -LiteralPath (Join-Path $out "$($pair.id)-office-$n.png"), (Join-Path $out "$($pair.id)-ours-$n.png")
    }
  }
  [pscustomobject]@{
    id = $pair.id; kind = $pair.kind
    officePages = $expected.pages; ourPages = $actual.pages
    textSamePage = if ($samePage) { [Math]::Round(($samePage | Measure-Object -Average).Average * 100) } else { $null }
    textAnywhere = if ($null -ne $anywhere) { [Math]::Round($anywhere * 100) } else { $null }
    visual = if ($similar) { [Math]::Round(($similar | Measure-Object -Average).Average, 3) } else { $null }
    seconds = $seconds; features = $pair.features
  }
}
$results | Format-Table id, kind, officePages, ourPages, textSamePage, textAnywhere, visual, seconds, error -AutoSize | Out-String -Width 200 | Write-Output
Write-Output 'textSamePage / textAnywhere: % of the Office PDF''s words found on the same page / anywhere in ours. visual: 1 = identical first pages (rough; shifted text lowers it).'
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'results.json') -Encoding utf8
