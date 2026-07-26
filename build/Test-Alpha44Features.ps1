Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Xaml = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml") -Raw
$Code = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml.cs") -Raw
$Settings = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Core\Configuration\AppSettings.cs") -Raw

if ($Xaml -notmatch 'SpotifyAlbumCoverImage') { throw "Spotify Albumcover fehlt." }
if ($Code -notmatch 'PreviewMouseMove') { throw "Spotify Live-Lautstärke während Drag fehlt." }
if ($Code -notmatch 'QueueSpotifyVolumeUpdateAsync') { throw "Spotify Lautstärke-Helfer fehlt." }
if ($Xaml -notmatch 'AdditionalScenesListBox') { throw "Zusätzliche Szenen-UI fehlt." }
if ($Settings -notmatch 'AdditionalScenes') { throw "AdditionalScenes fehlt im Settings-Modell." }

Write-Host "Alpha-44 Feature-Prüfung bestanden." -ForegroundColor Green
