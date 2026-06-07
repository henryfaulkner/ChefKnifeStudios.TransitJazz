# telemetry-mcp

A small, standalone Go MCP server that exposes a single `query_telemetry` tool to
Claude Code over stdio. It validates untrusted arguments against a strict allow-list
grammar, then invokes the existing `telemetry-query-tool.exe` to run a read-only query
over the TransitJazz telemetry datasets produced by the feature-013 logging sidecar.

## Datasets

Three frozen datasets are queryable (one parquet file set per dataset per UTC day):

- `snap`  — one row per per-vehicle snap decision in a reconciliation cycle
- `lerp`  — one row per per-vehicle position delta (vehicles with a prior state)
- `cycle` — one row per completed reconciliation cycle (counts, timing, health)

Each dataset has its own snake_case column contract (the frozen feature-013 schema).
The validator enforces per-dataset column scoping: a column valid in one dataset is
rejected when used against another.

## Tool arguments

`query_telemetry` takes three arguments:

| Argument  | Required | Form                | Notes |
|-----------|----------|---------------------|-------|
| `dataset` | yes      | `snap`\|`lerp`\|`cycle` | validated before the filter is parsed |
| `date`    | no       | `YYYY-MM-DD`        | strict, zero-padded, real calendar date; **default = today (UTC)** |
| `filter`  | yes      | predicate           | allow-list grammar over the dataset's columns |

The read source is assembled from a fixed template the operator cannot redirect:
`{TELEMETRY_STORAGE_URI}/{dataset}/dt={date}/*.parquet`.

### Value kinds in `filter`

- **numeric** columns compare against numbers (`snap_distance_km > 0.5`).
- **string** columns compare against quoted strings (`vehicle_id = 'v001'`); allowed
  characters inside quotes are `[A-Za-z0-9 _-]`.
- **timestamp** columns compare against a **date string** (`observation_utc > '2026-06-04'`).
  Comparisons are **date-granularity only** — a full ISO timestamp like
  `'2026-06-04T12:00:00'` is rejected (the `:`/`T` characters are not allowed inside a
  string literal). This is a deliberate non-goal, not a bug.
- **bool** columns compare against the bare literals `true` / `false`
  (`is_stale = false`). A numeric (`is_stale = 1`) or quoted (`is_stale = 'true'`)
  literal is rejected.

Identifiers are bare snake_case (`[a-z_][a-z0-9_]*`); `.` is **not** an identifier
character, so dotted paths (`snap.outcome`) are rejected as unknown columns.

## Configuration (environment variables)

| Variable                          | Required | Default | Notes |
|-----------------------------------|----------|---------|-------|
| `TELEMETRY_STORAGE_URI`           | yes      | —       | container base, e.g. `azure://telemetry` |
| `TELEMETRY_TOOL_PATH`             | yes      | —       | path to `telemetry-query-tool.exe` |
| `TELEMETRY_TIMEOUT_SECONDS`       | no       | `30`    | raised from 10 (parquet-over-Azure is slower) |
| `TELEMETRY_MAX_OUTPUT_BYTES`      | no       | `65536` | output truncation cap |

The delegated `telemetry-query-tool` still reads `AZURE_STORAGE_CONNECTION_STRING`
from its own environment (unchanged).

### Migration from feature 012

`TELEMETRY_DATASET_URI` (the old single-dataset URI, e.g.
`azure://telemetry/iris.parquet`) has been **removed**. It is now ignored; configure
`TELEMETRY_STORAGE_URI` (the container base) instead. If `TELEMETRY_STORAGE_URI` is
unset the server fails fast at startup with an error naming the new variable.

> Full build, configuration, registration, and acceptance steps for the transit
> retarget live in
> [`specs/014-transit-datasets/quickstart.md`](../../specs/014-transit-datasets/quickstart.md).
