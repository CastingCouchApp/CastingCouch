& (Join-Path $PSScriptRoot "Test-BuildScriptEncoding.ps1")
& (Join-Path $PSScriptRoot "Test-ShortBuildPaths.ps1")
& (Join-Path $PSScriptRoot "Test-ServiceIntegrationCompleteness.ps1")
Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Errors = New-Object System.Collections.Generic.List[string]
$Warnings = New-Object System.Collections.Generic.List[string]

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        $Errors.Add("Fehlendes Programm: $Name")
    }
}

try {
    [void](& (Join-Path $PSScriptRoot "Test-DotNetSdk.ps1") -RequiredMajor 10)
}
catch {
    $Errors.Add($_.Exception.Message)
}

$Required = @(
    "CreatorControlSuite.sln",
    "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj",
    "installer\CreatorControlSuite.Installer\CreatorControlSuite.Installer.wixproj"
)

foreach ($Relative in $Required) {
    $Path = Join-Path $Root $Relative
    if (-not (Test-Path -LiteralPath $Path)) {
        $Errors.Add("Fehlende Projektdatei: $Relative")
    }
}

$SettingsModel = Join-Path $Root "src\CreatorControlSuite.Core\Configuration\AppSettings.cs"
if (Test-Path -LiteralPath $SettingsModel) {
    $Content = Get-Content -LiteralPath $SettingsModel -Raw
    if ($Content -notmatch "2\.0\.129") {
        $Warnings.Add("Versionsnummer im Settings-Modell prüfen.")
    }
}

Write-Host ""
Write-Host "CastingCouch Build-Preflight" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan

foreach ($Warning in $Warnings) {
    Write-Host "WARNUNG: $Warning" -ForegroundColor Yellow
}
foreach ($ErrorItem in $Errors) {
    Write-Host "FEHLER: $ErrorItem" -ForegroundColor Red
}

if ($Errors.Count -gt 0) {
    throw "Preflight fehlgeschlagen: $($Errors.Count) Fehler."
}

Write-Host "Preflight erfolgreich." -ForegroundColor Green
