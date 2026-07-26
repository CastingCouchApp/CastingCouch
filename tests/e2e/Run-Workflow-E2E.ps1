param([string]$CommandClient = ".\CreatorControlSuite.CommandClient.exe")
Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"
$Steps = @("system.ping","system.status","workflow.prepare","workflow.live","workflow.pause","workflow.resume","workflow.end","system.status")
foreach ($Step in $Steps) { Write-Host ">>> $Step" -ForegroundColor Cyan; & $CommandClient $Step; if ($LASTEXITCODE -ne 0) { throw "E2E-Schritt fehlgeschlagen: $Step" }; Start-Sleep -Milliseconds 750 }
Write-Host "Workflow E2E erfolgreich." -ForegroundColor Green
