# HnswIndex Server API Testing Guide

This directory contains comprehensive API testing scripts that exercise all endpoints of the HnswIndex Server.

## Available Test Scripts

### 1. `test-api.sh` (Linux/macOS/WSL)
Bash script that tests all API endpoints using cURL.

**Usage:**
```bash
# Make executable (if needed)
chmod +x test-api.sh

# Run the tests (API key required)
./test-api.sh "your-api-key-here"

# With custom base URL
./test-api.sh "your-api-key-here" "http://localhost:9000"
```

### 2. `test-api.bat` (Windows Command Prompt)
Windows batch script that tests all API endpoints using cURL.

**Usage:**
```cmd
REM API key required as first parameter
test-api.bat "your-api-key-here"

REM With custom base URL
test-api.bat "your-api-key-here" "http://localhost:9000"
```

### 3. `test-api.ps1` (Windows PowerShell)
PowerShell script with advanced features and colored output.

**Usage:**
```powershell
# API key is required (will prompt if not provided)
.\test-api.ps1 -ApiKey "your-api-key-here"

# With custom base URL
.\test-api.ps1 -ApiKey "your-api-key-here" -BaseUrl "http://localhost:9000"
```

## Prerequisites

1. **Server Running**: Make sure the HnswIndex Server is running before executing any test script
2. **cURL Installed**: Required for bash and batch scripts (usually pre-installed on most systems)
3. **API Key**: Get your API key from the `AdminApiKey` field in your `hnswindex.json` configuration file

## API Endpoints Tested

### Core Functionality
- ✅ `GET /` - Root endpoint (HTML status page)
- ✅ `HEAD /` - Root endpoint (headers only)
- ✅ `OPTIONS /` - CORS preflight handling
- ✅ `GET /v1.0/indexes` - List all indexes
- ✅ `POST /v1.0/indexes` - Create new index
- ✅ `GET /v1.0/indexes/{name}` - Get index details
- ✅ `DELETE /v1.0/indexes/{name}` - Delete index

### Vector Operations
- ✅ `POST /v1.0/indexes/{name}/vectors` - Add single vector
- ✅ `POST /v1.0/indexes/{name}/vectors/batch` - Add multiple vectors
- ✅ `POST /v1.0/indexes/{name}/search` - Search for similar vectors
- ✅ `DELETE /v1.0/indexes/{name}/vectors/{guid}` - Remove specific vector

### Error Handling
- ✅ Non-existent index (404)
- ✅ Invalid endpoints (404)
- ✅ Unauthorized requests (401)
- ✅ Invalid JSON payloads (400)

## Test Flow

1. **Setup**: Tests basic server connectivity
2. **Index Management**: Creates, retrieves, and manages indexes
3. **Vector Operations**: Adds, searches, and removes vectors
4. **Data Verification**: Confirms operations worked correctly
5. **Cleanup**: Removes test data
6. **Error Scenarios**: Tests proper error handling

## Sample API Calls

### Create Index
```json
POST /v1.0/indexes
{
  "Name": "test-index",
  "Dimension": 3,
  "StorageType": "Sqlite",
  "DistanceFunction": "Euclidean",
  "M": 16,
  "MaxM": 32,
  "EfConstruction": 200
}
```

### Add Vector
```json
POST /v1.0/indexes/test-index/vectors
{
  "Vector": [1.0, 2.0, 3.0]
}
```

### Search Vectors
```json
POST /v1.0/indexes/test-index/search
{
  "Vector": [2.0, 3.0, 4.0],
  "K": 5,
  "Ef": 50
}
```

### Add Multiple Vectors
```json
POST /v1.0/indexes/test-index/vectors/batch
{
  "Vectors": [
    {
      "Vector": [4.0, 5.0, 6.0]
    },
    {
      "Vector": [7.0, 8.0, 9.0]
    }
  ]
}
```

## Configuration

All scripts now require the API key as a command-line parameter:

- **API Key**: Required parameter - get from `AdminApiKey` field in `hnswindex.json`
- **Base URL**: Optional parameter - defaults to `http://localhost:8080`

**Finding your API Key:**
```bash
# Look for AdminApiKey in your configuration file
cat hnswindex.json | grep AdminApiKey
```

## Expected Output

Each test displays:
- ✅ Test name and status
- 📄 Response data (JSON formatted)
- ⏱️ Execution time and HTTP status codes
- 📊 Final summary of all tests

## Troubleshooting

### Common Issues

1. **Connection Refused**: Make sure the server is running
2. **401 Unauthorized**: Check your API key matches `hnswindex.json`
3. **cURL Not Found**: Install cURL or use the PowerShell version
4. **Permission Denied**: Run `chmod +x test-api.sh` for the bash script

### Server Configuration

Default server configuration in `hnswindex.json`:
```json
{
  "Server": {
    "Hostname": "localhost",
    "Port": 8080,
    "AdminApiKey": "your-api-key-here"
  }
}
```

## Integration Testing

These scripts can be integrated into CI/CD pipelines for automated testing:

```bash
# Example CI integration
./test-api.sh && echo "All API tests passed!" || exit 1
```

For more information about the HnswIndex Server API, refer to the main project documentation.