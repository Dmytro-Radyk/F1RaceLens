. (Join-Path $PSScriptRoot 'Common.ps1')
$db = Get-RaceLensDatabase
$folder = Join-Path $db.Project '.local/backups'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$output = Join-Path $folder ("f1racelens-{0}-{1}.dump" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0,8)))
$previousPassword = $env:PGPASSWORD
try {
    $env:PGPASSWORD = $db.Password
    & (Join-Path $db.Root 'runtime/bin/pg_dump.exe') -h 127.0.0.1 -p $db.Port -U $db.User -d $db.Database -Fc --no-owner --no-acl -f $output
    if ($LASTEXITCODE -ne 0) { throw 'Database backup failed.' }
    Write-Output $output
} finally { $env:PGPASSWORD = $previousPassword }
