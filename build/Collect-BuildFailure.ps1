param([string]$OutputPath = "")

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $Artifacts (
        "CreatorControlSuite-BuildFailure-" +
        (Get-Date -Format "yyyyMMdd-HHmmss") + ".zip")
}

$Staging = Join-Path $env:TEMP (
    "CreatorControlSuite.BuildFailure." + [guid]::NewGuid().ToString("N"))
[void](New-Item -ItemType Directory -Path $Staging -Force)

try {
    foreach ($Relative in @(
        "artifacts\build-logs",
        "artifacts\triage",
        "artifacts\diagnostics"
    )) {
        $Source = Join-Path $Root $Relative
        if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
            continue
        }

        $Destination = Join-Path $Staging $Relative
        [void](New-Item -ItemType Directory -Path $Destination -Force)

        Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
        }
    }

    foreach ($Relative in @(
        "Directory.Build.props",
        "Directory.Packages.props",
        "CreatorControlSuite.sln"
    )) {
        $Source = Join-Path $Root $Relative
        if (Test-Path -LiteralPath $Source) {
            Copy-Item -LiteralPath $Source -Destination (Join-Path $Staging $Relative) -Force
        }
    }

    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    Compress-Archive -Path (Join-Path $Staging "*") -DestinationPath $OutputPath -CompressionLevel Optimal

    $ZipInfo = Get-Item -LiteralPath $OutputPath
    if ($ZipInfo.Length -lt 1024) {
        throw "Diagnose-ZIP ist unerwartet klein: $($ZipInfo.Length) Bytes."
    }

    Write-Host "Build-Fehlerpaket erstellt:" -ForegroundColor Green
    Write-Host $OutputPath
}
finally {
    Remove-Item -LiteralPath $Staging -Recurse -Force -ErrorAction SilentlyContinue
}
