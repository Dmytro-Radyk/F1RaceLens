param([string]$InstallRoot = ((Split-Path -Qualifier $PSScriptRoot) + '\F1RaceLensDatabase'))
$ErrorActionPreference = 'Stop'
if ($InstallRoot -notmatch '^[A-Za-z]:\\[A-Za-z0-9_\\-]+$') { throw 'Use an absolute ASCII Windows path, e.g. E:\F1RaceLensDatabase.' }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$project = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$local = Join-Path $project '.local'
$metadata = Join-Path $local 'database-path.json'
$config = Join-Path $project 'backend/F1RaceLens.Api/appsettings.Local.json'
if (Test-Path -LiteralPath $metadata) {
    $existing = Get-Content -Raw -LiteralPath $metadata | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($existing.installRoot) -ne $InstallRoot) { throw 'An existing database is configured at another location.' }
    & (Join-Path $PSScriptRoot 'Start.ps1')
    Push-Location $project
    try { & dotnet run --project backend/F1RaceLens.Api --no-launch-profile -- --migrate; if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' } } finally { Pop-Location }
    return
}
if (Test-Path -LiteralPath $InstallRoot) { throw 'Refusing to overwrite an existing installation or cluster.' }
if (Test-Path -LiteralPath $config) { throw 'Refusing to overwrite an existing local API configuration.' }
New-Item -ItemType Directory -Path $local -Force | Out-Null
New-Item -ItemType Directory -Path $InstallRoot | Out-Null
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().Name
foreach ($folder in @($local,$InstallRoot)) {
    & icacls $folder /inheritance:r /grant:r "${owner}:(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restrict local database file permissions.' }
}
$archivePath = Join-Path $local 'postgres-download.zip'
Invoke-WebRequest 'https://sbp.enterprisedb.com/getfile.jsp?fileid=1260609' -OutFile $archivePath
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $runtime = Join-Path $InstallRoot 'runtime'
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -notmatch '^pgsql/(bin|lib|share)/' -or $entry.FullName.EndsWith('.pdb')) { continue }
        $destination = [IO.Path]::GetFullPath((Join-Path $runtime $entry.FullName.Substring(6)))
        if (!$destination.StartsWith($runtime + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive entry.' }
        if ($entry.FullName.EndsWith('/')) { New-Item -ItemType Directory -Path $destination -Force | Out-Null; continue }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$false)
    }
} finally { $archive.Dispose() }
$adminPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$appPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$passwordFile = Join-Path $local 'admin-password'
[IO.File]::WriteAllText($passwordFile,$adminPassword)
& (Join-Path $runtime 'bin/initdb.exe') -D (Join-Path $InstallRoot 'data') --username=postgres --auth-host=scram-sha-256 --auth-local=scram-sha-256 "--pwfile=$passwordFile" --encoding=UTF8 --locale=C
if ($LASTEXITCODE -ne 0) { throw 'Database initialization failed. Existing files were retained for inspection.' }
@"
listen_addresses = '127.0.0.1'
port = 55432
password_encryption = 'scram-sha-256'
shared_buffers = '128MB'
max_connections = 40
timezone = 'UTC'
"@ | Add-Content -Encoding utf8 (Join-Path $InstallRoot 'data/postgresql.conf')
& (Join-Path $runtime 'bin/pg_ctl.exe') -D (Join-Path $InstallRoot 'data') -l (Join-Path $InstallRoot 'postgresql.log') -w start
if ($LASTEXITCODE -ne 0) { throw 'Database startup failed.' }
$roleFile = Join-Path $local 'create-role.sql'
[IO.File]::WriteAllText($roleFile,"CREATE ROLE f1racelens LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '$appPassword';")
$previousPassword = $env:PGPASSWORD
try {
    $env:PGPASSWORD = $adminPassword
    & (Join-Path $runtime 'bin/psql.exe') -h 127.0.0.1 -p 55432 -U postgres -d postgres -v ON_ERROR_STOP=1 -f $roleFile
    if ($LASTEXITCODE -ne 0) { throw 'Application role creation failed.' }
    & (Join-Path $runtime 'bin/createdb.exe') -h 127.0.0.1 -p 55432 -U postgres -O f1racelens -E UTF8 f1racelens
    if ($LASTEXITCODE -ne 0) { throw 'Application database creation failed.' }
} finally { $env:PGPASSWORD = $previousPassword; Remove-Item -LiteralPath $roleFile }
$connection = "Host=127.0.0.1;Port=55432;Database=f1racelens;Username=f1racelens;Password=$appPassword;Timeout=5;Command Timeout=30"
$config = Join-Path $project 'backend/F1RaceLens.Api/appsettings.Local.json'
if (Test-Path -LiteralPath $config) { throw 'Refusing to overwrite an existing local API configuration.' }
@{ConnectionStrings=@{RaceLens=$connection}} | ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath $config
& icacls $config /inheritance:r /grant:r "${owner}:F" 'SYSTEM:F' | Out-Null
@{installRoot=$InstallRoot;version='18.6';port=55432} | ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath $metadata
Push-Location $project
try {
    & dotnet run --project backend/F1RaceLens.Api --no-launch-profile -- --migrate
    if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }
} finally { Pop-Location }
Write-Host 'PostgreSQL ready on 127.0.0.1:55432. Local credentials were not added to Git.'
