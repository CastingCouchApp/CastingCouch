param(
    [Parameter(Mandatory=$true)][string]$WixPath
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

[xml]$Xml = Get-Content -LiteralPath $WixPath -Raw
$Ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
$Ns.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")

$Defined = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
[void]$Defined.Add("INSTALLFOLDER")

$Xml.SelectNodes("//w:Directory[@Id]", $Ns) | ForEach-Object {
    [void]$Defined.Add($_.Id)
}

$Referenced = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$Xml.SelectNodes("//w:Component[@Directory]", $Ns) | ForEach-Object {
    [void]$Referenced.Add($_.Directory)
}

$Missing = @($Referenced | Where-Object { -not $Defined.Contains($_) })

Write-Host "Generierte WiX-Verzeichnisprüfung:"
Write-Host "  definierte Directory-IDs: $($Defined.Count)"
Write-Host "  referenzierte Directory-IDs: $($Referenced.Count)"
Write-Host "  fehlende Directory-IDs: $($Missing.Count)"

if ($Missing.Count -gt 0) {
    throw "Generierte WiX-Datei referenziert nicht definierte Directory-IDs: $($Missing -join ', ')"
}

Write-Host "WiX-Verzeichnisprüfung bestanden." -ForegroundColor Green
