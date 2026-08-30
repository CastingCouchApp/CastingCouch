# Smoke-test: New-UpdateArtifacts.ps1 writes a named, signed manifest.
[CmdletBinding()]
param()

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$gitOpenSsl = "C:\Program Files\Git\usr\bin"
if (Test-Path -LiteralPath (Join-Path $gitOpenSsl "openssl.exe")) {
    $env:PATH = $gitOpenSsl + [IO.Path]::PathSeparator + $env:PATH
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("ccs-update-manifest-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    $package = Join-Path $work "CastingCouch-8.0.0-test-win-x64-setup.exe"
    [System.IO.File]::WriteAllBytes($package, [byte[]](1, 2, 3, 4, 5, 6, 7, 8, 9))

    $keyPath = Join-Path $work "test-private.pem"
    & openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out $keyPath
    if ($LASTEXITCODE -ne 0) {
        throw "openssl genpkey fehlgeschlagen (Exit $LASTEXITCODE)."
    }

    $manifestName = "update-manifest-tauri-win.json"
    $created = & (Join-Path $root "build\New-UpdateArtifacts.ps1") `
        -PackageZipPath $package `
        -Version "8.0.0-test" `
        -Channel "Alpha" `
        -OutputDirectory $work `
        -PrivateKeyPath $keyPath `
        -ReleaseNotes "smoke" `
        -ManifestFileName $manifestName

    $manifestPath = Join-Path $work $manifestName
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Manifest fehlt: $manifestPath (script returned $created)"
    }
    if (Test-Path -LiteralPath (Join-Path $work "update-manifest.json")) {
        throw "Default-Manifest darf bei -ManifestFileName nicht erzeugt werden."
    }

    $json = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($json.ProductId -ne "CreatorControlSuite") {
        throw "ProductId erwartet CreatorControlSuite, war $($json.ProductId)"
    }
    if ($json.PackageFileName -ne "CastingCouch-8.0.0-test-win-x64-setup.exe") {
        throw "PackageFileName unerwartet: $($json.PackageFileName)"
    }
    if ([string]::IsNullOrWhiteSpace($json.PackageSha256)) {
        throw "PackageSha256 fehlt."
    }
    if ($json.PackageSizeBytes -ne 9) {
        throw "PackageSizeBytes erwartet 9, war $($json.PackageSizeBytes)"
    }
    if ([string]::IsNullOrWhiteSpace($json.Signature)) {
        throw "Signature fehlt."
    }

    $expectedSha = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($json.PackageSha256 -ne $expectedSha) {
        throw "SHA-256 weicht ab: $($json.PackageSha256) vs $expectedSha"
    }

    Write-Host "Update-Manifest-Smoke-Test ok: $manifestPath"
    exit 0
}
finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
