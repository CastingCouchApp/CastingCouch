Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Failures = New-Object System.Collections.Generic.List[string]
$RequiredFiles = @("artifacts\publish\win-x64\CreatorControlSuite.App.exe","artifacts\publish\win-x64\CreatorControlSuite.CommandClient.exe","artifacts\publish\win-x64\CreatorControlSuite.Updater.exe","src\CreatorControlSuite.App\Keys\license-public.pem","src\CreatorControlSuite.App\Keys\update-public.pem")
foreach($Relative in $RequiredFiles){if(-not(Test-Path -LiteralPath (Join-Path $Root $Relative))){$Failures.Add("Fehlt: $Relative")}}
$DraftFiles=Get-ChildItem -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\Legal") -Filter "*DRAFT*" -ErrorAction SilentlyContinue
if($DraftFiles){$Failures.Add("Es sind noch DRAFT-Rechtstexte enthalten.")}
$PrivateKeys=Get-ChildItem -LiteralPath $Root -Recurse -Include "*private*.pem","*.pfx","*.p12" -ErrorAction SilentlyContinue | Where-Object {$_.FullName -notlike "*\tools\dev-keys\*"}
if($PrivateKeys){$Failures.Add("Private Schlüssel im Projekt gefunden.")}
Write-Host "Creator Control Suite Release-Check" -ForegroundColor Cyan
if($Failures.Count -gt 0){$Failures|ForEach-Object{Write-Host "BLOCKER: $_" -ForegroundColor Red};exit 1}
Write-Host "Automatischer Release-Check bestanden." -ForegroundColor Green
