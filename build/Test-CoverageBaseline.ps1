param(
    [Parameter(Mandatory = $true)]
    [string]$CoveragePath,

    [string]$BaselinePath = (Join-Path $PSScriptRoot "coverage-baseline.json")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $CoveragePath)) {
    throw "Coverage-Datei nicht gefunden: $CoveragePath"
}

if (-not (Test-Path -LiteralPath $BaselinePath)) {
    throw "Coverage-Baseline nicht gefunden: $BaselinePath"
}

[xml]$coverage = Get-Content -LiteralPath $CoveragePath -Raw
$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$lineRate = [double]::Parse($coverage.coverage.'line-rate', $culture)
$branchRate = [double]::Parse($coverage.coverage.'branch-rate', $culture)

Write-Host ("Coverage: Lines {0:P2}, Branches {1:P2}" -f $lineRate, $branchRate)
Write-Host ("Baseline: Lines {0:P2}, Branches {1:P2}" -f $baseline.lineRate, $baseline.branchRate)

if ($lineRate -lt [double]$baseline.lineRate) {
    throw "Line-Coverage ist unter die ratcheting Baseline gefallen."
}

if ($branchRate -lt [double]$baseline.branchRate) {
    throw "Branch-Coverage ist unter die ratcheting Baseline gefallen."
}
