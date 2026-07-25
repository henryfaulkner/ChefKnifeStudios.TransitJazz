# Quickstart: Transit Telemetry Datasets for the Query Bridge

Verifies the retargeted `tools/telemetry-mcp/` bridge end to end. Assumes Go is
installed and you are in `tools/telemetry-mcp/`.

## 1. Build

```powershell
cd tools/telemetry-mcp
go build ./...
```

Expect a clean build. Also build the offline stub query tool used by tests:

```powershell
go build -o testdata/stub-query-tool/stub-query-tool.exe ./testdata/stub-query-tool
```

## 2. Run the test suite (no Azure credential needed)

```powershell
go test ./...
```

Expect the validate and query packages to pass, including the new transit
accept/reject matrix and the per-dataset glob-construction assertions.

## 3. Configure environment

```powershell
$env:TELEMETRY_STORAGE_URI = "azure://telemetry"
$env:TELEMETRY_TOOL_PATH   = "C:\path\to\telemetry-query-tool.exe"
# optional:
$env:TELEMETRY_TIMEOUT_SECONDS = "30"
# the delegated tool still needs:
$env:AZURE_STORAGE_CONNECTION_STRING = "<connection string>"
```

### Migration check (FR-015 / SC-005)

```powershell
Remove-Item Env:TELEMETRY_STORAGE_URI
# (legacy) $env:TELEMETRY_DATASET_URI = "azure://telemetry/iris.parquet"
go run .
```

Expect an immediate startup error naming `TELEMETRY_STORAGE_URI`. The legacy
`TELEMETRY_DATASET_URI` is ignored. Re-set `TELEMETRY_STORAGE_URI` before continuing.

## 4. Nine validation scenarios

Drive these through Claude Code (MCP) or a direct stdio harness. Each maps to a
contract vector.

| # | Call | Expected |
|---|------|----------|
| 1 | `dataset=snap, filter="snap_distance_km > 0.5"` | rows from today's `snap` partition |
| 2 | `dataset=cycle, filter="buses_stale > 10 AND duplicate_feed = false"` | matching cycle rows |
| 3 | `dataset=lerp, date="2026-06-04", filter="pos_delta_km > 1.0 AND vehicle_id = 'v001'"` | rows from that day's `lerp` partition |
| 4 | `dataset=snap, filter="is_stale = true"` | accept (bool literal) |
| 5 | `dataset=snap, filter="is_stale = 1"` | reject: bool column expects `true`/`false` |
| 6 | `dataset=snap, filter="observation_utc > '2026-06-04'"` | accept (timestamp vs date string) |
| 7 | `dataset=snap, filter="observation_utc > 1234567"` | reject: timestamp expects string |
| 8 | `dataset=snap, filter="buses_stale > 10"` | reject: unknown column (cycle column on snap) |
| 9 | `dataset=other, filter="buses_stale > 1"` | reject: unknown dataset (before filter parse) |

## 5. Security spot-checks (must all reject)

| Call | Expected |
|------|----------|
| `dataset=snap, filter="petal.length > 5"` | reject: unknown column (`.` removed from identifiers) |
| `dataset=cycle, filter="buses_stale > 10; DROP TABLE x"` | reject: forbidden character `;` |
| `dataset=cycle, filter="SELECT * FROM cycle"` | reject: forbidden keyword |
| `dataset=snap, date="../secret", filter="raw_lat > 0"` | reject: date fails `^\d{4}-\d{2}-\d{2}$` |
| `dataset=snap, filter="observation_utc > '2026-06-04T12:00:00'"` | reject: `:` forbidden in string literal |

## 6. Confirm the assembled source (FR-012 / SC-006)

In `internal/query/runner_test.go`, the glob assertion confirms that for
`dataset=snap, date=2026-06-04` the source is exactly
`azure://telemetry/snap/dt=2026-06-04/*.parquet` and that no filter content can
appear before the `WHERE`. This is the structural guarantee that operator input
cannot change the data source.

## Done

If steps 2–6 behave as above, the bridge correctly targets the three transit
datasets, enforces per-dataset column scoping and the new value kinds, scopes by day,
and preserves the feature-012 security model.
