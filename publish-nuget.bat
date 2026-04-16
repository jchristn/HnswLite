@ECHO OFF
SETLOCAL EnableDelayedExpansion

IF "%~1"=="" (
    ECHO Usage: publish-nuget.bat YOUR_NUGET_API_KEY
    EXIT /B 1
)

SET APIKEY=%~1
SET CONFIG=Release
SET FEED=https://api.nuget.org/v3/index.json
SET FAILED=0

PUSHD %~dp0

ECHO ============================================================================
ECHO  Cleaning stale packages from bin\%CONFIG% directories
ECHO ============================================================================
ECHO.

CALL :CleanDir "src\HnswIndex\bin\%CONFIG%"
CALL :CleanDir "src\HnswIndex.RamStorage\bin\%CONFIG%"
CALL :CleanDir "src\HnswIndex.SqliteStorage\bin\%CONFIG%"
CALL :CleanDir "sdk\csharp\HnswLite.Sdk\bin\%CONFIG%"

ECHO.
ECHO ============================================================================
ECHO  Packing all projects in %CONFIG%
ECHO ============================================================================
ECHO.

CALL :PackProject "src\HnswIndex\HnswIndex.csproj"                         || SET FAILED=1
CALL :PackProject "src\HnswIndex.RamStorage\HnswIndex.RamStorage.csproj"   || SET FAILED=1
CALL :PackProject "src\HnswIndex.SqliteStorage\HnswIndex.SqliteStorage.csproj" || SET FAILED=1
CALL :PackProject "sdk\csharp\HnswLite.Sdk\HnswLite.Sdk.csproj"            || SET FAILED=1

IF %FAILED% NEQ 0 (
    ECHO.
    ECHO Pack failed. Aborting before push.
    POPD
    EXIT /B 1
)

ECHO.
ECHO ============================================================================
ECHO  Pushing packages to %FEED%
ECHO ============================================================================
ECHO.

CALL :PushDir "src\HnswIndex\bin\%CONFIG%"                       || SET FAILED=1
CALL :PushDir "src\HnswIndex.RamStorage\bin\%CONFIG%"            || SET FAILED=1
CALL :PushDir "src\HnswIndex.SqliteStorage\bin\%CONFIG%"         || SET FAILED=1
CALL :PushDir "sdk\csharp\HnswLite.Sdk\bin\%CONFIG%"             || SET FAILED=1

POPD

ECHO.
IF %FAILED% NEQ 0 (
    ECHO ============================================================================
    ECHO  One or more packages failed to publish.
    ECHO ============================================================================
    EXIT /B 1
)

ECHO ============================================================================
ECHO  Done. All packages and symbols pushed.
ECHO ============================================================================
EXIT /B 0


:CleanDir
SET DIR=%~1
IF NOT EXIST "%DIR%" EXIT /B 0
DEL /Q "%DIR%\*.nupkg" 2>NUL
DEL /Q "%DIR%\*.snupkg" 2>NUL
ECHO   cleaned %DIR%
EXIT /B 0


:PackProject
ECHO --- Packing %~1 ---
dotnet pack "%~1" -c %CONFIG%
IF ERRORLEVEL 1 (
    ECHO   FAILED to pack %~1
    EXIT /B 1
)
EXIT /B 0


:PushDir
SET DIR=%~1
IF NOT EXIST "%DIR%" (
    ECHO WARN: no %DIR% directory; skipping
    EXIT /B 0
)
ECHO --- Pushing packages from %DIR% ---
REM dotnet nuget push a .nupkg automatically uploads the sibling .snupkg,
REM so iterating only over .nupkg files avoids duplicate symbol pushes.
FOR %%P IN ("%DIR%\*.nupkg") DO (
    ECHO   push %%~nxP
    dotnet nuget push "%%~fP" -s %FEED% -k %APIKEY% --skip-duplicate
    IF ERRORLEVEL 1 (
        ECHO   FAILED to push %%~nxP
        EXIT /B 1
    )
)
EXIT /B 0
