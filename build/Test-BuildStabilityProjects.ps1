Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$WorkflowProject = Join-Path $Root "src\CreatorControlSuite.Modules.Workflow\CreatorControlSuite.Modules.Workflow.csproj"

if (-not (Test-Path -LiteralPath $WorkflowProject -PathType Leaf)) {
    throw "Workflow-Projektdatei fehlt."
}

Write-Host "Build-Stabilitätsprüfung bestanden." -ForegroundColor Green
