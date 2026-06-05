# Design: Update telemetry-mcp for Transit Parquet Datasets

**Status**: Draft  
**Date**: 2026-06-05  
**Affects**: `tools/telemetry-mcp/` only (no changes to `telemetry-query-tool`)

---

## Context

Feature 013 shipped a logging sidecar that writes three parquet datasets to Azure Blob:

| Dataset | Blob path pattern | One row per |
|---------|-------------------|-------------|
| `snap`  | `telemetry/snap/dt=YYYY-MM-DD/part-*.parquet`  | per-vehicle snap decision in a cycle |
| `lerp`  | `telemetry/lerp/dt=YYYY-MM-DD/part-*.parquet`  | per-vehicle delta (prior-state vehicles only) |
| `cycle` | `telemetry/cycle/dt=YYYY-MM-DD/part-*.parquet` | one per completed reconciliation cycle |

The `telemetry-mcp` bridge currently targets a single `iris.parquet` demo dataset and its allow-list is hardcoded to iris column names (`sepal.length`, `petal.width`, `variety`, …). This design replaces that with the three transit datasets and their frozen column contracts from feature 013.

---

## What changes

### 1. `internal/config/config.go` — drop `TELEMETRY_DATASET_URI`, add per-dataset URIs

The single `DatasetURI` field and env var no longer make sense once there are three distinct datasets with different schemas. Replace with three explicit base URIs — or, more practically, one **storage base URI** plus a **container name**, mirroring how the sidecar's `LoggingOptions` works.

**New env vars**:

| Variable | Required | Example | Notes |
|----------|----------|---------|-------|
| `TELEMETRY_STORAGE_URI` | yes | `azure://telemetry` | DuckDB Azure URI prefix; container is the path segment |
| `TELEMETRY_TOOL_PATH` | yes | `/path/to/telemetry-query-tool` | unchanged |
| `TELEMETRY_TIMEOUT_SECONDS` | no (default 30) | `30` | increase default — parquet-over-Azure is slower than iris |
| `TELEMETRY_MAX_OUTPUT_BYTES` | no (default 65536) | `65536` | unchanged |

`TELEMETRY_DATASET_URI` is **removed**. Callers that set it today will see a startup error pointing to the new variable.

**`Config` struct delta**:
```go
// before
DatasetURI string

// after
StorageURI string  // e.g. "azure://telemetry"
```

The runner builds the per-query source glob at query time:
```
azure://telemetry/{dataset}/dt={date}/*.parquet
```

---

### 2. `internal/validate/validate.go` — replace iris allow-list with transit columns

The allow-list is the load-bearing security control. It must be replaced wholesale.

#### Dataset routing

The `filter` argument alone is insufficient; the caller must also specify which dataset to query. Add a required `dataset` tool argument alongside `filter`:

```
dataset  : "snap" | "lerp" | "cycle"
filter   : validated predicate (existing grammar, new column set)
```

The `dataset` value is validated against the literal set `{"snap", "lerp", "cycle"}` before any column validation — an unknown dataset is rejected immediately, preventing the value from ever reaching the SQL string.

#### Column allow-lists (from `contracts/parquet-schemas.md`)

All column names are snake_case `[a-z_][a-z0-9_]*` — no dots, so the existing dot-quoting path in the parser (`"petal.length"` → `"petal.length"`) can be removed.

**Snap columns**:
```
cycle_id            string
observation_utc     timestamp  (datetime comparisons: use ISO string literal)
vehicle_id          string
route_id            string
snap_outcome        string     ("FirstObservation"|"Moved"|"Unchanged"|"Stationary"|"Stale")
raw_lat             double
raw_lon             double
snapped_lat         double
snapped_lon         double
snap_distance_km    double
snap_index          int
route_point_count   int
speed_mps           double?
bearing_deg         double?
is_stale            bool       (use 1/0 or 'true'/'false' as DuckDB accepts)
```

**Lerp columns**:
```
cycle_id              string
observation_utc       timestamp
vehicle_id            string
prior_route_id        string
prior_snapped_lat     double
prior_snapped_lon     double
prior_observation_utc timestamp
prior_speed_mps       double?
prior_bearing_deg     double?
pos_delta_km          double
speed_delta           double?
bearing_delta         double?
time_delta_sec        double
```

**Cycle columns**:
```
cycle_id                    string
cycle_start_utc             timestamp
cycle_end_utc               timestamp
cycle_execution_seconds     double
buses_processed             int
buses_moved                 int
buses_unchanged             int
buses_stationary            int
buses_stale                 int
buses_skipped_no_route_id   int
buses_skipped_unknown_route int
feed_header_ts              int64?
duplicate_feed              bool
last_update_cache_size      int
vehicle_state_cache_size    int
sidecar_buffer_occupancy    int
sidecar_dropped_records     int64
sidecar_persist_failures    int64
```

**Type categories for the parser's type-check pass**:
- `numericColumns`: all `double`, `int`, `int64`, `double?`, `int64?` columns above
- `stringColumns`: `cycle_id`, `vehicle_id`, `route_id`, `snap_outcome`, `prior_route_id`
- `timestampColumns` (new category): `observation_utc`, `cycle_start_utc`, `cycle_end_utc`, `prior_observation_utc` — accept string literals only; DuckDB will cast them (e.g. `observation_utc > '2026-06-04'`)
- `boolColumns` (new category): `is_stale`, `duplicate_feed` — accept only the literals `true` or `false` (unquoted)

The `allowedColumns` map becomes per-dataset: `snapColumns`, `lerpColumns`, `cycleColumns`. The `Filter` function signature gains a `dataset string` parameter and selects the correct map before tokenising.

#### Identifier grammar change

Current `isIdentifierChar` allows `.` (for `petal.length`). Transit column names use only `[a-z0-9_]`. Remove `.` from `isIdentifierChar` — this is a tightening, not a relaxation, and eliminates a whole class of dotted-path injection attempts.

---

### 3. `internal/query/runner.go` — two-argument path glob

Currently builds:
```go
fmt.Sprintf("SELECT * FROM '%s' WHERE %s", cfg.DatasetURI, validatedFilter)
```

Replace with a date-parameterised path that the caller can optionally scope:

```go
// dataset and date are validated before reaching here
sourceGlob := fmt.Sprintf("%s/%s/dt=%s/*.parquet", cfg.StorageURI, dataset, date)
fullQuery := fmt.Sprintf("SELECT * FROM '%s' WHERE %s", sourceGlob, validatedFilter)
```

`date` is supplied by the tool call as an optional third argument `date` (ISO `YYYY-MM-DD`). If omitted, it defaults to today's UTC date. The date value is validated with a strict regex `^\d{4}-\d{2}-\d{2}$` before interpolation — it never comes from the filter string.

**`Run` signature change**:
```go
func Run(ctx context.Context, cfg *config.Config, dataset, date, validatedFilter string) (string, error)
```

---

### 4. `main.go` — updated tool definition

```
tool: query_telemetry

arguments:
  dataset  (required) – one of: snap, lerp, cycle
  date     (optional) – UTC date to query, format YYYY-MM-DD; defaults to today
  filter   (required) – validated predicate over that dataset's columns
```

Tool description updated to name the three datasets and give example queries instead of iris examples.

---

### 5. Tests

#### `internal/validate/validate_test.go`

Replace the iris test matrix with transit equivalents. Preserve the existing test structure (accept/reject table). Key new cases:

| Case | Input | Expected |
|------|-------|----------|
| valid snap filter | `snap_distance_km > 0.5` | accept |
| valid cycle filter | `buses_stale > 10 AND duplicate_feed = false` | accept |
| valid lerp filter | `pos_delta_km > 1.0 AND vehicle_id = 'v001'` | accept |
| cross-dataset column | `sepal.length > 5` on `snap` | reject: unknown column |
| wrong dataset | `dataset = 'other'` | reject at dataset-routing step |
| bool literal valid | `is_stale = true` | accept |
| bool literal invalid | `is_stale = 1` | reject: expects bool literal |
| timestamp comparison | `observation_utc > '2026-06-04'` | accept |
| timestamp with number | `observation_utc > 1234567` | reject: expects string |
| dot in column name | `snap.outcome > 5` | reject: unknown column (`.` no longer identifier char) |
| iris column rejected | `petal.length > 5` | reject: unknown column |

#### `internal/query/runner_test.go`

Update the stub tool to accept the new three-argument call pattern. Add a test confirming the path glob is constructed correctly for each dataset.

---

## What does NOT change

- The `telemetry-query-tool` binary — it already accepts arbitrary SQL and uses `AZURE_STORAGE_CONNECTION_STRING`; that interface is unchanged.
- The security model — `dataset`, `date`, and `filter` are each validated before assembly; none reach the SQL string unvalidated. The data source path is a constant template, not user-controlled.
- The forbidden keyword/character lists — they remain as-is; they're dataset-agnostic.
- The MCP transport — stdio, same `mcp-go` library, same tool name.

---

## Migration notes for callers

If you have a Claude Code MCP config pointing at this server today:

1. Update `TELEMETRY_DATASET_URI` → `TELEMETRY_STORAGE_URI` (value changes from a full glob to the container prefix, e.g. `azure://telemetry`).
2. Tool calls need a `dataset` argument added. There is no backward-compatible shim — the old iris columns will be rejected immediately.
3. Make sure `AZURE_STORAGE_CONNECTION_STRING` is still set for `telemetry-query-tool` (unchanged).

---

## Open questions

1. **`SELECT *` vs explicit column projection** — currently the runner always does `SELECT *`. For large Snap/Lerp files this could be noisy. Consider adding an optional `columns` argument (comma-separated, validated against the same allow-list) that projects before returning. Deferred unless context-window budget becomes a problem.

2. **`hive_partitioning`** — DuckDB can auto-surface the `dt` partition key as a column with `read_parquet(..., hive_partitioning=true)`. This would let callers filter on `dt` without knowing the blob layout. Deferred; requires switching from `read_parquet(glob)` to the function form.

3. **Multi-day range queries** — today's design scopes to one `dt=` partition. A future `date_range` argument (start/end, both validated) could glob multiple partitions. Out of scope for this change.
