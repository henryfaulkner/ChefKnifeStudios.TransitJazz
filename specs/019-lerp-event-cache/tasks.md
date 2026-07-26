# Tasks: Last Lerp Event Cache

**Input**: Design documents from `/specs/019-lerp-event-cache/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Pure unit tests ARE included (user-requested). Integration tests are explicitly out of scope; endpoint HTTP and client load-path behavior are verified via `quickstart.md`.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths included in each description

## Path Conventions

Web application (constitution three-deployable layout). Server code under `src/Server/...WebAPI`, shared constants under `src/ChefKnifeStudios.TransitJazz.Shared`, client under `src/Client/...`. Tests in the new `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` project.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolding for this feature's new test project.

- [x] T001 Create the WebAPI unit-test project `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj` per `contracts/tests.md` (xUnit; `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`; `net10.0`; `Nullable` + `ImplicitUsings` enabled; `IsPackable=false`) with project references to `Server.WebAPI` and `Shared`.
- [x] T002 Add `ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` to `ChefKnifeStudios.TransitJazz.sln` (`dotnet sln add ...`) and confirm `dotnet build` succeeds.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared route constant + the in-memory cache + its DI registration + the write-path hook + the read endpoint. Every user story consumes these, so they MUST land first.

**⚠️ CRITICAL**: No user-story-specific work can begin until this phase is complete.

- [x] T003 Add `Transit` nested class with `public const string GetLastBatch = "/transit/last-batch";` to `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs` (sibling of `Gtfs`/`Test`).
- [x] T004 Create `ILastBatchCache` + `LastBatchCache` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/ILastBatchCache.cs` per `contracts/batch-cache.md`: single-slot atomic-swap (`Volatile.Read`/`Volatile.Write`) over `IReadOnlyList<EventEnvelope>`, seeded to `Array.Empty<EventEnvelope>()`, `Set(null)` ⇒ empty (FR-002, FR-004, FR-007, FR-008).
- [x] T005 Register the cache as a singleton in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`: `builder.Services.AddSingleton<ILastBatchCache, LastBatchCache>();` (place near the SignalR/`ITransitHubPublisher` registrations).
- [x] T006 Hook the write path in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/WorkerTransitHub.cs`: constructor-inject `ILastBatchCache`; in `PublishBatch`, call `_lastBatchCache.Set(batch)` **before** the `SendAsync("ReceiveBatch", batch)` relay (FR-001, FR-002, FR-010).
- [x] T007 Create `TransitEndpoints` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/TransitEndpoints.cs` per `contracts/last-batch-endpoint.md`: `MapTransitEndpoints()` group mapping `GET ApiEndpoints.Transit.GetLastBatch` to a thin handler that returns `Results.Ok(cache.Current)`; `.WithName(...)` + `.Produces<IEnumerable<EventEnvelope>>(StatusCodes.Status200OK)`; anonymous access (FR-003, FR-004).
- [x] T008 Map the new group in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` by appending `.MapTransitEndpoints()` to the existing `app.MapTestEndpoints().MapGtfsEndpoints()` chain.

**Checkpoint**: Server caches and serves the latest batch; HTTP shape verifiable via `quickstart.md` Steps 1–2. User-story work can begin.

---

## Phase 3: User Story 1 - Buses appear immediately on page load (Priority: P1) 🎯 MVP

**Goal**: On fresh map load, the client fetches the cached snapshot once and renders those vehicles immediately, then transitions smoothly to the next live push.

**Independent Test**: With at least one batch published, hard-reload the map — buses render within the load (not after a multi-second wait), and the next live push causes no flicker/duplicate/teleport.

### Tests for User Story 1 ⚠️

> Write these unit tests FIRST; ensure they FAIL before implementing T006's hook behavior.

- [x] T009 [P] [US1] `LastBatchCacheTests` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/LastBatchCacheTests.cs` per `contracts/tests.md`: cold-start empty/non-null; `Set(b1)` ⇒ `Current==b1`; `Set(b1)`→`Set(b2)` ⇒ latest wins; `Set(null)` ⇒ empty non-null; concurrent set/read never null/torn (FR-002, FR-004, FR-008).
- [x] T010 [P] [US1] `WorkerTransitHubTests` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/WorkerTransitHubTests.cs` per `contracts/tests.md`: with `FakeLastBatchCache` + fake `IHubContext<TransitHub>`, `PublishBatch(b)` calls `Set(b)` once AND relays `ReceiveBatch` once; empty batch still caches + relays (FR-001, FR-002, FR-010).

### Implementation for User Story 1

- [x] T011 [P] [US1] Create `ITransitEndpointsService` + `TransitEndpointsService` in `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/EndpointsServices/TransitEndpointsService.cs`, mirroring `GtfsEndpointsService`: `Task<Result<IEnumerable<EventEnvelope>>> GetLastBatch(CancellationToken ct = default)` via `IHttpServiceFactory.Create(nameof(APIs.TransitJazzAPI))` calling `ApiEndpoints.Transit.GetLastBatch`, with try/catch → `Result.Error` (FR-005, FR-009).
- [x] T012 [US1] Register the client service in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs`: `builder.Services.AddSingleton<ITransitEndpointsService, TransitEndpointsService>();` (next to `IGtfsEndpointsService`).
- [x] T013 [US1] Inject `ITransitEndpointsService` into `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` (`[Inject]`) and add a one-time snapshot fetch in the `OnInitializedAsync` load sequence — after `NotificationService.InitAsync()` and `LoadRoutesAsync()` — that, on success, feeds the result into the existing `HandleVehicleBatchAsync(...)`; best-effort (log + continue on failure), non-blocking, relying on the existing `_pendingBatch` guard if the map is not yet ready (FR-005, FR-006).

**Checkpoint**: MVP complete — buses appear on load and transition smoothly. Validate via `quickstart.md` Steps 3–4.

---

## Phase 4: User Story 2 - Graceful behavior before any data exists (Priority: P2)

**Goal**: Loading during the cold-start window (no batch yet) loads cleanly with no vehicles and no error, then populates on the first push.

**Independent Test**: Restart the service, load the map before the first push — map loads, no vehicles, no error; first push then populates.

### Implementation for User Story 2

> No new production code expected beyond Foundational + US1: the cache returns empty `[]` (T004/T007) and `HandleVehicleBatchAsync` already no-ops on an empty batch. These tasks confirm and harden that path.

- [x] T014 [US2] Verify the cold-start client path in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`: an empty `IEnumerable<EventEnvelope>` from `GetLastBatch` flows through `HandleVehicleBatchAsync` to a harmless empty render (no exception, no spinner-forever); add a guard/early-return only if a gap is found (FR-004, US2 AC-1/AC-2).
- [ ] T015 [US2] Execute `quickstart.md` Step 1 (cold-start endpoint `200`+`[]`) and Step 5 (cold-start client load) and record results (SC-003).

**Checkpoint**: Cold start is graceful on both server and client.

---

## Phase 5: User Story 3 - The cache always reflects the latest push (Priority: P3)

**Goal**: After successive pushes, the cached snapshot always equals the most recent batch.

**Independent Test**: Trigger several pushes; a fresh load after the Nth returns the Nth batch.

### Implementation for User Story 3

> Latest-wins is implemented by T004's atomic overwrite and asserted by T009. This phase confirms it end-to-end.

- [ ] T016 [US3] Execute `quickstart.md` Step 2 across multiple Worker cycles and confirm each fresh `GET /transit/last-batch` returns the newest batch (latest-wins; FR-002, US3 AC-1). No code change expected; file a defect against T004 if it fails.

**Checkpoint**: All three user stories independently verified.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T017 Run the full unit suite: `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` — confirm `LastBatchCacheTests` and `WorkerTransitHubTests` all pass (`quickstart.md` Step 7).
- [ ] T018 Run `quickstart.md` Step 6 (no upstream fetch on repeated reads; FR-007 / SC-005) and confirm logs show no extra GTFS-RT/Worker activity from reads.
- [x] T019 [P] Add structured `ILogger` debug logging in `TransitEndpoints` (snapshot size served) and at the `WorkerTransitHub` cache write, consistent with existing logging style (Principle IV); no user-facing copy, so no `.resx` change.
- [x] T020 Full-solution `dotnet build` + `dotnet test` to confirm no regressions in existing projects.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. T002 depends on T001.
- **Foundational (Phase 2)**: T003 and T004 are independent; T005/T006 depend on T004; T007 depends on T003+T004; T008 depends on T007. BLOCKS all user stories.
- **User Stories (Phase 3+)**: All depend on Foundational completion.
  - US1 (P1) is the MVP. US2 and US3 are largely verification of behavior already implemented in Foundational + US1.
- **Polish (Phase 6)**: After the targeted stories are complete.

### User Story Dependencies

- **US1 (P1)**: After Foundational. T009/T010 (tests) depend on the cache/hub existing (T004/T006); T011→T012→T013 are the client chain.
- **US2 (P2)**: After US1 (reuses the client fetch path). Independently testable.
- **US3 (P3)**: After Foundational (latest-wins lives in the cache). Independently testable.

### Within Each User Story

- Unit tests (T009/T010) should be written to FAIL first, then made green by the Foundational implementation.
- Shared constant (T003) before endpoint/service that reference it.
- Cache (T004) before DI (T005), hub hook (T006), and endpoint (T007).
- Client service (T011) before its DI (T012) before the page wiring (T013).

### Parallel Opportunities

- T009 and T010 are `[P]` — different test files, independent.
- T011 is `[P]` relative to the server tests (different project/files).
- T003 and T004 can be authored in parallel (different files).
- T019 is `[P]` in Polish.

---

## Parallel Example: User Story 1

```bash
# Unit tests (different files) in parallel:
Task: "LastBatchCacheTests in .../WebAPI.Tests/LastBatchCacheTests.cs"
Task: "WorkerTransitHubTests in .../WebAPI.Tests/WorkerTransitHubTests.cs"

# Client service can be built alongside the server tests:
Task: "ITransitEndpointsService + TransitEndpointsService in Client.Core/.../TransitEndpointsService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (test project).
2. Phase 2: Foundational (cache + endpoint + hub hook + DI + constant) — CRITICAL.
3. Phase 3: US1 (unit tests + client service + page wiring).
4. **STOP & VALIDATE**: `quickstart.md` Steps 3–4 + `dotnet test` (Step 7).
5. Deploy/demo — the lag-on-load window is eliminated.

### Incremental Delivery

1. Setup + Foundational → server caches & serves.
2. US1 → buses on load (MVP).
3. US2 → confirm graceful cold start.
4. US3 → confirm latest-wins.
5. Polish → logging, no-refetch check, full build/test.

---

## Notes

- `[P]` tasks = different files, no dependencies.
- `[Story]` label maps tasks to spec user stories for traceability.
- Integration tests are intentionally excluded; `quickstart.md` is the verification vehicle for HTTP and client behavior.
- The endpoint handler stays thin (returns `cache.Current`); no `WebApplicationFactory` needed.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
- Constitution: additive only — no change to Worker passes, pipelines, UI, or `.resx`.
