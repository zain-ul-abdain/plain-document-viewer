# Checks what screen readers such as Narrator can see, through Windows UI Automation. Opens one sample of each
# format in the app (windows appear briefly), then fails if an enabled control has no accessible name, a native
# control cannot take keyboard focus, or the document's text is not exposed. Sends no keystrokes. -App tests an
# installed PlainViewer.exe. Not covered: the status line's live-region setting (set in MainWindow.xaml; the .NET
# Framework UI Automation client cannot read it), keyboard order, and Narrator itself, which needs a manual check.
param([string]$App, [string[]]$Only)
if ($App) { $ErrorActionPreference = 'Stop'; $repoRoot = Split-Path $PSScriptRoot -Parent } else { . "$PSScriptRoot\env.ps1" }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.ControlType]
$interactive = @($T::Button, $T::Edit, $T::ComboBox, $T::CheckBox, $T::RadioButton, $T::Hyperlink, $T::MenuItem, $T::TabItem, $T::Slider, $T::SplitButton)
# File, and a phrase that must be readable in the document area.
$samples = [ordered]@{ 'simple.txt' = 'Hello'; 'complex.markdown' = 'Hello'; 'complex.csv' = 'Hello'; 'pdf\complex.pdf' = 'Hello';
  'xlsx\complex.xlsx' = 'Hello'; 'docx\simple.docx' = 'Hello'; 'pptx\complex.pptx' = 'Hello' }
$problems = [System.Collections.Generic.List[string]]::new()
# Chromium (inside WebView2) builds the web page's accessibility tree only when it detects a screen reader such as
# Narrator. This audit is not one, so it asks for the tree explicitly; the app's own browser settings are unchanged.
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--force-renderer-accessibility'

function Find($root, $scope, $property, $value) { $root.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new($property, $value)) }
foreach ($sample in @($samples.Keys)) {
  if ($Only -and $sample -notin $Only) { continue }
  $path = Join-Path $repoRoot "tests\corpus\$sample"
  $process = if ($App) { Start-Process -FilePath $App -ArgumentList "`"$path`"" -PassThru }
    else { Start-Process -FilePath $Dotnet -ArgumentList "`"$(Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll')`" `"$path`"" -PassThru -WindowStyle Hidden }
  try {
    $window = $null; $status = $null; $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
      $window = Find $A::RootElement ([System.Windows.Automation.TreeScope]::Children) $A::ProcessIdProperty $process.Id
      if ($window) { $status = Find $window ([System.Windows.Automation.TreeScope]::Descendants) $A::AutomationIdProperty 'Status' }
      if ($status -and $status.Current.Name -match '^(Read only|This |The |Opening took)') { break }
      Start-Sleep -Milliseconds 500
    }
    if (-not $status) { $problems.Add("${sample}: window or status line not found"); continue }
    Start-Sleep -Seconds 2   # let the web view build its accessibility tree
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $checked = 0; $readable = $false
    foreach ($element in $all) {
      $current = $element.Current
      if (-not $readable -and $current.Name -match $samples[$sample] -and $current.ControlType -notin $interactive) { $readable = $true }
      if ($current.ControlType -notin $interactive -or -not $current.IsEnabled -or $current.IsOffscreen) { continue }
      $checked++
      if ([string]::IsNullOrWhiteSpace($current.Name)) { $problems.Add("${sample}: unnamed $($current.ControlType.ProgrammaticName) (id '$($current.AutomationId)', class '$($current.ClassName)')") }
      if ($current.FrameworkId -eq 'WPF' -and -not $current.IsKeyboardFocusable) { $problems.Add("${sample}: '$($current.Name)' cannot take keyboard focus") }
    }
    # Text views and web documents expose their text through the text pattern rather than names.
    $documents = @('TextView', 'MarkdownDisplay' | ForEach-Object { Find $window ([System.Windows.Automation.TreeScope]::Descendants) $A::AutomationIdProperty $_ }) +
      @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new($A::ControlTypeProperty, $T::Document)))
    foreach ($document in $documents | Where-Object { $_ }) {
      if ($readable) { break }
      try { $readable = $document.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(200000) -match $samples[$sample] }
      catch { Write-Output "  (text of '$($document.Current.Name)' could not be read: $($_.Exception.Message))" }
    }
    if (-not $readable) { $problems.Add("${sample}: the text '$($samples[$sample])' is not exposed to screen readers") }
    Write-Output ("{0,-20} {1,4} controls checked, document text readable: {2}, status: {3}" -f $sample, $checked, $readable, $status.Current.Name)
  }
  finally {
    try { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    if (-not $process.WaitForExit(10000)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
  }
}
if ($problems.Count) { $problems | ForEach-Object { Write-Output "PROBLEM $_" }; throw "$($problems.Count) accessibility problem(s)." }
Write-Output 'No accessibility problems found by the automated checks.'
