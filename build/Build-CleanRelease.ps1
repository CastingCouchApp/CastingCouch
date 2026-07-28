param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Logs = Join-Path $Artifacts "clean-build-logs"
$Solution = Join-Path $Root "CreatorControlSuite.sln"
$AppProject = Join-Path $Root "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"
$TestsProject =
    Join-Path `
        $Root `
        "tests\CreatorControlSuite.Tests\CreatorControlSuite.Tests.csproj"

[void](New-Item -ItemType Directory -Path $Logs -Force)

. (Join-Path $PSScriptRoot "Invoke-NativeChecked.ps1")

& (Join-Path $PSScriptRoot "Test-BuildScriptEncoding.ps1")
& (Join-Path $PSScriptRoot "Test-CleanInstallerScript.ps1")
& (Join-Path $PSScriptRoot "Test-DashboardBuildContracts.ps1")
& (Join-Path $PSScriptRoot "Test-AppBuildConfiguration.ps1")

$DotNet = & (Join-Path $PSScriptRoot "Test-DotNetSdk.ps1") -RequiredMajor 10

Write-Host ""
Write-Host "CastingCouch 2.0.129 - Clean Release Build" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

function Remove-GeneratedProjectOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ParentPath
    )

    if (-not (Test-Path -LiteralPath $ParentPath -PathType Container)) {
        return
    }

    $ProjectDirectories = @(
        Get-ChildItem -LiteralPath $ParentPath -Directory -ErrorAction Stop
    )

    foreach ($ProjectDirectory in $ProjectDirectories) {
        foreach ($FolderName in @("bin", "obj")) {
            $Path = Join-Path $ProjectDirectory.FullName $FolderName

            if (-not (Test-Path -LiteralPath $Path)) {
                continue
            }

            try {
                # Remove the directory as a single target. Do not enumerate its
                # children first: build tools and antivirus scanners may remove
                # transient files concurrently on Windows.
                Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            }
            catch [System.IO.DirectoryNotFoundException] {
                # The target disappeared between Test-Path and Remove-Item.
                # That already satisfies the clean operation.
            }
            catch [System.IO.FileNotFoundException] {
                # Same race condition for a transient child file.
            }

            if (Test-Path -LiteralPath $Path) {
                throw "Clean fehlgeschlagen: $Path konnte nicht vollständig entfernt werden."
            }
        }
    }
}

# Only clean generated output. No legacy custom preflight chain.
Write-Host "0/5 Clean" -ForegroundColor Cyan

Remove-GeneratedProjectOutput -ParentPath (Join-Path $Root "src")
Remove-GeneratedProjectOutput -ParentPath (Join-Path $Root "tests")

# Directory.Build.props redirects all project output into the shared
# artifacts\obj and artifacts\bin directories. These directories must be
# removed as part of a clean build as well. Otherwise stale MSBuild caches can
# mark CoreCompile as up to date even when the expected DLL is no longer there,
# which later causes MSB3030 while copying the project output.
foreach ($CentralOutputName in @("obj", "bin")) {
    $CentralOutputPath = Join-Path $Artifacts $CentralOutputName

    if (-not (Test-Path -LiteralPath $CentralOutputPath)) {
        continue
    }

    try {
        Remove-Item `
            -LiteralPath $CentralOutputPath `
            -Recurse `
            -Force `
            -ErrorAction Stop
    }
    catch [System.IO.DirectoryNotFoundException] {
        # A transient build file disappeared while the directory was removed.
    }
    catch [System.IO.FileNotFoundException] {
        # The directory is already sufficiently clean.
    }

    if (Test-Path -LiteralPath $CentralOutputPath) {
        throw "Clean fehlgeschlagen: $CentralOutputPath konnte nicht vollständig entfernt werden."
    }
}

Write-Host "1/5 Restore" -ForegroundColor Cyan

Invoke-NativeChecked `
    -FilePath $DotNet `
    -Step "Restore" `
    -Arguments @(
        "restore",
        $Solution,
        "-m:1",
        "-nr:false",
        "-bl:$([IO.Path]::Combine($Logs,'restore.binlog'))"
    )

Write-Host "2/5 Build" -ForegroundColor Cyan

Invoke-NativeChecked `
    -FilePath $DotNet `
    -Step "Build" `
    -Arguments @(
        "build",
        $AppProject,
        "-c", $Configuration,
        "--no-restore",
        "-m:1",
        "-nr:false",
        "-p:BuildInParallel=false",
        "-bl:$([IO.Path]::Combine($Logs,'build.binlog'))"
    )

Write-Host "3/5 Tests" -ForegroundColor Cyan

Invoke-NativeChecked `
    -FilePath $DotNet `
    -Step "Tests" `
    -Arguments @(
        "test",
        $TestsProject,
        "-c", $Configuration,
        "--no-restore",
        "--logger", "console;verbosity=normal",
        "-bl:$([IO.Path]::Combine($Logs,'tests.binlog'))"
    )

Write-Host "4/5 Publish" -ForegroundColor Cyan

# This is the publish path that already succeeded in the Alpha-50 Windows run.
& (Join-Path $PSScriptRoot "Build-App.ps1") `
    -Configuration $Configuration `
    -SkipTests

if ($LASTEXITCODE -ne 0) {
    throw "Publish fehlgeschlagen."
}

$Publish =
    Join-Path `
        $Root `
        "artifacts\publish\win-x64"

$MainExe =
    Join-Path `
        $Publish `
        "CreatorControlSuite.exe"

if (-not (Test-Path -LiteralPath $MainExe -PathType Leaf)) {
    throw "Publish unvollständig: CreatorControlSuite.exe fehlt."
}

Write-Host "5/5 Setup-Paket" -ForegroundColor Cyan

& (Join-Path $PSScriptRoot "New-CleanSetupPackage.ps1") `
    -PublishPath $Publish `
    -Version "2.0.129"

Write-Host ""
Write-Host "CLEAN RELEASE ERFOLGREICH" -ForegroundColor Green
Write-Host ""
Write-Host "Setup-Paket:"
Write-Host (Join-Path $Root "artifacts\setup\CreatorControlSuite-2.0.129-Setup.zip")
