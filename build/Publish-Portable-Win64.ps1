param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"
$outDir = Join-Path $root "artifacts\portable\CreatorControlSuite-2.0.116-$Runtime"

Write-Host "Publishing Creator Control Suite 2.0.116 ($Runtime, self-contained)..." -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "Das .NET SDK wurde nicht gefunden. Installiere das zu diesem Projekt passende .NET SDK und starte das Skript erneut."
}

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

dotnet restore $appProject -r $Runtime
dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen." }

$readme = @"
Creator Control Suite 2.0.116 - Portable Windows Testversion

1. Den gesamten Ordner auf den Testrechner kopieren.
2. CreatorControlSuite.App.exe starten.
3. OBS WebSocket, Twitch, Spotify und Streamer.bot in der Suite konfigurieren.
4. Für OBS 28+ ist WebSocket bereits integriert; Zugangsdaten in OBS und Suite müssen übereinstimmen.

Diese Ausgabe ist self-contained und benötigt auf dem Zielrechner keine separate .NET Runtime.
"@
Set-Content -Path (Join-Path $outDir "README-PORTABLE.txt") -Value $readme -Encoding UTF8

Write-Host "Portable Ausgabe erstellt:" -ForegroundColor Green
Write-Host $outDir
