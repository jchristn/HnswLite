@echo off
REM Pulls the latest published images and recreates the stack. Non-destructive:
REM named volumes and bind-mounted data are preserved. See factory\reset.bat for a full reset.
pushd %~dp0
docker compose pull
if errorlevel 1 goto :error
docker compose down
if errorlevel 1 goto :error
docker compose up -d
if errorlevel 1 goto :error
docker ps -a
popd
exit /b 0

:error
echo Update failed.
popd
exit /b 1
