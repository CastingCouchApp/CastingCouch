param([string]$Output = (Join-Path $PSScriptRoot "dev-keys"))
Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$AppKeys = Join-Path $Root "src\CreatorControlSuite.App\Keys"
[void](New-Item -ItemType Directory -Path $Output -Force)
[void](New-Item -ItemType Directory -Path $AppKeys -Force)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out (Join-Path $Output "update-private.pem")
openssl rsa -pubout -in (Join-Path $Output "update-private.pem") -out (Join-Path $Output "update-public.pem")
Copy-Item -LiteralPath (Join-Path $Output "update-public.pem") -Destination (Join-Path $AppKeys "update-public.pem") -Force
Write-Host "Entwicklungsschlüssel erstellt. PRIVATE KEYS NICHT AUSLIEFERN." -ForegroundColor Yellow
Write-Host "Public Keys nach $AppKeys kopiert."
Write-Host "Für CI: Inhalt von update-private.pem als Secret UPDATE_SIGNING_KEY_PEM hinterlegen."
