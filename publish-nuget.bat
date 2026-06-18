@echo off
setlocal

if "%~1"=="" goto usage
if "%~2"=="" goto usage

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-nuget.ps1" -Version "%~1" -ApiKey "%~2"
exit /b %ERRORLEVEL%

:usage
echo Usage: publish-nuget.bat VERSION NUGET_API_KEY
echo Example: publish-nuget.bat 2.0.0 YOUR_API_KEY
exit /b 1
