# Tasks: Egress Reduction at Current Scale

**Input**: Design documents from `/specs/051-egress-reduction/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D8), data-model.md, contracts/ (×4), test-plan.md, quickstart.md

**Tests**: INCLUDED — test-plan.md is binding (util-testing gold standard). Test tasks reference test-plan IDs (P0-U1 … P3-S1) and are ordered test-first within each story: failing tests precede the implementation that turns them green.

**Organization**: One phase per user story, in spec priority order (US1→US4 = source doc Phases 0→3). Delivery order is binding per plan.md: **US4 MUST NOT start until US1's baseline has ≥3 days of data** (T013). US2 and US3 are mutually independent and independent of US4.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 (measure/observability), US2 (HTTP efficiency), US3 (hidden-tab pause), US4 (wire slimming)

## Path Conventions

Repo layout per plan.md. Abbreviations used below: `Worker/` = `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/`, `WebAPI/` = `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/`, `Shared/` = `src/ChefKnifeStudios.TransitJazz.Shared/`, `Client.Core/` = `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/`, `Client.Shared/` = `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`, `WebApp/` = `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`. Test projects: same name + `.Tests`.

---

## Phase 1: Setup

**Purpose**: Tooling prerequisites; no production code.

- [X] T001 Add `Microsoft.AspNetCore.TestHost` package reference to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj` (Tier 1 greybox dependency — test-plan "Strategy"; needed by T022, T046)
- [X] T002 [P] Baseline green run: `dotnet test` across all four test projects and `go test ./...` in `tools/telemetry-mcp` — confirm the suite is green before any 051 change (any pre-existing red is out of scope and must be reported, not fixed here)

---

## Phase 2: Foundational (Blocking Prerequisites)

**No foundational tasks.** The four stories share no new blocking infrastructure — each touches disjoint seams (telemetry/bicep, HTTP pipeline, hub membership, wire contract). Story phases begin immediately after Setup.

---

## Phase 3: User Story 1 — Operator can see what the system actually transfers (Priority: P1) 🎯 MVP

**Goal**: Durable per-city `batch_wire_bytes` telemetry, logs actually landing in Log Analytics, SWA off the capped Free tier. Produces the baseline every later story is measured against.

**Independent Test**: quickstart.md Phase 0 — telemetry query returns per-city sizes (NULL on no-publish ticks), FullCycle sums match, KQL returns Worker logs, portal shows SWA Standard.

### Tests for User Story 1 (write first, confirm failing)

- [X] T003 [P] [US1] Write failing unit tests `Measure_KnownBatch_EqualsSerializerLength` (P0-U1) and `Measure_EmptyEnvelopeList_ReturnsSmallEnvelopeOverheadOnly` (P0-U2) in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WireSizeTests.cs` — fixed 3-record batch builders per house `MakeBatch` pattern, no clocks asserted
- [X] T004 [P] [US1] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/TelemetryEventSchemaTests.cs`: add `batch_wire_bytes` to `ExpectedColumns`; add parquet round-trip asserting a `long` value on a PerCityCycle row and `null` on a no-publish row (P0-U3) — failing until T006

### Implementation for User Story 1

- [X] T005 [US1] Implement `static long WireSize.Measure(List<EventEnvelope> batch)` in `Worker/Logging/WireSize.cs` — `MessagePackSerializer.Serialize` into a pooled/reused `ArrayBufferWriter<byte>`, return written count (research D1 seam) → T003 green
- [X] T006 [US1] Add `public long? batch_wire_bytes { get; init; }` to `Worker/Logging/TelemetryEvent.cs` with the PerCityCycle/summed-on-FullCycle doc comment (data-model §2) → T004 green
- [X] T007 [US1] `Worker/Worker.cs`: measure via `WireSize.Measure` immediately before `PublishBatchAsync` (~L576), carry on `CityTickResult`, emit on the PerCityCycle post (~L98-119) and sum onto FullCycle; `null` (never 0) when nothing published — contract telemetry-observability C1
- [X] T008 [US1] Component tests P0-C1/C2/C3 in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WireBytesTelemetryTests.cs` — reuse the `CityLoopTests` harness style: spy `ITransitHubPublisher` captures the exact batch, spy `IEventNotificationService` captures posted rows; assert published-tick value equals `WireSize.Measure` of captured batch, empty-feed tick posts `null`, FullCycle equals sum of two fake cities
- [X] T009 [P] [US1] Add `batch_wire_bytes` to the numeric-kind allow-list in `tools/telemetry-mcp/internal/validate/validate.go` and add accept/reject vectors to `tools/telemetry-mcp/internal/validate/validate_test.go` (P0-G1): accept `batch_wire_bytes > 100000`, `batch_wire_bytes >= 0`; reject `batch_wire_bytes = 'x'` and unknown column `batch_wire_byte > 1` — contract C2
- [X] T010 [P] [US1] Create `bicep/modules/logAnalytics.bicep`: `Microsoft.OperationalInsights/workspaces`, sku `PerGB2018`, outputs `customerId` and workspace resource for `listKeys` at the call site (research D2)
- [X] T011 [US1] `bicep/main.bicep`: instantiate the logAnalytics module and pass `logAnalyticsCustomerId`/`logAnalyticsSharedKey` into the existing `cae` module call (~L187-195); `bicep/modules/containerAppsEnvironment.bicep` is NOT edited — contract C3. Run `az bicep build` on `bicep/main.bicep` as the lint gate
- [X] T012 [P] [US1] `bicep/modules/staticWebApp.bicep` (~L31-34): `sku.name`/`sku.tier` `'Free'` → `'Standard'` — contract C4
- [ ] T013 [US1] **DEFERRED (requires Azure deploy access)** Deploy + verify quickstart.md Phase 0 steps 1–5 (telemetry rows incl. NULL semantics, validator query accepted, FullCycle sum, KQL log hit, SWA Standard); **start the ≥3-day per-city baseline capture and record NYMTA's share** (quickstart step 6) — this task GATES Phase 6 (US4)
- [ ] T013a [US1] **DEFERRED (requires Azure Monitor access)** SC-005 total-egress baseline (must be captured BEFORE Phase 4/US2 deploys): record the pre-feature monthly outbound-transfer figures that `batch_wire_bytes` does NOT cover — Azure Monitor SWA data-out and Container App egress metrics over the same ≥3-day window as T013. `batch_wire_bytes` measures SignalR payload only; without this, SC-005's HTTP half has no denominator and the number can only ever be projected. Record alongside T013's per-city baseline.

**Checkpoint**: SC-001 + SC-006 verifiable; baseline accumulating. MVP deliverable.

---

## Phase 4: User Story 2 — App startup costs a fraction of today's transfer (Priority: P2)

**Goal**: Compressed, precomputed, revalidatable route-catalog responses; per-request deserialize/re-serialize eliminated.

**Independent Test**: quickstart.md Phase 1 — `curl` shows `Content-Encoding`/`ETag`/`Cache-Control`, replayed `If-None-Match` → 304, map renders identically, SignalR untouched.

### Tests for User Story 2 (write first, confirm failing)

- [X] T014 [P] [US2] Write failing unit tests `SameBytes_ProduceSameETag`, `DifferentBytes_ProduceDifferentETag` (P1-U1) and `Repopulate_SwapsEntry_OldEntryUnchanged` (P1-U2) in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/RouteShapeResponseCacheTests.cs` — ETag is content-hash only, deliberately no clock in any assertion
- [X] T015 [P] [US2] Write failing `[Theory]` conditional-GET matrix (P1-U3: exact match→304, absent header→200, stale ETag→200, multi-value-with-match→304, `*`→304) in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ConditionalGetTests.cs`

### Implementation for User Story 2

- [X] T016 [US2] Implement `IRouteShapeResponseCache` + `RouteShapeResponseCache` in `WebAPI/GtfsStatic/RouteShapeResponseCache.cs` — immutable `(byte[] Utf8Json, string ETag, DateTimeOffset GeneratedUtc)` entries keyed `(Endpoint, CityKey)` with `"*"` for the no-city all-shapes variant; strong quoted SHA-256-based ETag; atomic reference swap (data-model §3) → T014 green
- [X] T017 [US2] Implement the pure conditional-GET helper (If-None-Match vs ETag → 304?) as a static class in `WebAPI/EndpointGroups/HttpCaching.cs` (plan's binding seam #2) → T015 green
- [X] T018 [US2] `WebAPI/GtfsStatic/GtfsStaticLoader.cs`: on completing a city's shape build (initial load AND the refresh path), serialize the aggregate all-shapes and all-routes responses once and populate the cache — research D4
- [X] T019 [US2] Equivalence regression test P1-U4 `PrecomputedJson_EquivalentToLegacyPath` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/RouteResponseEquivalenceTests.cs` — fake `IKeyValueRepository` with 3 fixed shapes; cache-built bytes deserialize to the same `RouteShapeFeature` set the legacy per-request path produces; order-independent set comparison
- [X] T020 [US2] `WebAPI/EndpointGroups/GtfsEndpoints.cs`: `GetAllRouteShapes` + `GetAllRoutes` serve cache bytes via `Results.Bytes(..., "application/json")` + `ETag` + `Cache-Control: public, max-age=3600`; `HttpCaching` helper → 304; not-ready → 503 unchanged; unknown-city → empty-list 200 unchanged (contract http-caching C2)
- [X] T021 [US2] `WebAPI/Program.cs`: register `IRouteShapeResponseCache` singleton; `AddResponseCompression` (`EnableForHttps = true`, `BrotliCompressionProvider` at `CompressionLevel.Fastest` then `GzipCompressionProvider`, default MIME set) and `app.UseResponseCompression()` before endpoint mapping (research D3)
- [X] T022 [US2] TestHost greybox tests P1-S1 (`br`/`gzip`/identity negotiation with intact decompressed body) and P1-S2 (cold 200 header contract, If-None-Match→304 empty body, not-ready→503) in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/CompressionAndCachingEndpointTests.cs` (needs T001)
- [ ] T023 [US2] **DEFERRED (requires a running deployed instance)** Verify quickstart.md Phase 1 steps 1–5 in a local run + record before/after transfer size of the all-shapes response (SC-002 evidence)

**Checkpoint**: SC-002 verifiable; REST egress reduction live independently of other stories.

---

## Phase 5: User Story 3 — Backgrounded, silent tabs stop consuming data (Priority: P3)

**Goal**: Hidden+muted sessions leave the SignalR city group (zero fan-out egress) and resume via the existing join-replay snapshot; audio-on sessions never pause (confirmed product decision, spec Clarifications 2026-07-25).

**Independent Test**: quickstart.md Phase 2 — WS frames stop while hidden+muted, snapshot catch-up on return with no motion replay, unmuted hidden tab keeps streaming.

### Tests for User Story 3 (write first, confirm failing)

- [X] T024 [P] [US3] Write failing `AttentionGateTests` (P2-U1…P2-U5) in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/AttentionGateTests.cs` — spy delivery-control per house spy pattern; P2-U5 rapid-toggle burst uses a test-held `TaskCompletionSource` the test releases (event-based sync, zero sleeps): final state == last desired, never two calls in flight (no `.csproj` change needed: `Client.Core` is already transitively referenced via `Client.Shared`)

### Implementation for User Story 3

- [X] T025 [US3] Implement `AttentionGate` + `IDeliveryControl` in `Client.Core/Services/AttentionGate.cs` — `IDeliveryControl` exposes `PauseAsync()`, `ResumeAsync()`, and **`bool DesiredDelivery { set; }`**; the gate computes `desiredDelivery = !(hidden && !audioEnabled)`, **writes it to the control before issuing any Pause/Resume call**, then reconciles (single in-flight guard, reconcile-after-completion). The gate holds `IDeliveryControl`; **the control MUST NOT hold a reference to the gate** — this keeps the dependency one-directional (data-model §4; plan's binding seam #3) → T024 green
- [X] T026 [P] [US3] Add `public const string LeaveCity = "LeaveCity";` to `HubMethods` in `Shared/CityNames.cs`
- [X] T027 [US3] Add `TransitHub.LeaveCity(string city)` → `Groups.RemoveFromGroupAsync` in `WebAPI/SignalR/TransitHub.cs` (contract visibility-pause C1; `JoinCity` untouched this phase)
- [X] T028 [US3] Tests P2-U6 in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/TransitHubTests.cs` — `LeaveCity_RemovesConnectionFromCityGroup`, `JoinCity_ReplaysCachedSnapshotToCaller`, `JoinCity_EmptyCache_SendsNothing`, using a fake group-manager/caller-proxy in the `WorkerTransitHubTests` fake style + `FakeLastBatchCache`
- [X] T029 [P] [US3] Create `Client.Shared/wwwroot/js/page-visibility.js` — logic-free ES module: subscribe `visibilitychange`, invoke the .NET callback with current `document.hidden`; expose dispose (mirrors `outside-click.js`)
- [X] T030 [US3] Create `IPageVisibilityJsInterop` + `PageVisibilityJsInterop` in `Client.Shared/Services/JsInterop/PageVisibilityJsInterop.cs` — lazy module import with cache-bust GUID, `DotNetObjectReference` callback, `IAsyncDisposable`, try/catch per the `OutsideClickJsInterop` idiom; register in DI where sibling interops are registered (`WebApp/Program.cs`)
- [X] T031 [US3] `Client.Core/Services/SignalRNotificationService.cs`: implement `PauseAsync` (invoke `LeaveCity`, keep connection open) / `ResumeAsync` (invoke `JoinCity`); implement `bool DesiredDelivery { set; }` as a `private volatile bool _desiredDelivery = true;` backing field; the existing `Reconnected` handler (`SignalRNotificationService.cs:98-102`) rejoins **only when `_desiredDelivery` is true** — it reads the local field, never the gate (contract C3 reconnect rule — pinned by P2-U4)
- [X] T032 [US3] `WebApp/Pages/TransitMap.razor.cs`: wire `AttentionGate` — visibility interop lifecycle (init/dispose), `SettingsService` mute snapshot at startup, live updates via the existing `AudioSettingChangedEventArgs` bus subscription, `ISignalRNotificationService` as the `IDeliveryControl`
- [ ] T033 [US3] **DEFERRED (requires a running browser session)** Verify quickstart.md Phase 2 steps 1–6 manually (WS frame inspection, catch-up correctness, unmuted-hidden streaming, reconnect-while-paused, rapid toggling)

**Checkpoint**: SC-003 verifiable; behavior invisible to active foreground sessions.

---

## Phase 6: User Story 4 — Live vehicle updates are half their current size (Priority: P4)

**Goal**: One coordinated `RouteNearestPointRecord` v2 revision (scaled-int coords, omitted prior pair, unknown-only category) gated by `JoinCityV2`, landing on all three hops and `deploy/marta-jazz`.

**⚠️ GATE**: Do not start until T013's baseline has ≥3 days of data (SC-004 denominator).

**Independent Test**: quickstart.md Phase 3 — suites green, map indistinguishable, old client fails cleanly at join, measured per-vehicle reduction ≥40%.

### Tests for User Story 4 (write first, confirm failing)

- [ ] T034 [P] [US4] Write failing serialization tests in `src/ChefKnifeStudios.TransitJazz.Shared.Tests/RouteNearestPointRecordV2Tests.cs` (P3-U1 `[Theory]` round-trips: all-fields, steady-state nulls, boundary `±89.99999/±179.99999`, exactness vs `Math.Round(x,5)*1e5`; P3-U2 deterministic 1,000-record size budget ≥40% below a frozen v1-replica record type local to the test) and extend `src/ChefKnifeStudios.TransitJazz.Shared.Tests/EventEnvelopeMessagePackTests.cs` for v2 envelope polymorphism — will not compile until T036 (expected)
- [ ] T035 [P] [US4] Write failing `HubMethods_JoinCity_IsJoinCityV2` (P3-U5) in `src/ChefKnifeStudios.TransitJazz.Shared.Tests/HubMethodsTests.cs` — freezes the wire gate constant. Add `HubMethods_LeaveCity_IsUnversioned` in the same file asserting `HubMethods.LeaveCity == "LeaveCity"`: contract wire-slimming C5 requires `LeaveCity` NOT be renamed alongside `JoinCity` (group membership only matters post-join), so freeze both halves of the gate — a blanket V2 rename would otherwise pass T035's first assertion while silently breaking Phase 2's pause path

### Implementation for User Story 4

- [ ] T036 [US4] Record v2 in `Shared/Events/RouteNearestPointBatchEvent.cs`: Keys 2/3 → `int? PriorNearestLatE5/LonE5` (atomic pair), Keys 4/5 → `int CurrentNearestLatE5/LonE5`, Key 10 → `string? Category` (no default); update doc comments with data-model §1 semantics → T034 green
- [ ] T037 [US4] `Shared/CityNames.cs`: `HubMethods.JoinCity` value → `"JoinCityV2"` (same commit as T036 — contract wire-slimming C5) → T035 green
- [ ] T038 [US4] `Worker/Worker.cs` emit rules (~L428-475): scale coords ×1e5; steady-state → null prior pair; first observation OR `RouteJoinKey` change since prior state → full prior pair (first-obs keeps prior==current, `DurationMs 0`); `Category` null unless `ResolveCategory` yields `"unknown"` (contract C2)
- [ ] T039 [US4] Emit-rule matrix P3-U3 in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/RecordEmitRulesTests.cs` via the city-loop harness + spy `ITransitHubPublisher` (first-seen / steady / route-change / stale / category rules), and extend `CategoryFallbackTests.cs` for null-vs-"unknown"
- [ ] T040 [US4] Compile-only builder updates in existing suites — `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/LastBatchCacheTests.cs`, `LastBatchCacheCrossingExclusionTests.cs`, `WorkerTransitHubTests.cs` (and any other `MakeBatch`/`MakeRecord` builders): update record constructor shapes ONLY; zero assertion/semantic changes, reviewed as such (contract C4)
- [ ] T041 [US4] Add P3-U4 `ReplayedNullPriorRecord_RetainsIsStale_AndSurvivesUpsert` to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/LastBatchCacheTests.cs` — cache production code untouched
- [ ] T042 [US4] `WebAPI/SignalR/TransitHub.cs`: rename hub method `JoinCity` → `JoinCityV2` (body unchanged — replay + group add; `LeaveCity` NOT renamed); update `TransitHubTests` method-name references
- [ ] T043 [US4] `Client.Core/Services/SignalRNotificationService.cs`: all join call sites (initial join ~L105, `Reconnected` rejoin ~L101, `ResumeAsync` from T031) ride the renamed `HubMethods.JoinCity` constant — verify no string literals bypass it
- [ ] T044 [US4] `WebApp/Pages/TransitMap.razor.cs` payload builder (~L496-519): pass nullable prior/category through and divide current (and prior when present) by `1e5` at this single decode seam (contract C3)
- [ ] T044a [US4] **Category catalog resolution (FR-013/FR-013a — blocks T045)**: expose a route-category accessor on `ChefMap` for `ChefMapAnimator` (catalog already at `map-interop.js:422`, populated ~L588); resolution order = record category → catalog by `routeJoinKey` → `"unknown"`, **never defaulting to `"bus"`**; re-resolve vehicles that fell back to `"unknown"` only because the catalog had not yet loaded, hooked at the same population seam (the known `JoinCity`-replay-beats-shape-load race). Leave `map-interop.js:588`'s `|| 'bus'` intact for existing checkpoint-coloring consumers — scope the change to vehicle-category resolution (data-model §1 "Category catalog contract"; contract wire-slimming C3)
- [ ] T045 [US4] `Client.Shared/wwwroot/js/vehicle-animator.js` (+ `map-interop.js` ingestion point if it forwards records): retained last-position store keyed `vehicleId`; precedence record-prior → retained → snap-into-place; drop entries when a vehicle leaves the rendered set (a post-eviction reappearance must snap, never animate from a stale origin); consume T044a's resolver at the `rec.category || 'unknown'` site (~L586) instead of the current per-vehicle-only fallback (data-model §1 decode rules)
- [ ] T046 [US4] TestHost hub gate test P3-S1 in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/HubVersionGateTests.cs` — real MessagePack `HubConnection` against in-proc server: invoking legacy `"JoinCity"` → `HubException` + no batch delivered; `"JoinCityV2"` → success + cache replay received (timeboxed spike per test-plan; if MessagePack-over-TestHost proves infeasible, fall back to quickstart step 3 and record the gap in test-plan.md)
- [ ] T047 [US4] Local end-to-end per quickstart.md Phase 3 steps 1–3: full suites green, map/audio indistinguishable (FR-016), old-bundle-vs-new-server clean failure observed
- [ ] T048 [US4] Coordinated ship per contract C5: server+worker container first, SWA client immediately after, identical revision landed on `deploy/marta-jazz` (do NOT split the three field changes across revisions)
- [ ] T049 [US4] Post-deploy measurement (quickstart Phase 3 step 5): per-city `batch_wire_bytes`/`vehicles_processed` vs T013 baseline → record **SC-004 (≥40%, measured)**; then re-read the same Azure Monitor SWA/Container App egress metrics T013a captured and record **SC-005 (60–75%) as a measured total** against that baseline. If T013a was not captured before US2 shipped, SC-005 is reported as *projected* with the gap stated explicitly — do not present a projection as a measurement. Append results to `specs/051-egress-reduction/results.md`

**Checkpoint**: All four stories live; SC-004/SC-005 evidenced from telemetry.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T050 [P] Add a one-line banner to `docs/EGRESS_REDUCTION_SMALL_SCALE.md` pointing to `specs/051-egress-reduction/` as the implementing feature (source-doc ↔ spec traceability)
- [X] T051 Apply the test-plan review gate to the full 051 diff: every new test asserts behaviour at a boundary, negative/boundary rows present, no real clocks/sleeps/retries/order-dependence, names state behaviour+expectation, one failure reason each; confirm existing cache/crossing suites show compile-only diffs (LastBatchCacheTests/LastBatchCacheCrossingExclusionTests are untouched — 0 diff, since US4 wasn't in this session's scope)
- [ ] T052 **DEFERRED (needs the Tier 2 manual walkthroughs from T013/T013a/T023/T033, all deferred)** Full quickstart.md traceability sweep: walk the SC-001…SC-007 table and confirm each row's evidence exists (telemetry query results, curl transcripts, WS frame observations, deploy records)

---

## Session Scope Note

This session implemented all **code and Tier 0/1 test tasks** for **US1 (Phase 3), US2 (Phase 4), and US3 (Phase 5)** — 231 new/updated .NET tests plus the Go validator suite, all green. **US4 (Phase 6, wire-slimming)** was explicitly out of scope: it's gated on ≥3 days of production `batch_wire_bytes` baseline data (T013), which doesn't exist yet. Tasks requiring live Azure deploy/portal/browser access (T013, T013a, T023, T033, T052) are marked DEFERRED above — they need a human to run `az deployment` / open a browser / query Azure Monitor. Everything else is implemented, tested, and ready for review.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately. T001 only blocks T022/T046.
- **Foundational (Phase 2)**: empty.
- **US1 (Phase 3)**: independent. **T013 gates Phase 6.** **T013a must land before US2 deploys** (it is the SC-005 denominator — once compression ships, the pre-feature HTTP baseline is unrecoverable).
- **US2 (Phase 4)**: independent of US1/US3/US4 (T022 needs T001), but **do not deploy US2 until T013a's baseline is captured** (E1).
- **US3 (Phase 5)**: independent of US1/US2. T031 precedes T043's `ResumeAsync` touch-point, so finish US3 before starting T043 or rebase carefully.
- **US4 (Phase 6)**: starts only after T013's ≥3-day baseline. Within-story: T034/T035 → T036/T037 → T038–T044 → **T044a → T045** (catalog resolver precedes its consumer) → T046 → T047 → T048 → T049.
- **Polish (Phase 7)**: after all delivered stories.

### Within-story ordering (binding)

Tests precede the implementation that satisfies them (T003/T004→T005/T006; T014/T015→T016/T017; T024→T025; T034/T035→T036/T037). T040 (builder compile fixes) must land in the same change as T036 or the solution doesn't build.

### Parallel Opportunities

- **Across stories**: after Setup, US1, US2, US3 can proceed in parallel (disjoint files). US4 waits on the T013 gate.
- **Within US1**: T003 ∥ T004; then T009 ∥ T010 ∥ T012 (Go / new bicep module / SWA sku — all different files).
- **Within US2**: T014 ∥ T015.
- **Within US3**: T024 ∥ T026 ∥ T029 (gate tests / Shared const / JS module).
- **Within US4**: T034 ∥ T035.

## Parallel Example: User Story 1

```bash
# After T002, launch together:
Task: "T003 failing WireSizeTests in Server.TransitDataWorker.Tests/WireSizeTests.cs"
Task: "T004 extend TelemetryEventSchemaTests with batch_wire_bytes"
# After T007, launch together:
Task: "T009 Go validator allow-list + vectors in tools/telemetry-mcp"
Task: "T010 new bicep/modules/logAnalytics.bicep"
Task: "T012 staticWebApp.bicep Free→Standard"
```

## Implementation Strategy

**MVP = US1 (Phase 3)**: measurement + observability + the SWA availability fix stand alone, deliver SC-001/SC-006, and unblock everything else — ship it first and let the baseline accumulate while US2/US3 are built.

**Incremental delivery**: US1 → (US2 ∥ US3, either order, each independently shippable) → US4 once the baseline gate opens → Polish. Each story leaves the app fully working with zero user-visible change (SC-007 checked at every checkpoint).

**Reminder (repo policy)**: no auto-commits — the implementer stops for the user to review/commit; per-phase deploys follow the lanes in contract C5 and the SignalR wire-deploy constraint.
