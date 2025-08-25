@echo off
setlocal enabledelayedexpansion

REM HnswIndex Server API Test Script (Windows)
REM This script tests all available API endpoints
REM Make sure the server is running before executing this script

REM Check if API key is provided
if "%~1"=="" (
    echo Error: API key is required as the first parameter
    echo Usage: %0 ^<API_KEY^> [BASE_URL]
    echo Example: %0 b6b6f6b0-c251-4733-93c8-5587370baa42
    echo Example: %0 b6b6f6b0-c251-4733-93c8-5587370baa42 http://localhost:9000
    exit /b 1
)

REM Server configuration
set API_KEY=%~1
if "%~2"=="" (
    set BASE_URL=http://localhost:8080
) else (
    set BASE_URL=%~2
)
set HEADERS=-H "Content-Type: application/json" -H "x-api-key: %API_KEY%"

echo === HnswIndex Server API Test Suite ===
echo Base URL: %BASE_URL%
echo API Key: %API_KEY%
echo.

REM Test 1: GET / (Root endpoint)
echo 1. Testing GET / (Root endpoint)
echo Response:
curl -s "%BASE_URL%/"
echo.
echo.

REM Test 2: HEAD / (Root endpoint)
echo 2. Testing HEAD / (Root endpoint)
echo Response:
curl -I -s "%BASE_URL%/"
echo.
echo.

REM Test 3: OPTIONS / (CORS preflight)
echo 3. Testing OPTIONS / (CORS preflight)
echo Response:
curl -X OPTIONS -s -I "%BASE_URL%/"
echo.
echo.

REM Test 4: GET /v1.0/indexes (List all indexes)
echo 4. Testing GET /v1.0/indexes (List all indexes)
echo Response:
curl -s %HEADERS% "%BASE_URL%/v1.0/indexes"
echo.
echo.

REM Test 5: POST /v1.0/indexes (Create new index)
echo 5. Testing POST /v1.0/indexes (Create new index)
set INDEX_NAME=test-index-windows
echo Creating index: %INDEX_NAME%
echo Response:
curl -s -X POST %HEADERS% -d "{\"Name\": \"%INDEX_NAME%\",\"Dimension\": 3,\"StorageType\": \"Sqlite\",\"DistanceFunction\": \"Euclidean\",\"M\": 16,\"MaxM\": 32,\"EfConstruction\": 200}" "%BASE_URL%/v1.0/indexes"
echo.
echo.

REM Test 6: GET /v1.0/indexes/{name} (Get specific index)
echo 6. Testing GET /v1.0/indexes/%INDEX_NAME% (Get index details)
echo Response:
curl -s %HEADERS% "%BASE_URL%/v1.0/indexes/%INDEX_NAME%"
echo.
echo.

REM Test 7: POST /v1.0/indexes/{name}/vectors (Add single vector)
echo 7. Testing POST /v1.0/indexes/%INDEX_NAME%/vectors (Add single vector)
echo Response:
curl -s -X POST %HEADERS% -d "{\"Vector\": [1.0, 2.0, 3.0]}" "%BASE_URL%/v1.0/indexes/%INDEX_NAME%/vectors"
echo.
echo.

REM Test 8: POST /v1.0/indexes/{name}/vectors/batch (Add multiple vectors)
echo 8. Testing POST /v1.0/indexes/%INDEX_NAME%/vectors/batch (Add multiple vectors)
echo Response:
curl -s -X POST %HEADERS% -d "{\"Vectors\": [{\"Vector\": [4.0, 5.0, 6.0]},{\"Vector\": [7.0, 8.0, 9.0]}]}" "%BASE_URL%/v1.0/indexes/%INDEX_NAME%/vectors/batch"
echo.
echo.

REM Test 9: POST /v1.0/indexes/{name}/search (Search vectors)
echo 9. Testing POST /v1.0/indexes/%INDEX_NAME%/search (Search vectors)
echo Response:
curl -s -X POST %HEADERS% -d "{\"Vector\": [2.0, 3.0, 4.0],\"K\": 2,\"Ef\": 50}" "%BASE_URL%/v1.0/indexes/%INDEX_NAME%/search"
echo.
echo.

REM Test 10: GET /v1.0/indexes (List all indexes - after creation)
echo 10. Testing GET /v1.0/indexes (List all indexes - after creation)
echo Response:
curl -s %HEADERS% "%BASE_URL%/v1.0/indexes"
echo.
echo.

REM Test 11: DELETE /v1.0/indexes/{name}/vectors/{guid} (Remove specific vector - SKIPPED)
echo 11. Testing DELETE /v1.0/indexes/%INDEX_NAME%/vectors/{guid} (Remove specific vector - SKIPPED)
echo Skipped: GUIDs are now auto-generated, cannot predict GUID for deletion
echo.
echo.

REM Test 12: Search again to verify vector was removed
echo 12. Testing search after vector removal
echo Response:
curl -s -X POST %HEADERS% -d "{\"Vector\": [1.0, 2.0, 3.0],\"K\": 5,\"Ef\": 50}" "%BASE_URL%/v1.0/indexes/%INDEX_NAME%/search"
echo.
echo.

REM Test 13: DELETE /v1.0/indexes/{name} (Delete entire index)
echo 13. Testing DELETE /v1.0/indexes/%INDEX_NAME% (Delete entire index)
echo Response:
curl -s -X DELETE %HEADERS% "%BASE_URL%/v1.0/indexes/%INDEX_NAME%"
echo.
echo.

REM Test 14: GET /v1.0/indexes (Verify index was deleted)
echo 14. Testing GET /v1.0/indexes (Verify index deletion)
echo Response:
curl -s %HEADERS% "%BASE_URL%/v1.0/indexes"
echo.
echo.

REM Error handling tests
echo === Error Handling Tests ===
echo.

REM Test 15: Try to access non-existent index
echo 15. Testing GET non-existent index
echo Expect: HTTP status 404
echo Response:
curl -s %HEADERS% "%BASE_URL%/v1.0/indexes/non-existent-index"
echo.
echo.

REM Test 16: Invalid API endpoint
echo 16. Testing invalid endpoint
echo Expect: HTTP status 404
echo Response:
curl -s %HEADERS% "%BASE_URL%/v1.0/invalid-endpoint"
echo.
echo.

REM Test 17: Unauthorized request (no API key)
echo 17. Testing unauthorized request
echo Expect: HTTP status 401
echo Response:
curl -s -H "Content-Type: application/json" "%BASE_URL%/v1.0/indexes"
echo.
echo.

REM Test 18: Invalid JSON body
echo 18. Testing invalid JSON body
echo Expect: HTTP status 400
echo Response:
curl -s -X POST %HEADERS% -d "invalid json" "%BASE_URL%/v1.0/indexes"
echo.
echo.

echo === All Tests Completed ===
echo.
echo Note: Make sure the HnswIndex Server is running on %BASE_URL% before running this script.
echo Usage: %0 ^<API_KEY^> [BASE_URL]
echo Get your API key from the AdminApiKey field in hnswindex.json