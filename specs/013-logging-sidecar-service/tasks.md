---
description: "Task list for Logging Sidecar Service"
---

# Tasks: Logging Sidecar Service

**Input**: Design documents from `specs/013-logging-sidecar-service/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/parquet-schemas.md, contracts/blob-layout.md, quickstart.md

**Tests**: INCLUDED. The spec's Independent Tests and quickstart explicitly call for automated verification (parquet round-trip, partition path, load-shedding, failure isolation), and plan.md provisions a dedicated test project.

**Organization**: Tasks are grouped by user story. The decoupling pipeline (notification bus â†’ bounded channel â†’ hosted consumer â†’ parquet sink â†’ blob upload) is **shared foundation** (Phase 2). Each user story then adds its own event schema, parquet schema, and the `Worker.cs` capture point for that dataset.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 = Cycle telemetry (P1/MVP), US2 = Snap telemetry (P2), US3 = Lerp telemetry (P3)
- All paths are relative to repo root. Production code lives in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/` (abbreviated **WORKER/** below); tests in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/` (**TESTS/**).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependencies, folder, config, and test project so all later work compiles.

- [x] T001 Add `Parquet.Net` (5.*) and `Azure.Storage.Blobs` (12.*) PackageReferences to `WORKER/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.csproj` (Azure.Identity already present); run `dotnet restore`.
- [x] T002 Create the `WORKER/Logging/` directory (all sidecar production files land here, per FR-013).
- [x] T003 [P] Create xunit test project `TESTS/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests.csproj` (net10.0, references the worker project + `Parquet.Net`), and add it to `ChefKnifeStudios.MartaJazz.sln`.
- [x] T004 [P] Create `WORKER/Logging/LoggingOptions.cs` binding `Logging:Telemetry:*` (BlobServiceUri, Container=`telemetry`, FlushIntervalSeconds=300, ChannelCapacity=10000, Enabled=true) per contracts/blob-layout.md.
- [x] T005 [P] Add a `Logging:Telemetry` block (non-secret keys only; no account key/connection string) to `WORKER/appsettings.Development.json` for local runs, per quickstart.md Â§2.

**Checkpoint**: Project builds with new deps; config binds; empty `Logging/` folder ready.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared, decoupled pipeline every user story rides on â€” notification bus, bounded channel, hosted consumer, parquet sink, blob upload, and the `LogEventArgs` base + frozen column constants. **No user story can flush telemetry until this is done.**

**âš ï¸ CRITICAL**: Phases 3â€“5 all depend on this phase.

- [x] T006 [P] Create `WORKER/Logging/IEventNotificationService.cs` with `IEventArgs`, `EventReceivedEventHandler`, `IEventNotificationService`, and `EventNotificationService` â€” a server-side mirror of `src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/EventNotificationService.cs` (FR-001, FR-014).
- [x] T007 [P] Create `WORKER/Logging/LogEventArgs.cs` â€” abstract base `: IEventArgs` carrying `CycleId` (string), per data-model.md (FR-009).
- [x] T008 [P] Create `WORKER/Logging/TelemetryColumns.cs` â€” `const string` names for every column in contracts/parquet-schemas.md (snake_case), the frozen downstream contract (research R4).
- [x] T009 [P] Create `WORKER/Logging/ILoggingService.cs` â€” sink abstraction: `void Accumulate(IEventArgs e)` and `Task FlushAsync(CancellationToken)`, plus health accessors (dropped/persist-failure counters), per data-model.md lifecycle.
- [x] T010 Create `WORKER/Logging/ParquetLoggingService.cs` implementing `ILoggingService`: per-dataset in-memory row buffers; `FlushAsync` serializes each non-empty dataset to parquet via Parquet.Net (Snappy) into a `MemoryStream` and uploads to `{container}/{dataset}/dt={utc:yyyy-MM-dd}/part-{utc:yyyyMMddTHHmmssfffZ}-{shortguid}.parquet` with `BlobContainerClient` + `DefaultAzureCredential`, `overwrite:false`; catch/count/log persist failures and swallow them (FR-004a, FR-004b, FR-004c, FR-004d, FR-010; contracts/blob-layout.md). Schema-building for each dataset is added by its user-story task.
- [x] T011 Create `WORKER/Logging/LogEventWorker.cs` as `IHostedService`: owns a bounded `Channel<IEventArgs>` (capacity from options, `BoundedChannelFullMode.DropWrite`); subscribes `EventReceived += HandleEventReceived` in `StartAsync` and starts the consumer; `TryWrite` with drop-counting on overflow (FR-002, FR-003, SC-004); consumer `await foreach` accumulates via `ILoggingService`; `PeriodicTimer` flush every `FlushIntervalSeconds`; `StopAsync` completes the writer, drains best-effort, does one final flush, unsubscribes â€” no hang (FR-004b, FR-011, SC-006). Avoid the source-spec bugs (wrong field name in `Dispose`, consumer never started).
- [x] T012 Register the pipeline in `WORKER/Program.cs`: `AddSingleton<IEventNotificationService, EventNotificationService>()`, `AddSingleton<ILoggingService, ParquetLoggingService>()`, `Configure<LoggingOptions>(config.GetSection("Logging:Telemetry"))`, `AddHostedService<LogEventWorker>()`; honor `Enabled=false` as a no-op kill switch.
- [x] T013 [US-shared] Inject `IEventNotificationService` into `WORKER/Worker.cs` constructor and generate one `CycleId` (string Guid/ULID) at the top of each `ProcessSpatialReconciliationAsync` cycle, threaded to all posted events (research R6, FR-009).

### Foundational tests

- [x] T014 [P] `TESTS/ChannelLoadSheddingTests.cs` â€” bounded channel at low capacity drops newest on overflow, increments the dropped counter, never blocks the producer (FR-003, SC-004).
- [x] T015 [P] `TESTS/FailureIsolationTests.cs` â€” a sink whose upload throws causes `FlushAsync` to swallow, increment `sidecar_persist_failures`, and not rethrow; consumer keeps running (FR-010, SC-003).
- [x] T016 [P] `TESTS/PartitionPathTests.cs` â€” path derivation yields `{dataset}/dt=YYYY-MM-DD/part-â€¦parquet` with UTC date and unique part names across two calls in the same millisecond window (FR-004c, research R5).

**Checkpoint**: Pipeline runs end-to-end with a stub/in-memory sink; events post without touching the hot path; flush timer fires. No dataset schemas wired yet.

---

## Phase 3: User Story 1 â€” Cycle health telemetry (Priority: P1) ðŸŽ¯ MVP

**Goal**: Each completed cycle durably persists exactly one Cycle parquet row (counts, timing, duplicate-feed, cache sizes, sidecar self-health) to `cycle/dt=â€¦/`.

**Independent Test**: Run the worker for several cycles; confirm one row per cycle appears in `cycle/dt=<today UTC>/*.parquet` with counts matching the worker's `Spatial reconciliation:` log line, and that a forced-offline destination does not error the loop.

### Implementation for User Story 1

- [x] T017 [P] [US1] Create `WORKER/Logging/CycleEventArgs.cs : LogEventArgs` with all Cycle fields incl. sidecar self-health (buffer occupancy, dropped, persist-failures), per data-model.md Cycle entity (FR-005, FR-012).
- [x] T018 [US1] In `WORKER/Logging/ParquetLoggingService.cs`, add the **Cycle** `ParquetSchema` + rowâ†’column mapping exactly matching contracts/parquet-schemas.md (`cycle` dataset), and route `CycleEventArgs` to the cycle buffer (FR-004d, FR-008 names-as-strings n/a here).
- [x] T019 [US1] In `WORKER/Worker.cs`, at the cycle epilogue (after the existing `logger.LogInformation("Spatial reconciliation: â€¦")`), post a `CycleEventArgs` built from `movedCount/unchangedCount/stationaryCount/staleCount/skippedNoRouteId/skippedUnknownRoute`, cycle start/end + duration, `feedTs`, `feedIsDuplicate`, `_lastUpdateCache.Count`, `_vehicleStateCache.Count`, and the sidecar health counters read from `LogEventWorker`/sink (research R6, FR-005, FR-012). `buses_processed` = sum of outcome counts.
- [x] T020 [US1] Ensure sidecar self-health is also emitted via `ILogger` (not only parquet) so OTEL/Log Analytics observes drops/failures (plan Constitution IV WATCH mitigation).

### Tests for User Story 1

- [x] T021 [P] [US1] `TESTS/CycleParquetSchemaTests.cs` â€” write a `CycleEventArgs` batch, read the parquet back, assert column names/types/order match contracts/parquet-schemas.md `cycle` and values round-trip (incl. nullable `feed_header_ts`).
- [x] T022 [US1] Run quickstart.md verification steps 1, 4, 5, 6, 7 against a real/dev blob to confirm Cycle rows land, are queryable via the tool, survive destination outage, shed load, and shut down cleanly (SC-001/002/003/004/005/006).

**Checkpoint**: MVP â€” the full sidecar works end-to-end for Cycle telemetry. Stop and validate before US2.

---

## Phase 4: User Story 2 â€” Snap decisions (Priority: P2)

**Goal**: Each per-vehicle snap decision persists a Snap parquet row (route/bus data, snap position+distance+index, outcome by name) to `snap/dt=â€¦/`.

**Independent Test**: Process a feed with known vehicles; confirm `snap/dt=<today>/*.parquet` has one row per snapped vehicle with `snap_outcome` a readable name and correct snapped lat/lon/index.

### Implementation for User Story 2

- [x] T023 [P] [US2] Create `WORKER/Logging/SnapEventArgs.cs : LogEventArgs` and the `SnapDecision` enum (`FirstObservation|Moved|Unchanged|Stationary|Stale`), per data-model.md (FR-006, FR-008).
- [x] T024 [US2] In `WORKER/Logging/ParquetLoggingService.cs`, add the **Snap** `ParquetSchema` + mapping per contracts/parquet-schemas.md (`snap`), writing `snap_outcome` as `nameof`/enum name (FR-008), and route `SnapEventArgs` to the snap buffer (FR-004d).
- [x] T025 [US2] In `WORKER/Worker.cs`, post a `SnapEventArgs` at each per-vehicle snap point (where `BatchDebugRecord`/`debugRecord` is already built), mapping raw/snapped lat-lon, `snapValue.DistanceKm`, `snapValue.Index`, `routePoints.Length`, speed, bearing, `isStale`, the outcome string, and the cycle's `CycleId` (research R6, FR-006, FR-009).

### Tests for User Story 2

- [x] T026 [P] [US2] `TESTS/SnapParquetSchemaTests.cs` â€” round-trip a `SnapEventArgs` batch; assert columns match contracts `snap` and `snap_outcome` is the enum name.

**Checkpoint**: Snap telemetry queryable independently; Cycle (US1) still works.

---

## Phase 5: User Story 3 â€” Lerp deltas (Priority: P3)

**Goal**: Each per-vehicle delta computation persists a Lerp parquet row (prior route/bus data + position/speed/bearing/time deltas) to `lerp/dt=â€¦/`.

**Independent Test**: Process consecutive observations of a vehicle; confirm `lerp/dt=<today>/*.parquet` rows carry prior state and the four deltas tagged with `cycle_id`.

### Implementation for User Story 3

- [x] T027 [P] [US3] Create `WORKER/Logging/LerpEventArgs.cs : LogEventArgs` per data-model.md Lerp entity (FR-007, FR-009).
- [x] T028 [US3] In `WORKER/Logging/ParquetLoggingService.cs`, add the **Lerp** `ParquetSchema` + mapping per contracts/parquet-schemas.md (`lerp`), and route `LerpEventArgs` to the lerp buffer (FR-004d).
- [x] T029 [US3] In `WORKER/Worker.cs`, in the prior-observation branch, post a `LerpEventArgs` from `prior` (`VehicleState`) + current values: `prior_*` fields, `pos_delta_km` (`DeltaFromPriorSnapKm`), `speed_delta`/`bearing_delta` (current âˆ’ prior), `time_delta_sec` (`SecondsSincePriorObservation`), and `CycleId` (research R6, FR-007).

### Tests for User Story 3

- [x] T030 [P] [US3] `TESTS/LerpParquetSchemaTests.cs` â€” round-trip a `LerpEventArgs` batch; assert columns match contracts `lerp`, including nullable deltas.

**Checkpoint**: All three datasets land independently; correlatable via `cycle_id`.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T031 [P] Add XML doc comments to all `Logging/` public types matching the repo style (see `BatchDebugRecord.cs`/`VehicleState.cs`).
- [x] T032 Verify hot-path isolation: micro-benchmark or instrument `ProcessSpatialReconciliationAsync` with sidecar enabled vs. `Enabled=false`; confirm median/p95 cycle time within run-to-run variance (SC-001).
- [x] T033 [P] Document the Azure Storage **lifecycle-management (retention)** policy for the `telemetry` container as an infra/Bicep follow-up (research R7, quickstart Â§6) â€” note in plan/PR; no worker code.
- [x] T034 [P] PR note: flag the feature-012 alignment â€” its allow-list `allowedColumns` + `TELEMETRY_DATASET_URI` must be updated to the Snap/Lerp/Cycle schemas in contracts/parquet-schemas.md (research R2; ships with 012, not this branch).
- [x] T035 Run full `dotnet test TESTS/` and a final `dotnet build` of the solution; confirm green.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **User Stories (Phase 3â€“5)**: each depends only on Foundational. They touch a shared file (`ParquetLoggingService.cs` and `Worker.cs`) so the dataset-mapping/capture tasks within them are **sequential w.r.t. each other** on those two files, but each story is independently *testable and deliverable*.
- **Polish (Phase 6)**: after the desired stories are complete.

### User Story Dependencies

- **US1 (P1)**: after Phase 2. MVP. No dependency on US2/US3.
- **US2 (P2)**: after Phase 2. Independent of US1 (shares pipeline + the two shared files only).
- **US3 (P3)**: after Phase 2. Independent of US1/US2.

### Within Each User Story

- EventArgs model (`[P]`) â†’ parquet schema/mapping in `ParquetLoggingService.cs` â†’ `Worker.cs` capture point â†’ schema round-trip test.
- The `ParquetLoggingService.cs` and `Worker.cs` edits across US1/US2/US3 are on the **same files** â€” do not run those specific tasks in parallel across stories.

### Parallel Opportunities

- Phase 1: T003, T004, T005 in parallel (T001/T002 first).
- Phase 2: T006, T007, T008, T009 in parallel (separate new files); then T010 â†’ T011 â†’ T012 â†’ T013 (T010â€“T013 are sequential: T011 uses T009/T010, T012 wires all, T013 edits Worker). Foundational tests T014â€“T016 in parallel.
- The per-story EventArgs files (T017, T023, T027) and the schema-round-trip tests (T021, T026, T030) are each `[P]` (distinct new files).
- Polish: T031, T033, T034 in parallel.

---

## Parallel Example: Phase 2 Foundational

```bash
# New, independent files â€” launch together:
Task: "Create WORKER/Logging/IEventNotificationService.cs"      # T006
Task: "Create WORKER/Logging/LogEventArgs.cs"                    # T007
Task: "Create WORKER/Logging/TelemetryColumns.cs"               # T008
Task: "Create WORKER/Logging/ILoggingService.cs"                # T009
# Then sequential: T010 (sink) â†’ T011 (hosted worker) â†’ T012 (DI) â†’ T013 (Worker CycleId)
# Then parallel foundational tests: T014, T015, T016
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup â†’ 2. Phase 2 Foundational (the whole pipeline) â†’ 3. Phase 3 US1 (Cycle) â†’ **STOP & VALIDATE** via quickstart steps 1/4/5/6/7. This alone delivers production health telemetry queryable by the tool.

### Incremental Delivery

- Foundation + US1 = MVP (Cycle health). Then add US2 (Snap) â†’ validate. Then US3 (Lerp) â†’ validate. Each adds a dataset without changing the others.

---

## Notes

- [P] = different files, no incomplete dependencies. The two shared files (`ParquetLoggingService.cs`, `Worker.cs`) serialize the per-story mapping/capture tasks â€” that's expected and called out above.
- Security: `DefaultAzureCredential` only; **never** commit an account key/connection string (feature-012 FR-020 gate).
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.

