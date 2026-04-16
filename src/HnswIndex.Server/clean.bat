@ECHO OFF
SETLOCAL

ECHO ========================================
ECHO  HnswLite Server - Clean
ECHO ========================================

PUSHD %~dp0

IF EXIST "hnswindex.json" (
    DEL /Q "hnswindex.json" 2>NUL
    ECHO   hnswindex.json deleted.
) ELSE (
    ECHO   hnswindex.json not found.
)

IF EXIST "data" (
    RMDIR /S /Q "data" 2>NUL
    ECHO   data\ directory deleted.
) ELSE (
    ECHO   data\ directory not found.
)

IF EXIST "logs" (
    RMDIR /S /Q "logs" 2>NUL
    ECHO   logs\ directory deleted.
) ELSE (
    ECHO   logs\ directory not found.
)

POPD

ECHO ========================================
ECHO  Clean complete.
ECHO ========================================
EXIT /B 0
