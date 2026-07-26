$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$PropsPath = Join-Path $Root "Directory.Build.props"
$Props = Get-Content -LiteralPath $PropsPath -Raw

if ($Props -notmatch '<BaseIntermediateOutputPath>\$\(MSBuildThisFileDirectory\)artifacts\\obj\\\$\(MSBuildProjectName\)\\</BaseIntermediateOutputPath>') {
    throw "Kurzer zentraler BaseIntermediateOutputPath fehlt."
}

if ($Props -notmatch '<BaseOutputPath>\$\(MSBuildThisFileDirectory\)artifacts\\bin\\\$\(MSBuildProjectName\)\\</BaseOutputPath>') {
    throw "Kurzer zentraler BaseOutputPath fehlt."
}

Write-Host "Build-Pfadlängen-Vertrag geprüft."
