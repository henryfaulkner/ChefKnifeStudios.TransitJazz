# Phase 0 Research: Telemetry Denormalization

The source pre-spec doc (`docs/telemetry-denormalization.md`) already resolved the product-level design questions via a `/grill-me` pass. This file records the *technical* decisions the plan depends on, plus verification of the one genuinely open implementation detail (the `Parquet.Net` POCO API).

## R1. Parquet.Net POCO/attribute serialization (resolves the FR-006 / FR-024 mechanism)

**Decision**: Serialize the single `TelemetryEvent` record with `ParquetSerializer.SerializeAsync(rows, stream)` and pin each parquet column name with `[ParquetColumn(Name = "snake_case")]` on the property. This replaces the hand-built `ParquetSchema` + per-column `DataColumn` writes and the separate `TelemetryColumns` const file.

**Rationale**:
- The installed package is `Parquet.Net` `5.*` (`...TransitDataWorker.csproj:13`, `...Tests.csproj:12`), whose `ParquetSerializer` fully supports POCO-driven schema generation.
- `[ParquetColumn(Name = "cust_id")]` overrides the emitted column name, so the snake_case wire contract (consumed by the Go allow-list) is expressed *on the property itself* — the C# name and the parquet name cannot drift (FR-024, FR-006). Adding a field is one nullable property (FR-006, SC-003).
- C# nullable types (`int?`, `long?`, `double?`, `bool?`, `string?`) map directly to nullable parquet columns — exactly what the per-event-type fields need (null on the non-applicable event type, FR-004).
- Verified against the library docs/issues: attribute-name mapping and nullable mapping are supported in the 5.x/6.x line (historical issue #29 about attributes being ignored is fixed).

**Alternatives considered**:
- *Keep the hand-built `ParquetSchema`/`DataColumn` approach for one merged schema* — rejected: it re-introduces the "adding a field touches a schema list + a row-builder" cost the feature exists to remove, and keeps `TelemetryColumns` alive.
- *Reflection over plain properties with a naming-policy shim (snake_case-from-PascalCase)* — viable, but an explicit `[ParquetColumn(Name=...)]` is more legible and immune to a naming-policy edge case silently renaming a contract column. Chosen the explicit attribute.

**Confirm during implementation**: exact `ParquetColumn` property spelling (`Name`) and that `SerializeAsync` writes a single row group for a `List<TelemetryEvent>` at the sizes we flush (≤ a few hundred rows/flush today). If a 5.x edge requires `ParquetSerializerOptions`, add it in the rewrite — does not change the contract.

## R2. Two post sites, PerCityCycle wraps the whole try/catch (resolves FR-008 / FR-010 visibility)

**Decision**: In `Worker.cs ExecuteAsync` (the `while` loop, lines 53-76 as read):
- Compute all PerCityCycle metrics **once** right before a single `PostEvent`, placed so it runs on **every** path of the per-city `try/catch` (normal, `continue` when route index not ready, and the `catch`). Use a `finally`-style single emission (or a local flag + post after the try/catch) so exactly one PerCityCycle row is posted per city per tick.
- Add a **post-loop block after the `foreach` closes** (there is none today — line 75 is the last line inside the `while` body) that aggregates the tick's per-city values and posts one FullCycle row.

**Rationale**: Today's `CycleEventArgs` is posted from *inside* `ProcessSpatialReconciliationAsync` (line 539), which never runs on the not-ready `continue` (line 64) or when an exception is thrown (line 71) — so unhealthy ticks emit nothing. Moving the post to wrap the try/catch is what makes `health_ok=false` rows exist (FR-008, FR-010, SC-001). This is a genuine behavior change, called out in the spec.

**Alternatives considered**:
- *Keep posting from inside `ProcessSpatialReconciliationAsync`* — rejected: cannot see failures (the whole point).
- *Emit FullCycle from a separate timer* — rejected: FullCycle is a per-tick roll-up; it must align 1:1 with a tick and see that tick's per-city values, so it posts inline after the `foreach`.

## R3. Memory sampling — two signals, once per tick (resolves FR-017 / FR-018)

**Decision**: Sample `GC.GetTotalMemory(false)` → `gc_heap_bytes` and `Process.GetCurrentProcess().WorkingSet64` → `process_working_set_bytes` **once at the top of the `while` tick body**, before the `foreach`. Reuse the two captured values verbatim on every PerCityCycle row and the FullCycle row that tick. Do **not** sum them on FullCycle.

**Rationale**: Two signals because a managed-heap-only number previously hid a real RAM culprit (repo memory `project_browser_ram_wasm_heap`: the heap-snapshot conclusion was wrong because the cause was outside the managed heap). `GC.GetTotalMemory(false)` avoids forcing a collection (cheap, no hot-path stall). Memory is process-wide and not partitionable per city, so summing would be meaningless (FR-018, SC-005). `System.Diagnostics` is already imported in `Worker.cs` (line 11).

**Alternatives considered**: `GC.GetTotalMemory(true)` (forces GC — rejected, perturbs the process it measures); a single combined field (rejected by the source doc for the reason above).

## R4. Per-city cache-size columns (resolves FR-019 / FR-020 / FR-021)

**Decision**: One column per in-memory cache `Worker.cs` keys by city name:

| Column | Backing cache (Worker.cs) |
|---|---|
| `vehicle_state_cache_size` | `_vehicleStateCaches[city]` (line 26) — kept, name unchanged from old `CycleEventArgs.VehicleStateCacheSize` |
| `crossing_baseline_cache_size` | `_crossingBaselines[city]` (line 39) — new |
| `route_index_size` | `_routeIndex[city]` (line 31) — new |
| `route_trigger_point_cache_size` | `_routeTriggerPoints[city]` (line 37) — new |

Read `.Count` per-city on PerCityCycle; **sum across cities** on FullCycle.

- `_routeMode` / `_routeCumDist` get **no** column — they're rebuilt in lockstep with `_routeIndex` in the same `BuildRouteIndex` call (lines 192/657), so their counts are always identical to `route_index_size` (FR-021 — a separate column would be redundant).
- The old `last_update_cache_size` is **dropped**, not migrated — it was hardcoded to `0` (`Worker.cs:556`, `LastUpdateCacheSize = 0`), i.e. dead/fake telemetry with no real source (FR-020).

**Rationale**: The doc's `/grill-me` pass chose "every city-keyed cache gets a column" as the memory-diagnostic breakdown. Reading `.Count` on each is O(1)-ish and cheap enough for the failure paths too (they don't depend on this tick's feed).

## R5. `tones_emitted` naming and source (resolves FR-022)

**Decision**: `tones_emitted` = `crossingRecords.Count` for the tick (the count of detected trigger-point crossings, `CrossingDetector.Detect` accumulation ending at `Worker.cs:502`; already logged as `crossingsEmitted` at `Worker.cs:527`). Distinct from `vehicles_processed` (one vehicle can cross 0..n trigger points/tick).

**Rationale**: Names the real downstream effect (each crossing fires a synthesized Tone.js note per the 009 soundscape design), not an internal detection step. On PerCityCycle it's that city's count; on FullCycle it's the sum across cities (FR-015). `crossingRecords` is a per-`ProcessSpatialReconciliationAsync` local — to post PerCityCycle *outside* that method, the count (and `vehicles_processed`, feed freshness) must be surfaced out (return a small result struct from `ProcessSpatialReconciliationAsync`, or compute into locals the post site reads). Resolve the plumbing in implementation; contract is the value.

## R6. `vehicles_processed` and `feed_freshness_seconds` (data-model detail)

**Decision**:
- `vehicles_processed` = the tick's processed count (mirrors today's `BusesProcessed = movedCount + unchangedCount + stationaryCount + staleCount`, `Worker.cs:545`). `0` on failure paths (nothing processed).
- `feed_freshness_seconds` = age of the feed header timestamp at observation time = `(observationUtc - feedHeaderTs)` in seconds, from `feed.Header?.Timestamp` (`Worker.cs:518`). Null when no feed header / on failure paths. Replaces the old raw `feed_header_ts` + `duplicate_feed` pair with a single derived freshness number (staleness stays its own axis, never folded into `health_ok` — FR-011).

**Rationale**: Keeps the freshness signal the old `cycle` dataset had (`feed_header_ts`, `buses_stale`) but as one interpretable number, per the data-model section of the source doc.

## R7. Go validator collapse (resolves FR-025 / FR-026 / FR-027 / FR-028)

**Decision**: In `tools/telemetry-mcp/internal/validate/validate.go`:
- `validDatasets` → `{"telemetry": true}` (single name; matches the blob container + doc vocabulary, no rename cascade).
- `datasetColumns` → one map under key `"telemetry"` holding the union: `event_type`(string), `event_id`(string), `observation_utc`(timestamp) + the 14 metric/detail columns with their kinds (see contracts/query-validator.md).
- Update the three hard-coded error strings that name `snap, lerp, cycle` (`ValidateDataset` line 138, `Filter` line 161) to name `telemetry`.
- Tokenizer/parser (`parsePredicate`, `tokenize`, kinds) are **already dataset-agnostic** — no change beyond the two maps + error text (FR-028).

**Rationale**: The validator is a lookup over `datasetColumns[dataset]`; collapsing to one dataset with one merged column set is the minimal change. `event_type` becomes a filterable string column so `event_type = 'PerCityCycle'` scopes a query (FR-027) — DuckDB returns null-column rows for the other type's fields, matching FR-004.

**Alternatives considered**: keeping `snap`/`lerp`/`cycle` as aliases of `telemetry` — rejected: they no longer exist as datasets; aliasing would mislead callers and keep dead vocabulary.

## R8. Blob path (resolves FR-029)

**Decision**: `telemetry/dt={yyyy-MM-dd}/part-{yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet` — identical to today's `BuildBlobPath` (`ParquetLoggingService.cs:274-279`) minus the `{dataset}/` segment. `LoggingOptions.Container` already defaults to `"telemetry"` (`LoggingOptions.cs:10`), so the container is unchanged; only the in-container prefix loses one segment.

**Rationale**: Immutable part-file convention preserved (FR-007, FR-029); one dataset ⇒ no per-dataset prefix.

## R9. Tests (resolves FR-031)

**Decision**: Delete `Snap/Lerp/CycleParquetSchemaTests.cs`; add `TelemetryEventSchemaTests.cs` asserting the `ParquetSerializer`-reflected schema has the expected column names/types and that both event-type shapes round-trip including nulls on non-applicable columns. Re-point `ChannelLoadSheddingTests.cs`, `FailureIsolationTests.cs`, `PartitionPathTests.cs` at the single-buffer / single-path service (they construct `SnapEventArgs`/`CycleEventArgs` today — swap to `TelemetryEvent`).

**Rationale**: One record ⇒ one schema test; the load-shedding/failure-isolation/partition-path behaviors are unchanged in intent, just re-pointed.

## Open items carried to implementation (none block the design)

1. Exact `ParquetSerializer` option surface in 5.x (row-group sizing) — R1.
2. Plumbing the per-city `crossingRecords.Count` / processed-count / freshness out of `ProcessSpatialReconciliationAsync` to the post site — R5 (return a small result struct is the cleanest).
3. Whether to keep `event_id` as `Guid.NewGuid().ToString("N")` (matches old `cycle_id` format) — yes, per data-model.

## Sources

- [Parquet.Net (aloneguid/parquet-dotnet)](https://github.com/aloneguid/parquet-dotnet)
- [parquet-dotnet serialisation docs](https://github.com/aloneguid/parquet-dotnet/blob/master/docs/serialisation.md)
- [Serialize/Deserialize Parquet in C#](https://ssojet.com/serialize-and-deserialize/serialize-and-deserialize-parquet-in-csharp)
