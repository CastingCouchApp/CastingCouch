@echo off
setlocal
cd /d "%~dp0.."
call ".\build\Run-CleanRelease.cmd"
exit /b %errorlevel%
