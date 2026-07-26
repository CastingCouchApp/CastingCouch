Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$InstallerPath =
    Join-Path `
        $Root `
        "build\Install-CreatorControlSuite.ps1"

$Content =
    Get-Content `
        -LiteralPath $InstallerPath `
        -Raw

if ($Content -match 'exit\s+\$LASTEXITCODE') {
    throw "Installer verwendet weiterhin das unzuverlässige `$LASTEXITCODE nach Start-Process."
}

if ($Content -notmatch '-PassThru') {
    throw "Installer startet den erhöhten Prozess nicht mit -PassThru."
}

if ($Content -notmatch '\$ElevatedProcess\.ExitCode') {
    throw "Installer wertet den echten ExitCode des erhöhten Prozesses nicht aus."
}

if ($Content -notmatch '\$ScriptPath\s*=\s*\$MyInvocation\.MyCommand\.Path') {
    throw "Installer sichert den eigenen Skriptpfad nicht auf Skriptebene."
}

Write-Host "Clean-Installer-Skriptprüfung bestanden." -ForegroundColor Green
