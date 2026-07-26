param(
    [Parameter(Mandatory=$true)][string]$PackageWixPath
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

[xml]$Xml = Get-Content -LiteralPath $PackageWixPath -Raw
$Ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
$Ns.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")

$PackageLevelRef = $Xml.SelectSingleNode(
    "/w:Wix/w:Package/w:ComponentGroupRef[@Id='PublishedApplicationFiles']",
    $Ns
)

if ($null -ne $PackageLevelRef) {
    throw "PublishedApplicationFiles darf nicht direkt auf Package-Ebene referenziert werden."
}

$FeatureRef = $Xml.SelectSingleNode(
    "/w:Wix/w:Package/w:Feature/w:ComponentGroupRef[@Id='PublishedApplicationFiles']",
    $Ns
)

if ($null -eq $FeatureRef) {
    throw "PublishedApplicationFiles ist keinem Feature zugeordnet."
}

Write-Host "WiX Feature-Zuordnung geprüft:" -ForegroundColor Green
Write-Host "  PublishedApplicationFiles innerhalb eines Feature: JA"
Write-Host "  Package-Level ComponentGroupRef: NEIN"
