param(
    [Parameter(Mandatory=$true)][string]$WixPath
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

[xml]$Xml = Get-Content -LiteralPath $WixPath -Raw
$Ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
$Ns.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")

$Seen = @{}
$Duplicates = New-Object System.Collections.Generic.List[string]

$Xml.SelectNodes("//w:Component[@Directory]/w:File[@ShortName]", $Ns) | ForEach-Object {
    $Component = $_.ParentNode
    $Directory = $Component.Directory
    $ShortName = $_.ShortName.ToLowerInvariant()
    $Key = "$Directory|$ShortName"

    if ($Seen.ContainsKey($Key)) {
        $Duplicates.Add("$Directory : $ShortName")
    }
    else {
        $Seen[$Key] = $true
    }
}

Write-Host "WiX ShortName-Prüfung:"
Write-Host "  geprüfte ShortNames: $($Seen.Count)"
Write-Host "  Duplikate im selben Zielordner: $($Duplicates.Count)"

if ($Duplicates.Count -gt 0) {
    throw "Doppelte WiX ShortNames gefunden: $($Duplicates -join ', ')"
}

Write-Host "WiX ShortName-Prüfung bestanden." -ForegroundColor Green
