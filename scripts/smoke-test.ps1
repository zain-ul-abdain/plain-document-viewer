. "$PSScriptRoot\env.ps1"
$app = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
$fixtures = @('simple.txt','complex.txt','simple.csv','complex.csv','simple.md','complex.markdown',
  'pdf\simple.pdf','pdf\complex.pdf','pdf\attack-javascript.pdf','pdf\attack-links.pdf',
  'xlsx\simple.xlsx','xlsx\complex.xlsx','docx\simple.docx','docx\complex-20-pages.docx','pptx\simple.pptx','pptx\complex.pptx') | ForEach-Object { Join-Path $repoRoot "tests\corpus\$_" }
& $Dotnet $app --smoke-test @fixtures
if ($LASTEXITCODE -ne 0) { throw 'Native view/worker smoke test failed.' }
