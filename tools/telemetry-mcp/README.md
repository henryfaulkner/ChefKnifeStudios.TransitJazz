# telemetry-mcp

A small, standalone Go MCP server that exposes a single `query_telemetry` tool to
Claude Code over stdio. It validates untrusted arguments against a strict allow-list
grammar, then invokes the existing `telemetry-query-tool.exe` to run a read-only query
over the TransitJazz telemetry produced by the logging sidecar.

## Dataset

One denormalized dataset, `telemetry`, discriminated by an `event_type` column:

- `PerCityCycle` — one row per telemetry-emitting city per worker tick
- `FullCycle` — one row per worker tick across all cities (counts, timing, health)

The dataset has one merged snake_case column contract (22 columns; some populated
only for one event type — see
[`specs/038-telemetry-denormalization/contracts/telemetry-event-schema.md`](../../specs/038-telemetry-denormalization/contracts/telemetry-event-schema.md),
[`specs/045-time-to-first-note/contracts/telemetry-schema.md`](../../specs/045-time-to-first-note/contracts/telemetry-schema.md),
and [`specs/051-egress-reduction/contracts/telemetry-observability.md`](../../specs/051-egress-reduction/contracts/telemetry-observability.md)
for the columns added since; `internal/validate/validate.go`'s `datasetColumns` is
the final authority).

## Tool arguments

`query_telemetry` takes three arguments:

| Argument  | Required | Form         | Notes |
|-----------|----------|--------------|-------|
| `dataset` | yes      | `telemetry`  | validated before the filter is parsed |
| `date`    | no       | `YYYY-MM-DD` | strict, zero-padded, real calendar date; **default = today (UTC)** |
| `filter`  | yes      | predicate    | allow-list grammar over the telemetry columns |

The read source is assembled from a fixed template the operator cannot redirect:
`{TELEMETRY_STORAGE_URI}/telemetry/dt={date}/*.parquet`. `telemetry/` is a literal
virtual-directory prefix inside whichever container `TELEMETRY_STORAGE_URI` points
at — the container itself is not necessarily named `telemetry` (e.g. prod's worker
writes into a container named `parquet`).

The glob is read via `read_parquet(..., union_by_name=true)`, not a bare `'glob'`
string. A day's partition can span a schema change mid-day (a new nullable column
added to `TelemetryEvent`) — part-files written before and after the change coexist
under the same `dt=` prefix. The bare-string form takes its schema from the first
file it opens and hard-errors on any mismatch; `union_by_name=true` coalesces a
missing column to `NULL` per file instead, so adding a column never breaks queries
against days that straddle the rollout.

### Value kinds in `filter`

- **numeric** columns compare against numbers (`vehicles_processed > 0`).
- **string** columns compare against quoted strings (`city_name = 'MARTA'`); allowed
  characters inside quotes are `[A-Za-z0-9 _-]`.
- **timestamp** columns compare against a **date string** (`observation_utc > '2026-07-11'`).
  Comparisons are **date-granularity only** — a full ISO timestamp like
  `'2026-07-11T12:00:00'` is rejected (the `:`/`T` characters are not allowed inside a
  string literal). This is a deliberate non-goal, not a bug.
- **bool** columns compare against the bare literals `true` / `false`
  (`health_ok = false`). A numeric (`health_ok = 1`) or quoted (`health_ok = 'true'`)
  literal is rejected.

Identifiers are bare snake_case (`[a-z_][a-z0-9_]*`); `.` is **not** an identifier
character, so dotted paths (`event_type.value`) are rejected.

## Configuration (environment variables)

| Variable                          | Required | Default | Notes |
|-----------------------------------|----------|---------|-------|
| `TELEMETRY_STORAGE_URI`           | yes      | —       | container base, e.g. `azure://parquet` |
| `TELEMETRY_TOOL_PATH`             | yes      | —       | path to `telemetry-query-tool.exe` |
| `TELEMETRY_TIMEOUT_SECONDS`       | no       | `30`    | raised from 10 (parquet-over-Azure is slower) |
| `TELEMETRY_MAX_OUTPUT_BYTES`      | no       | `65536` | output truncation cap |

The delegated `telemetry-query-tool` still reads `AZURE_STORAGE_CONNECTION_STRING`
from its own environment (unchanged).

> Full build, configuration, and query-shape details live in
> [`specs/038-telemetry-denormalization/`](../../specs/038-telemetry-denormalization/)
> (plan, data-model, contracts, quickstart).
