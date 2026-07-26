Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Extensions = @(".ps1", ".psm1", ".cmd", ".bat")
$BadFiles = New-Object System.Collections.Generic.List[string]

$Files =
    Get-ChildItem -LiteralPath (Join-Path $Root "build") -File -Recurse |
    Where-Object { $Extensions -contains $_.Extension.ToLowerInvariant() }

foreach ($File in $Files) {
    $Bytes = [System.IO.File]::ReadAllBytes($File.FullName)

    if ($Bytes.Length -ge 3 -and
        $Bytes[0] -eq 0xEF -and
        $Bytes[1] -eq 0xBB -and
        $Bytes[2] -eq 0xBF) {
        $BadFiles.Add("$($File.FullName): UTF-8-BOM am Dateianfang")
        continue
    }

    $Text = [System.Text.Encoding]::UTF8.GetString($Bytes)
    if ($Text.IndexOf([char]0xFEFF) -ge 0) {
        $BadFiles.Add("$($File.FullName): eingebettetes U+FEFF/BOM-Zeichen")
    }
}

if ($BadFiles.Count -gt 0) {
    $Details = $BadFiles -join [Environment]::NewLine
    throw "Build-Skript-Encoding-Vertrag verletzt:`n$Details"
}

Write-Host "Build-Skript-Encoding-Vertrag geprueft." -ForegroundColor Green
