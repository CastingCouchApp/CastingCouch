Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ReportRoot = Join-Path $Root "artifacts\diagnostics"
[void](New-Item -ItemType Directory -Path $ReportRoot -Force)

$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$ReportPath = Join-Path $ReportRoot "build-environment-$Timestamp.txt"

$Lines = New-Object System.Collections.Generic.List[string]

function Add-Line {
    param([string]$Text = "")
    $Lines.Add($Text)
    Write-Host $Text
}

Add-Line "CastingCouch Build Environment"
Add-Line "======================================="
Add-Line "Zeit: $(Get-Date -Format o)"
Add-Line "Computer: $env:COMPUTERNAME"
Add-Line "Benutzer: $env:USERNAME"
Add-Line "Windows: $([System.Environment]::OSVersion.VersionString)"
Add-Line "PowerShell: $($PSVersionTable.PSVersion)"
Add-Line ""

Add-Line ".NET"
Add-Line "----"

$DotNet = Get-Command dotnet -ErrorAction SilentlyContinue

if ($null -eq $DotNet) {
    Add-Line "FEHLT: dotnet.exe"
}
else {
    Add-Line "Pfad: $($DotNet.Source)"
    Add-Line ""
    Add-Line (dotnet --info 2>&1 | Out-String)
    Add-Line "Installierte SDKs:"
    Add-Line (dotnet --list-sdks 2>&1 | Out-String)
    Add-Line "Installierte Runtimes:"
    Add-Line (dotnet --list-runtimes 2>&1 | Out-String)
}

Add-Line ""
Add-Line "WiX / Installer"
Add-Line "---------------"

$Wix = Get-Command wix -ErrorAction SilentlyContinue
if ($null -eq $Wix) {
    Add-Line "Hinweis: wix CLI nicht global gefunden."
}
else {
    Add-Line "WiX CLI: $($Wix.Source)"
    Add-Line (wix --version 2>&1 | Out-String)
}

Add-Line ""
Add-Line "Projekt"
Add-Line "-------"
Add-Line "Root: $Root"
Add-Line "Solution vorhanden: $(Test-Path -LiteralPath (Join-Path $Root 'CreatorControlSuite.sln'))"
Add-Line "App-Projekt vorhanden: $(Test-Path -LiteralPath (Join-Path $Root 'src\CreatorControlSuite.App\CreatorControlSuite.App.csproj'))"
Add-Line "Installer-Projekt vorhanden: $(Test-Path -LiteralPath (Join-Path $Root 'installer\CreatorControlSuite.Installer\CreatorControlSuite.Installer.wixproj'))"

$Lines | Set-Content -LiteralPath $ReportPath -Encoding UTF8

Write-Host ""
Write-Host "Diagnose gespeichert:" -ForegroundColor Green
Write-Host $ReportPath
