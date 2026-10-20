. (Join-Path $PSScriptRoot 'Common.ps1')
$db = Get-RaceLensDatabase
& (Join-Path $db.Root 'runtime/bin/pg_ctl.exe') -D (Join-Path $db.Root 'data') -m fast -w stop
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL failed to stop.' }
