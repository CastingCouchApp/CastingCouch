param(
    [Parameter(Mandatory=$true)][string]$PublishPath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$PublishPath = (Resolve-Path -LiteralPath $PublishPath).Path.TrimEnd('\')
$Files = @(Get-ChildItem -LiteralPath $PublishPath -Recurse -File | Sort-Object FullName)

if ($Files.Count -lt 10) {
    throw "Zu wenige Publish-Dateien für Installer-Harvesting: $($Files.Count)"
}

function Escape-Xml([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
}

function Make-Id([string]$Prefix, [string]$Value) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
        $hash = $sha.ComputeHash($bytes)
        $hex = -join ($hash[0..11] | ForEach-Object { $_.ToString("x2") })
        return "${Prefix}_$hex"
    }
    finally {
        $sha.Dispose()
    }
}


function Make-ShortName([string]$RelativePath) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($RelativePath.ToLowerInvariant())
        $hash = $sha.ComputeHash($bytes)
        $base = -join ($hash[0..2] | ForEach-Object { $_.ToString("x2") })
        $extension = [IO.Path]::GetExtension($RelativePath).TrimStart('.').ToLowerInvariant()

        if ($extension.Length -gt 3) {
            $extension = $extension.Substring(0, 3)
        }

        if ([string]::IsNullOrWhiteSpace($extension)) {
            return $base
        }

        return "$base.$extension"
    }
    finally {
        $sha.Dispose()
    }
}

# Normalize all relative paths to backslash form.
$RelativeFiles = foreach ($File in $Files) {
    $Relative = $File.FullName.Substring($PublishPath.Length).TrimStart('\','/')
    $Relative = $Relative -replace '/', '\'
    $Segments = @($Relative -split '\\')
    $DirectorySegments = if ($Segments.Count -gt 1) {
        @($Segments[0..($Segments.Count - 2)])
    } else {
        @()
    }

    [pscustomobject]@{
        Full = $File.FullName
        Relative = $Relative
        DirectorySegments = $DirectorySegments
    }
}

# Build every directory path from explicit path segments.
$DirectoryPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

foreach ($Item in $RelativeFiles) {
    $Current = ""
    foreach ($Segment in $Item.DirectorySegments) {
        $Current = if ([string]::IsNullOrWhiteSpace($Current)) {
            $Segment
        } else {
            "$Current\$Segment"
        }
        [void]$DirectoryPaths.Add($Current)
    }
}

$Directories = @($DirectoryPaths | Sort-Object { ($_ -split '\\').Count }, { $_ })

$DirectoryIds = @{}
foreach ($Directory in $Directories) {
    $DirectoryIds[$Directory] = Make-Id "DIR" $Directory
}

# Parent -> immediate children map, also segment-based.
$Children = @{}
foreach ($Directory in $Directories) {
    $Segments = @($Directory -split '\\')
    $Parent = if ($Segments.Count -le 1) {
        ""
    } else {
        ($Segments[0..($Segments.Count - 2)] -join '\')
    }

    if (-not $Children.ContainsKey($Parent)) {
        $Children[$Parent] = New-Object System.Collections.Generic.List[string]
    }
    $Children[$Parent].Add($Directory)
}

$Lines = New-Object System.Collections.Generic.List[string]
$Lines.Add('<?xml version="1.0" encoding="utf-8"?>')
$Lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$Lines.Add('  <Fragment>')
$Lines.Add('    <DirectoryRef Id="INSTALLFOLDER">')

function Add-DirectoryXml([string]$Parent, [int]$Indent) {
    if (-not $Children.ContainsKey($Parent)) { return }

    foreach ($Directory in ($Children[$Parent] | Sort-Object)) {
        $Segments = @($Directory -split '\\')
        $Name = $Segments[$Segments.Count - 1]
        $Id = $DirectoryIds[$Directory]
        $Pad = ' ' * $Indent

        $Lines.Add("$Pad<Directory Id=`"$Id`" Name=`"$(Escape-Xml $Name)`">")
        Add-DirectoryXml $Directory ($Indent + 2)
        $Lines.Add("$Pad</Directory>")
    }
}

Add-DirectoryXml "" 6
$Lines.Add('    </DirectoryRef>')
$Lines.Add('  </Fragment>')
$Lines.Add('  <Fragment>')
$Lines.Add('    <ComponentGroup Id="PublishedApplicationFiles">')

$ReferencedDirectoryIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

foreach ($Item in $RelativeFiles) {
    $ComponentId = Make-Id "CMP" $Item.Relative
    $FileId = Make-Id "FIL" $Item.Relative

    $DirectoryPath = if ($Item.DirectorySegments.Count -eq 0) {
        ""
    } else {
        ($Item.DirectorySegments -join '\')
    }

    $DirectoryId = if ([string]::IsNullOrWhiteSpace($DirectoryPath)) {
        "INSTALLFOLDER"
    } else {
        if (-not $DirectoryIds.ContainsKey($DirectoryPath)) {
            throw "Interner Generatorfehler: Verzeichnis nicht definiert: $DirectoryPath"
        }
        $DirectoryIds[$DirectoryPath]
    }

    [void]$ReferencedDirectoryIds.Add($DirectoryId)

    $Source = '$(var.PublishDir)' + $Item.Relative
    $ShortName = Make-ShortName $Item.Relative
    $Lines.Add("      <Component Id=`"$ComponentId`" Directory=`"$DirectoryId`" Guid=`"*`">")
    $Lines.Add("        <File Id=`"$FileId`" Source=`"$(Escape-Xml $Source)`" ShortName=`"$ShortName`" KeyPath=`"yes`" />")
    $Lines.Add('      </Component>')
}

$Lines.Add('    </ComponentGroup>')
$Lines.Add('  </Fragment>')
$Lines.Add('</Wix>')

# Internal directory-reference integrity check before writing.
$DefinedDirectoryIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
[void]$DefinedDirectoryIds.Add("INSTALLFOLDER")
foreach ($Id in $DirectoryIds.Values) {
    [void]$DefinedDirectoryIds.Add($Id)
}

$MissingDirectoryIds = @(
    $ReferencedDirectoryIds |
    Where-Object { -not $DefinedDirectoryIds.Contains($_) }
)

if ($MissingDirectoryIds.Count -gt 0) {
    throw "Nicht definierte WiX Directory-IDs: $($MissingDirectoryIds -join ', ')"
}

$OutputDirectory = Split-Path -Parent $OutputPath
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$Lines | Set-Content -LiteralPath $OutputPath -Encoding UTF8

Write-Host "WiX-Payload erzeugt:"
Write-Host "  Dateien/Komponenten: $($RelativeFiles.Count)"
Write-Host "  definierte Unterordner: $($Directories.Count)"
Write-Host "  referenzierte Directory-IDs: $($ReferencedDirectoryIds.Count)"
Write-Host "  fehlende Directory-IDs: 0"
Write-Host "  Ausgabe: $OutputPath"

if ($RelativeFiles.Count -ne $Files.Count) {
    throw "WiX-Payload-Zählung stimmt nicht mit Publish-Dateien überein."
}
