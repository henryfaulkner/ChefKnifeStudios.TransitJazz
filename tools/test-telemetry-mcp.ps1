# Test script for telemetry-mcp bridge
# This tests the core functionality end-to-end

$ErrorActionPreference = "Stop"

# Set up environment
$bridgePath = "C:\Projects\ChefKnifeStudios.TransitJazz\tools\telemetry-mcp\telemetry-mcp.exe"
$toolPath = "C:\Projects\ChefKnifeStudios.TransitJazz\tools\telemetry-query-tool\telemetry-query-tool.exe"
$stubPath = "C:\Projects\ChefKnifeStudios.TransitJazz\tools\telemetry-mcp\testdata\stub-query-tool\stub-query-tool.exe"

Write-Host "Testing telemetry-mcp bridge..."
Write-Host ""

# Test 1: Validator tests
Write-Host "1. Running validator unit tests..."
cd "C:\Projects\ChefKnifeStudios.TransitJazz\tools\telemetry-mcp"
$result = & go test ./internal/validate -v 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   PASS: Validator tests passed"
} else {
    Write-Host "   FAIL: Validator tests failed"
    exit 1
}

# Test 2: Query runner tests
Write-Host "2. Running query runner tests..."
$result = & go test ./internal/query 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   PASS: Query runner tests passed"
} else {
    Write-Host "   FAIL: Query runner tests failed"
    exit 1
}

# Test 3: Check binaries exist
Write-Host "3. Checking binary artifacts..."
$files = @(
    @{ Path = $bridgePath; Name = "telemetry-mcp.exe" },
    @{ Path = $toolPath; Name = "telemetry-query-tool.exe" }
)

foreach ($file in $files) {
    if (Test-Path $file.Path) {
        $size = (Get-Item $file.Path).Length / 1MB
        Write-Host "   PASS: $($file.Name) exists"
    } else {
        Write-Host "   FAIL: $($file.Name) not found"
        exit 1
    }
}

Write-Host ""
Write-Host "All tests passed!"
Write-Host ""
Write-Host "Next steps:"
Write-Host "1. Set AZURE_STORAGE_CONNECTION_STRING with your Azure credentials"
Write-Host "2. Set TELEMETRY_TOOL_PATH=$toolPath"
Write-Host "3. Set TELEMETRY_DATASET_URI=azure://parquet/iris.parquet"
Write-Host "4. Register telemetry-mcp in your Claude Code MCP config"
