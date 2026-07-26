Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$Twitch = Get-Content `
    -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.Twitch\TwitchOAuthClient.cs") `
    -Raw

if ($Twitch -notmatch 'ValidateClientId\(clientId\)') {
    throw "Twitch Client-ID-Validierung fehlt."
}

if ($Twitch -notmatch 'Twitch Client-ID ist ungültig') {
    throw "Benutzerfreundliche Twitch-Fehlermeldung fehlt."
}

$Spotify = Get-Content `
    -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.Spotify\SpotifyOAuthClient.cs") `
    -Raw

if ($Spotify -notmatch 'SpotifyScopeConverter') {
    throw "Spotify scope String/Array Converter fehlt."
}

$AppXaml = Get-Content `
    -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\App.xaml") `
    -Raw

if ($AppXaml -notmatch '<Style TargetType="CheckBox">') {
    throw "Globaler CheckBox-Dark-Theme-Style fehlt."
}

Write-Host "Auth-/Theme-Prüfung bestanden." -ForegroundColor Green
