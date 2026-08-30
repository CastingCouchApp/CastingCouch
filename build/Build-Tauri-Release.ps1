# Builds Tauri bundles (NSIS/MSI on Windows, DMG on macOS) into artifacts/tauri
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location (Join-Path $root "tauri-app")
npm ci
npm test
npx tauri build
$dest = Join-Path $root "artifacts/tauri"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Get-ChildItem "src-tauri/target/release/bundle" -Recurse -Include *.msi,*.exe,*.dmg,*.app | ForEach-Object {
  Copy-Item $_.FullName $dest -Force
}
Write-Host "Tauri artifacts -> $dest"
