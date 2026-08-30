# Builds Tauri bundles (NSIS/MSI on Windows, DMG on macOS) into artifacts/tauri
# and optionally signs update-manifest-tauri-*.json when a signing key is present.
[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipTests,
    [switch]$SkipSign
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tauriApp = Join-Path $root "tauri-app"
$dest = Join-Path $root "artifacts/tauri"
$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionJsonPath = Join-Path $root "version.json"
    $versionJson = Get-Content -LiteralPath $versionJsonPath -Raw | ConvertFrom-Json
    $Version = [string]$versionJson.version
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Keine Versionsnummer ermittelbar (version.json / -Version)."
}

Set-Location $tauriApp
npm ci
if (-not $SkipTests) {
    npm test
}

$bundles = if ($onWindows) { "nsis,msi" } else { "dmg" }
npx tauri build --bundles $bundles
if ($LASTEXITCODE -ne 0) {
    throw "tauri build fehlgeschlagen (Exit $LASTEXITCODE)."
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null

function Copy-BundleFile {
    param(
        [string]$BundleRoot,
        [string[]]$NameLike,
        [string]$DestinationPath
    )
    $hit = Get-ChildItem -LiteralPath $BundleRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            foreach ($pattern in $NameLike) {
                if ($_.Name -like $pattern) { return $true }
            }
            return $false
        } |
        Select-Object -First 1
    if (-not $hit) {
        return $false
    }
    Copy-Item -LiteralPath $hit.FullName -Destination $DestinationPath -Force
    Write-Host "Copied $($hit.Name) -> $DestinationPath"
    return $true
}

$bundleRoot = Join-Path $tauriApp "src-tauri/target/release/bundle"
$setupName = "CastingCouch-$Version-win-x64-setup.exe"
$msiName = "CastingCouch-$Version-win-x64.msi"
$dmgName = "CastingCouch-$Version-macos.dmg"

if ($onWindows) {
    if (-not (Copy-BundleFile -BundleRoot $bundleRoot -NameLike @("*-setup.exe") -DestinationPath (Join-Path $dest $setupName))) {
        throw "NSIS-Setup nicht gefunden unter $bundleRoot"
    }
    if (-not (Copy-BundleFile -BundleRoot $bundleRoot -NameLike @("*.msi") -DestinationPath (Join-Path $dest $msiName))) {
        throw "MSI nicht gefunden unter $bundleRoot"
    }
} else {
    if (-not (Copy-BundleFile -BundleRoot $bundleRoot -NameLike @("*.dmg") -DestinationPath (Join-Path $dest $dmgName))) {
        throw "DMG nicht gefunden unter $bundleRoot"
    }
}

function Test-SigningKeyAvailable {
    if (-not [string]::IsNullOrWhiteSpace($env:UPDATE_SIGNING_KEY_PEM)) {
        return $true
    }
    $devKey = Join-Path $root "tools/dev-keys/update-private.pem"
    return (Test-Path -LiteralPath $devKey)
}

function Get-ChannelFromVersion {
    param([string]$Value)
    if ($Value -match '(?i)alpha') { return "Alpha" }
    if ($Value -match '(?i)beta') { return "Beta" }
    return "Stable"
}

if (-not $SkipSign -and (Test-SigningKeyAvailable)) {
    $signScript = Join-Path $root "build/New-UpdateArtifacts.ps1"
    $channel = Get-ChannelFromVersion -Value $Version
    $changelog = Join-Path $root "docs/changelogs/CHANGELOG-$Version.md"
    $notes = ""
    if (Test-Path -LiteralPath $changelog) {
        $notes = Get-Content -LiteralPath $changelog -Raw
    }
    $setupPath = Join-Path $dest $setupName
    $dmgPath = Join-Path $dest $dmgName
    if (Test-Path -LiteralPath $setupPath) {
        & $signScript `
            -PackageZipPath $setupPath `
            -Version $Version `
            -Channel $channel `
            -OutputDirectory $dest `
            -ReleaseNotes $notes `
            -ManifestFileName "update-manifest-tauri-win.json"
    }
    if (Test-Path -LiteralPath $dmgPath) {
        & $signScript `
            -PackageZipPath $dmgPath `
            -Version $Version `
            -Channel $channel `
            -OutputDirectory $dest `
            -ReleaseNotes $notes `
            -ManifestFileName "update-manifest-tauri-macos.json"
    }
} else {
    Write-Host "Kein Signaturschlüssel — Tauri-Manifeste werden übersprungen (CI github-release signiert)."
}

Write-Host "Tauri artifacts -> $dest"
