@echo off
setlocal

if "%~1"=="" goto usage

call "%~dp0build-server.bat" "%~1"
if errorlevel 1 exit /b %ERRORLEVEL%

call "%~dp0build-dashboard.bat" "%~1"
if errorlevel 1 exit /b %ERRORLEVEL%

echo Done
exit /b 0

:usage
echo Usage: build-all.bat DOCKER_IMAGE_TAG
echo Example: build-all.bat v2.0.0
exit /b 1
