function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$Step,
        [string]$LogDirectory = ""
    )

    Write-Host ""
    Write-Host $Step -ForegroundColor Cyan

    if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
        $Root = Split-Path -Parent $PSScriptRoot
        $LogDirectory = Join-Path $Root "artifacts\build-logs"
    }

    [void](New-Item -ItemType Directory -Path $LogDirectory -Force)

    $SafeName = ($Step -replace '[^a-zA-Z0-9_-]', '-').Trim('-')
    $TextLog = Join-Path $LogDirectory ($SafeName + ".txt")

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "$Step konnte nicht gestartet werden. Programm fehlt: $FilePath"
    }

    # Windows PowerShell 5.1 can promote native stderr records to terminating
    # NativeCommandError exceptions when the caller uses ErrorActionPreference=Stop.
    # A native tool must instead be judged by its process exit code.
    $PreviousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $Output = @(& $FilePath @Arguments 2>&1)
        $ExitCode = $LASTEXITCODE
    }
    catch {
        $Message = $_.Exception.Message
        $Message | Out-File -LiteralPath $TextLog -Encoding utf8
        throw "$Step konnte nicht gestartet werden. Programm: $FilePath. Fehler: $Message. Textlog: $TextLog"
    }
    finally {
        $ErrorActionPreference = $PreviousErrorActionPreference
    }

    $Output | ForEach-Object { Write-Host $_ }
    $Output | Out-File -LiteralPath $TextLog -Encoding utf8

    if ($null -eq $ExitCode) {
        throw "$Step lieferte keinen ExitCode. Textlog: $TextLog"
    }

    if ($ExitCode -ne 0) {
        Write-Host ""
        Write-Host "Relevante Fehlerausgabe:" -ForegroundColor Red

        $Relevant = @(
            $Output | Where-Object {
                $Line = [string]$_
                $Line -match '(?i)(error\s+(CS|MSB|NETSDK|NU|IL)[0-9]+|:\s*error\s|Build FAILED|Build fehlgeschlagen|Exception|Unhandled)'
            }
        )

        if ($Relevant.Count -gt 0) {
            $Relevant | Select-Object -First 40 | ForEach-Object {
                Write-Host $_ -ForegroundColor Red
            }
        }
        else {
            Write-Host "Keine typische Compilerfehlerzeile erkannt. Letzte 40 Ausgaben:" -ForegroundColor Yellow
            $Output | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
        }

        throw "$Step fehlgeschlagen. ExitCode: $ExitCode. Textlog: $TextLog"
    }
}
