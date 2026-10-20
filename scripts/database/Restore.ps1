param(
    [Parameter(Mandatory)][string]$BackupFile,
    [Parameter(Mandatory)][ValidatePattern('^f1racelens_restore_[a-z0-9_]+$')][string]$TargetDatabase
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$db = Get-RaceLensDatabase
$backup = (Resolve-Path -LiteralPath $BackupFile).Path
$adminPasswordFile = Join-Path $db.Project '.local/admin-password'
if (!(Test-Path -LiteralPath $adminPasswordFile)) { throw 'Local bootstrap admin credentials are unavailable.' }
$previousPassword = $env:PGPASSWORD
try {
    $env:PGPASSWORD = [IO.File]::ReadAllText($adminPasswordFile)
    # createdb refuses an existing target. The active database cannot match the restore-only name.
    & (Join-Path $db.Root 'runtime/bin/createdb.exe') -h 127.0.0.1 -p $db.Port -U postgres -O $db.User -E UTF8 $TargetDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Restore requires a new empty database; existing data was not replaced.' }
    $env:PGPASSWORD = $db.Password
    & (Join-Path $db.Root 'runtime/bin/pg_restore.exe') -h 127.0.0.1 -p $db.Port -U $db.User -d $TargetDatabase --exit-on-error --no-owner --no-acl $backup
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed; the separate target was retained for inspection.' }
    Write-Host "Backup restored into $TargetDatabase. The active database was not modified."
} finally { $env:PGPASSWORD = $previousPassword }
