@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building HnswLite dashboard for linux/amd64 and linux/arm64/v8...
PUSHD dashboard
docker buildx build --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/hnswlite-dashboard:%1 --tag jchristn77/hnswlite-dashboard:latest --push .
POPD

GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-dashboard.bat v1.1.0

:Done
ECHO Done
@ECHO ON
