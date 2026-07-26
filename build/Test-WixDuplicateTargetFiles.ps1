param(
    [Parameter(Mandatory=$true)][string]$PackageWixPath,
    [Parameter(Mandatory=$true)][string]$GeneratedWixPath
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Paths = @($PackageWixPath, $GeneratedWixPath)
$Seen = @{}
$Duplicates = New-Object System.Collections.Generic.List[string]

foreach ($Path in $Paths) {
    [xml]$Xml = Get-Content -LiteralPath $Path -Raw
    $Ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
    $Ns.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")

    $Xml.SelectNodes("//w:Component[@Directory]/w:File", $Ns) | ForEach-Object {
        $FileNode = $_
        $Component = $FileNode.ParentNode

        $Directory = $Component.GetAttribute("Directory")
        $ExplicitName = $FileNode.GetAttribute("Name")
        $Source = $FileNode.GetAttribute("Source")

        $TargetName = if (-not [string]::IsNullOrWhiteSpace($ExplicitName)) {
            $ExplicitName
        }
        elseif (-not [string]::IsNullOrWhiteSpace($Source)) {
            # Remove the WiX preprocessor variable prefix before resolving the
            # literal target file name. Example:
            # $(var.PublishDir)CreatorControlSuite.exe -> CreatorControlSuite.exe
            $SourceForName = $Source -replace '^\$\(var\.PublishDir\)', ''
            [IO.Path]::GetFileName($SourceForName)
        }
        else {
            throw "File-Element ohne Name und Source gefunden: $($FileNode.OuterXml)"
        }

        if ([string]::IsNullOrWhiteSpace($TargetName)) {
            throw "Zieldateiname konnte nicht ermittelt werden: $($FileNode.OuterXml)"
        }

        $Key = ("{0}|{1}" -f $Directory, $TargetName).ToLowerInvariant()

        if ($Seen.ContainsKey($Key)) {
            $Duplicates.Add("$Directory\$TargetName")
        }
        else {
            $Seen[$Key] = "$Path :: $($Component.GetAttribute('Id'))"
        }
    }
}

Write-Host "WiX Zielpfad-Duplikatprüfung:"
Write-Host "  eindeutige Zielpfade: $($Seen.Count)"
Write-Host "  doppelte Zielpfade: $($Duplicates.Count)"

if ($Duplicates.Count -gt 0) {
    throw "Doppelt installierte WiX-Zielpfade gefunden: $($Duplicates -join ', ')"
}

Write-Host "WiX Zielpfad-Duplikatprüfung bestanden." -ForegroundColor Green
