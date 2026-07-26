param(
    [Parameter(Mandatory=$true)]
    [string]$TrxPath
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $TrxPath)) {
    throw "TRX-Datei fehlt: $TrxPath"
}

[xml]$Document = Get-Content -LiteralPath $TrxPath -Raw

$Counters = $Document.TestRun.ResultSummary.Counters

if ($null -eq $Counters) {
    throw "TRX-Datei enthält keine Testzähler."
}

$Total = [int]$Counters.total
$Passed = [int]$Counters.passed
$Failed = [int]$Counters.failed
$Outcome = [string]$Document.TestRun.ResultSummary.outcome

Write-Host "TRX: Total=$Total, Passed=$Passed, Failed=$Failed, Outcome=$Outcome"

if ($Outcome -ne "Completed") {
    throw "Testlauf wurde nicht vollständig abgeschlossen: $Outcome"
}

if ($Failed -ne 0) {
    throw "$Failed Test(s) fehlgeschlagen."
}

if ($Total -le 0 -or $Passed -ne $Total) {
    throw "Testzähler sind inkonsistent."
}

Write-Host "TRX-Testresultat bestätigt." -ForegroundColor Green
