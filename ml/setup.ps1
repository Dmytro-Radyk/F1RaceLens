$ErrorActionPreference = 'Stop'
$taskPreviousCache = $env:UV_CACHE_DIR
$taskPreviousTemp = $env:TEMP
$taskPreviousTmp = $env:TMP
Push-Location $PSScriptRoot
try {
    $env:UV_CACHE_DIR = Join-Path $PSScriptRoot '.cache/uv'
    $env:TEMP = Join-Path $PSScriptRoot '.cache/tmp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    uv venv --python 3.13 .venv
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create Python environment' }
    uv pip sync --python .venv/Scripts/python.exe requirements.txt
    if ($LASTEXITCODE -ne 0) { throw 'Cannot install ML dependencies' }
} finally {
    $env:UV_CACHE_DIR = $taskPreviousCache
    $env:TEMP = $taskPreviousTemp
    $env:TMP = $taskPreviousTmp
    Pop-Location
}
