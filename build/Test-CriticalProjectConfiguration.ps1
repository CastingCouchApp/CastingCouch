Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot

$Workflow = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.Workflow\CreatorControlSuite.Modules.Workflow.csproj") -Raw
$StreamDeck = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.Modules.StreamDeck\CreatorControlSuite.Modules.StreamDeck.csproj") -Raw

if ($StreamDeck -notmatch '<OutputType>Library</OutputType>') {
    throw "StreamDeck OutputType=Library fehlt."
}

if ($StreamDeck -notmatch '<ProduceReferenceAssembly>true</ProduceReferenceAssembly>') {
    throw "StreamDeck ProduceReferenceAssembly=true fehlt."
}

if ($Workflow -notmatch '<OutputType>Library</OutputType>') {
    throw "Workflow OutputType=Library fehlt."
}

Write-Host "Kritische Projektkonfiguration geprüft." -ForegroundColor Green
