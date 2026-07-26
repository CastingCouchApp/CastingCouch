Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Content = Get-Content -LiteralPath (Join-Path $Root "src\CreatorControlSuite.App\App.xaml.cs") -Raw

if ($Content -match 'AddHttpClient<IUpdateService,\s*LocalUpdateService>') {
    throw "Fehlerhafte typed HttpClient-Registrierung für LocalUpdateService gefunden."
}
if ($Content -notmatch 'AddSingleton<IUpdateService>\(provider\s*=>') {
    throw "Explizite IUpdateService-Factory fehlt."
}
if ($Content -notmatch 'CreateClient\(\s*"CreatorControlSuite\.UpdateClient"\s*\)') {
    throw "Benannter Update-HttpClient fehlt."
}
if ($Content -notmatch 'GetRequiredService<ISettingsStore>') {
    throw "ISettingsStore-Auflösung für LocalUpdateService fehlt."
}
if ($Content -notmatch 'GetRequiredService<IUpdateSignatureVerifier>') {
    throw "IUpdateSignatureVerifier-Auflösung für LocalUpdateService fehlt."
}

Write-Host "DI-Registrierungsprüfung bestanden." -ForegroundColor Green
