param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateRange(0, 600)]
    [int]$DebounceSeconds = 20,

    [switch]$NoHotReload,
    [switch]$VerboseWatch
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectFile = Join-Path $projectRoot "src\CreatorControlSuite.App\CreatorControlSuite.App.csproj"
$watchRoot = Join-Path $projectRoot "src"
$script:AppProcess = $null

# Shared state for FileSystemWatcher callbacks (separate runspace).
$watchState = [hashtable]::Synchronized(@{
        ProjectRoot     = $projectRoot
        WatchRoot       = $watchRoot
        DebounceSeconds = $DebounceSeconds
        VerboseWatch    = [bool]$VerboseWatch
        RebuildPending  = $false
        LastChangeUtc   = [datetime]::MinValue
        ChangeCount     = 0
        LastLoggedPath  = ""
    })

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

    if ($null -ne $script:AppProcess -and -not $script:AppProcess.HasExited) {
        try {
            Stop-Process -Id $script:AppProcess.Id -Force -ErrorAction SilentlyContinue
            $null = $script:AppProcess.WaitForExit(3000)
        }
        catch {
        }
    }

    $script:AppProcess = $null

    # Kurz warten, damit File-Locks auf bin/obj freigegeben werden (WPF MarkupCompile).
    Start-Sleep -Milliseconds 750
}

function Clear-AppIntermediateOutput {
    $candidates = @(
        (Join-Path $projectRoot "artifacts\obj\CreatorControlSuite.App"),
        (Join-Path $projectRoot "src\CreatorControlSuite.App\obj")
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) {
            Write-Host ("Bereinige Zwischenausgabe: {0}" -f $path) -ForegroundColor DarkYellow
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-AppBuild {
    param([switch]$CleanIntermediate)

    if ($CleanIntermediate) {
        Clear-AppIntermediateOutput
    }

    Write-Host ("[{0:HH:mm:ss}] Baue App ({1}) ..." -f (Get-Date), $Configuration) -ForegroundColor Cyan
    & dotnet build $projectFile --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    return $true
}

function Start-AppProcess {
    Write-Host ("[{0:HH:mm:ss}] Starte App ({1}) ..." -f (Get-Date), $Configuration) -ForegroundColor Cyan

    if (-not (Invoke-AppBuild)) {
        Write-Host "Erster Build fehlgeschlagen - bereinige WPF-Zwischenausgabe und versuche erneut ..." -ForegroundColor Yellow
        if (-not (Invoke-AppBuild -CleanIntermediate)) {
            Write-Host "Fehler beim Buildvorgang. Beheben Sie die Buildfehler, und versuchen Sie es anschließend noch mal." -ForegroundColor Red
            return
        }
    }

    # Start-Process -ArgumentList (array) zerlegt Pfade mit Leerzeichen falsch
    # (Windows PowerShell 5.1). Deshalb ProcessStartInfo + quotierte Arguments.
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = 'run --project "{0}" --configuration {1} --no-build --no-launch-profile' -f $projectFile, $Configuration
    $psi.WorkingDirectory = $projectRoot
    $psi.UseShellExecute = $false

    $script:AppProcess = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $script:AppProcess) {
        throw "dotnet run konnte nicht gestartet werden."
    }
}

function Invoke-DebouncedRebuild {
    param([hashtable]$State)

    $count = [int]$State.ChangeCount
    Write-Host ""
    Write-Host ("[{0:HH:mm:ss}] {1}s Ruhe erreicht ({2} Datei-Events) - baue neu ..." -f (Get-Date), $DebounceSeconds, $count) -ForegroundColor Green
    $State.RebuildPending = $false
    $State.ChangeCount = 0
    $State.LastLoggedPath = ""
    Stop-AppInstances
    Start-AppProcess
}

function Start-DotnetWatchMode {
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

    & dotnet @($watchArgs.ToArray())
    return $LASTEXITCODE
}

function Start-DebouncedWatchMode {
    param([hashtable]$State)

    if (-not (Test-Path -LiteralPath $watchRoot)) {
        throw ("Watch-Root nicht gefunden: {0}" -f $watchRoot)
    }

    $watcher = New-Object System.IO.FileSystemWatcher $watchRoot
    $watcher.IncludeSubdirectories = $true
    $watcher.NotifyFilter = [System.IO.NotifyFilters]::FileName -bor
        [System.IO.NotifyFilters]::LastWrite -bor
        [System.IO.NotifyFilters]::CreationTime -bor
        [System.IO.NotifyFilters]::DirectoryName
    $watcher.EnableRaisingEvents = $true

    $onChange = {
        $path = $Event.SourceEventArgs.FullPath
        $state = $Event.MessageData
        if ([string]::IsNullOrWhiteSpace($path)) {
            return
        }

        try {
            $full = [System.IO.Path]::GetFullPath($path)
        }
        catch {
            return
        }

        $root = [string]$state.WatchRoot
        if (-not $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }

        $normalized = $full.Replace('\', '/')
        if ($normalized -match '/(bin|obj|artifacts|\.vs|generated)/') {
            return
        }

        $ext = [System.IO.Path]::GetExtension($full).ToLowerInvariant()
        $watched = @(
            '.cs', '.xaml', '.props', '.targets',
            '.resx', '.json', '.config', '.xml', '.razor'
        )
        if ($ext -notin $watched) {
            return
        }

        $state.LastChangeUtc = [datetime]::UtcNow
        $state.RebuildPending = $true
        $state.ChangeCount = [int]$state.ChangeCount + 1
        $count = [int]$state.ChangeCount
        $debounce = [int]$state.DebounceSeconds

        $relative = $full
        $projectRootLocal = [string]$state.ProjectRoot
        if ($relative.StartsWith($projectRootLocal, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $relative.Substring($projectRootLocal.Length).TrimStart('\', '/')
        }

        if ([bool]$state.VerboseWatch -or $count -eq 1 -or $relative -ne [string]$state.LastLoggedPath) {
            $state.LastLoggedPath = $relative
            Write-Host ("[{0:HH:mm:ss}] Aenderung erkannt: {1} (Rebuild in {2}s Ruhe)" -f (Get-Date), $relative, $debounce) -ForegroundColor DarkYellow
        }
        elseif ($count % 25 -eq 0) {
            Write-Host ("[{0:HH:mm:ss}] {1} Events gepuffert - warte auf {2}s Ruhe ..." -f (Get-Date), $count, $debounce) -ForegroundColor DarkYellow
        }
    }

    $subscribers = @(
        (Register-ObjectEvent -InputObject $watcher -EventName Changed -Action $onChange -MessageData $State),
        (Register-ObjectEvent -InputObject $watcher -EventName Created -Action $onChange -MessageData $State),
        (Register-ObjectEvent -InputObject $watcher -EventName Deleted -Action $onChange -MessageData $State),
        (Register-ObjectEvent -InputObject $watcher -EventName Renamed -Action $onChange -MessageData $State)
    )

    try {
        Start-AppProcess

        while ($true) {
            Start-Sleep -Milliseconds 500

            if ($null -ne $script:AppProcess -and $script:AppProcess.HasExited -and -not [bool]$State.RebuildPending) {
                Write-Host ("[{0:HH:mm:ss}] App beendet (Exit {1}). Warte auf Dateiaenderungen ..." -f (Get-Date), $script:AppProcess.ExitCode) -ForegroundColor DarkGray
                $script:AppProcess = $null
            }

            if ([bool]$State.RebuildPending) {
                $idle = ([datetime]::UtcNow - [datetime]$State.LastChangeUtc).TotalSeconds
                if ($idle -ge $DebounceSeconds) {
                    Invoke-DebouncedRebuild -State $State
                }
            }
        }
    }
    finally {
        $watcher.EnableRaisingEvents = $false
        foreach ($subscriber in $subscribers) {
            Unregister-Event -SourceIdentifier $subscriber.Name -Force -ErrorAction SilentlyContinue
            Remove-Job -Id $subscriber.Id -Force -ErrorAction SilentlyContinue
        }
        $watcher.Dispose()
        Stop-AppInstances
    }
}

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw ("Projektdatei nicht gefunden: {0}" -f $projectFile)
}

Stop-AppInstances

Write-Host ""
Write-Host "CastingCouch - Hot Reload Mode" -ForegroundColor Cyan
Write-Host ("  Projekt:       {0}" -f $projectFile)
Write-Host ("  Konfiguration: {0}" -f $Configuration)
if ($DebounceSeconds -gt 0) {
    Write-Host ("  Modus:         Debounced Rebuild ({0}s Ruhe nach letzter Aenderung)" -f $DebounceSeconds) -ForegroundColor Yellow
    Write-Host "  Hinweis:       Geeignet fuer Agent-Edits; startet nach Ruhe neu."
    Write-Host "  Sofort-Watch:  -DebounceSeconds 0"
}
elseif ($NoHotReload) {
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
    if ($DebounceSeconds -gt 0) {
        Start-DebouncedWatchMode -State $watchState
        [System.Environment]::Exit(0)
    }
    else {
        $exitCode = Start-DotnetWatchMode
        [System.Environment]::Exit($exitCode)
    }
}
finally {
    Pop-Location
    Stop-AppInstances
}
