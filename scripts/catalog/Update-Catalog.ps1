param([string]$Version = 'v2026.17.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^v\d{4}\.\d+\.\d+$') { throw 'Invalid release version' }
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$work = Join-Path $repository '.local/catalog-import'
New-Item -ItemType Directory -Force $work | Out-Null
$archive = Join-Path $work 'f1db.zip'
Invoke-WebRequest "https://github.com/f1db/f1db/releases/download/$Version/f1db-json-splitted.zip" -OutFile $archive -TimeoutSec 60
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    function Read-Table([string]$name) {
        $entry = $zip.GetEntry("f1db-$name.json")
        if ($null -eq $entry -or $entry.Length -gt 25000000) { throw 'Missing or oversized table' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return ConvertFrom-Json -InputObject $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $countries = @{}
    foreach ($country in (Read-Table 'countries')) { $countries[$country.id] = $country.alpha2Code }
    $directory = @{}
    foreach ($kind in @('drivers', 'constructors')) {
        $people = @(Read-Table $kind | Where-Object { $_.totalRaceEntries -gt 0 })
        $seasons = @(Read-Table "seasons-$kind")
        $standings = @(Read-Table $(if ($kind -eq 'drivers') { 'seasons-driver-standings' } else { 'seasons-constructor-standings' }))
        $idField = if ($kind -eq 'drivers') { 'driverId' } else { 'constructorId' }
        $years = @{}; $titles = @{}
        foreach ($row in $seasons) { $id = $row.$idField; $years[$id] = @($years[$id]) + [int]$row.year }
        foreach ($row in $standings) { if ($row.championshipWon) { $id = $row.$idField; $titles[$id] = @($titles[$id]) + [int]$row.year } }
        $directory[$kind] = @($people | ForEach-Object {
            $person = $_; $careerYears = @($years[$person.id] | Where-Object { $null -ne $_ } | Sort-Object -Unique)
            [ordered]@{
                id = $person.id; name = $person.name; fullName = $person.fullName
                country = $countries[$(if ($kind -eq 'drivers') { $person.nationalityCountryId } else { $person.countryId })]
                born = if ($kind -eq 'drivers') { $person.dateOfBirth } else { $null }
                died = if ($kind -eq 'drivers') { $person.dateOfDeath } else { $null }
                birthplace = if ($kind -eq 'drivers') { $person.placeOfBirth } else { $null }
                titles = [int]$person.totalChampionshipWins; wins = [int]$person.totalRaceWins
                entries = [int]$person.totalRaceEntries; starts = [int]$person.totalRaceStarts
                podiums = [int]$person.totalPodiums; poles = [int]$person.totalPolePositions
                fastestLaps = [int]$person.totalFastestLaps; points = $person.totalPoints
                seasons = $careerYears; titleYears = @($titles[$person.id] | Where-Object { $null -ne $_ } | Sort-Object -Unique)
            }
        })
        if ($directory[$kind].Count -lt $(if ($kind -eq 'drivers') { 800 } else { 150 })) { throw 'Incomplete directory' }
    }
    $directory['lineage'] = @(Read-Table 'constructors-chronology')
    $directory['source'] = 'F1DB'; $directory['version'] = $Version
    $directory['sourceUrl'] = "https://github.com/f1db/f1db/releases/tag/$Version"
    $directory['licenseUrl'] = 'https://creativecommons.org/licenses/by/4.0/'
    $directory['throughSeason'] = [int]$Version.Substring(1,4)
    $directory['throughRound'] = [int]($Version.Split('.')[1])
    $target = Join-Path $repository 'backend/F1RaceLens.Api/Catalog/Data'
    New-Item -ItemType Directory -Force $target | Out-Null
    $json = ConvertTo-Json -InputObject $directory -Depth 12 -Compress
    [IO.File]::WriteAllText((Join-Path $target 'catalog.json.tmp'), $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath (Join-Path $target 'catalog.json.tmp') -Destination (Join-Path $target 'catalog.json') -Force
    Write-Output "Catalog $Version : $($directory['drivers'].Count) drivers, $($directory['constructors'].Count) constructors."
} finally { $zip.Dispose() }
