param([switch]$Foreground, [switch]$Cpu)
$ErrorActionPreference = 'Stop'
$assistantRoot = Join-Path (Split-Path (Split-Path $PSScriptRoot)) '.local/assistant'
$runtime = Join-Path $assistantRoot $(if ($Cpu) { 'runtime/llama-server.exe' } else { 'cuda/llama-server.exe' })
$model = Join-Path $assistantRoot 'instruct.gguf'
if (!(Test-Path -LiteralPath $runtime) -or !(Test-Path -LiteralPath $model)) { throw 'Run scripts/assistant/Setup.ps1 first.' }
$listener = Get-NetTCPConnection -LocalPort 4310 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $existing = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    if (!$existing -or $existing.Path -ne [IO.Path]::GetFullPath($runtime)) { throw 'Port 4310 belongs to another process. No second model was started.' }
    Set-Content -LiteralPath (Join-Path $assistantRoot 'server.pid') -Value $existing.Id
    Write-Host 'The local assistant is already running.'
    return
}
# Quantized KV cache keeps the 4B model within the laptop GPU budget. No public listener or prompt logs.
$arguments = @('-m', "`"$model`"", '--host', '127.0.0.1', '--port', '4310', '-c', '2048', '-np', '1', '-t', '4', '-b', '128', '-ub', '64',
    '--alias', 'f1-assistant', '--offline', '--no-webui', '--no-slots', '--no-cache-prompt', '--no-cache-idle-slots', '--cache-reuse', '0', '--log-disable')
if (!$Cpu) { $arguments += @('-ngl', '99', '--device', 'CUDA0', '-ctk', 'q8_0', '-ctv', 'q8_0', '-fa', 'on') }
if ($Foreground) { & $runtime @($arguments | ForEach-Object { $_.Trim('"') }); return }
$process = Start-Process -FilePath $runtime -ArgumentList $arguments -WindowStyle Hidden -PassThru
Set-Content -LiteralPath (Join-Path $assistantRoot 'server.pid') -Value $process.Id
Write-Host 'Local F1 assistant is starting on loopback port 4310. First load may take a moment.'
