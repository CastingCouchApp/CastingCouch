param(
    [Parameter(Mandatory=$true)]
    [string]$PublishPath,

    [Parameter(Mandatory=$true)]
    [string]$Version
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$PublishPath = (Resolve-Path -LiteralPath $PublishPath).Path

$SetupRoot =
    Join-Path `
        $Root `
        "artifacts\setup"

$PackageRoot =
    Join-Path `
        $SetupRoot `
        "CreatorControlSuite-Setup"

$Payload =
    Join-Path `
        $PackageRoot `
        "Payload"

$ZipPath =
    Join-Path `
        $SetupRoot `
        ("CreatorControlSuite-" + $Version + "-Setup.zip")

if (Test-Path -LiteralPath $PackageRoot) {
    Remove-Item `
        -LiteralPath $PackageRoot `
        -Recurse `
        -Force
}

if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item `
        -LiteralPath $ZipPath `
        -Force
}

[void](New-Item `
    -ItemType Directory `
    -Path $Payload `
    -Force)

Copy-Item `
    -Path (Join-Path $PublishPath "*") `
    -Destination $Payload `
    -Recurse `
    -Force

$SourceFiles = @(
    Get-ChildItem `
        -LiteralPath $PublishPath `
        -Recurse `
        -File
)

$PayloadFiles = @(
    Get-ChildItem `
        -LiteralPath $Payload `
        -Recurse `
        -File
)

if ($SourceFiles.Count -ne $PayloadFiles.Count) {
    throw "Setup-Payload unvollständig: $($PayloadFiles.Count) von $($SourceFiles.Count) Dateien."
}

Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot "Install-CreatorControlSuite.ps1") `
    -Destination (Join-Path $PackageRoot "Install-CreatorControlSuite.ps1") `
    -Force

Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot "Install-CreatorControlSuite.cmd") `
    -Destination (Join-Path $PackageRoot "Install-CreatorControlSuite.cmd") `
    -Force

Compress-Archive `
    -Path (Join-Path $PackageRoot "*") `
    -DestinationPath $ZipPath `
    -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "Setup-ZIP wurde nicht erzeugt."
}

Write-Host "Setup-Paket erstellt:" -ForegroundColor Green
Write-Host $ZipPath
Write-Host "Payload-Dateien: $($PayloadFiles.Count)"
