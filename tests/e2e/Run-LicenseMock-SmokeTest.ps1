param([string]$BaseUrl = "http://localhost:5058")
Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"
$Health = Invoke-RestMethod "$BaseUrl/health"
if ($Health.status -ne "ok") { throw "Mock-Server ist nicht bereit." }
$Body = @{ productId="creator-control-suite"; licenseKey="PRO-TEST-001"; installationId="e2e-installation"; appVersion="2.0.81" } | ConvertTo-Json
$Activation = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/licenses/activate" -ContentType "application/json" -Body $Body
if (-not $Activation.success) { throw $Activation.detail }
$Status = Invoke-RestMethod "$BaseUrl/api/v1/licenses/status/$($Activation.activationId)?installationId=e2e-installation"
if (-not $Status.success -or $Status.revoked) { throw "Statusprüfung fehlgeschlagen." }
Write-Host "Lizenzserver-Mock Smoke-Test erfolgreich." -ForegroundColor Green
