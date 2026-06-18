# Updated API Test Scripts - Parameter-Based API Keys

I've successfully updated all three test scripts to require the API key as a command-line parameter. Here's what I changed:

V2 update: the scripts now create test indexes with `StorageType` set to
`PostgreSQL`, matching the Docker/default server configuration.

## ✅ Updated API Test Scripts - Parameter-Based API Keys

### **1. `test-api.sh` (Bash Script)**
- ✅ **Required API key**: First parameter `$1`
- ✅ **Optional base URL**: Second parameter `$2` (defaults to `http://localhost:8080`)
- ✅ **Validation**: Script fails with usage message if no API key provided
- ✅ **Usage**: `./test-api.sh "your-api-key" [base-url]`

### **2. `test-api.bat` (Windows Batch)**
- ✅ **Required API key**: First parameter `%~1`
- ✅ **Optional base URL**: Second parameter `%~2` (defaults to `http://localhost:8080`)
- ✅ **Validation**: Script fails with usage message if no API key provided
- ✅ **Usage**: `test-api.bat "your-api-key" [base-url]`

### **3. `test-api.ps1` (PowerShell)**
- ✅ **Mandatory API key**: `-ApiKey` parameter marked as `Mandatory=$true`
- ✅ **Optional base URL**: `-BaseUrl` parameter with default value
- ✅ **Auto-prompting**: PowerShell will automatically prompt if API key not provided
- ✅ **Usage**: `.\test-api.ps1 -ApiKey "your-api-key" [-BaseUrl "base-url"]`

### **Security & Usability Improvements:**
- 🔒 **No hardcoded API keys** - eliminates security risk
- ⚡ **Fail-fast validation** - scripts exit immediately if API key missing
- 📖 **Clear usage messages** - helpful examples shown on error
- 🔧 **Flexible configuration** - both API key and base URL can be customized
- 📚 **Updated documentation** - API-TESTING.md reflects new parameter requirements

### **Example Usage:**
```bash
# Bash/Linux/macOS
./test-api.sh "b6b6f6b0-c251-4733-93c8-5587370baa42"

# Windows Command Prompt
test-api.bat "b6b6f6b0-c251-4733-93c8-5587370baa42"

# PowerShell
.\test-api.ps1 -ApiKey "b6b6f6b0-c251-4733-93c8-5587370baa42"
```

### **Finding Your API Key:**
Your API key is stored in the `hnswindex.json` configuration file:

```json
{
  "Server": {
    "AdminApiKey": "b6b6f6b0-c251-4733-93c8-5587370baa42"
  }
}
```

Use this command to quickly find it:
```bash
cat hnswindex.json | grep AdminApiKey
```

### **Error Handling:**
If you run any script without providing the API key, you'll see:

**Bash Script:**
```
Error: API key is required as the first parameter
Usage: ./test-api.sh <API_KEY> [BASE_URL]
Example: ./test-api.sh b6b6f6b0-c251-4733-93c8-5587370baa42
Example: ./test-api.sh b6b6f6b0-c251-4733-93c8-5587370baa42 http://localhost:9000
```

**Windows Batch:**
```
Error: API key is required as the first parameter
Usage: test-api.bat <API_KEY> [BASE_URL]
Example: test-api.bat b6b6f6b0-c251-4733-93c8-5587370baa42
Example: test-api.bat b6b6f6b0-c251-4733-93c8-5587370baa42 http://localhost:9000
```

**PowerShell:**
```
cmdlet test-api.ps1 at command pipeline position 1
Supply values for the following parameters:
(Type !? for Help.)
ApiKey: 
```

The scripts now properly enforce the requirement for API keys as input parameters and will fail gracefully with helpful usage information if not provided!
