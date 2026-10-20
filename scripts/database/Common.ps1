$ErrorActionPreference = 'Stop'
function Get-RaceLensDatabase {
    $project = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $location = Join-Path $project '.local/database-path.json'
    $configuration = Join-Path $project 'backend/F1RaceLens.Api/appsettings.Local.json'
    if (!(Test-Path -LiteralPath $location) -or !(Test-Path -LiteralPath $configuration)) { throw 'Run scripts/database/Setup.ps1 first.' }
    $metadata = Get-Content -Raw -LiteralPath $location | ConvertFrom-Json
    $settings = Get-Content -Raw -LiteralPath $configuration | ConvertFrom-Json
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection.set_ConnectionString($settings.ConnectionStrings.RaceLens)
    if ($connection['Host'] -ne '127.0.0.1' -or $connection['Database'] -notmatch '^[a-z0-9_]+$') { throw 'These scripts operate on the local database only.' }
    return @{ Project = $project; Root = $metadata.installRoot; Port = [int]$connection['Port']; Database = $connection['Database']; User = $connection['Username']; Password = $connection['Password'] }
}
