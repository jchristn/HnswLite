@ECHO OFF
SETLOCAL EnableDelayedExpansion

PUSHD %~dp0src

ECHO ============================================================================
ECHO   Test.Automated (console runner)
ECHO ============================================================================
dotnet run --project Test.Automated\Test.Automated.csproj -c Release
IF ERRORLEVEL 1 SET FAILED=1

ECHO.
ECHO ============================================================================
ECHO   Test.XUnit
ECHO ============================================================================
dotnet test Test.XUnit\Test.XUnit.csproj -c Release --nologo
IF ERRORLEVEL 1 SET FAILED=1

ECHO.
ECHO ============================================================================
ECHO   Test.NUnit
ECHO ============================================================================
dotnet test Test.NUnit\Test.NUnit.csproj -c Release --nologo
IF ERRORLEVEL 1 SET FAILED=1

ECHO.
ECHO ============================================================================
ECHO   Test.MSTest
ECHO ============================================================================
dotnet test Test.MSTest\Test.MSTest.csproj -c Release --nologo
IF ERRORLEVEL 1 SET FAILED=1

POPD

IF DEFINED FAILED (
    ECHO.
    ECHO One or more test runs failed.
    EXIT /B 1
)

ECHO.
ECHO All test runs passed.
EXIT /B 0
