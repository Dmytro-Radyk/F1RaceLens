param(
    [ValidateRange(1950,9999)][int]$Season = (Get-Date).Year,
    [string]$Api = 'http://127.0.0.1:5080',
    [switch]$IncludeProfiles
)
$ErrorActionPreference = 'Stop'
$target = [Uri]$Api
$Api = $Api.TrimEnd('/')
if ($target.Scheme -ne 'http' -or $target.Host -ne '127.0.0.1' -or $target.AbsolutePath -ne '/') { throw 'Use a loopback API base URL.' }
if ((Invoke-RestMethod "$Api/health/storage").status -ne 'ready') { throw 'PostgreSQL storage is not ready.' }
function Import-Resource([string]$Path) {
    for ($attempt=0; $attempt -lt 4; $attempt++) {
        try { $response = Invoke-RestMethod "$Api$Path"; Start-Sleep -Milliseconds 500; return $response }
        catch { if ($attempt -eq 3) { throw }; Start-Sleep -Seconds ([Math]::Min(8, [Math]::Pow(2,$attempt))) }
    }
}
$calendar = Import-Resource "/api/seasons/$Season/calendar"
$drivers = Import-Resource "/api/seasons/$Season/standings/drivers"
$null = Import-Resource "/api/seasons/$Season/standings/constructors"
$through = if ($Season -lt (Get-Date).Year) { 100 } else { [int]$drivers.round }
$rounds = @($calendar.races | Where-Object { $_.round -le $through })
foreach ($race in $rounds) {
    $null = Import-Resource "/api/seasons/$Season/races/$($race.round)"
    foreach ($kind in @('race','qualifying','sprint')) {
        if ($kind -eq 'sprint' -and !($race.sessions | Where-Object kind -eq 'Sprint')) { continue }
        $null = Import-Resource "/api/seasons/$Season/races/$($race.round)/results/$kind"
    }
    Write-Host "Imported round $($race.round)/$($rounds.Count) of $Season"
}
if ($Season -le 1957) {
    $teams = Import-Resource "/api/seasons/$Season/participants/teams"
    foreach ($team in $teams.entries) { $null = Import-Resource "/api/seasons/$Season/participants/teams/$($team.id)/drivers" }
}
if ($IncludeProfiles) {
    foreach ($driver in $drivers.entries) { $null = Import-Resource "/api/seasons/$Season/drivers/$($driver.id)" }
}
Write-Host "Season $Season imported. Repeating this command reuses fresh database snapshots."
