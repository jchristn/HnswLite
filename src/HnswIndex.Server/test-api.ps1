# HnswIndex Server API Test Script (PowerShell)
# This script tests all available API endpoints
# Make sure the server is running before executing this script

param(
    [Parameter(Mandatory=$true, HelpMessage="API key is required. Get it from AdminApiKey field in hnswindex.json")]
    [string]$ApiKey,
    
    [string]$BaseUrl = "http://localhost:8080"
)

# Configuration
$Headers = @{
    "Content-Type" = "application/json"
    "x-api-key" = $ApiKey
}

Write-Host "=== HnswIndex Server API Test Suite ===" -ForegroundColor Cyan
Write-Host "Base URL: $BaseUrl" -ForegroundColor Yellow
Write-Host "API Key: $ApiKey" -ForegroundColor Yellow
Write-Host ""

function Test-ApiEndpoint {
    param(
        [string]$TestName,
        [string]$Url,
        [string]$Method = "GET",
        [hashtable]$Headers = @{},
        [string]$Body = $null,
        [bool]$ShowResponse = $true
    )
    
    Write-Host "Testing: $TestName" -ForegroundColor Green
    
    try {
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $Headers
        }
        
        if ($Body) {
            $params.Body = $Body
        }
        
        $response = Invoke-RestMethod @params -ErrorAction Stop
        
        if ($ShowResponse) {
            Write-Host "Response:"
            $response | ConvertTo-Json -Depth 10 | Write-Host
        }
        
        Write-Host ""
        Write-Host ""
        return $response
    }
    catch {
        Write-Host "✗ $TestName failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Response: $($_.ErrorDetails.Message)`n" -ForegroundColor Red
        return $null
    }
}

# Test 1: GET / (Root endpoint)
try {
    Write-Host "1. Testing GET / (Root endpoint)" -ForegroundColor Green
    $response = Invoke-WebRequest -Uri "$BaseUrl/" -Method GET -ErrorAction Stop
    Write-Host "Response:"
    Write-Host "Status: $($response.StatusCode)" -ForegroundColor Yellow
    Write-Host "Content-Type: $($response.Headers['Content-Type'])" -ForegroundColor Yellow
    Write-Host "Body: $($response.Content)" -ForegroundColor White
    Write-Host ""
    Write-Host ""
}
catch {
    Write-Host "✗ Root endpoint test failed: $($_.Exception.Message)`n" -ForegroundColor Red
}

# Test 2: HEAD / (Root endpoint)
try {
    Write-Host "2. Testing HEAD / (Root endpoint)" -ForegroundColor Green
    $response = Invoke-WebRequest -Uri "$BaseUrl/" -Method HEAD -ErrorAction Stop
    Write-Host "Response:"
    Write-Host "Status: $($response.StatusCode)" -ForegroundColor Yellow
    Write-Host "Headers:" -ForegroundColor Yellow
    $response.Headers.Keys | ForEach-Object {
        Write-Host "  $_: $($response.Headers[$_])" -ForegroundColor White
    }
    Write-Host ""
    Write-Host ""
}
catch {
    Write-Host "✗ HEAD root endpoint test failed: $($_.Exception.Message)`n" -ForegroundColor Red
}

# Test 3: OPTIONS / (CORS preflight)
try {
    Write-Host "3. Testing OPTIONS / (CORS preflight)" -ForegroundColor Green
    $response = Invoke-WebRequest -Uri "$BaseUrl/" -Method OPTIONS -ErrorAction Stop
    Write-Host "Response:"
    Write-Host "Status: $($response.StatusCode)" -ForegroundColor Yellow
    Write-Host "CORS Headers:" -ForegroundColor Yellow
    $response.Headers.Keys | Where-Object { $_ -like "*Access-Control*" } | ForEach-Object {
        Write-Host "  $_: $($response.Headers[$_])" -ForegroundColor White
    }
    Write-Host ""
    Write-Host ""
}
catch {
    Write-Host "✗ OPTIONS test failed: $($_.Exception.Message)`n" -ForegroundColor Red
}

# Test 4: GET /v1.0/indexes (List all indexes)
$indexes = Test-ApiEndpoint -TestName "4. GET /v1.0/indexes (List all indexes)" -Url "$BaseUrl/v1.0/indexes" -Headers $Headers

# Test 5: POST /v1.0/indexes (Create new index)
$indexName = "test-index-ps-$(Get-Date -Format 'yyyyMMddHHmmss')"
$createIndexBody = @{
    Name = $indexName
    Dimension = 3
    StorageType = "Sqlite"
    DistanceFunction = "Euclidean"
    M = 16
    MaxM = 32
    EfConstruction = 200
} | ConvertTo-Json

$createdIndex = Test-ApiEndpoint -TestName "5. POST /v1.0/indexes (Create index: $indexName)" -Url "$BaseUrl/v1.0/indexes" -Method "POST" -Headers $Headers -Body $createIndexBody

if ($createdIndex) {
    # Test 6: GET /v1.0/indexes/{name} (Get specific index)
    Test-ApiEndpoint -TestName "6. GET /v1.0/indexes/$indexName (Get index details)" -Url "$BaseUrl/v1.0/indexes/$indexName" -Headers $Headers

    # Test 7: POST /v1.0/indexes/{name}/vectors (Add single vector)
    $addVectorBody = @{
        Vector = @(1.0, 2.0, 3.0)
    } | ConvertTo-Json

    Test-ApiEndpoint -TestName "7. POST /v1.0/indexes/$indexName/vectors (Add single vector)" -Url "$BaseUrl/v1.0/indexes/$indexName/vectors" -Method "POST" -Headers $Headers -Body $addVectorBody

    # Test 8: POST /v1.0/indexes/{name}/vectors/batch (Add multiple vectors)
    $addVectorsBody = @{
        Vectors = @(
            @{
                Vector = @(4.0, 5.0, 6.0)
            },
            @{
                Vector = @(7.0, 8.0, 9.0)
            }
        )
    } | ConvertTo-Json -Depth 10

    Test-ApiEndpoint -TestName "8. POST /v1.0/indexes/$indexName/vectors/batch (Add multiple vectors)" -Url "$BaseUrl/v1.0/indexes/$indexName/vectors/batch" -Method "POST" -Headers $Headers -Body $addVectorsBody

    # Test 9: POST /v1.0/indexes/{name}/search (Search vectors)
    $searchBody = @{
        Vector = @(2.0, 3.0, 4.0)
        K = 2
        Ef = 50
    } | ConvertTo-Json

    Test-ApiEndpoint -TestName "9. POST /v1.0/indexes/$indexName/search (Search vectors)" -Url "$BaseUrl/v1.0/indexes/$indexName/search" -Method "POST" -Headers $Headers -Body $searchBody

    # Test 10: GET /v1.0/indexes (List all indexes - after creation)
    Test-ApiEndpoint -TestName "10. GET /v1.0/indexes (List indexes after creation)" -Url "$BaseUrl/v1.0/indexes" -Headers $Headers

    # Test 11: DELETE /v1.0/indexes/{name}/vectors/{guid} (Remove specific vector - SKIPPED)
    Write-Host "11. DELETE /v1.0/indexes/$indexName/vectors/{guid} (Remove specific vector - SKIPPED)" -ForegroundColor Green
    Write-Host "Skipped: GUIDs are now auto-generated, cannot predict GUID for deletion" -ForegroundColor Yellow
    Write-Host ""
    Write-Host ""

    # Test 12: Search again to verify vector was removed
    $searchAfterRemovalBody = @{
        Vector = @(1.0, 2.0, 3.0)
        K = 5
        Ef = 50
    } | ConvertTo-Json

    Test-ApiEndpoint -TestName "12. POST search after vector removal" -Url "$BaseUrl/v1.0/indexes/$indexName/search" -Method "POST" -Headers $Headers -Body $searchAfterRemovalBody

    # Test 13: DELETE /v1.0/indexes/{name} (Delete entire index)
    Test-ApiEndpoint -TestName "13. DELETE /v1.0/indexes/$indexName (Delete index)" -Url "$BaseUrl/v1.0/indexes/$indexName" -Method "DELETE" -Headers $Headers

    # Test 14: GET /v1.0/indexes (Verify index was deleted)
    Test-ApiEndpoint -TestName "14. GET /v1.0/indexes (Verify deletion)" -Url "$BaseUrl/v1.0/indexes" -Headers $Headers
}

Write-Host "=== Error Handling Tests ===" -ForegroundColor Cyan

# Test 15: Try to access non-existent index
try {
    Write-Host "15. Testing GET non-existent index" -ForegroundColor Green
    Write-Host "Expect: HTTP status 404"
    $response = Invoke-RestMethod -Uri "$BaseUrl/v1.0/indexes/non-existent-index" -Headers $Headers -ErrorAction Stop
    Write-Host "Response:"
    Write-Host "$($response | ConvertTo-Json)" -ForegroundColor White
}
catch {
    Write-Host "Response:"
    if ($_.ErrorDetails.Message) {
        Write-Host "$($_.ErrorDetails.Message)" -ForegroundColor White
    }
}
Write-Host ""
Write-Host ""

# Test 16: Invalid API endpoint
try {
    Write-Host "16. Testing invalid endpoint" -ForegroundColor Green
    Write-Host "Expect: HTTP status 404"
    $response = Invoke-RestMethod -Uri "$BaseUrl/v1.0/invalid-endpoint" -Headers $Headers -ErrorAction Stop
    Write-Host "Response:"
    Write-Host "$($response | ConvertTo-Json)" -ForegroundColor White
}
catch {
    Write-Host "Response:"
    if ($_.ErrorDetails.Message) {
        Write-Host "$($_.ErrorDetails.Message)" -ForegroundColor White
    }
}
Write-Host ""
Write-Host ""

# Test 17: Unauthorized request (no API key)
try {
    Write-Host "17. Testing unauthorized request" -ForegroundColor Green
    Write-Host "Expect: HTTP status 401"
    $response = Invoke-RestMethod -Uri "$BaseUrl/v1.0/indexes" -Headers @{"Content-Type" = "application/json"} -ErrorAction Stop
    Write-Host "Response:"
    Write-Host "$($response | ConvertTo-Json)" -ForegroundColor White
}
catch {
    Write-Host "Response:"
    if ($_.ErrorDetails.Message) {
        Write-Host "$($_.ErrorDetails.Message)" -ForegroundColor White
    }
}
Write-Host ""
Write-Host ""

# Test 18: Invalid JSON body
try {
    Write-Host "18. Testing invalid JSON body" -ForegroundColor Green
    Write-Host "Expect: HTTP status 400"
    $response = Invoke-RestMethod -Uri "$BaseUrl/v1.0/indexes" -Method POST -Headers $Headers -Body "invalid json" -ErrorAction Stop
    Write-Host "Response:"
    Write-Host "$($response | ConvertTo-Json)" -ForegroundColor White
}
catch {
    Write-Host "Response:"
    if ($_.ErrorDetails.Message) {
        Write-Host "$($_.ErrorDetails.Message)" -ForegroundColor White
    }
}
Write-Host ""
Write-Host ""

Write-Host "=== All Tests Completed ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Note: Make sure the HnswIndex Server is running on $BaseUrl before running this script." -ForegroundColor Yellow
Write-Host "Usage: .\test-api.ps1 -ApiKey 'your-api-key' [-BaseUrl 'http://localhost:8080']" -ForegroundColor Yellow
Write-Host "Get your API key from the AdminApiKey field in hnswindex.json" -ForegroundColor Yellow