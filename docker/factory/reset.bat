@echo off
echo ========================================
echo  HnswLite Factory Reset
echo ========================================
echo.
echo This will delete all index databases and log files.
echo Configuration (hnswindex.json) will be preserved.
echo.
set /p confirm="Type 'RESET' to confirm: "
if /i not "%confirm%"=="RESET" (
    echo Cancelled.
    exit /b 1
)
echo.
echo [1/3] Stopping containers...
pushd ..
docker compose down
popd
echo.
echo [2/3] Deleting index databases...
if exist "..\hnswlite\data" (
    del /q /s "..\hnswlite\data\*" 2>nul
    for /d %%D in ("..\hnswlite\data\*") do rd /s /q "%%D" 2>nul
    echo   Index databases deleted.
) else (
    echo   No data directory found.
)
echo.
echo [3/3] Deleting log files...
if exist "..\hnswlite\logs" (
    del /q /s "..\hnswlite\logs\*" 2>nul
    echo   Log files deleted.
) else (
    echo   No logs directory found.
)
echo.
echo ========================================
echo  Factory reset complete.
echo  Run 'docker compose up -d' to restart.
echo ========================================
