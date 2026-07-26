Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$MockProject = Join-Path $Root "src\CreatorControlSuite.LicenseMockServer\CreatorControlSuite.LicenseMockServer.csproj"
$WorkflowProject = Join-Path $Root "src\CreatorControlSuite.Modules.Workflow\CreatorControlSuite.Modules.Workflow.csproj"

[xml]$MockXml = Get-Content -LiteralPath $MockProject -Raw

$UseAppHost = @(
    $MockXml.Project.PropertyGroup.UseAppHost
) | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_)
} | Select-Object -First 1

if ($UseAppHost -ne "false") {
    throw "LicenseMockServer muss mit UseAppHost=false gebaut werden."
}

if (-not (Test-Path -LiteralPath $WorkflowProject -PathType Leaf)) {
    throw "Workflow-Projektdatei fehlt."
}

Write-Host "Build-Stabilitätsprüfung bestanden." -ForegroundColor Green
