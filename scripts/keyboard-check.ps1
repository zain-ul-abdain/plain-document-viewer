# Keyboard-only check: opens each format, presses Tab repeatedly and records where focus lands (Windows UI Automation).
# Fails if a toolbar control is never reached, the document area is never reached, or focus cannot leave the document
# with Tab (a keyboard trap). SENDS KEYSTROKES to the focused window, so sandbox-inner.ps1 runs it inside Windows
# Sandbox only. Usage: keyboard-check.ps1 -App <PlainViewer.exe> -Corpus <tests\corpus folder> -Log <results file>
param([Parameter(Mandatory)][string]$App, [Parameter(Mandatory)][string]$Corpus, [Parameter(Mandatory)][string]$Log, [switch]$NoWeb)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$A = [System.Windows.Automation.AutomationElement]
$documentIds = 'TextView', 'MarkdownDisplay', 'CsvGrid'
$toolbar = 'Open file', 'Find text', 'About', 'Theme'
$problems = 0
# -NoWeb: only the native views (the PDF, Word, PowerPoint and Excel views need the WebView2 Runtime).
$samples = @('simple.txt', 'complex.markdown', 'complex.csv') + $(if ($NoWeb) { @() } else { @('pdf\complex.pdf', 'xlsx\complex.xlsx') })
foreach ($sample in $samples) {
  $process = Start-Process -FilePath $App -ArgumentList "`"$(Join-Path $Corpus $sample)`"" -PassThru
  try {
    $window = $null; $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
      $window = $A::RootElement.FindFirst('Children', [System.Windows.Automation.PropertyCondition]::new($A::ProcessIdProperty, $process.Id))
      $status = if ($window) { $window.FindFirst('Descendants', [System.Windows.Automation.PropertyCondition]::new($A::AutomationIdProperty, 'Status')) }
      if ($status -and $status.Current.Name -like 'Read only*') { break }
      Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Seconds 1
    $window.SetFocus()
    $stops = for ($i = 0; $i -lt 45; $i++) {
      [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Start-Sleep -Milliseconds 200
      $focus = $A::FocusedElement.Current
      $inDocument = $focus.AutomationId -in $documentIds -or $focus.FrameworkId -eq 'Chrome' -or $focus.ClassName -in 'DataGridCell', 'DataGridRow'
      [pscustomobject]@{ Name = $focus.Name; InDocument = $inDocument }
    }
    $firstDocument = [array]::IndexOf(@($stops.InDocument), $true)
    $reached = @($toolbar | Where-Object { $_ -in $stops.Name })
    $left = $firstDocument -ge 0 -and @($stops | Select-Object -Skip ($firstDocument + 1) | Where-Object { -not $_.InDocument -and $_.Name -in $toolbar }).Count -gt 0
    $ok = $reached.Count -eq $toolbar.Count -and $firstDocument -ge 0 -and $left
    if (-not $ok) { $problems++ }
    Add-Content -LiteralPath $Log -Value ("{0} keyboard {1}: toolbar reached {2}/{3}, document reached {4}, Tab leaves the document {5}; order: {6}" -f
      $(if ($ok) { 'PASS' } else { 'FAIL' }), $sample, $reached.Count, $toolbar.Count, ($firstDocument -ge 0), $left,
      ((@($stops | Select-Object -First 25 | ForEach-Object { if ($_.InDocument) { "[document]" } else { $_.Name } }) -join ' > ')))
  }
  finally { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
exit $problems
