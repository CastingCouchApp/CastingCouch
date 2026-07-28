param(
    [Parameter(Mandatory = $true)]
    [string]$PackageZipPath,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Channel = "",

    [string]$OutputDirectory = "",

    [string]$PrivateKeyPath = "",

    [string]$ReleaseNotes = "",

    [string]$MinimumVersion = "0.0.0",

    [string]$ChangelogPath = ""
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

function Get-UpdateChannelFromVersion {
    param([string]$Value)
    if ($Value -match '(?i)alpha') { return "Alpha" }
    if ($Value -match '(?i)beta') { return "Beta" }
    return "Stable"
}

function Get-CanonicalPayload {
    param(
        [string]$ProductId,
        [string]$Version,
        [string]$Channel,
        [string]$PackageFileName,
        [string]$PackageSha256,
        [long]$PackageSizeBytes,
        [datetimeoffset]$PublishedAt,
        [string]$MinimumVersion,
        [string]$ReleaseNotes
    )

    $published = $PublishedAt.ToUniversalTime().ToString("o")
    $notes = $ReleaseNotes -replace "`r`n", "`n"
    return ($ProductId + "`n" + $Version + "`n" + $Channel + "`n" + $PackageFileName + "`n" + $PackageSha256 + "`n" + $PackageSizeBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture) + "`n" + $published + "`n" + $MinimumVersion + "`n" + $notes)
}

if (-not (Test-Path -LiteralPath $PackageZipPath -PathType Leaf)) {
    throw "Update-Paket fehlt: $PackageZipPath"
}

$PackageZipPath = (Resolve-Path -LiteralPath $PackageZipPath).Path
$packageInfo = Get-Item -LiteralPath $PackageZipPath
$packageFileName = $packageInfo.Name
$packageSize = [long]$packageInfo.Length

$hash = Get-FileHash -LiteralPath $PackageZipPath -Algorithm SHA256
$packageSha = $hash.Hash.ToUpperInvariant()

if ([string]::IsNullOrWhiteSpace($Channel)) {
    $Channel = Get-UpdateChannelFromVersion -Value $Version
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Split-Path -Parent $PackageZipPath
}

[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)

if ([string]::IsNullOrWhiteSpace($ReleaseNotes) -and -not [string]::IsNullOrWhiteSpace($ChangelogPath) -and (Test-Path -LiteralPath $ChangelogPath)) {
    $ReleaseNotes = Get-Content -LiteralPath $ChangelogPath -Raw
}

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "CastingCouch $Version"
}

$ReleaseNotes = $ReleaseNotes -replace "`r`n", "`n"
$publishedAt = [DateTimeOffset]::UtcNow
$productId = "CreatorControlSuite"
$payload = Get-CanonicalPayload `
    -ProductId $productId `
    -Version $Version `
    -Channel $Channel `
    -PackageFileName $packageFileName `
    -PackageSha256 $packageSha `
    -PackageSizeBytes $packageSize `
    -PublishedAt $publishedAt `
    -MinimumVersion $MinimumVersion `
    -ReleaseNotes $ReleaseNotes

$payloadPath = Join-Path $OutputDirectory "update-manifest.payload.txt"
[System.IO.File]::WriteAllText($payloadPath, $payload, [System.Text.UTF8Encoding]::new($false))

$keyPath = $PrivateKeyPath
if ([string]::IsNullOrWhiteSpace($keyPath) -and -not [string]::IsNullOrWhiteSpace($env:UPDATE_SIGNING_KEY_PEM)) {
    $keyPath = Join-Path ([System.IO.Path]::GetTempPath()) ("ccs-update-key-" + [guid]::NewGuid().ToString("N") + ".pem")
    [System.IO.File]::WriteAllText($keyPath, $env:UPDATE_SIGNING_KEY_PEM.Replace("`r`n", "`n").Trim() + "`n")
    $script:CleanupKeyPath = $keyPath
}

if ([string]::IsNullOrWhiteSpace($keyPath)) {
    $defaultDevKey = Join-Path (Split-Path -Parent $PSScriptRoot) "tools\dev-keys\update-private.pem"
    if (Test-Path -LiteralPath $defaultDevKey) {
        $keyPath = $defaultDevKey
    }
}

if ([string]::IsNullOrWhiteSpace($keyPath) -or -not (Test-Path -LiteralPath $keyPath)) {
    throw "Update-Signaturschlüssel fehlt. Setze UPDATE_SIGNING_KEY_PEM oder -PrivateKeyPath."
}

$signaturePath = Join-Path $OutputDirectory "update-manifest.sig.bin"
try {
    & openssl dgst -sha256 -sign $keyPath -out $signaturePath $payloadPath
    if ($LASTEXITCODE -ne 0) {
        throw "openssl dgst fehlgeschlagen (Exit $LASTEXITCODE)."
    }

    $signatureBytes = [System.IO.File]::ReadAllBytes($signaturePath)
    $signature = [Convert]::ToBase64String($signatureBytes)
    $publishedText = $publishedAt.ToUniversalTime().ToString("o")

    $manifestPath = Join-Path $OutputDirectory "update-manifest.json"
    $escape = {
        param([string]$Value)
        if ($null -eq $Value) { return "" }
        return ($Value.
            Replace('\', '\\').
            Replace('"', '\"').
            Replace("`r", '\r').
            Replace("`n", '\n').
            Replace("`t", '\t'))
    }

    $json = '{' +
        '"ProductId":"' + (& $escape $productId) + '",' +
        '"Version":"' + (& $escape $Version) + '",' +
        '"Channel":"' + (& $escape $Channel) + '",' +
        '"PackageFileName":"' + (& $escape $packageFileName) + '",' +
        '"PackageSha256":"' + (& $escape $packageSha) + '",' +
        '"PackageSizeBytes":' + $packageSize.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ',' +
        '"PublishedAt":"' + (& $escape $publishedText) + '",' +
        '"MinimumVersion":"' + (& $escape $MinimumVersion) + '",' +
        '"ReleaseNotes":"' + (& $escape $ReleaseNotes) + '",' +
        '"Signature":"' + (& $escape $signature) + '"' +
        '}'

    [System.IO.File]::WriteAllText($manifestPath, $json, [System.Text.UTF8Encoding]::new($false))

    Write-Host "Update-Manifest: $manifestPath"
    Write-Host "Package: $packageFileName ($packageSize bytes, SHA256=$packageSha)"
    Write-Host "Channel: $Channel"

    return $manifestPath
}
finally {
    if (Test-Path -LiteralPath $payloadPath) { Remove-Item -LiteralPath $payloadPath -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $signaturePath) { Remove-Item -LiteralPath $signaturePath -Force -ErrorAction SilentlyContinue }
    if ($script:CleanupKeyPath -and (Test-Path -LiteralPath $script:CleanupKeyPath)) {
        Remove-Item -LiteralPath $script:CleanupKeyPath -Force -ErrorAction SilentlyContinue
    }
}
