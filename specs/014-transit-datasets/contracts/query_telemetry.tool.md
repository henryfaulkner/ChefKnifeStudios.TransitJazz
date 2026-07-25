# Contract: `query_telemetry` MCP Tool (transit datasets)

**Transport**: stdio MCP server (`github.com/mark3labs/mcp-go`), tool name unchanged:
`query_telemetry`. This contract supersedes the feature-012 iris contract.

---

## Tool definition

```
tool: query_telemetry
description: >
  Query the TransitJazz telemetry datasets produced by the logging sidecar.
  Three datasets are available:
    - snap  : one row per per-vehicle snap decision in a reconciliation cycle
    - lerp  : one row per per-vehicle position delta (vehicles with a prior state)
    - cycle : one row per completed reconciliation cycle (counts, timing, health)
  Supply a read-only filter over that dataset's columns. Only column names,
  numeric/string/bool literals, comparison operators (<, <=, >, >=, =, !=),
  and logical connectors (AND, OR) are allowed. Timestamp columns compare
  against date strings (e.g. observation_utc > '2026-06-04'). Boolean columns
  compare against true/false (e.g. is_stale = false).

arguments:
  dataset  (string, required) : one of "snap" | "lerp" | "cycle"
  date     (string, optional) : UTC day to query, format YYYY-MM-DD; default = today
  filter   (string, required) : validated predicate over the dataset's columns
```

## Behavioral contract

| Guarantee | Detail |
|-----------|--------|
| Dataset-routed | `dataset` is validated against `{snap,lerp,cycle}` **before** the filter is parsed; an unknown dataset never reaches SQL. |
| Day-scoped | The query reads exactly one partition: `{TELEMETRY_STORAGE_URI}/{dataset}/dt={date}/*.parquet`. |
| Fixed data source | The source glob is a constant template filled only with the validated `dataset` and `date`; no argument can redirect it. |
| Column-scoped filter | Every column in `filter` must belong to the chosen dataset; cross-dataset and unknown columns are rejected. |
| Kind-checked | Numeric/string/timestamp/bool literal must match the column's kind. |
| Read-only | Forbidden keywords (`SELECT`, `FROM`, `UNION`, `DROP`, `ATTACH`, `COPY`, `INSTALL`, …), comment markers, shell chars, and URL/path patterns remain rejected. |
| Bounded | `filter` ≤ 256 chars; output truncated to `TELEMETRY_MAX_OUTPUT_BYTES`. |
| Errors sanitized | Tool errors strip absolute paths and the storage connection string. |

## Canonical form

The validator re-emits a canonical predicate (the only thing interpolated after
`WHERE`). Transit columns are bare snake_case identifiers — **no quoting** (the
dot-quoting path is removed).

| Input filter | Canonical |
|--------------|-----------|
| `snap_distance_km > 0.5` | `snap_distance_km > 0.5` |
| `  buses_stale  >  10  ` | `buses_stale > 10` |
| `vehicle_id = 'v001'` | `vehicle_id = 'v001'` |
| `is_stale = false` | `is_stale = false` |
| `observation_utc > '2026-06-04'` | `observation_utc > '2026-06-04'` |

## Accept vectors

| # | dataset | filter | Result |
|---|---------|--------|--------|
| A1 | `snap` | `snap_distance_km > 0.5` | accept |
| A2 | `cycle` | `buses_stale > 10 AND duplicate_feed = false` | accept |
| A3 | `lerp` | `pos_delta_km > 1.0 AND vehicle_id = 'v001'` | accept |
| A4 | `snap` | `is_stale = true` | accept |
| A5 | `snap` | `observation_utc > '2026-06-04'` | accept |
| A6 | `snap` | `(snap_outcome = 'Moved' OR snap_outcome = 'Stale') AND raw_lat > 33.0` | accept |
| A7 | `cycle` | `sidecar_dropped_records > 0` | accept |

## Reject vectors

| # | dataset | filter | Reason |
|---|---------|--------|--------|
| R1 | `other` | `buses_stale > 1` | dataset not in `{snap,lerp,cycle}` (rejected before filter) |
| R2 | `snap` | `sepal.length > 5` | unknown column (iris) |
| R3 | `snap` | `petal.length > 5` | unknown column (`.` not an identifier char) |
| R4 | `snap` | `snap.outcome > 5` | unknown column (dotted identifier) |
| R5 | `snap` | `buses_stale > 10` | unknown column (cycle column used on snap) |
| R6 | `snap` | `is_stale = 1` | bool column expects `true`/`false` |
| R7 | `snap` | `is_stale = 'true'` | bool column expects unquoted bool literal |
| R8 | `snap` | `observation_utc > 1234567` | timestamp column expects string literal |
| R9 | `snap` | `observation_utc > '2026-06-04T12:00:00'` | string literal contains forbidden char `:` (full ISO not supported) |
| R10 | `cycle` | `buses_stale > 10; DROP TABLE x` | forbidden character `;` |
| R11 | `cycle` | `SELECT * FROM cycle` | forbidden keyword |
| R12 | `snap` | `vehicle_id = 'v001' -- x` | forbidden comment marker |
| R13 | `snap` | `raw_lat = 'abc'` | numeric column expects number |
| R14 | `snap` | `vehicle_id = 123` | string column expects string |

## Date argument vectors

| date input | Result |
|------------|--------|
| omitted | default to today UTC (`YYYY-MM-DD`) |
| `2026-06-04` | accept |
| `2026-6-4` | reject (not zero-padded `^\d{4}-\d{2}-\d{2}$`) |
| `2026-13-40` | reject (not a real calendar date) |
| `../secret` | reject (regex fail; cannot redirect source) |
| `2026-06-04/*.parquet` | reject (regex fail) |

## Config / migration vectors

| Environment | Result |
|-------------|--------|
| `TELEMETRY_STORAGE_URI` set, `TELEMETRY_TOOL_PATH` set | start OK |
| `TELEMETRY_STORAGE_URI` unset | startup error naming `TELEMETRY_STORAGE_URI` |
| only legacy `TELEMETRY_DATASET_URI` set | startup error (legacy var ignored; points to `TELEMETRY_STORAGE_URI`) |
| `TELEMETRY_TOOL_PATH` unset | startup error naming `TELEMETRY_TOOL_PATH` (unchanged) |

## What does NOT change

- The delegated `telemetry-query-tool` binary and its `AZURE_STORAGE_CONNECTION_STRING`.
- The MCP transport (stdio) and tool name (`query_telemetry`).
- The forbidden keyword/char/URL/comment lists.
- Output truncation and error sanitization behavior.
