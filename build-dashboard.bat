@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building HnswLite dashboard for linux/amd64 and linux/arm64/v8...
PUSHD dashboard
docker buildx build --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/hnswlite-dashboard:%1 --tag jchristn77/hnswlite-dashboard:latest --push .
IF ERRORLEVEL 1 (POPD & GOTO :Error)
POPD

ECHO.
ECHO Loading images into the local registry...
docker pull jchristn77/hnswlite-dashboard:%1
IF ERRORLEVEL 1 GOTO :Error
docker pull jchristn77/hnswlite-dashboard:latest
IF ERRORLEVEL 1 GOTO :Error

GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-dashboard.bat v2.0.0
GOTO :End

:Error
ECHO.
ECHO Build failed.
EXIT /B 1

:Done
ECHO Done

:End
@ECHO ON
