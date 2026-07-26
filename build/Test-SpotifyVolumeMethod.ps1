Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Path = Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml.cs"
$Content = Get-Content -LiteralPath $Path -Raw

$DefinitionCount = @(
    Select-String `
        -InputObject $Content `
        -Pattern 'private async Task QueueSpotifyVolumeUpdateAsync\(' `
        -AllMatches
).Matches.Count

if ($DefinitionCount -ne 1) {
    throw "QueueSpotifyVolumeUpdateAsync muss genau einmal definiert sein. Gefunden: $DefinitionCount"
}

$CallCount = @(
    Select-String `
        -InputObject $Content `
        -Pattern 'QueueSpotifyVolumeUpdateAsync\(' `
        -AllMatches
).Matches.Count

if ($CallCount -lt 3) {
    throw "QueueSpotifyVolumeUpdateAsync wird unerwartet selten verwendet. Gefunden: $CallCount"
}

if ($Content -notmatch '_spotifyVolumeChangeCts') {
    throw "_spotifyVolumeChangeCts fehlt."
}

if ($Content -notmatch '_updatingSpotifyUi') {
    throw "_updatingSpotifyUi fehlt."
}

Write-Host "Spotify-Lautstärke-Methodenprüfung bestanden." -ForegroundColor Green
