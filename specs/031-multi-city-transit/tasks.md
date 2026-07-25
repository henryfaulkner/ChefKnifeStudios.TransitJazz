---
description: "Task list for Multi-City Transit Targets"
---

# Tasks: Multi-City Transit Targets

**Input**: Design documents from `specs/031-multi-city-transit/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Design source**: `docs/MULTI_CITY_TRANSIT_DESIGN.md` (authoritative Q1â€“Q8)

**Tests**: Included â€” the contracts define testable invariants (INV-*) and existing `*.Tests`
projects already cover the cache/hub/loop. Test tasks target those invariants.

**Organization**: Grouped by user story. Note the design's two-slice plan:
- **Slice 1** (Foundational + US1 + US3): refactor MARTA onto the `ITransitCity` pattern with
  per-city plumbing, **zero behavior change** for Atlanta.
- **Slice 2** (US2): add WMATA as **config only**.

US1 (per-city viewing) and US3 (one isolated bespoke class = `MartaCity`) are delivered together in
Slice 1 because per-city scoping cannot exist without `ITransitCity` + the first concrete city. US2
proves the config-only path on top.

**Namespace note**: source root is `ChefKnifeStudios.MartaJazz` (not `.TransitJazz`). All paths below
are real.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (Setup/Foundational/Polish have no story label)

## Path Conventions

Web app, existing 11-project solution. No new projects; one new folder `Cities/` under the worker.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Additive shared contract changes that everything else builds on.

- [X] T001 [P] Add `string? City = null` to `RouteShapeProperties` record in `src/ChefKnifeStudios.MartaJazz.Shared/GtfsData/RouteShapeFeature.cs` (additive, nullable for back-compat)
- [X] T002 Change `ITransitHubPublisher.PublishBatchAsync` signature to `(string city, List<EventEnvelope> batch, CancellationToken ct = default)` in `src/ChefKnifeStudios.MartaJazz.Shared/ITransitHubPublisher.cs`

**Checkpoint**: Shared contracts updated; solution will not compile until publisher callers (T012) and worker (T010) are updated â€” expected mid-refactor.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The anti-drift mechanism + per-city keying that ALL user stories depend on.
This is the load-bearing core of Slice 1.

**âš ï¸ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Create `Cities/ITransitCity.cs` (interface: `string Name`, `Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct)`, `bool EmitsTelemetry`) in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/` per `contracts/itransitcity.md`
- [X] T004 [P] Create `Cities/CityConfig.cs` binding model (`Name`, `GtfsRtUrls[]`, `StaticZipUrls[]`, `RailRealtime{BaseUrl,Enabled}?`, `RailRouteIdMap?`, `ApiKeyEnvVar?`, `EmitsTelemetry`) in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/` per `contracts/city-config.md`
- [X] T005 Key `ILastBatchCache` by city â€” change to `Current(string city)` / `Set(string city, IReadOnlyList<EventEnvelope> batch)` backed by `Dictionary<string, LastBatchCache>` (preserve per-vehicle upsert + stale-skip logic) in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/SignalR/ILastBatchCache.cs`
- [X] T006 Update `WorkerTransitHub.PublishBatch` to `(string city, List<EventEnvelope> batch)` â†’ `_lastBatchCache.Set(city, batch)` â†’ `Clients.Group(city).SendAsync("ReceiveBatch", batch)` in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/SignalR/WorkerTransitHub.cs` per `contracts/signalr-transport.md`
- [X] T007 Add `JoinCity(string city)` to `TransitHub` â†’ `Groups.AddToGroupAsync(...)` + immediate replay of `_lastBatchCache.Current(city)` to `Clients.Caller`; inject `ILastBatchCache` into `TransitHub` in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/SignalR/TransitHub.cs`
- [X] T008 Make worker route index per-city â€” change `_routeIndex` to `Dictionary<string city, IReadOnlyDictionary<string routeId, RoutePoint[]>>`, update `BuildRouteIndex`, `InitializeRouteIndexAsync`, `RefreshRouteIndexAsync` to partition the shapes response by `RouteShapeProperties.City` (single HTTP call, no N round-trips) in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- [X] T009 Make worker vehicle-state cache per-city (key by `(city, vehicleId)` or per-city dictionary) so identical vehicle IDs across cities never collide; update prune logic in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`

**Checkpoint**: City contract + per-city keying for index, cache, and transport exist. Server-side fan-out is group-scoped. Ready for the loop + concrete city.

---

## Phase 3: User Story 1 - View a city's live transit by visiting its link (Priority: P1) ðŸŽ¯ MVP

**Goal**: A viewer is scoped to exactly one city (default Atlanta) and sees only that city's
vehicles/routes/audio; new joiners see current vehicles promptly.

**Independent Test**: Open `?city=marta` and a second city in two tabs â€” neither shows the other's
vehicles; no-param load shows Atlanta; joining mid-stream shows vehicles within seconds.

### Tests for User Story 1

- [X] T010 [P] [US1] Per-city cache isolation test (same `vehicleId` under two cities never collides; `Current(city)` returns only that city) in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/LastBatchCacheTests.cs` (INV-T3 / FR-011)
- [X] T011 [P] [US1] `WorkerTransitHub` routing test â€” `PublishBatch(city, batch)` calls `Clients.Group(city)` and `Set(city, â€¦)`, never `Clients.All` in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/WorkerTransitHubTests.cs` (INV-T1 / SC-001)

### Implementation for User Story 1

- [X] T012 [US1] Thread `city` through the publisher â€” `SignalRHubPublisher.PublishBatchAsync(string city, â€¦)` calls `InvokeAsync("PublishBatch", city, batch, ct)` in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/SignalRHubPublisher.cs` (depends on T002, T006)
- [X] T013 [US1] Convert worker to loop `IEnumerable<ITransitCity>` on the 10s tick with per-city try/catch (log `{City}` on failure); per city: `FetchVehiclesAsync` â†’ `_routeIndex[city.Name]` â†’ reconcile â†’ `PublishBatchAsync(city.Name, batch)`; loop NEVER branches on `city.Name` in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs` (INV-1, INV-2; depends on T003, T008, T009, T012)
- [X] T014 [US1] Add `?city=` to the route-shapes endpoint â€” `GetAllRouteShapes`/`GetAllRoutes`/`GetRouteShape` filter KV keys by `{city}:` prefix (default `marta`); retain `ReadyKey` filter in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/EndpointGroups/GtfsEndpoints.cs` per `contracts/shapes-endpoint.md` (INV-S1)
- [X] T015 [US1] Client reads city from URL/query (default `marta`); call `InvokeAsync("JoinCity", city)` after `StartAsync` and re-invoke on `Reconnected` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/SignalRNotificationService.cs` (FR-003, INV-T2/T4; depends on T007)
- [X] T016 [US1] Client shape fetch appends `?city={city}`; surface city to `RouteFilterViewModel` and consume `RouteShapeProperties.City` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs` (depends on T001, T014)
- [X] T017 [US1] Unknown/unconfigured city falls back to `marta` (client default + endpoint empty-result handling never blanks the map) â€” verify across `SignalRNotificationService` and `RouteFilterViewModel` (FR-004)

**Checkpoint**: Per-city scoping works end-to-end with one city registered. Cross-city isolation, default Atlanta, and mid-stream replay all functional.

---

## Phase 4: User Story 3 - Isolate a bespoke-feed city in one place (Priority: P3)

**Goal**: MARTA's non-standard JSON rail feed is sealed inside ONE isolated `ITransitCity` class;
the loop/hub/client/other cities are untouched. This is also the MARTA-unchanged gate.

**Independent Test**: Atlanta renders identically to pre-refactor (routes, vehicles, audio,
telemetry); the bespoke handling lives in exactly one new `MartaCity.cs`.

> Sequenced after US1 because `MartaCity` is the concrete city the US1 loop iterates. In practice
> T018 is built alongside T013 to make Slice 1 compile/run; it is listed separately to keep the
> US3 isolation guarantee explicit and independently verifiable.

### Implementation for User Story 3

- [X] T018 [US3] Create `Cities/MartaCity.cs : ITransitCity` (`Name="marta"`, `EmitsTelemetry=true`) whose `FetchVehiclesAsync` fetches the MARTA bus protobuf + composes the `IRailRealtimeAdapter` JSON rail call internally and returns a merged normalized `FeedMessage`; carries the rail `vehicleId` set for `TransitMode.Rail` tagging in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/` (Q7)
- [X] T019 [US3] Retire the global `IRailRealtimeAdapter` + hardcoded `_gtfsRtUrl` singleton wiring; `RailRealtimeAdapter` now composed inside `MartaCity` (class itself unchanged) â€” update `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Program.cs` and remove the field from `Worker.cs`
- [X] T020 [US3] Gate `PostEvent` (snap/lerp/cycle) on `city.EmitsTelemetry` â€” passed into reconciliation, never a city-name check; MARTA emits, others do not in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs` (INV-3 / FR-015 / Q6)
- [X] T021 [P] [US3] Telemetry-gate + loop fault-isolation tests (MARTA posts events; a city with `EmitsTelemetry=false` posts none; a throwing city does not stop others) in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/` (INV-2, INV-3)

**Checkpoint**: Slice 1 complete. MARTA byte-identical end-to-end; all city-specific logic for Atlanta lives in `MartaCity.cs`.

---

## Phase 5: User Story 2 - Add a standard-feed city with configuration only (Priority: P2)

**Goal**: A standard-GTFS-RT city (WMATA) is added with a `Cities:` config entry + a secret â€”
**zero new application code** â€” via the generic `GtfsRtCity`.

**Independent Test**: WMATA viewable at `?city=wmata`; the add-WMATA commit contains only config (+ a
secret), no `.cs`; no agency key in committed files.

### Implementation for User Story 2

- [X] T022 [US2] Create the generic `Cities/GtfsRtCity.cs : ITransitCity` â€” fetches one-or-more `GtfsRtUrls` protobufs, applies optional `RailRouteIdMap` (remap `route_id`), merges, returns normalized `FeedMessage`; `EmitsTelemetry` from config; reads API key from `ApiKeyEnvVar` env var (never committed) in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/` per `contracts/city-config.md` (Q8)
- [X] T023 [US2] City registry in `Program.cs` â€” bind `Cities:` array; for each entry use a registered named impl (`MartaCity` for `marta`) else `GtfsRtCity`; register `IEnumerable<ITransitCity>` for the Worker loop in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Program.cs` (depends on T018, T022)
- [X] T024 [US2] `GtfsStaticLoader` loops the city registry â€” load each city's `StaticZipUrls` (multi-zip merged), seed KV under `{city}:{routeId}`, set `RouteShapeProperties.City` on every shape in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs` (Q4; depends on T001)
- [X] T025 [US2] Replace flat `Marta:` block with a `Cities:` array (marta + wmata entries) in worker `appsettings.json` and `appsettings.Development.json`; WMATA `ApiKeyEnvVar: "WMATA_API_KEY"` only â€” NO key value committed in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/` per `contracts/city-config.md` (FR-014 / SC-008)
- [X] T026 [P] [US2] City-scoped shapes endpoint test â€” `?city=wmata` returns only `wmata:*`, zero `marta:*`; default returns the MARTA set unchanged in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/` (INV-S1, INV-S3)

**Checkpoint**: Two cities live. Adding WMATA required config + a secret only. Both cities isolated end-to-end.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T027 Run `quickstart.md` verification checklist (#1â€“#9) and record results
- [X] T028 [P] Grep `appsettings*.json` + source for any committed agency API key value; confirm none (SC-008 final gate)
- [X] T029 [P] Confirm deployed container/process count is unchanged with N cities (FR-016 / SC-006) â€” review AppHost/worker registration, no per-city service added
- [X] T030 Update `docs/MULTI_CITY_TRANSIT_DESIGN.md` status line to reflect implementation complete (or note deltas if the build diverged from the doc)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies â€” start immediately.
- **Foundational (Phase 2)**: Depends on Setup â€” BLOCKS all user stories.
- **US1 (Phase 3)**: Depends on Foundational. Delivers the MVP (per-city scoping).
- **US3 (Phase 4)**: Depends on Foundational + US1 loop (T013); `MartaCity` is the concrete city the loop runs. Completes Slice 1.
- **US2 (Phase 5)**: Depends on US1 + US3 (registry needs both `MartaCity` and `GtfsRtCity`; loader needs per-city keying). This is Slice 2.
- **Polish (Phase 6)**: Depends on all stories.

### User Story Dependencies

- **US1 (P1)**: Foundational only â€” the independently testable MVP.
- **US3 (P3)**: Sequenced with US1 (shared Worker.cs edits); isolation guarantee independently verifiable via T021 + MARTA-unchanged check.
- **US2 (P2)**: Builds on the proven Slice-1 pattern; independently verifiable via the config-only diff + `?city=wmata`.

> Priority vs. order note: US2 is P2 but is sequenced **after** US3 because the design's Slice 1
> (US1+US3, MARTA refactor) is the prerequisite that makes US2 a config-only change. P2's *value*
> (cheap city addition) is only realizable once the pattern exists.

### Within Each User Story

- Tests before/with implementation; models (city classes, config) before services (loop, registry); services before endpoints/client wiring.
- Worker.cs is touched by T008, T009, T013, T019, T020 â€” these are **sequential** (same file).
- `Program.cs` (worker) touched by T019, T023 â€” sequential.

### Parallel Opportunities

- T001 âˆ¥ (T002 is on a different file but T012 depends on it).
- T004 âˆ¥ T003 (different files).
- T010 âˆ¥ T011 (different test files).
- T021, T026, T028, T029 marked [P] (independent files).
- Worker.cs and Program.cs edits are NOT parallel with each other within their file.

---

## Parallel Example: User Story 1 tests

```bash
Task: "Per-city cache isolation test in LastBatchCacheTests.cs"
Task: "WorkerTransitHub group-routing test in WorkerTransitHubTests.cs"
```

---

## Implementation Strategy

### MVP First (Slice 1 = US1 + US3)

1. Phase 1 Setup â†’ Phase 2 Foundational â†’ Phase 3 US1 â†’ Phase 4 US3.
2. **STOP and VALIDATE**: Atlanta byte-identical (quickstart #2), cross-city isolation (#1 with a test stub city), mid-stream replay (#7), no collisions (#9).
3. Deploy/demo â€” single city, but on the multi-city pattern.

### Incremental Delivery

1. Slice 1 (US1+US3) â†’ MARTA on the pattern, unchanged â†’ Deploy.
2. Slice 2 (US2) â†’ add WMATA via config only â†’ Deploy. Adding further cities is now config + secret.

---

## Notes

- [P] = different files, no incomplete dependencies.
- The single biggest risk is regressing MARTA during the Worker.cs refactor â€” T013/T018/T019/T020 must keep V2 reconciliation, stale handling, and telemetry identical (FR-017/SC-004).
- `EventEnvelope` stays city-free; city is a routing parameter only (Q2).
- No new deployed infrastructure (FR-016) â€” one worker process iterates all cities.

