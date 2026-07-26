Set-StrictMode -Version 1.0
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "Build-Installer.ps1") -Configuration Release
