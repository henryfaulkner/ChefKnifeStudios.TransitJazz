# Telemetry Denormalization — Pre-Spec Notes

> Staging doc for `speckit-specify`. Not yet run through the SDD spec phase —
> this captures a fully-resolved design (including a `/grill-me` pass) so the
> spec phase can start from settled decisions instead of open questions.

## Context

The TransitDataWorker's logging sidecar (feature 013) currently emits three
separate marker event types — `SnapEventArgs`, `LerpEventArgs`, `CycleEventArgs`
— which `ParquetLoggingService` buffers into three `ConcurrentBag`s and flushes
as three independently-schemaed parquet datasets (`snap/`, `lerp/`, `cycle/` on
blob), queried through a hand-rolled allow-list grammar in the Go MCP bridge
(`tools/telemetry-mcp/internal/validate/validate.go`).

This is a single-person hobby project, and the three-dataset design has a real
cost: **every new field requires touching five places** — the `TelemetryColumns`
const list, the `*EventArgs` class, the `switch` in `ParquetLoggingService.
Accumulate`, the manual `Schema`/row-building code in the matching
`Flush*Async` method, and the Go allow-list's `datasetColumns` map. The goal
is to replace this with **one denormalized table**, discriminated by an
`event_type` column, where absent fields are `null` and adding a new property
is close to a one-line change on the C# side.

This also introduces two **new** event types that don't exist today:
**PerCityCycle** (time taken, health ping, feed freshness, tones emitted,
vehicles processed, plus memory + cache-size diagnostics — scoped to one
city) and **FullCycle** (time taken, health ping, tones emitted, vehicles
processed, cities processed count, cities processed CSV, plus the same
memory + cache-size diagnostics summed/reused across cities — scoped to one
worker tick across all cities). This is a **full replacement**:
`SnapEventArgs`, `LerpEventArgs`, `CycleEventArgs` and their parquet outputs
are retired.

## Current implementation (what's being replaced)

- `Logging/IEventNotificationService.cs` — the in-process pub/sub bus.
  `IEventArgs` marker interface, `PostEvent`/`EventReceived`. **Unchanged** —
  still the transport.
- `Logging/LogEventArgs.cs`, `SnapEventArgs.cs`, `LerpEventArgs.cs`,
  `CycleEventArgs.cs` — the three concrete event types. **Retired**, replaced
  by one event shape (see Data Model).
- `Logging/LogEventWorker.cs` — bounded `Channel<IEventArgs>` with
  `DropWrite` shedding, 5-min `PeriodicTimer` flush, drains via
  `_sink.Accumulate(e)`. **Unchanged in structure** — it's already
  dataset-agnostic (moves `IEventArgs` instances without knowing their shape).
- `Logging/ParquetLoggingService.cs` (`ILoggingService`) — buffers into three
  typed `ConcurrentBag`s via a `switch` on concrete type, builds three
  hand-written `Parquet.Net` schemas, uploads to
  `{dataset}/dt={date}/part-{ts}-{guid}.parquet`. **Rewritten**: one buffer,
  one reflection-driven schema, one output path (`telemetry/dt={date}/part-
  {ts}-{guid}.parquet`).
- `Logging/TelemetryColumns.cs` — const strings, one per column, grouped by
  dataset with comments. **Retired** — properties become self-describing via
  attributes on the event record instead of a separate const list.
- `Worker.cs` — per-city `foreach` loop inside a 10s `PeriodicTimer` tick
  (`ExecuteAsync`, lines 41-77). Today's `SnapEventArgs`/`LerpEventArgs`/
  `CycleEventArgs` are posted from three scattered points mid-processing
  (lines 381-396, 449-467, 539-561, inside/around
  `ProcessSpatialReconciliationAsync`). The new design posts from exactly
  **two** points, both at the end of processing, not interleaved with it:
  one **PerCityCycle** post immediately after each city finishes its work
  inside the `foreach` — but wrapping the *entire* per-city `try/catch`, not
  just the success path, so a row is emitted every tick regardless of
  outcome (see `health_ok` below). All per-city metrics, including the
  memory/cache columns, are computed once right before this single post, not
  accumulated across multiple post sites. One **FullCycle** post follows
  after the `foreach` closes. There is currently **no post-loop hook** — the
  `foreach` (lines 55-75) closes with no code after it inside the `while`
  body — so a new block after line 75 (before the `while` body ends) is
  needed to aggregate across the tick's cities and emit the single
  **FullCycle** row.
- `tools/telemetry-mcp/internal/validate/validate.go` — `validDatasets`
  (3-entry set) and `datasetColumns` (3 per-dataset column→kind maps).
  **Simplified**: one dataset name, one merged column→kind map covering the
  union of all event types' fields, plus `event_type` itself as a filterable
  string column.
- No existing memory-usage instrumentation anywhere in the repo. The natural
  insertion point mirrors the existing self-health pattern already used for
  `SidecarBufferOccupancy`/`SidecarDroppedRecords`/`SidecarPersistFailures`
  computed inline in `Worker.cs` right before today's `CycleEventArgs` post.

## Data model

One wide record type, `TelemetryEvent`, replacing the three `*EventArgs`
classes. Every emitted event is one row in one table. Columns fall into three
groups:

1. **Common columns** (present on every row): `event_type` (string
   discriminator — `"PerCityCycle"` | `"FullCycle"`), `event_id` (string,
   replaces `cycle_id` as the row's own identity — no cross-row correlation
   is needed once there's no snap/lerp detail to join back to a parent
   cycle), `observation_utc` (timestamp).
2. **Per-event-type columns** (nullable, populated only for the event types
   that define them):
   - **PerCityCycle**: `city_name` (string), `time_taken_seconds` (numeric),
     `health_ok` (bool), `feed_freshness_seconds` (numeric — age of the
     feed's header timestamp at observation time), `tones_emitted` (numeric),
     `vehicles_processed` (numeric), `gc_heap_bytes` (numeric),
     `process_working_set_bytes` (numeric), `vehicle_state_cache_size`
     (numeric), `crossing_baseline_cache_size` (numeric), `route_index_size`
     (numeric), `route_trigger_point_cache_size` (numeric).
   - **FullCycle**: `time_taken_seconds` (numeric), `health_ok` (bool),
     `tones_emitted` (numeric), `vehicles_processed` (numeric),
     `cities_processed_count` (numeric), `cities_processed_csv` (string),
     `gc_heap_bytes` (numeric), `process_working_set_bytes` (numeric),
     `vehicle_state_cache_size` (numeric), `crossing_baseline_cache_size`
     (numeric), `route_index_size` (numeric),
     `route_trigger_point_cache_size` (numeric).
   - Note `time_taken_seconds`, `health_ok`, `tones_emitted`,
     `vehicles_processed`, and all six memory/cache columns are shared
     *names* across both event types (same meaning, different scope) — one
     column each, not duplicated per type. `tones_emitted`,
     `vehicles_processed`, and the four cache-size columns are **summed
     across cities** on the FullCycle row; `gc_heap_bytes` and
     `process_working_set_bytes` are process-wide values **sampled once per
     tick and reused verbatim** on every row emitted that tick (not summed —
     memory isn't partitionable per city). Only `city_name`/
     `feed_freshness_seconds` (PerCityCycle-only) and
     `cities_processed_count`/`cities_processed_csv` (FullCycle-only) are
     exclusive to one type and null on the other.

This keeps the union at 17 real columns total (3 common + 14 metric/detail
columns) — wider than an initial 11-column sketch because the memory/cache
breakdown (resolved via `/grill-me`, see below) replaced one vague
`memory_usage_bytes` field with six precise ones. Still not a sparse mess —
the two event types overlap heavily by design.

### Memory & cache columns (resolved)

Two distinct memory signals, not one — chosen because a managed-heap-only
number previously hid the real story in a past RAM investigation (see repo
memory `project_browser_ram_wasm_heap`: a heap-snapshot conclusion was wrong
because the actual culprit was outside the managed heap):

- `gc_heap_bytes` — `GC.GetTotalMemory(false)` (managed heap only, no forced
  collection).
- `process_working_set_bytes` — `Process.GetCurrentProcess().WorkingSet64`
  (OS-resident set: managed + unmanaged + native, the ops-view number).

Both sampled once per tick, reused unchanged across every `PerCityCycle` row
and the one `FullCycle` row emitted that tick (memory isn't meaningfully
partitionable per city within one process).

Cache sizes: **every in-memory cache in `Worker.cs` keyed by city name** gets
a column, each read per-city on `PerCityCycle` and summed across cities on
`FullCycle`:

| Column | Cache (Worker.cs) | Notes |
|---|---|---|
| `vehicle_state_cache_size` | `_vehicleStateCaches[city]` (line 26) | already existed as `VehicleStateCacheSize` in old `CycleEventArgs` — kept, name unchanged |
| `crossing_baseline_cache_size` | `_crossingBaselines[city]` (line 39) | new |
| `route_index_size` | `_routeIndex[city]` (line 31) | new |
| `route_trigger_point_cache_size` | `_routeTriggerPoints[city]` (line 37) | new |

`_routeMode`/`_routeCumDist` are **not** given separate columns — they're
rebuilt in lockstep with `_routeIndex` (same routes, same `BuildRouteIndex`
call, lines 192/657) so their counts are always identical to
`route_index_size`; a separate column would be pure redundancy.

The old `LastUpdateCacheSize` field is **dropped**, not carried forward — it
was hardcoded to `0` in the current code (`Worker.cs:556`,
`LastUpdateCacheSize = 0`), i.e. already dead/fake telemetry. No replacement
needed since nothing real ever fed it.

### `tones_emitted` (was `points_processed`, renamed)

Source: `crossingRecords.Count` — the count of detected trigger-point
crossings that tick (`CrossingDetector.Detect`, `Worker.cs:500`), already
logged today as `CrossingsEmitted` (`Worker.cs:527`). Distinct from
`vehicles_processed`: one vehicle can cross zero, one, or several trigger
points per tick. Renamed from the originally-specified `points_processed` to
`tones_emitted` because that names the actual downstream effect (each
crossing fires a synthesized Tone.js note per the 009 soundscape design)
rather than an internal detection step.

### `health_ok` (resolved — also changes where PerCityCycle is posted)

Three-way mapping, evaluated per city per tick:

| Path in `Worker.cs`'s per-city `foreach` (lines 55-75) | `health_ok` |
|---|---|
| Exception caught (line 71-74) | `false` |
| Route index not ready → `continue` (lines 61-65) | `false` |
| Ran normally (even if `feed.Entities.Count == 0`) | `true` |

**Important behavior change from today:** currently, an exception or
not-ready route index means `ProcessSpatialReconciliationAsync` never runs,
so no `CycleEventArgs`/`PerCityCycle` post happens at all for that city that
tick — unhealthy ticks are silently invisible. This redesign **fixes that**:
the `PerCityCycle` post site moves from inside
`ProcessSpatialReconciliationAsync` to wrap the *entire* per-city
`try/catch` block in the `foreach`, so exactly one `PerCityCycle` row is
emitted per city per tick regardless of outcome. On the failure paths, only
`health_ok=false` and whatever's cheaply available (memory/cache-size
columns, which don't depend on this tick's feed processing) are populated;
`tones_emitted`/`vehicles_processed`/`feed_freshness_seconds` are `0`/`null`
since no processing occurred.

An empty feed is explicitly **not** unhealthy — `health_ok=true` even when
`vehicles_processed=0`, since nothing failed, there was just nothing to do.
Feed staleness/duplication is a separate axis from process health and stays
in its own `feed_freshness_seconds` column rather than folding into
`health_ok`.

### Confirmed: intentional loss of snap/lerp granularity

Per-vehicle GPS-snap telemetry (`snap_outcome`, `snap_distance_km`, raw vs.
snapped lat/lon, per-vehicle `is_stale`) and per-vehicle movement telemetry
(`pos_delta_km`, speed/bearing deltas) are **fully dropped, no middle
ground**. After this change, telemetry cannot answer "which vehicle had a
bad GPS snap" or "how far did vehicle X move," only city/worker-level
aggregates. The existing query-guide patterns `snap` `snap_distance_km >
0.5` and `lerp` `pos_delta_km > 1.0 AND vehicle_id = 'v001'` have no
equivalent after this change — this is accepted as intentional scope
reduction, not an oversight.

### Why two `event_type` rows instead of one merged row per city

The query tool is filter-only with no aggregation (see
`telemetry-query-guide.md`), so every query result is rows you eyeball
yourself. If PerCityCycle and FullCycle were merged into a single row
emitted per city, the full-cycle fields (`cities_processed_count`,
`cities_processed_csv`, full-cycle `time_taken_seconds`, etc.) would be
**repeated on every city's row that tick** — a "how's the full cycle
looking" query would return N duplicate-full-cycle rows per tick (N = city
count) that you'd have to dedupe by hand, and that duplication grows
linearly as more cities are added. Keeping FullCycle as its own row means
exactly **one row per tick** regardless of city count — flat as the city
list grows — while PerCityCycle rows correctly scale with city count (that
data really is per-city). Parquet's columnar compression makes the *storage*
cost of either design cheap; the *query ergonomics* cost is what scales
badly with the merged-row approach, which is why this design keeps them
separate.

## Design for "loose model / programmatic / minimal code to add a property"

Reuse **`Parquet.Net`'s attribute-driven schema support** instead of the
current hand-built `Schema`/row-array code in each `Flush*Async` method:

- Define `TelemetryEvent` as a single C# record with `[ParquetColumn]`-style
  properties (or plain nullable properties reflected over, depending on what
  `Parquet.Net`'s installed version supports — confirm during implementation;
  `Parquet.Net` has supported POCO-driven schema generation via
  `ParquetSerializer` since v4). One property = one column. Adding a field to
  a future event type means adding one nullable property to this one record
  — no touching a separate const-name file, no touching a `switch`, no
  touching a manually-built `Schema` object.
- `ParquetLoggingService.Accumulate` becomes a single
  `ConcurrentBag<TelemetryEvent>.Add(e)` — no type-switch, since there's only
  one type now.
- `FlushAsync` becomes one `Flush*Async` method (serialize the one buffer,
  one blob path), replacing the current three.
- Column naming: keep the existing snake_case convention (still consumed by
  the Go allow-list) — use `Parquet.Net`'s column-name-from-attribute (or a
  naming-policy shim) rather than hand-typed constants, so the C# property
  name and the parquet column name can't drift out of sync.

## Blob layout

Single partitioned path, replacing the three dataset paths:
`telemetry/dt={yyyy-MM-dd}/part-{yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet`
(same immutable-part-file convention as today, minus the `{dataset}/` prefix
segment since there's only one dataset now).

## Query-side impact (tools/telemetry-mcp)

- `validDatasets`: shrinks to a single valid dataset name — **`telemetry`**
  (matches the existing blob container name and doc vocabulary, so no other
  renaming cascades from picking it).
- `datasetColumns`: one map covering `event_type` (string), `event_id`
  (string), `observation_utc` (timestamp), and the 14 metric/detail columns
  from the Data Model section above, each with its value-kind.
- Filtering on `event_type = 'PerCityCycle'` (or `'FullCycle'`) becomes the
  primary way callers scope a query to one event shape — worth calling out
  explicitly in the query guide docs since fields like `city_name` are
  meaningless (null) on FullCycle rows and vice versa.
- The rest of `validate.go` (tokenizer/parser) is already dataset-agnostic
  and needs no changes beyond the two maps above.

## Docs to update after implementation

- `.claude/skills/mj-data-explorer/references/telemetry-schema.md` — replace
  the three-dataset tables with the single `TelemetryEvent` table + the
  event_type discriminator explanation.
- `.claude/skills/mj-data-explorer/references/telemetry-query-guide.md` —
  update dataset name, accept/reject examples, and note the `event_type`
  filtering pattern.
- `specs/013-logging-sidecar-service/` and `specs/014-transit-datasets/` —
  these become historical; this feature (038) supersedes them rather than
  editing the old ones in place.
- Retire/replace `SnapParquetSchemaTests.cs`, `LerpParquetSchemaTests.cs`,
  `CycleParquetSchemaTests.cs` with a single schema test for `TelemetryEvent`;
  `PartitionPathTests.cs`, `ChannelLoadSheddingTests.cs`,
  `FailureIsolationTests.cs` stay conceptually the same, just re-pointed at
  the new single-buffer/single-path service.

## Design questions already resolved (via `/grill-me`)

1. **Dataset/table name** → `telemetry`.
2. **`points_processed`** → renamed **`tones_emitted`**, sourced from
   `crossingRecords.Count` (`Worker.cs:500,527`).
3. **Memory sampling** → not one field but six: `gc_heap_bytes` +
   `process_working_set_bytes` (process-wide, sampled once per tick) plus
   four per-city cache-size columns (`vehicle_state_cache_size`,
   `crossing_baseline_cache_size`, `route_index_size`,
   `route_trigger_point_cache_size`), summed on FullCycle.
4. **`health_ok`** → three-way mapping (exception / route-index-not-ready /
   ran normally). Resolving this surfaced a real behavior change: the
   `PerCityCycle` post site moves to wrap the *entire* per-city `try/catch`
   in `Worker.cs`'s `foreach`, so every city posts exactly one row every
   tick — including failure ticks, which are silently invisible today.
5. **Snap/lerp granularity loss** → confirmed fully intentional, no middle
   ground. Per-vehicle GPS-snap/movement debugging is not preservable after
   this change; accepted as an explicit scope reduction.

## Verification (for the plan/tasks phase)

- Unit tests: new `TelemetryEventSchemaTests.cs` asserting the reflected
  Parquet schema has the expected columns/types for both event types (mirror
  the structure of the retired `SnapParquetSchemaTests.cs` etc.).
- `ChannelLoadSheddingTests.cs`/`FailureIsolationTests.cs` re-run against the
  single-buffer service to confirm drop/flush/shutdown behavior is preserved.
- Manual: run the worker locally against MARTA (the only city with
  `EmitsTelemetry: true` today), confirm one `telemetry/dt=.../part-*.parquet`
  file appears per flush interval, and query it via
  `mcp__telemetry-query-bridge__query_telemetry` with `event_type =
  'PerCityCycle'` and `event_type = 'FullCycle'` filters to confirm both
  shapes round-trip correctly (including nulls on the non-applicable columns).
