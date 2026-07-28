param(
    [ValidateRange(1, 168)]
    [int]$DurationHours = 24,
    [ValidateRange(5, 3600)]
    [int]$SampleIntervalSeconds = 30,
    [string]$AgentStatusUri = "https://127.0.0.1:47631/api/v1/status",
    [string]$AgentKey = $env:CCS_SOAK_AGENT_KEY,
    [string]$CertificateFingerprint = $env:CCS_SOAK_AGENT_FINGERPRINT,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\soak")
)

$ErrorActionPreference = "Stop"
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$samplesPath = Join-Path $output "samples.csv"
$summaryPath = Join-Path $output "summary.json"
$startedAt = [DateTimeOffset]::UtcNow
$endsAt = $startedAt.AddHours($DurationHours)
$failures = 0
$consecutiveFailures = 0
$maximumConsecutiveFailures = 0
$samples = 0
if ([string]::IsNullOrWhiteSpace($CertificateFingerprint)) {
    throw "CCS_SOAK_AGENT_FINGERPRINT ist nicht gesetzt."
}

$expectedFingerprint = $CertificateFingerprint.Replace(":", "").Replace("-", "").
    Replace(" ", "").ToUpperInvariant()
if ($expectedFingerprint -notmatch '^[0-9A-F]{64}$') {
    throw "Der Agent-Fingerprint muss ein SHA-256-Fingerprint sein."
}

$handler = [Net.Http.HttpClientHandler]::new()
$handler.ServerCertificateCustomValidationCallback = {
    param($request, $certificate, $chain, $errors)
    if ($null -eq $certificate) { return $false }
    $actual = $certificate.GetCertHashString(
        [Security.Cryptography.HashAlgorithmName]::SHA256).ToUpperInvariant()
    return $actual -eq $expectedFingerprint
}
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
$client.DefaultRequestHeaders.Add("X-CCS-Agent-Key", $AgentKey)
"timestamp,agentLatencyMs,agentHealthy,obsRunning,spotifyRunning,streamerBotRunning,error" |
    Set-Content -LiteralPath $samplesPath -Encoding utf8

while ([DateTimeOffset]::UtcNow -lt $endsAt) {
    $timestamp = [DateTimeOffset]::UtcNow
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $healthy = $false
    $obs = $false
    $spotify = $false
    $streamerBot = $false
    $errorMessage = ""
    try {
        if ([string]::IsNullOrWhiteSpace($AgentKey)) { throw "CCS_SOAK_AGENT_KEY ist nicht gesetzt." }
        $httpResponse = $client.GetAsync($AgentStatusUri).GetAwaiter().GetResult()
        $httpResponse.EnsureSuccessStatusCode()
        $json = $httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $response = $json | ConvertFrom-Json
        $healthy = $true
        $obs = [bool]$response.obsRunning
        $spotify = [bool]$response.spotifyRunning
        $streamerBot = [bool]$response.streamerBotRunning
        $consecutiveFailures = 0
    }
    catch {
        $failures++
        $consecutiveFailures++
        $maximumConsecutiveFailures = [Math]::Max(
            $maximumConsecutiveFailures,
            $consecutiveFailures)
        $errorMessage = $_.Exception.Message.Replace('"', '""')
    }
    finally {
        $watch.Stop()
    }

    $samples++
    ('"{0}",{1},{2},{3},{4},{5},"{6}"' -f `
        $timestamp.ToString("O"), $watch.ElapsedMilliseconds, $healthy, $obs, `
        $spotify, $streamerBot, $errorMessage) |
        Add-Content -LiteralPath $samplesPath -Encoding utf8
    Start-Sleep -Seconds $SampleIntervalSeconds
}

$summary = [ordered]@{
    startedAt = $startedAt
    completedAt = [DateTimeOffset]::UtcNow
    durationHours = $DurationHours
    samples = $samples
    failures = $failures
    failureRate = if ($samples -eq 0) { 1 } else { $failures / $samples }
    maximumConsecutiveFailures = $maximumConsecutiveFailures
    passed = $samples -gt 0 -and $failures -eq 0
}
$summary | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8
$client.Dispose()
$handler.Dispose()
if (-not $summary.passed) {
    throw "Soak-Test fehlgeschlagen. Ergebnis: $summaryPath"
}

Write-Host "Soak-Test erfolgreich: $summaryPath"
