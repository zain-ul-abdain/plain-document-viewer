. "$PSScriptRoot\env.ps1"
$app = Join-Path $repoRoot 'src\PlainViewer.App\bin\Release\net10.0-windows\PlainViewer.dll'
$fixtures = @('simple.txt','complex.txt','simple.csv','complex.csv','simple.md','complex.markdown') | ForEach-Object { Join-Path $repoRoot "tests\corpus\$_" }
& $Dotnet $app --smoke-test @fixtures
if ($LASTEXITCODE -ne 0) { throw 'Native view/worker smoke test failed.' }
