param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "Build-App.ps1") -Configuration $Configuration

$PublishDir = Join-Path $Root "artifacts\publish\win-x64"
$InstallerProject = Join-Path $Root "installer\CreatorControlSuite.Installer\CreatorControlSuite.Installer.wixproj"

dotnet build $InstallerProject `
    -c $Configuration `
    -p:PublishDir="$PublishDir\"

Write-Host "MSI erstellt unter artifacts\installer" -ForegroundColor Green
