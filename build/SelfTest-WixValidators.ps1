Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$TempRoot = Join-Path $env:TEMP ("CreatorControlSuite.WixValidatorSelfTest." + [guid]::NewGuid().ToString("N"))
[void](New-Item -ItemType Directory -Path $TempRoot -Force)

try {
    $PackagePath = Join-Path $TempRoot "Package.wxs"
    $GeneratedPath = Join-Path $TempRoot "Files.wxs"
    $DuplicatePath = Join-Path $TempRoot "Files-Duplicate.wxs"

    @'
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package>
    <Feature Id="MainFeature" Title="CastingCouch" Level="1">
      <ComponentGroupRef Id="PublishedApplicationFiles" />
    </Feature>
  </Package>
</Wix>
'@ | Set-Content -LiteralPath $PackagePath -Encoding UTF8

    @'
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <DirectoryRef Id="INSTALLFOLDER">
      <Directory Id="DIR_A" Name="Sub" />
    </DirectoryRef>
  </Fragment>
  <Fragment>
    <ComponentGroup Id="PublishedApplicationFiles">
      <Component Id="CMP_1" Directory="INSTALLFOLDER" Guid="*">
        <File Id="FIL_1" Source="$(var.PublishDir)CreatorControlSuite.exe" ShortName="a1b2c3.exe" KeyPath="yes" />
      </Component>
      <Component Id="CMP_2" Directory="INSTALLFOLDER" Guid="*">
        <File Id="FIL_2" Source="$(var.PublishDir)CreatorControlSuite.dll" ShortName="d4e5f6.dll" KeyPath="yes" />
      </Component>
      <Component Id="CMP_3" Directory="DIR_A" Guid="*">
        <File Id="FIL_3" Source="$(var.PublishDir)Sub\readme.txt" Name="CustomName.txt" ShortName="1a2b3c.txt" KeyPath="yes" />
      </Component>
    </ComponentGroup>
  </Fragment>
</Wix>
'@ | Set-Content -LiteralPath $GeneratedPath -Encoding UTF8

    @'
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="PublishedApplicationFiles">
      <Component Id="CMP_D1" Directory="INSTALLFOLDER" Guid="*">
        <File Id="FIL_D1" Source="$(var.PublishDir)Same.exe" ShortName="111111.exe" KeyPath="yes" />
      </Component>
      <Component Id="CMP_D2" Directory="INSTALLFOLDER" Guid="*">
        <File Id="FIL_D2" Source="$(var.PublishDir)Same.exe" ShortName="222222.exe" KeyPath="yes" />
      </Component>
    </ComponentGroup>
  </Fragment>
</Wix>
'@ | Set-Content -LiteralPath $DuplicatePath -Encoding UTF8

    # Positive path: must pass.
    & (Join-Path $PSScriptRoot "Test-WixDuplicateTargetFiles.ps1") `
        -PackageWixPath $PackagePath `
        -GeneratedWixPath $GeneratedPath

    & (Join-Path $PSScriptRoot "Test-GeneratedWixDirectories.ps1") `
        -WixPath $GeneratedPath

    & (Join-Path $PSScriptRoot "Test-WixShortNames.ps1") `
        -WixPath $GeneratedPath

    & (Join-Path $PSScriptRoot "Test-WixFeatureAssignment.ps1") `
        -PackageWixPath $PackagePath

    # Negative path: a real duplicate must be detected.
    $DuplicateDetected = $false
    try {
        & (Join-Path $PSScriptRoot "Test-WixDuplicateTargetFiles.ps1") `
            -PackageWixPath $PackagePath `
            -GeneratedWixPath $DuplicatePath
    }
    catch {
        $DuplicateDetected = $true
    }

    if (-not $DuplicateDetected) {
        throw "Validator-Selbsttest fehlgeschlagen: echtes Zielpfad-Duplikat wurde nicht erkannt."
    }

    Write-Host "WiX Validator-Selbsttest bestanden." -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
