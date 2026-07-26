@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Install-CreatorControlSuite.ps1"
if errorlevel 1 (
    echo.
    echo Installation fehlgeschlagen.
    pause
    exit /b 1
)
echo.
echo Creator Control Suite wurde erfolgreich installiert oder aktualisiert.
pause
