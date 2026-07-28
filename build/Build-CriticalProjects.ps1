param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$Projects = @(
    @{
        Name = "Workflow"
        Project = Join-Path $Root "src\CreatorControlSuite.Modules.Workflow\CreatorControlSuite.Modules.Workflow.csproj"
        Output = Join-Path $Root "src\CreatorControlSuite.Modules.Workflow\bin\$Configuration\net10.0-windows\CreatorControlSuite.Modules.Workflow.dll"
    }
)

foreach ($Item in $Projects) {
    Write-Host ""
    Write-Host "Kritisches Projekt bauen: $($Item.Name)" -ForegroundColor Cyan

    & dotnet build `
        $Item.Project `
        -c $Configuration `
        --no-restore `
        -m:1 `
        -nr:false `
        -p:BuildInParallel=false

    if ($LASTEXITCODE -ne 0) {
        throw "Isolierter Build fehlgeschlagen: $($Item.Name)"
    }

    if (-not (Test-Path -LiteralPath $Item.Output -PathType Leaf)) {
        throw "Erwartete DLL wurde nicht erzeugt: $($Item.Output)"
    }

    Write-Host "DLL erzeugt: $($Item.Output)" -ForegroundColor Green
}
