[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"
$outputDirectory = Join-Path $repositoryRoot "artifacts\bin\CreatorControlSuite.App\$Configuration\net10.0-windows"
$executablePath = Join-Path $outputDirectory "CreatorControlSuite.exe"

Write-Host "CastingCouch wird gebaut ($Configuration) ..." -ForegroundColor Cyan
& dotnet build $projectPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Der Build ist fehlgeschlagen. Die laufende Suite wurde nicht beendet."
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Die gebaute Anwendung wurde nicht gefunden: $executablePath"
}

$runningProcesses = @(Get-Process -Name "CreatorControlSuite" -ErrorAction SilentlyContinue)
foreach ($process in $runningProcesses) {
    Write-Host "Laufende Suite wird beendet (PID $($process.Id)) ..." -ForegroundColor Yellow
    if ($process.MainWindowHandle -ne 0) {
        $null = $process.CloseMainWindow()
    }
}

if ($runningProcesses.Count -gt 0) {
    $runningProcesses | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    $runningProcesses |
        Where-Object { -not $_.HasExited } |
        Stop-Process -Force
}

Write-Host "Aktuelle Suite wird gestartet ..." -ForegroundColor Green
Start-Process -FilePath $executablePath -WorkingDirectory $outputDirectory

