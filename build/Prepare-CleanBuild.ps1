param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

Write-Host ""
Write-Host "Build-Artefakte bereinigen" -ForegroundColor Cyan

$Projects = Get-ChildItem `
    -LiteralPath (Join-Path $Root "src") `
    -Directory

foreach ($Project in $Projects) {
    foreach ($FolderName in @("bin", "obj")) {
        $Path = Join-Path $Project.FullName $FolderName

        if (Test-Path -LiteralPath $Path) {
            Remove-Item `
                -LiteralPath $Path `
                -Recurse `
                -Force `
                -ErrorAction Stop
        }
    }
}

$TestProjects = Get-ChildItem `
    -LiteralPath (Join-Path $Root "tests") `
    -Directory `
    -ErrorAction SilentlyContinue

foreach ($Project in $TestProjects) {
    foreach ($FolderName in @("bin", "obj")) {
        $Path = Join-Path $Project.FullName $FolderName

        if (Test-Path -LiteralPath $Path) {
            Remove-Item `
                -LiteralPath $Path `
                -Recurse `
                -Force `
                -ErrorAction Stop
        }
    }
}

Write-Host "Alte bin/obj-Artefakte entfernt." -ForegroundColor Green
