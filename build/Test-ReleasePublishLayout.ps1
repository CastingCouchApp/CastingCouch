param(
    [string]$PublishPath = ""
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublishPath)) {
    $PublishPath = Join-Path $Root "artifacts\publish\win-x64"
}

$RequiredFiles = @(
    "CreatorControlSuite.exe",
    "CreatorControlSuite.runtimeconfig.json",
    "CreatorControlSuite.deps.json",
    "CreatorControlSuite.CommandClient.exe",
    "CreatorControlSuite.Updater.exe"
)

# Canvas/Chat-Overlays liegen als Embedded Resources in der Overlay-DLL;
# ein separater BundledOverlay-Ordner wird nicht mehr ausgeliefert.
$RequiredDirectories = @(
    "Legal",
    "Keys"
)

$Errors = New-Object System.Collections.Generic.List[string]

foreach ($Relative in $RequiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishPath $Relative) -PathType Leaf)) {
        $Errors.Add("Datei fehlt: $Relative")
    }
}

foreach ($Relative in $RequiredDirectories) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishPath $Relative) -PathType Container)) {
        $Errors.Add("Ordner fehlt: $Relative")
    }
}

if ($Errors.Count -gt 0) {
    foreach ($ErrorItem in $Errors) {
        Write-Host "FEHLER: $ErrorItem" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Vollständiges Release-Publish-Layout vorhanden." -ForegroundColor Green
