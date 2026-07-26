param(
    [string]$PublishPath = ""
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublishPath)) {
    $PublishPath = Join-Path $Root "artifacts\publish\win-x64"
}

if (-not (Test-Path -LiteralPath $PublishPath -PathType Container)) {
    throw "Publish-Ordner fehlt: $PublishPath"
}

$Files = @(Get-ChildItem -LiteralPath $PublishPath -Recurse -File)
$Directories = @(Get-ChildItem -LiteralPath $PublishPath -Recurse -Directory)

Write-Host "Installer-Payload:"
Write-Host "  Dateien: $($Files.Count)"
Write-Host "  Unterordner: $($Directories.Count)"

if ($Files.Count -lt 10) {
    throw "Publish-Payload enthält unerwartet wenige Dateien: $($Files.Count)"
}

$Required = @(
    "CreatorControlSuite.exe",
    "CreatorControlSuite.dll",
    "CreatorControlSuite.deps.json",
    "CreatorControlSuite.runtimeconfig.json",
    "CreatorControlSuite.CommandClient.exe",
    "CreatorControlSuite.Updater.exe"
)

foreach ($Relative in $Required) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishPath $Relative) -PathType Leaf)) {
        throw "Erforderliche Payload-Datei fehlt: $Relative"
    }
}

Write-Host "Installer-Payload-Prüfung bestanden." -ForegroundColor Green
