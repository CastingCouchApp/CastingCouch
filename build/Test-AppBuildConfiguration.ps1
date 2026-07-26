Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$AppProject = Join-Path $Root "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"

[xml]$Project = Get-Content -LiteralPath $AppProject -Raw
$ProjectXml = Get-Content -LiteralPath $AppProject -Raw

if ($ProjectXml -match '<RuntimeIdentifier>') {
    throw "App-Build-Vertrag verletzt: RuntimeIdentifier darf nicht dauerhaft im App-Projekt stehen. RID wird nur beim Publish gesetzt."
}

if ($ProjectXml -match '<SelfContained>') {
    throw "App-Build-Vertrag verletzt: SelfContained darf nicht dauerhaft im App-Projekt stehen. Self-contained wird nur beim Publish gesetzt."
}

$PublishScripts = @(
    (Join-Path $PSScriptRoot "Build-App.ps1")
    (Join-Path $PSScriptRoot "Build-Release.ps1")
    (Join-Path $PSScriptRoot "Publish-Portable-Win64.ps1")
)

foreach ($Script in $PublishScripts) {
    if (-not (Test-Path -LiteralPath $Script -PathType Leaf)) {
        continue
    }

    $Content = Get-Content -LiteralPath $Script -Raw
    if ($Content -notmatch 'win-x64') {
        throw "Publish-Vertrag verletzt: win-x64 fehlt in $Script"
    }
}

Write-Host "App-Build-/Publish-Verträge geprüft." -ForegroundColor Green
