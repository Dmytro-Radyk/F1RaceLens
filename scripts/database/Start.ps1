. (Join-Path $PSScriptRoot 'Common.ps1')
$db = Get-RaceLensDatabase
& (Join-Path $db.Root 'runtime/bin/pg_ctl.exe') -D (Join-Path $db.Root 'data') status | Out-Null
if ($LASTEXITCODE -ne 0) {
    & (Join-Path $db.Root 'runtime/bin/pg_ctl.exe') -D (Join-Path $db.Root 'data') -l (Join-Path $db.Root 'postgresql.log') -w start
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL failed to start.' }
}
