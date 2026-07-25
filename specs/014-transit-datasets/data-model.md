# Phase 1 Data Model: Transit Telemetry Datasets for the Query Bridge

This bridge owns no persistent data. The "model" here is (a) the validation policy
state the validator carries, and (b) the frozen column contract — replicated here as
the source of truth the `validate` package's allow-list maps must mirror exactly. The
column names/types are owned by feature 013
(`specs/013-logging-sidecar-service/contracts/parquet-schemas.md`); this file
restates them as the value-kind classification the validator needs.

---

## Entity: Query Request (transient)

The arguments of one `query_telemetry` tool call.

| Field | Required | Form | Validation |
|-------|----------|------|------------|
| `dataset` | yes | one of `snap` \| `lerp` \| `cycle` | literal-set membership; reject otherwise |
| `date` | no | `YYYY-MM-DD` | strict regex `^\d{4}-\d{2}-\d{2}$` + real-calendar-date check; default = today UTC |
| `filter` | yes | predicate over the dataset's columns | allow-list grammar (tokenize → parse → canonical re-emit) over the selected column map |

**Derived (not an input)**: `sourceGlob = {StorageURI}/{dataset}/dt={date}/*.parquet`
— assembled only from validated `dataset` and `date` over a constant template.

## Entity: Config (process-lifetime)

| Field | Env var | Required | Default | Notes |
|-------|---------|----------|---------|-------|
| `StorageURI` | `TELEMETRY_STORAGE_URI` | yes | — | e.g. `azure://telemetry`; replaces removed `DatasetURI` |
| `ToolPath` | `TELEMETRY_TOOL_PATH` | yes | — | path to `telemetry-query-tool` (unchanged) |
| `TimeoutSeconds` | `TELEMETRY_TIMEOUT_SECONDS` | no | **30** | raised from 10 (parquet-over-Azure is slower) |
| `MaxOutputBytes` | `TELEMETRY_MAX_OUTPUT_BYTES` | no | 65536 | unchanged |

**Removed**: `DatasetURI` / `TELEMETRY_DATASET_URI`. Absence of `TELEMETRY_STORAGE_URI`
→ startup error naming the new variable.

## Entity: Validation Policy (compile-time tables)

Per-dataset column allow-lists plus value-kind classification. A column belongs to
exactly one value kind within a dataset.

### Value kinds

| Kind | Literal accepted in filter | Reject if |
|------|----------------------------|-----------|
| numeric | number (`-?\d+(\.\d+)?`) | non-numeric literal |
| string | `'...'` (chars `[A-Za-z0-9 _-]` only) | numeric literal; `:`/`T`/other chars inside quotes |
| timestamp | `'...'` date string (date granularity) | numeric literal; non-string literal |
| bool | bare `true` / `false` (case-insensitive) | numeric (`1`), quoted (`'true'`), any other literal |

### Dataset `snap`

| Column | Kind | Nullable |
|--------|------|----------|
| `cycle_id` | string | no |
| `observation_utc` | timestamp | no |
| `vehicle_id` | string | no |
| `route_id` | string | no |
| `snap_outcome` | string | no |
| `raw_lat` | numeric | no |
| `raw_lon` | numeric | no |
| `snapped_lat` | numeric | no |
| `snapped_lon` | numeric | no |
| `snap_distance_km` | numeric | no |
| `snap_index` | numeric | no |
| `route_point_count` | numeric | no |
| `speed_mps` | numeric | yes |
| `bearing_deg` | numeric | yes |
| `is_stale` | bool | no |

### Dataset `lerp`

| Column | Kind | Nullable |
|--------|------|----------|
| `cycle_id` | string | no |
| `observation_utc` | timestamp | no |
| `vehicle_id` | string | no |
| `prior_route_id` | string | no |
| `prior_snapped_lat` | numeric | no |
| `prior_snapped_lon` | numeric | no |
| `prior_observation_utc` | timestamp | no |
| `prior_speed_mps` | numeric | yes |
| `prior_bearing_deg` | numeric | yes |
| `pos_delta_km` | numeric | no |
| `speed_delta` | numeric | yes |
| `bearing_delta` | numeric | yes |
| `time_delta_sec` | numeric | no |

### Dataset `cycle`

| Column | Kind | Nullable |
|--------|------|----------|
| `cycle_id` | string | no |
| `cycle_start_utc` | timestamp | no |
| `cycle_end_utc` | timestamp | no |
| `cycle_execution_seconds` | numeric | no |
| `buses_processed` | numeric | no |
| `buses_moved` | numeric | no |
| `buses_unchanged` | numeric | no |
| `buses_stationary` | numeric | no |
| `buses_stale` | numeric | no |
| `buses_skipped_no_route_id` | numeric | no |
| `buses_skipped_unknown_route` | numeric | no |
| `feed_header_ts` | numeric | yes |
| `duplicate_feed` | bool | no |
| `last_update_cache_size` | numeric | no |
| `vehicle_state_cache_size` | numeric | no |
| `sidecar_buffer_occupancy` | numeric | no |
| `sidecar_dropped_records` | numeric | no |
| `sidecar_persist_failures` | numeric | no |

> Nullability is **not** enforced by the validator (it is informational; the filter
> grammar has no IS NULL operator). All numeric kinds — `DOUBLE`, `INT32`, `INT64`,
> and their nullable variants — collapse to the single `numeric` value kind.

## Validation rules (cross-cutting)

1. **Dataset-first**: reject unknown `dataset` before touching `filter` (FR-001).
2. **Column scoping**: every identifier in `filter` must exist in the *selected*
   dataset's map; a column valid in another dataset is rejected here (FR-003, edge
   case "shared column / wrong dataset").
3. **Kind match**: literal kind must match the column's value kind (FR-005, FR-006).
4. **No dotted identifiers**: `.` is not an identifier char; `a.b` → unknown column
   (FR-008).
5. **No demo columns**: iris columns (`sepal.length`, `variety`, …) are simply absent
   from all three maps → rejected as unknown (FR-007).
6. **Unchanged guards**: forbidden keywords/chars, comment markers, URL/path patterns
   still apply (FR-013).
7. **Date strictness**: `date` must match `^\d{4}-\d{2}-\d{2}$` and be a real date;
   never sourced from `filter` (FR-009).

## State transitions

None. Each tool call is a stateless validate → assemble → delegate → return cycle.
The process holds only immutable `Config` and the compile-time policy tables.
