param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "tests\CreatorControlSuite.Tests\CreatorControlSuite.Tests.csproj"
$Logs = Join-Path $Root "artifacts\build-logs"
$Results = Join-Path $Root "artifacts\test-results"

[void](New-Item -ItemType Directory -Path $Logs -Force)
[void](New-Item -ItemType Directory -Path $Results -Force)

function Stop-StaleTestProcesses {
    $Names = @(
        "testhost",
        "vstest.console",
        "dotnet"
    )

    foreach ($Name in $Names) {
        Get-Process -Name $Name -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Id -ne $PID
            } |
            ForEach-Object {
                # dotnet-Prozesse werden nicht pauschal beendet. Nur Prozesse,
                # deren CommandLine auf Testhost/VSTest hinweist.
                if ($Name -eq "dotnet") {
                    try {
                        $Cim = Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)"
                        if ($null -eq $Cim -or
                            ($Cim.CommandLine -notmatch "testhost|vstest|dotnet test")) {
                            return
                        }
                    }
                    catch {
                        return
                    }
                }

                try {
                    Stop-Process -Id $_.Id -Force -ErrorAction Stop
                    Write-Host "Veralteten Testprozess beendet: $Name / PID $($_.Id)" -ForegroundColor Yellow
                }
                catch {
                    Write-Host "Testprozess konnte nicht beendet werden: $Name / PID $($_.Id)" -ForegroundColor Yellow
                }
            }
    }
}

function Invoke-TestAttempt {
    param(
        [int]$Attempt
    )

    $Prefix = "alpha28-tests-attempt$Attempt"
    $Combined = Join-Path $Logs "$Prefix.txt"
    $BinLog = Join-Path $Logs "$Prefix.binlog"
    $TrxName = "$Prefix.trx"
    $TrxPath = Join-Path $Results $TrxName

    foreach ($Path in @($Combined, $BinLog, $TrxPath)) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }

    Write-Host ""
    Write-Host "3/5 Tests - Versuch $Attempt von 2" -ForegroundColor Cyan
    Write-Host "Testprojekt: $Project"

    $Output = & dotnet test `
        $Project `
        -c $Configuration `
        --no-build `
        --logger "console;verbosity=detailed" `
        --logger "trx;LogFileName=$TrxName" `
        --results-directory $Results `
        --blame-crash `
        --blame-hang `
        --blame-hang-timeout 60s `
        -bl:$BinLog `
        2>&1

    $ExitCode = $LASTEXITCODE
    $OutputText = ($Output | Out-String)

    $Output | Tee-Object -FilePath $Combined

    $TestHostCrashed = (
        $OutputText -match "Testhostprozess ist abgestürzt" -or
        $OutputText -match "test host process crashed" -or
        $OutputText -match "active test run was aborted" -or
        $OutputText -match "aktive Testlauf wurde abgebrochen"
    )

    if ($ExitCode -eq 0 -and (Test-Path -LiteralPath $TrxPath)) {
        & (Join-Path $PSScriptRoot "Verify-TestResults.ps1") -TrxPath $TrxPath
        return @{
            Success = $true
            RetryableCrash = $false
            ExitCode = $ExitCode
            Log = $Combined
        }
    }

    return @{
        Success = $false
        RetryableCrash = $TestHostCrashed
        ExitCode = $ExitCode
        Log = $Combined
    }
}

$First = Invoke-TestAttempt -Attempt 1

if ($First.Success) {
    Write-Host "Tests erfolgreich abgeschlossen." -ForegroundColor Green
    exit 0
}

if (-not $First.RetryableCrash) {
    throw "3/5 Tests fehlgeschlagen. ExitCode: $($First.ExitCode). Log: $($First.Log)"
}

Write-Host ""
Write-Host "Testhost-Absturz erkannt. Es erfolgt genau ein Wiederholungsversuch." -ForegroundColor Yellow

Stop-StaleTestProcesses
Start-Sleep -Seconds 2

$Second = Invoke-TestAttempt -Attempt 2

if ($Second.Success) {
    Write-Host "Tests im zweiten Versuch erfolgreich abgeschlossen." -ForegroundColor Green
    exit 0
}

if ($Second.RetryableCrash) {
    throw "3/5 Tests: Testhost ist auch im zweiten Versuch abgestürzt. Log: $($Second.Log)"
}

throw "3/5 Tests im zweiten Versuch fehlgeschlagen. ExitCode: $($Second.ExitCode). Log: $($Second.Log)"
