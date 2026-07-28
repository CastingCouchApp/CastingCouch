Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$MainCode = Get-Content `
    -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml.cs") `
    -Raw

$MainXaml = Get-Content `
    -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\MainWindow.xaml") `
    -Raw

$AppXaml = Get-Content `
    -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\App.xaml") `
    -Raw

$PackageWix = Get-Content `
    -LiteralPath (Join-Path $Root "installer\CreatorControlSuite.Installer\Package.wxs") `
    -Raw

$WorkflowProject = Get-Content `
    -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.Workflow\CreatorControlSuite.Modules.Workflow.csproj") `
    -Raw

$Checks = @(
    @{
        Name = "Spotify volume helper"
        Passed = $MainCode -match 'private async Task QueueSpotifyVolumeUpdateAsync\('
    },
    @{
        Name = "Spotify album cover"
        Passed = $MainXaml -match 'SpotifyAlbumCoverImage'
    },
    @{
        Name = "Additional OBS scenes"
        Passed = $MainXaml -match 'AdditionalScenesListBox'
    },
    @{
        Name = "Dark text defaults"
        Passed = $AppXaml -match '<Style TargetType="TextBlock">'
    },
    @{
        Name = "ComboBox items black text"
        Passed = $AppXaml -match '<Style TargetType="ComboBoxItem">'
    },
    @{
        Name = "Workflow library output"
        Passed = $WorkflowProject -match '<OutputType>Library</OutputType>'
    },
    @{
        Name = "MajorUpgrade"
        Passed = $PackageWix -match 'MajorUpgrade'
    }
)

$Failed = @($Checks | Where-Object { -not $_.Passed })

foreach ($Check in $Checks) {
    $State = if ($Check.Passed) { "OK" } else { "FEHLER" }
    Write-Host "$State - $($Check.Name)"
}

if ($Failed.Count -gt 0) {
    throw "Alpha-48 Regressionsprüfung fehlgeschlagen: $($Failed.Name -join ', ')"
}

Write-Host "Alpha-48 Regressionsprüfung bestanden." -ForegroundColor Green
