@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building for linux/amd64 and linux/arm64/v8...
PUSHD src
docker buildx build -f HnswIndex.Server/Dockerfile --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/hnswlite-server:%1 --tag jchristn77/hnswlite-server:latest --push .
POPD

GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-server.bat v1.1.0

:Done
ECHO Done
@ECHO ON
