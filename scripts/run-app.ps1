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
$outputPrefix = $outputDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

function Get-ProcessExecutablePath {
    param([System.Diagnostics.Process]$Process)

    $pathProperty = $Process.PSObject.Properties["Path"]
    if ($null -ne $pathProperty) {
        try {
            $candidate = [string]$pathProperty.Value
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                return [System.IO.Path]::GetFullPath($candidate)
            }
        }
        catch {
            # Path ist ohne Rechte ggf. nicht lesbar.
        }
    }

    try {
        return [System.IO.Path]::GetFullPath($Process.MainModule.FileName)
    }
    catch {
        return $null
    }
}

function Stop-DevelopmentInstances {
    $developmentProcesses = @(
        Get-Process -Name "CreatorControlSuite" -ErrorAction SilentlyContinue |
            Where-Object {
                $processPath = Get-ProcessExecutablePath -Process $_
                if ([string]::IsNullOrWhiteSpace($processPath)) {
                    $false
                }
                else {
                    $processPath.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)
                }
            }
    )

    foreach ($developmentProcess in $developmentProcesses) {
        Write-Host "Beende laufende Entwicklungsinstanz (PID $($developmentProcess.Id)) ..."
        try {
            $closed = $developmentProcess.CloseMainWindow()
            if (-not $closed -or -not $developmentProcess.WaitForExit(3000)) {
                Stop-Process -Id $developmentProcess.Id -Force -ErrorAction SilentlyContinue
                $null = $developmentProcess.WaitForExit(3000)
            }
        }
        catch {
            Stop-Process -Id $developmentProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

function Show-WindowInForeground {
    param([System.Diagnostics.Process]$Process)

    if (-not ("CreatorControlSuite.Native.Foreground" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace CreatorControlSuite.Native
{
    public static class Foreground
    {
        public const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
    }
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
            if ([CreatorControlSuite.Native.Foreground]::IsIconic($handle)) {
                [void][CreatorControlSuite.Native.Foreground]::ShowWindow(
                    $handle,
                    [CreatorControlSuite.Native.Foreground]::SW_RESTORE)
            }

            [void][CreatorControlSuite.Native.Foreground]::SetForegroundWindow($handle)
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

Stop-DevelopmentInstances

Write-Host "Baue Creator Control Suite ($Configuration) aus den Quellen ..."
& dotnet build $projectFile -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build erfolgreich, aber Executable fehlt: $exePath"
}

# Erneuter Stop falls während des Builds noch eine Instanz gestartet wurde.
Stop-DevelopmentInstances

Write-Host "Starte Creator Control Suite im Vordergrund ..."
$started = Start-Process -FilePath $exePath -WorkingDirectory $outputDir -PassThru
if (-not $started) {
    throw "Start von '$exePath' fehlgeschlagen."
}

if (-not (Show-WindowInForeground -Process $started)) {
    if ($started.HasExited) {
        $code = $started.ExitCode
        throw "Die App wurde beendet, bevor ein Fenster erschien (ExitCode=$code)."
    }

    Write-Host "Hinweis: Fenster-Handle noch nicht verfügbar — App läuft (PID $($started.Id))."
}

Write-Host "Creator Control Suite gestartet (PID $($started.Id))." -ForegroundColor Green
exit 0
