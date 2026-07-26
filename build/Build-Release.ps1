param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",

    [switch]$SkipAppBuild
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Publish = Join-Path $Artifacts "publish\win-x64"
$InstallerOut = Join-Path $Artifacts "installer"
$Logs = Join-Path $Artifacts "build-logs"

[void](New-Item -ItemType Directory -Path $InstallerOut -Force)
[void](New-Item -ItemType Directory -Path $Logs -Force)

. (Join-Path $PSScriptRoot "Invoke-NativeChecked.ps1")
& (Join-Path $PSScriptRoot "Preflight.ps1")

if (-not $SkipAppBuild) {
    & (Join-Path $PSScriptRoot "Build-App.ps1") -Configuration $Configuration
}

if (-not (Test-Path -LiteralPath $Publish -PathType Container)) {
    throw "Publish-Ausgabe fehlt: $Publish"
}

Invoke-NativeChecked -FilePath "dotnet" -Step "CommandClient Publish" -Arguments @(
    "publish",
    (Join-Path $Root "src\CreatorControlSuite.CommandClient\CreatorControlSuite.CommandClient.csproj"),
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-o", $Publish
)

Invoke-NativeChecked -FilePath "dotnet" -Step "Updater Publish" -Arguments @(
    "publish",
    (Join-Path $Root "src\CreatorControlSuite.Updater\CreatorControlSuite.Updater.csproj"),
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-o", $Publish
)

& (Join-Path $PSScriptRoot "Test-ReleasePublishLayout.ps1") -PublishPath $Publish

& (Join-Path $PSScriptRoot "Test-InstallerPayload.ps1") -PublishPath $Publish

& (Join-Path $PSScriptRoot "SelfTest-WixValidators.ps1")


& (Join-Path $PSScriptRoot "Generate-WixPayload.ps1") `
    -PublishPath $Publish `
    -OutputPath (Join-Path $Root "installer\CreatorControlSuite.Installer\Files.wxs")

$GeneratedWix = Join-Path $Root "installer\CreatorControlSuite.Installer\Files.wxs"

& (Join-Path $PSScriptRoot "Test-GeneratedWixDirectories.ps1") `
    -WixPath $GeneratedWix

& (Join-Path $PSScriptRoot "Test-WixShortNames.ps1") `
    -WixPath $GeneratedWix

& (Join-Path $PSScriptRoot "Test-WixDuplicateTargetFiles.ps1") `
    -PackageWixPath (Join-Path $Root "installer\CreatorControlSuite.Installer\Package.wxs") `
    -GeneratedWixPath $GeneratedWix
$GeneratedComponentCount = @(
    Select-String -LiteralPath $GeneratedWix -Pattern '<Component Id=' -SimpleMatch
).Count

$PublishFileCount = @(
    Get-ChildItem -LiteralPath $Publish -Recurse -File
).Count

Write-Host "WiX-Komponentenprüfung: $GeneratedComponentCount Komponenten für $PublishFileCount Publish-Dateien"

if ($GeneratedComponentCount -ne $PublishFileCount) {
    throw "Generierter WiX-Payload ist unvollständig: $GeneratedComponentCount von $PublishFileCount Dateien."
}

if ($LASTEXITCODE -ne 0) {
    throw "Publish-Layout-Prüfung fehlgeschlagen."
}

& (Join-Path $PSScriptRoot "Test-WixFeatureAssignment.ps1") `
    -PackageWixPath (Join-Path $Root "installer\CreatorControlSuite.Installer\Package.wxs")

$PublishForMsBuild = $Publish.TrimEnd('\') + '\'

Write-Host "WiX PublishDir: $PublishForMsBuild"

Invoke-NativeChecked -FilePath "dotnet" -Step "Installer Build" -Arguments @(
    "build",
    (Join-Path $Root "installer\CreatorControlSuite.Installer\CreatorControlSuite.Installer.wixproj"),
    "-c", $Configuration,
    "-p:PublishDir=$PublishForMsBuild",
    "-bl:$([IO.Path]::Combine($Logs,'installer.binlog'))"
)

Write-Host "Release-Build abgeschlossen." -ForegroundColor Green
Write-Host "Publish: $Publish"
Write-Host "Installer: $InstallerOut"
