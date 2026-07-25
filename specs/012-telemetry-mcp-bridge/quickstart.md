# Quickstart: Contextual Telemetry Query MCP Bridge

Build, register, and validate the `telemetry-mcp` bridge locally. Targets the operator (tool owner) on their own machine.

## Prerequisites

- Go 1.23+ installed (`go version`).
- The `telemetry-query-tool.exe` present locally and runnable (in-repo at `telemetry-query-tool/`). It queries `iris.parquet` in Azure Blob Storage via DuckDB.
- Claude Code installed.

## 0. Secret remediation (do this first — FR-020 / SC-007)

The existing `telemetry-query-tool/main.go` hardcodes a live Azure storage AccountKey in committed source. Before anything else:

1. **Rotate/revoke** the exposed key in the Azure portal (Storage account → Access keys → Rotate).
2. Change `main.go` to read the connection string from the environment (the code's own error text already expects `AZURE_STORAGE_CONNECTION_STRING`) instead of the hardcoded literal, and delete the dead `if azureConnString == ""` guard's literal.
3. Rebuild the tool (see note below) and confirm no key remains in source (`git grep AccountKey` returns nothing).

> Build note: `telemetry-query-tool/build_err.txt` shows the source currently fails to build against `go-duckdb@v1.8.5` (`undefined: Conn`). Pin a working `go-duckdb`/DuckDB version before rebuilding.

## 1. Build the bridge

```powershell
cd tools/telemetry-mcp
go build -o telemetry-mcp.exe .
```

Produces a standalone `telemetry-mcp.exe` (single static binary).

## 2. Configure environment inputs

The bridge reads its fixed targets from environment variables (never from the model's filter):

- `TELEMETRY_TOOL_PATH` — absolute path to `telemetry-query-tool.exe`
- `TELEMETRY_DATASET_URI` — the fixed dataset target (e.g., `azure://parquet/iris.parquet`)
- `TELEMETRY_TIMEOUT_SECONDS` *(optional, default 10)*
- `TELEMETRY_MAX_OUTPUT_BYTES` *(optional, default 65536)*

## 3. Register with Claude Code

Add an `mcpServers` entry pointing at the built binary (absolute paths). Confirm the exact config file/scope for your Claude Code version (`~/.claude.json` global, or project `.mcp.json`):

```json
{
  "mcpServers": {
    "telemetry-query-bridge": {
      "command": "C:\\absolute\\path\\to\\telemetry-mcp.exe",
      "args": [],
      "env": {
        "TELEMETRY_TOOL_PATH": "C:\\absolute\\path\\to\\telemetry-query-tool.exe",
        "TELEMETRY_DATASET_URI": "azure://parquet/iris.parquet",
        "AZURE_STORAGE_CONNECTION_STRING": "<rotated connection string for the underlying tool>"
      }
    }
  }
}
```

Restart Claude Code so it launches the server.

## 4. Manual acceptance tests

| # | Action | Expected | Maps to |
|---|--------|----------|---------|
| 1 | Confirm tool is listed | `query_telemetry` appears in available tools | US3 / FR-001 |
| 2 | Ask: "How many records have petal length over 5?" | Claude calls the tool with `petal_length > 5.0`; a count is returned | US1 / SC-001 |
| 3 | Ask a compound question ("large petals AND narrow sepals") | Valid `AND` filter executes; count returned | US1 |
| 4 | Ask: "count of setosa species" | `species = 'setosa'` executes; count returned | US1 |
| 5 | Invoke tool with filter `1=1; DROP TABLE x` | Validation error, no execution | US2 / SC-002 |
| 6 | Invoke with `species='a' UNION SELECT * FROM users` | Validation error, no execution | US2 / SC-003 |
| 7 | Invoke with empty filter | "missing required filter" error | US2 / FR-011 |
| 8 | Rename/remove `telemetry-query-tool.exe`, ask a question | Sanitized tool-error (no crash/hang, no path leak) | FR-014 / FR-012 / SC-006 |
| 9 | `git grep -i accountkey` over the repo | No matches — credential is gone from source | FR-020 / SC-007 |

## 5. Run the automated tests

```powershell
cd tools/telemetry-mcp
go test ./...        # unit (validator) + integration (stub executable)
go test -run Fuzz -fuzz=Fuzz ./internal/validate   # optional: fuzz the filter parser
```

The validator tests assert the **Accept/Reject vectors** in `contracts/query_telemetry.tool.md`. The integration test uses `testdata/stub-query-tool` so it runs without the real `.exe` or dataset.

## Success check

- All 8 manual tests pass and `go test ./...` is green → feature meets US1–US3 and SC-001–SC-006.
- Specifically: every Reject vector is rejected **before** execution (SC-002), and no filter can reach data outside the configured dataset (SC-003).
