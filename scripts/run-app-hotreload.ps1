param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$NoHotReload,
    [switch]$VerboseWatch
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectFile = Join-Path $projectRoot "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"

function Stop-AppInstances {
    $processes = @(Get-Process -Name "CreatorControlSuite" -ErrorAction SilentlyContinue)
    foreach ($process in $processes) {
        Write-Host ("Beende laufende Instanz (PID {0}) ..." -f $process.Id)
        try {
            $closed = $process.CloseMainWindow()
            if (-not $closed -or -not $process.WaitForExit(3000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                $null = $process.WaitForExit(3000)
            }
        }
        catch {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw ("Projektdatei nicht gefunden: {0}" -f $projectFile)
}

Stop-AppInstances

$env:DOTNET_WATCH_SUPPRESS_EMOJIS = "1"
# Polling ist robuster bei OneDrive/AV/Netzlaufwerken und einigen Windows-Editoren.
$env:DOTNET_USE_POLLING_FILE_WATCHER = "1"

$watchArgs = [System.Collections.Generic.List[string]]::new()
$watchArgs.Add("watch")
if ($VerboseWatch) {
    $watchArgs.Add("--verbose")
}
if ($NoHotReload) {
    $watchArgs.Add("--no-hot-reload")
}
$watchArgs.AddRange([string[]]@(
    "run",
    "--project", $projectFile,
    "--configuration", $Configuration,
    "--no-launch-profile"
))

Write-Host ""
Write-Host "Creator Control Suite - Hot Reload Mode" -ForegroundColor Cyan
Write-Host ("  Projekt:       {0}" -f $projectFile)
Write-Host ("  Konfiguration: {0}" -f $Configuration)
if ($NoHotReload) {
    Write-Host "  Modus:         Restart-on-change (ohne Hot Reload)" -ForegroundColor Yellow
}
else {
    Write-Host "  Modus:         Hot Reload (dotnet watch)"
    Write-Host "  Hinweis:       Nicht alle Aenderungen sind hot-reloadbar;"
    Write-Host "                 bei Bedarf startet watch die App automatisch neu."
}
Write-Host "  Beenden:       Ctrl+C in diesem Fenster"
Write-Host ""

Push-Location $projectRoot
try {
    & dotnet @($watchArgs.ToArray())
    [System.Environment]::Exit($LASTEXITCODE)
}
finally {
    Pop-Location
    # Watch beendet ggf. nur den Watcher; App-Prozess absichern.
    Stop-AppInstances
}
