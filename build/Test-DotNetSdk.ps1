param(
    [int]$RequiredMajor = 10
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$DotNetCommand = Get-Command "dotnet.exe" -ErrorAction SilentlyContinue
if (-not $DotNetCommand) {
    $DotNetCommand = Get-Command "dotnet" -ErrorAction SilentlyContinue
}

if (-not $DotNetCommand) {
    throw @"
.NET SDK nicht gefunden.
Installiere das .NET $RequiredMajor SDK (x64). Eine .NET Runtime allein reicht zum Erstellen der Suite nicht aus.
Danach ein neues PowerShell-/CMD-Fenster öffnen und 'dotnet --list-sdks' ausführen.
"@
}

$DotNetPath = $DotNetCommand.Source
$SdkOutput = @(& $DotNetPath --list-sdks 2>&1)
$SdkExitCode = $LASTEXITCODE

if ($SdkExitCode -ne 0) {
    throw @"
Die gefundene dotnet.exe kann keine SDK-Liste laden:
$DotNetPath

Ausgabe:
$($SdkOutput -join [Environment]::NewLine)

Installiere oder repariere das .NET $RequiredMajor SDK (x64). Eine Runtime allein reicht nicht aus.
"@
}

$SdkLines = @($SdkOutput | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ })
$MatchingSdk = @(
    $SdkLines | Where-Object {
        if ($_ -match "^(?<Version>\d+\.\d+\.\d+)") {
            $ParsedVersion = $null
            if ([Version]::TryParse($Matches.Version, [ref]$ParsedVersion)) {
                return $ParsedVersion.Major -eq $RequiredMajor
            }
        }

        return $false
    }
)

if ($MatchingSdk.Count -eq 0) {
    $Installed = if ($SdkLines.Count -gt 0) { $SdkLines -join [Environment]::NewLine } else { "(keine SDKs erkannt)" }
    throw @"
.NET $RequiredMajor SDK fehlt.
Das Projekt verwendet net$RequiredMajor.0-windows und kann mit einem älteren SDK nicht gebaut werden.

Gefundene SDKs:
$Installed

Installiere das .NET $RequiredMajor SDK (x64), öffne danach ein neues Terminal und starte den Build erneut.
"@
}

$Selected = $MatchingSdk[-1]
Write-Host ".NET SDK-Prüfung bestanden: $Selected" -ForegroundColor Green
return $DotNetPath
