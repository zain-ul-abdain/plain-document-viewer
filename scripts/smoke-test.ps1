# Opens one or two files of every format in real (off-screen) windows through the worker.
# -App tests an installed or published PlainViewer.exe instead of the development build.
param([string]$App)
if ($App) { $ErrorActionPreference = 'Stop'; $repoRoot = Split-Path $PSScriptRoot -Parent } else { . "$PSScriptRoot\env.ps1" }
. "$PSScriptRoot\app.ps1"
$fixtures = @('simple.txt','complex.txt','simple.csv','complex.csv','simple.md','complex.markdown',
  'pdf\simple.pdf','pdf\complex.pdf','pdf\attack-javascript.pdf','pdf\attack-links.pdf',
  'xlsx\simple.xlsx','xlsx\complex.xlsx','docx\simple.docx','docx\complex-20-pages.docx','pptx\simple.pptx','pptx\complex.pptx',
  'xlsx\variant.xltx','xlsx\macro.xlsm','docx\variant.dotx','docx\macro.docm','pptx\variant.ppsx','pptx\variant.potx','pptx\macro.pptm',
  'images\simple.png','images\complex.png','images\simple.jpg','images\complex-rotated-exif.jpg','images\simple.gif','images\simple.bmp','images\simple.ico',
  'images\simple.webp','images\complex.webp','images\simple.avif','images\complex.avif','images\simple.svg','images\complex.svg','images\png-named.jpg',
  'data\simple.json','data\complex.json','data\simple.xml','data\simple.log','data\simple.ini','data\simple.yaml','data\simple.yml') | ForEach-Object { Join-Path $repoRoot "tests\corpus\$_" }
$code = Invoke-PlainViewer $App (@('--smoke-test') + $fixtures)
if ($code -ne 0) { throw 'Native view/worker smoke test failed.' }
