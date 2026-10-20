$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $project 'ml/.venv/Scripts/python.exe'
if (!(Test-Path -LiteralPath $python)) { throw 'Set up the ML environment first; see ml/README.md.' }
Push-Location (Join-Path $project 'ml')
try {
    & $python -u -m f1ml.update
    if ($LASTEXITCODE -ne 0) { throw "Forecast update failed (exit $LASTEXITCODE). See the status panel and .local/update-logs." }
} finally { Pop-Location }
