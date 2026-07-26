Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$XamlPath = Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml"
$CodePath = Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml.cs"
$AppXamlPath = Join-Path $Root "src\CreatorControlSuite.App\App.xaml"

$Xaml = Get-Content -LiteralPath $XamlPath -Raw
$Code = Get-Content -LiteralPath $CodePath -Raw
$AppXaml = Get-Content -LiteralPath $AppXamlPath -Raw

if ($Xaml -notmatch 'x:Name="SpotifyAlbumText"') {
    throw "Spotify Album-Anzeige fehlt."
}

if ($Xaml -match 'x:Name="SetSpotifyVolumeButton"') {
    throw "Alter Lautstärke-setzen-Button ist noch vorhanden."
}

if ($Code -notmatch 'SpotifyVolumeSlider\.ValueChanged') {
    throw "Live-Lautstärkeregelung fehlt."
}

if ($Code -notmatch '_updatingSpotifyUi') {
    throw "Spotify Slider-UI-Schutz fehlt."
}

if ($AppXaml -notmatch '<Style TargetType="TextBlock">') {
    throw "Globaler heller TextBlock-Style fehlt."
}

if ($AppXaml -notmatch '<Setter Property="Foreground" Value="Black"/>') {
    throw "Schwarze ComboBox-Schrift fehlt."
}

Write-Host "Spotify-/Theme-UI-Prüfung bestanden." -ForegroundColor Green
