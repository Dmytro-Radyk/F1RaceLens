$ErrorActionPreference = 'Stop'
$assistantRoot = Join-Path (Split-Path (Split-Path $PSScriptRoot)) '.local/assistant'
$pidFile = Join-Path $assistantRoot 'server.pid'
if (!(Test-Path -LiteralPath $pidFile)) { return }
$assistantProcess = Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
$expected = @('runtime/llama-server.exe', 'cuda/llama-server.exe') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $assistantRoot $_)) }
if ($assistantProcess -and $assistantProcess.Path -in $expected) { Stop-Process -Id $assistantProcess.Id }
