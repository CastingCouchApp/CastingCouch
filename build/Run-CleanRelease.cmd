@echo off
setlocal
cd /d "%~dp0.."
title Creator Control Suite 2.0.119 - Clean Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\build\Build-CleanRelease.ps1"
if errorlevel 1 (
    echo.
    echo CLEAN RELEASE FEHLGESCHLAGEN.
    pause
    exit /b 1
)
echo.
echo Build und Setup-Paket wurden erfolgreich erstellt.
pause
