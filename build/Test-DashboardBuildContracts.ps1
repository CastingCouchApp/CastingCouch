Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$CodePath =
    Join-Path `
        $Root `
        "src\CreatorControlSuite.App\MainWindow.xaml.cs"

$XamlPath =
    Join-Path `
        $Root `
        "src\CreatorControlSuite.App\MainWindow.xaml"

$Code = Get-Content -LiteralPath $CodePath -Raw
$Xaml = Get-Content -LiteralPath $XamlPath -Raw

if ($Code -match '\bOpenSettingsButton\b' -and
    $Xaml -notmatch 'x:Name="OpenSettingsButton"') {
    throw "MainWindow-Code referenziert OpenSettingsButton, aber das XAML enthält dieses Element nicht."
}

if ($Code -match '\bstats\.StreamTime\b') {
    throw "MainWindow-Code referenziert die nicht vorhandene Eigenschaft StreamSessionStats.StreamTime."
}

$RequiredDashboardControls = @(
    "DashboardTwitchChatList",
    "DashboardTwitchEventsList",
    "DashboardTwitchUsersList",
    "DashboardSpotifyAlbumCoverImage",
    "StreamDashboardLamp",
    "ObsDashboardLamp",
    "TwitchDashboardLamp",
    "SpotifyDashboardLamp"
)

foreach ($Control in $RequiredDashboardControls) {
    if ($Xaml -notmatch ('x:Name="' + [regex]::Escape($Control) + '"')) {
        throw "Dashboard-Control fehlt im XAML: $Control"
    }

    if ($Code -notmatch ('\b' + [regex]::Escape($Control) + '\b')) {
        throw "Dashboard-Control wird im Code nicht verwendet: $Control"
    }
}

Write-Host "Dashboard-Build-Verträge geprüft." -ForegroundColor Green
