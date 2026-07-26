param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",

    [switch]$SkipTests
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $Root "CreatorControlSuite.sln"
$Project = Join-Path $Root "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"
$Output = Join-Path $Root "artifacts\publish\win-x64"
$Logs = Join-Path $Root "artifacts\build-logs"
[void](New-Item -ItemType Directory -Path $Logs -Force)

. (Join-Path $PSScriptRoot "Invoke-NativeChecked.ps1")
$DotNet = & (Join-Path $PSScriptRoot "Test-DotNetSdk.ps1") -RequiredMajor 10
& (Join-Path $PSScriptRoot "Preflight.ps1")

Invoke-NativeChecked -FilePath $DotNet -Step "App Restore" -Arguments @(
    "restore", $Solution,
    "-bl:$([IO.Path]::Combine($Logs,'restore.binlog'))"
)

if (-not $SkipTests) {
    Invoke-NativeChecked -FilePath $DotNet -Step "App Tests" -Arguments @(
        "test", $Solution,
        "-c", $Configuration,
        "--no-restore",
        "-bl:$([IO.Path]::Combine($Logs,'tests.binlog'))"
    )
}

if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

Invoke-NativeChecked -FilePath $DotNet -Step "App Publish" -Arguments @(
    "publish", $Project,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishReadyToRun=false",
    "-p:DebugType=embedded",
    "-p:ContinuousIntegrationBuild=true",
    "-bl:$([IO.Path]::Combine($Logs,'publish.binlog'))",
    "-o", $Output
)

Write-Host "Anwendung veröffentlicht: $Output" -ForegroundColor Green
