@echo off
setlocal EnableExtensions
cd /d "%~dp0.."
title CastingCouch - Build and Run

rem A shortcut launched by Explorer can retain the PATH from before Node.js was installed.
if not exist "%ProgramFiles%\nodejs\node.exe" goto :run
echo %PATH%; | find /I "%ProgramFiles%\nodejs;" >nul
if errorlevel 1 set "PATH=%ProgramFiles%\nodejs;%PATH%"

:run
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-app.ps1" %*
if errorlevel 1 goto :fail
exit /b 0

:fail
echo.
echo Build oder Start fehlgeschlagen. ExitCode=%ERRORLEVEL%
pause
exit /b %ERRORLEVEL%
