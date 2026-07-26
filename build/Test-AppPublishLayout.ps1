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
    "CreatorControlSuite.deps.json"
)

$RequiredDirectories = @(
    "BundledOverlay",
    "Legal",
    "Keys"
)

$Errors = New-Object System.Collections.Generic.List[string]

foreach ($Relative in $RequiredFiles) {
    $Path = Join-Path $PublishPath $Relative

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $Errors.Add("Datei fehlt: $Relative")
    }
}

foreach ($Relative in $RequiredDirectories) {
    $Path = Join-Path $PublishPath $Relative

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        $Errors.Add("Ordner fehlt: $Relative")
    }
}

if ($Errors.Count -gt 0) {
    Write-Host ""
    Write-Host "App-Publish-Layout unvollständig:" -ForegroundColor Red

    foreach ($ErrorItem in $Errors) {
        Write-Host "FEHLER: $ErrorItem" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "Tatsächlicher Publish-Inhalt:" -ForegroundColor Yellow

    if (Test-Path -LiteralPath $PublishPath -PathType Container) {
        Get-ChildItem -LiteralPath $PublishPath |
            Select-Object Name, Length, PSIsContainer |
            Format-Table -AutoSize
    }

    exit 1
}

Write-Host "App-Publish-Layout vollständig." -ForegroundColor Green
