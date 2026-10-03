@echo off
echo ========================================
echo  HnswLite Factory Reset
echo ========================================
echo.
echo This will delete all PostgreSQL data, SQLite index databases, log files, and the
echo Prometheus, Tempo, and Grafana volumes (metrics history, traces, Grafana state).
echo Configuration (hnswindex.json) will be preserved.
echo.
set /p confirm="Type 'RESET' to confirm: "
if /i not "%confirm%"=="RESET" (
    echo Cancelled.
    exit /b 1
)
echo.
echo [1/4] Stopping containers and removing observability volumes...
pushd ..
docker compose down -v
popd
echo.
echo [2/4] Deleting PostgreSQL data...
if exist "..\postgres\data" (
    rd /s /q "..\postgres\data" 2>nul
    echo   PostgreSQL data deleted.
) else (
    echo   No PostgreSQL data directory found.
)
echo.
echo [3/4] Deleting SQLite index databases...
if exist "..\hnswlite\data" (
    del /q /s "..\hnswlite\data\*" 2>nul
    for /d %%D in ("..\hnswlite\data\*") do rd /s /q "%%D" 2>nul
    echo   SQLite index databases deleted.
) else (
    echo   No data directory found.
)
echo.
echo [4/4] Deleting log files...
if exist "..\hnswlite\logs" (
    del /q /s "..\hnswlite\logs\*" 2>nul
    echo   Log files deleted.
) else (
    echo   No logs directory found.
)
echo.
echo ========================================
echo  Factory reset complete.
echo  PostgreSQL will be re-provisioned on next startup.
echo  Run 'docker compose up -d' to restart.
echo ========================================
