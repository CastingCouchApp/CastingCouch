Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$Files = @(
    (Join-Path $Root "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"),
    (Join-Path $Root "build\Build-Release.ps1")
)

foreach ($File in $Files) {
    $Content = Get-Content -LiteralPath $File -Raw

    if ($Content -match 'PublishReadyToRun\s*=\s*true' -or
        $Content -match '<PublishReadyToRun>true</PublishReadyToRun>') {
        throw "ReadyToRun darf für den Alpha-50-Windows-Publish nicht aktiviert sein: $File"
    }
}

$AppProjectPath =
    Join-Path `
        $Root `
        "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"

$AppProject =
    Get-Content `
        -LiteralPath $AppProjectPath `
        -Raw

if ($AppProject -notmatch '<PublishReadyToRun>false</PublishReadyToRun>') {
    throw "App-Projekt setzt PublishReadyToRun nicht ausdrücklich auf false."
}

if ($AppProject -notmatch '<PublishReadyToRunComposite>false</PublishReadyToRunComposite>') {
    throw "App-Projekt setzt PublishReadyToRunComposite nicht ausdrücklich auf false."
}

Write-Host "Publish-Konfiguration geprüft: ReadyToRun deaktiviert." -ForegroundColor Green
