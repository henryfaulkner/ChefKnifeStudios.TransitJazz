---
description: "Task list for Telemetry Denormalization (feature 038)"
---

# Tasks: Telemetry Denormalization

**Input**: Design documents from `specs/038-telemetry-denormalization/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅ (telemetry-event-schema, query-validator, blob-layout), quickstart.md ✅

**Tests**: Included. FR-031 explicitly mandates schema/pipeline tests, and this codebase uses parquet-schema + pipeline tests as its primary verification mechanism, so test tasks are first-class here.

**Organization**: Grouped by the 4 user stories from spec.md. Because the two C# event shapes (US1 PerCityCycle, US2 FullCycle) and the Go query surface (US4) all share ONE `TelemetryEvent` record + ONE rewritten `ParquetLoggingService`, that shared record/service/retirements live in **Phase 2 Foundational** (blocking). US3 ("near-one-line add") is a *property* of the foundational POCO+attribute design — its tasks verify that property rather than build separate code.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4 (Setup/Foundational/Polish have no story label)

## Path Conventions

Paths are the real repo layout from plan.md:
- Worker: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/`
- Worker tests: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/`
- Go validator: `tools/telemetry-mcp/internal/validate/`
- Docs: `.claude/skills/mj-data-explorer/references/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No new project/deps — the change is inside the existing TransitDataWorker + its test project + the Go tool. This phase only establishes the branch state and confirms the toolchain.

- [ ] T001 Confirm on branch `038-telemetry-denormalization` and that `dotnet build src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj` and `go build ./...` (in `tools/telemetry-mcp`) both succeed on the current `main` state (baseline green before changes).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single `TelemetryEvent` record, the rewritten single-buffer/single-path `ParquetLoggingService`, and the retirement of the three old event types + `TelemetryColumns`. **Every user story depends on this.** After this phase the worker will not compile until Worker.cs is updated (Phase 3), which is expected — Phase 3 US1 completes the compile.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 [P] Add `TelemetryEvent` record in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/TelemetryEvent.cs` implementing `IEventArgs`, with all 17 `[ParquetColumn(Name = "…")]` snake_case properties exactly per `contracts/telemetry-event-schema.md` (common: event_type/event_id/observation_utc; per-city-only: city_name/feed_freshness_seconds; full-cycle-only: cities_processed_count/cities_processed_csv; shared nullable: time_taken_seconds/health_ok/tones_emitted/vehicles_processed/gc_heap_bytes/process_working_set_bytes/vehicle_state_cache_size/crossing_baseline_cache_size/route_index_size/route_trigger_point_cache_size).
- [ ] T003 Rewrite `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/ParquetLoggingService.cs`: replace the three `ConcurrentBag`s with one `ConcurrentBag<TelemetryEvent>`; `Accumulate` becomes a single `.Add((TelemetryEvent)e)` (no type switch); replace the three `Flush*Async` + hand-built `ParquetSchema`/`DataColumn` code with one `FlushAsync` using `await ParquetSerializer.SerializeAsync(rows, ms)` (Snappy if exposed via options — see research R1); keep `UploadAsync`/`RecordPersistFailure`/container-ensure logic. (Depends on T002)
- [ ] T004 Update `BuildBlobPath` in `ParquetLoggingService.cs` to drop the `{dataset}/` segment → `dt={yyyy-MM-dd}/part-{yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet` per `contracts/blob-layout.md` (verify the query-bridge source template resolves `{dataset}`→container `telemetry`; adjust prefix if the bridge needs a literal `telemetry/`). (Depends on T003)
- [ ] T005 [P] Delete retired files: `Logging/SnapEventArgs.cs`, `Logging/LerpEventArgs.cs`, `Logging/CycleEventArgs.cs`, `Logging/LogEventArgs.cs`, `Logging/TelemetryColumns.cs` under `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/`.
- [ ] T006 Verify `ILoggingService.cs`, `LogEventWorker.cs`, `IEventNotificationService.cs`, `LoggingOptions.cs`, and `Program.cs` DI registrations need **no** change (interface/`IEventArgs`-based) — read them, confirm, and note in the commit message. (Depends on T003)

**Checkpoint**: `TelemetryEvent` + single-buffer service exist; old event types gone. Worker.cs still references old types (compile red) — resolved in Phase 3.

---

## Phase 3: User Story 1 - Inspect per-city processing health each tick (Priority: P1) 🎯 MVP

**Goal**: Emit exactly one PerCityCycle row per telemetry-emitting city per tick, on every path (normal / exception / not-ready), with `health_ok` set correctly and cheap diagnostics populated on failure paths.

**Independent Test**: Run the worker against MARTA; force normal / empty-feed / thrown-exception / route-index-not-ready and confirm a PerCityCycle row is recorded each tick with the right `health_ok` (true on normal incl. empty feed; false on exception/not-ready).

### Tests for User Story 1 ⚠️

- [ ] T007 [P] [US1] Add `TelemetryEventSchemaTests.cs` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/` per `contracts/telemetry-event-schema.md` §"Schema test contract": serialize a PerCityCycle row (city fields set, full-cycle fields null) + a FullCycle row (full-cycle set, city fields null), assert all 17 columns/types present and non-applicable columns are null on each row. (Depends on T002; covers US1 + US2)
- [ ] T008 [P] [US1] Delete `SnapParquetSchemaTests.cs`, `LerpParquetSchemaTests.cs`, `CycleParquetSchemaTests.cs` from the Tests project.
- [ ] T009 [P] [US1] Update `PartitionPathTests.cs` to assert the new `dt=…/part-*.parquet` path with NO `snap|lerp|cycle` segment (contracts/blob-layout.md §PartitionPathTests). (Depends on T004)

### Implementation for User Story 1

- [ ] T010 [US1] In `Worker.cs ExecuteAsync`, sample the two process-wide memory signals ONCE per tick at the top of the `while` body (before the `foreach`): `GC.GetTotalMemory(false)` and `Process.GetCurrentProcess().WorkingSet64` into locals reused all tick (research R3).
- [ ] T011 [US1] Refactor `ProcessSpatialReconciliationAsync` to surface the per-city metrics the post site needs — return (or set into locals) `vehicles_processed` (= moved+unchanged+stationary+stale), `tones_emitted` (= `crossingRecords.Count`), and `feed_freshness_seconds` (= (observationUtc − feed.Header.Timestamp) seconds, null if absent) — WITHOUT re-fetching the feed (research R5/R6). Remove the in-method `CycleEventArgs` post (lines ~529-562) and the per-vehicle `SnapEventArgs`/`LerpEventArgs` posts (lines ~379-397, ~447-467).
- [ ] T012 [US1] In the per-city `try/catch` (Worker.cs lines ~55-75), emit exactly one PerCityCycle `TelemetryEvent` per city per tick on EVERY path: wrap so normal, `continue` (route index not ready, lines ~61-65), and `catch` (lines ~71-74) all post once. Set `health_ok` per the data-model state table (false on exception/not-ready, true otherwise incl. empty feed); populate `city_name`, memory (from T010) and the four cache sizes always; populate `time_taken_seconds`/`vehicles_processed`/`tones_emitted`/`feed_freshness_seconds` only on the normal path (0/null on failure). Guard with `if (city.EmitsTelemetry)`. (Depends on T010, T011)
- [ ] T013 [US1] Read the four per-city cache sizes for the PerCityCycle row: `_vehicleStateCaches[city].Count`, `_crossingBaselines[city].Count`, `_routeIndex[city].Count`, `_routeTriggerPoints[city].Count` (research R4) — safe/cheap even on failure paths (do not depend on this tick's feed). Confirm build is now GREEN. (Depends on T012)

**Checkpoint**: Worker compiles; one PerCityCycle row per city per tick on all paths. US1 independently testable via §4 of quickstart (health paths).

---

## Phase 4: User Story 2 - Inspect a single worker-summary per tick (Priority: P2)

**Goal**: Emit exactly one FullCycle row per tick, aggregating across the tick's cities (sum tones/vehicles/cache sizes; reuse the per-tick memory sample), with cities_processed_count/csv.

**Independent Test**: Run across multiple cities; confirm exactly one FullCycle row per tick regardless of city count, totals = sum of that tick's PerCityCycle values, memory identical across the tick's rows.

### Implementation for User Story 2

- [ ] T014 [US2] In `Worker.cs ExecuteAsync`, accumulate per-tick aggregates across the `foreach` (sum of `tones_emitted`, `vehicles_processed`, and the four cache sizes; collect processed city names + count; track overall `health_ok`; total `time_taken_seconds`). Use locals scoped to the `while` body, reset each tick. (Depends on T012, T013)
- [ ] T015 [US2] Add a NEW post-loop block AFTER the `foreach` closes (before the `while` body ends — there is no code there today) that posts exactly one FullCycle `TelemetryEvent`: `event_type="FullCycle"`, `cities_processed_count`/`cities_processed_csv` set, summed metric+cache fields, the per-tick memory sample (from T010) reused verbatim (NOT summed), `city_name`/`feed_freshness_seconds` left null. Guard so it emits when at least one telemetry-emitting city ran (research R2). (Depends on T014)
- [ ] T016 [US2] Extend `TelemetryEventSchemaTests` (from T007) if needed so the FullCycle-row assertions cover summed-field population and null city-only columns (may already be covered by T007's two-row test — confirm and extend only if a gap). (Depends on T007, T015)

**Checkpoint**: One FullCycle row per tick with correct sums + reused memory; US1 and US2 both work.

---

## Phase 5: User Story 4 - Query one unified telemetry dataset by event type (Priority: P1)

**Goal**: The Go MCP validator recognizes only `telemetry`, accepts all 17 columns (with `event_type` as the scoping filter) and rejects retired columns.

**Independent Test**: `go test ./internal/validate/...` passes the accept/reject vectors; a live query with `event_type='PerCityCycle'` and `='FullCycle'` returns the right rows with nulls on non-applicable columns.

### Tests for User Story 4 ⚠️

- [ ] T017 [P] [US4] Update `tools/telemetry-mcp/internal/validate/validate_test.go` with the accept/reject vectors from `contracts/query-validator.md` (accept: event_type filters, health_ok bool, new numeric columns, date timestamp; reject: `snap`/`lerp`/`cycle` datasets, retired columns `snap_distance_km`/`pos_delta_km`/`last_update_cache_size`, bool-as-number, unquoted string, dotted identifier, SQL keyword, `;`).

### Implementation for User Story 4

- [ ] T018 [US4] In `tools/telemetry-mcp/internal/validate/validate.go`, replace `validDatasets` with `{"telemetry": true}` and collapse `datasetColumns` to one `"telemetry"` map holding the 17 columns with kinds per `contracts/query-validator.md` (event_type/event_id/city_name/cities_processed_csv = string; observation_utc = timestamp; health_ok = bool; the rest numeric). (Depends on nothing in C#; can run parallel to Phases 3-4)
- [ ] T019 [US4] Update the three error strings in `validate.go` that name `snap, lerp, cycle` (ValidateDataset ~line 138, Filter fallback ~line 161) to name `telemetry`; leave tokenizer/parser/kind-checks/forbidden lists UNCHANGED (FR-028). Run `go test ./internal/validate/...` green. (Depends on T017, T018)

**Checkpoint**: Validator serves the single `telemetry` dataset; event_type scoping works; retired columns rejected.

---

## Phase 6: User Story 3 - Add a new telemetry field with a near-one-line change (Priority: P2)

**Goal**: Verify the foundational POCO+attribute design delivers the "one-place add" property (this story is satisfied by Phase 2's design, not by separate production code).

**Independent Test**: Add a throwaway nullable property with a `[ParquetColumn(Name=…)]`, confirm it appears as a column in output with no edits to any separate schema/const/switch, then revert.

- [ ] T020 [US3] Verification task (no shipped change): temporarily add a nullable `[ParquetColumn(Name = "scratch_probe")] public int? ScratchProbe { get; init; }` to `TelemetryEvent`, run `TelemetryEventSchemaTests`-style serialize, confirm `scratch_probe` appears with NO other recording-side file edited (no `TelemetryColumns`, no `switch`, no hand-built schema — SC-003/FR-006), then REVERT the property. Record the outcome in the PR description.

**Checkpoint**: FR-006/SC-003 demonstrably satisfied.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Docs, pipeline-test re-pointing, and the full manual/query validation.

- [ ] T021 [P] Re-point `ChannelLoadSheddingTests.cs` in the Tests project to construct `TelemetryEvent` instances (was Snap/Cycle event args) and assert drop/flush behavior against the single buffer is preserved (FR-007/FR-031).
- [ ] T022 [P] Re-point `FailureIsolationTests.cs` similarly to the single-buffer/single-path service (FR-031).
- [ ] T023 [P] Rewrite `.claude/skills/mj-data-explorer/references/telemetry-schema.md`: one `telemetry` table with the 17-column contract + the `event_type` discriminator explanation (which columns are null on which type); bump `last verified` date; update the "source of truth" pointer to this feature's contracts.
- [ ] T024 [P] Update `.claude/skills/mj-data-explorer/references/telemetry-query-guide.md`: `dataset = "telemetry"` (single), new accept/reject examples, the `event_type = 'PerCityCycle'|'FullCycle'` scoping pattern, note fields null on the non-applicable type; bump `last verified` date.
- [ ] T025 Run the full test suite: `dotnet test` (Worker.Tests) + `go test ./internal/validate/...` — all green (quickstart §1-§2).
- [ ] T026 Manual end-to-end per quickstart §4-§5: run the worker against MARTA for one flush interval; confirm ONE `telemetry/dt=…/part-*.parquet` per flush; force all four health paths; query via `mcp__telemetry-query-bridge__query_telemetry` with `dataset="telemetry"` and `event_type` filters for both shapes; confirm nulls on non-applicable columns and summed FullCycle fields.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — baseline check.
- **Foundational (Phase 2)**: after Setup. **BLOCKS all C# stories** (US1, US2, US3). US4 (Go) does not depend on it and can run in parallel.
- **US1 (Phase 3)**: after Phase 2. Completes the Worker compile — de facto prerequisite for US2.
- **US2 (Phase 4)**: after US1 (shares the ExecuteAsync tick body and per-tick memory sample from T010, and aggregates the per-city values US1 produces).
- **US4 (Phase 5)**: after Setup; **independent of Phase 2-4** (pure Go). Can start immediately in parallel.
- **US3 (Phase 6)**: after Phase 2 (needs `TelemetryEvent` to exist). Cheap verification.
- **Polish (Phase 7)**: after US1, US2, US4 complete (tests re-point + docs + e2e).

### Within stories

- Tests (T007-T009, T017) authored alongside/before their implementation; T007's schema test can fail-first before T012/T015 populate rows.
- Worker sequence is strict-serial (same file): T010 → T011 → T012 → T013 (US1) → T014 → T015 (US2). These are NOT [P] with each other.

### Parallel Opportunities

- **US4 (T017-T019, Go)** can run fully in parallel with the entire C# track (Phases 2-4) — different language, different files.
- T002 [P] and (after it) T005 [P] can overlap; T007/T008/T009 [P] are independent test-file edits.
- Polish T021/T022/T023/T024 are all [P] (distinct files).

---

## Parallel Example

```bash
# After T001 (Setup baseline), two independent tracks start at once:

# Track A (C#): T002 → T003 → T004 → (T005[P],T006) → US1 (T007..T013) → US2 (T014..T016)
# Track B (Go): T017 → T018 → T019     # entirely independent of Track A

# Within Polish, launch together:
Task: "Re-point ChannelLoadSheddingTests.cs (T021)"
Task: "Re-point FailureIsolationTests.cs (T022)"
Task: "Rewrite telemetry-schema.md (T023)"
Task: "Update telemetry-query-guide.md (T024)"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational (the shared record + service rewrite) → Phase 3 US1.
2. **STOP and VALIDATE**: force the four health paths; confirm a PerCityCycle row every tick. This alone delivers the core value (failed/skipped ticks stop being invisible).

### Incremental Delivery

1. Foundational + US1 → per-city health rows (MVP).
2. + US2 → the one-row-per-tick worker summary.
3. + US4 (parallelizable throughout) → query the unified dataset.
4. + US3 verification + Polish (docs, test re-point, e2e).

### Notes

- [P] = different files, no incomplete-task dependency.
- The four Worker.cs edits (T010-T015) touch one file and MUST be serial.
- Never re-fetch feed data to populate telemetry (Principle VII / research R5).
- Commit per task or logical group; the after_tasks hook offers an auto-commit.
