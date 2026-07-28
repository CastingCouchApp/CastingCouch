@echo off
setlocal EnableExtensions
cd /d "%~dp0.."
title CastingCouch - Build and Run

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-app.ps1" %*
if errorlevel 1 goto :fail
exit /b 0

:fail
echo.
echo Build oder Start fehlgeschlagen. ExitCode=%ERRORLEVEL%
pause
exit /b %ERRORLEVEL%
