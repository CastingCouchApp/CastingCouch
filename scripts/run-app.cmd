@echo off
setlocal
cd /d "%~dp0.."
title Creator Control Suite - Build & Run

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-app.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
    echo.
    echo Build oder Start fehlgeschlagen (ExitCode %EXITCODE%).
    pause
    exit /b %EXITCODE%
)

exit /b 0
