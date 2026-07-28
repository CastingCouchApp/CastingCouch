@echo off
setlocal EnableExtensions
cd /d "%~dp0.."
title CastingCouch - Hot Reload

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-app-hotreload.ps1" %*
if errorlevel 1 goto :fail
exit /b 0

:fail
echo.
echo Hot-Reload-Start fehlgeschlagen. ExitCode=%ERRORLEVEL%
pause
exit /b %ERRORLEVEL%
