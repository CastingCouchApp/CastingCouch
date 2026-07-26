param([Parameter(Mandatory=$true)][string]$Source)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Quellordner wurde nicht gefunden: $Source"
}

$Known = @(
    "settings.json",
    "overlay-data.json",
    "content",
    "alerts"
)

$Found = foreach ($Item in $Known) {
    if (Test-Path -LiteralPath (Join-Path $Source $Item)) {
        $Item
    }
}

if (-not $Found) {
    Write-Host "Keine bekannten Legacy-Dateien gefunden." -ForegroundColor Yellow
    exit 2
}

Write-Host "Erkannte Legacy-Inhalte:" -ForegroundColor Cyan
$Found | ForEach-Object {
    Write-Host " - $_" -ForegroundColor Green
}
