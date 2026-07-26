param([string]$Output = (Join-Path $PSScriptRoot "dev-keys"))
Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"
[void](New-Item -ItemType Directory -Path $Output -Force)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out (Join-Path $Output "license-private.pem")
openssl rsa -pubout -in (Join-Path $Output "license-private.pem") -out (Join-Path $Output "license-public.pem")
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out (Join-Path $Output "update-private.pem")
openssl rsa -pubout -in (Join-Path $Output "update-private.pem") -out (Join-Path $Output "update-public.pem")
Write-Host "Entwicklungsschlüssel erstellt. PRIVATE KEYS NICHT AUSLIEFERN." -ForegroundColor Yellow
