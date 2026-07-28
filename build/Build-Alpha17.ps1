param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",

    [switch]$SkipInstaller
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$LogRoot = Join-Path $Artifacts "build-logs"
$TriageRoot = Join-Path $Artifacts "triage"

[void](New-Item -ItemType Directory -Path $LogRoot -Force)
[void](New-Item -ItemType Directory -Path $TriageRoot -Force)

$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$Transcript = Join-Path $TriageRoot "alpha17-build-$Timestamp.txt"

Start-Transcript -LiteralPath $Transcript -Force

try {
    Write-Host "CastingCouch 2.0.81 Build" -ForegroundColor Cyan
    Write-Host "====================================" -ForegroundColor Cyan

    & (Join-Path $PSScriptRoot "Diagnose-BuildEnvironment.ps1")
    & (Join-Path $PSScriptRoot "Preflight.ps1")

    $Solution = Join-Path $Root "CreatorControlSuite.sln"

    Write-Host ""
    Write-Host "1/5 Restore" -ForegroundColor Cyan
    dotnet restore $Solution `
        -bl:(Join-Path $LogRoot "alpha17-restore.binlog")

    Write-Host ""
    Write-Host "2/5 Build" -ForegroundColor Cyan
    dotnet build $Solution `
        -c $Configuration `
        --no-restore `
        -bl:(Join-Path $LogRoot "alpha17-build.binlog")

    Write-Host ""
    Write-Host "3/5 Tests" -ForegroundColor Cyan
    dotnet test $Solution `
        -c $Configuration `
        --no-build `
        -bl:(Join-Path $LogRoot "alpha17-tests.binlog")

    Write-Host ""
    Write-Host "4/5 Publish" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "Build-App.ps1") `
        -Configuration $Configuration

    & (Join-Path $PSScriptRoot "Test-PublishLayout.ps1")

    if (-not $SkipInstaller) {
        Write-Host ""
        Write-Host "5/5 Installer" -ForegroundColor Cyan

        & (Join-Path $PSScriptRoot "Build-Release.ps1") `
            -Configuration $Configuration
    }
    else {
        Write-Host ""
        Write-Host "5/5 Installer übersprungen" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "2.0.81 Build erfolgreich." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "BUILD FEHLGESCHLAGEN" -ForegroundColor Red
    Write-Host $_ -ForegroundColor Red

    $Failure = Join-Path $TriageRoot "LAST-BUILD-FAILURE.txt"

    @(
        "Zeit: $(Get-Date -Format o)"
        "Fehler:"
        ($_ | Out-String)
        ""
        "Transcript:"
        $Transcript
        ""
        "Bitte Transcript und vorhandene .binlog-Dateien aus artifacts\build-logs bereitstellen."
    ) | Set-Content -LiteralPath $Failure -Encoding UTF8

    throw
}
finally {
    Stop-Transcript
}
