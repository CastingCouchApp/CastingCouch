param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectFile = Join-Path $projectRoot "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"
$outputDir = [System.IO.Path]::GetFullPath(
    (Join-Path $projectRoot "artifacts\bin\CreatorControlSuite.App\$Configuration\net10.0-windows"))
$exePath = Join-Path $outputDir "CreatorControlSuite.exe"

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

function Show-WindowInForeground {
    param([System.Diagnostics.Process]$Process)

    if (-not ("CreatorControlSuiteWin32" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class CreatorControlSuiteWin32
{
    public const int SW_RESTORE = 9;
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
}
"@
    }

    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            return $false
        }

        $handle = $Process.MainWindowHandle
        if ($handle -ne [IntPtr]::Zero) {
            if ([CreatorControlSuiteWin32]::IsIconic($handle)) {
                [void][CreatorControlSuiteWin32]::ShowWindow($handle, [CreatorControlSuiteWin32]::SW_RESTORE)
            }

            [void][CreatorControlSuiteWin32]::SetForegroundWindow($handle)
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

Stop-AppInstances

Write-Host ("Baue CastingCouch ({0}) aus den Quellen ..." -f $Configuration)
& dotnet build $projectFile -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    [System.Environment]::Exit($LASTEXITCODE)
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw ("Build erfolgreich, aber Executable fehlt: {0}" -f $exePath)
}

# Erneuter Stop falls waehrend des Builds noch eine Instanz gestartet wurde.
Stop-AppInstances

Write-Host "Starte CastingCouch im Vordergrund ..."
$started = Start-Process -FilePath $exePath -WorkingDirectory $outputDir -PassThru
if (-not $started) {
    throw ("Start von '{0}' fehlgeschlagen." -f $exePath)
}

if (-not (Show-WindowInForeground -Process $started)) {
    if ($started.HasExited) {
        throw ("Die App wurde beendet, bevor ein Fenster erschien (ExitCode={0})." -f $started.ExitCode)
    }

    Write-Host ("Hinweis: Fenster-Handle noch nicht verfuegbar - App laeuft (PID {0})." -f $started.Id)
}

Write-Host ("CastingCouch gestartet (PID {0})." -f $started.Id) -ForegroundColor Green
[System.Environment]::Exit(0)
