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

. (Join-Path $PSScriptRoot "Invoke-NativeChecked.ps1")

$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$Transcript = Join-Path $TriageRoot "alpha50-build-$Timestamp.txt"
Start-Transcript -LiteralPath $Transcript -Force

try {
    & (Join-Path $PSScriptRoot "Test-CriticalProjectConfiguration.ps1")

    Write-Host "Creator Control Suite 2.0.81 Build Fix 1" -ForegroundColor Cyan
    Write-Host "===========================================" -ForegroundColor Cyan

    & (Join-Path $PSScriptRoot "Diagnose-BuildEnvironment.ps1")
    & (Join-Path $PSScriptRoot "Preflight.ps1")
    & (Join-Path $PSScriptRoot "Test-PowerShellSyntax.ps1")
    & (Join-Path $PSScriptRoot "Test-PublishConfiguration.ps1")
    & (Join-Path $PSScriptRoot "Test-Alpha48Regression.ps1")
    & (Join-Path $PSScriptRoot "Test-SpotifyVolumeMethod.ps1")
    & (Join-Path $PSScriptRoot "Test-BuildStabilityProjects.ps1")
    & (Join-Path $PSScriptRoot "Prepare-CleanBuild.ps1") -Configuration $Configuration
    & (Join-Path $PSScriptRoot "Test-Alpha44Features.ps1")
    & (Join-Path $PSScriptRoot "Test-SpotifyThemeUiFixes.ps1")
    & (Join-Path $PSScriptRoot "Test-AuthThemeFixes.ps1")
    & (Join-Path $PSScriptRoot "Test-DIRegistrations.ps1")

    $Solution = Join-Path $Root "CreatorControlSuite.sln"

    Invoke-NativeChecked -FilePath "dotnet" -Step "1/5 Restore" -Arguments @(
        "restore", $Solution,
        "-m:1",
        "-nr:false",
        "-bl:$([IO.Path]::Combine($LogRoot,'alpha50-restore.binlog'))"
    )
Invoke-NativeChecked -FilePath "dotnet" -Step "2/5 Build" -Arguments @(
        "build", $Solution,
        "-c", $Configuration,
        "--no-restore",
        "-m:1",
        "-nr:false",
        "-p:BuildInParallel=false",
        "-bl:$([IO.Path]::Combine($LogRoot,'alpha50-build.binlog'))"
    )

    & (Join-Path $PSScriptRoot "Run-TestsChecked.ps1") `
        -Configuration $Configuration `
        -TimeoutSeconds 180

    Write-Host ""
    Write-Host "4/5 Publish" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "Build-App.ps1") -Configuration $Configuration -SkipTests
    & (Join-Path $PSScriptRoot "Test-AppPublishLayout.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "App-Publish-Layout-Prüfung fehlgeschlagen."
    }

    if (-not $SkipInstaller) {
        Write-Host ""
        Write-Host "5/5 Installer" -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot "Build-Release.ps1") -Configuration $Configuration -SkipAppBuild
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
        "Bitte das automatisch erzeugte CreatorControlSuite-BuildFailure-*.zip hochladen."
    ) | Set-Content -LiteralPath $Failure -Encoding UTF8

    throw
}
finally {
    Stop-Transcript
}
