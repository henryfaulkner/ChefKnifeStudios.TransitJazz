# Test Plan: Egress Reduction at Current Scale (051)

Gold standard: `.claude/skills/util-testing/` (classification/SMURF, constructs, reliability, doubles, review criteria, test types). Every behaviour is pushed to the **lowest layer that can prove it**; anything left above Tier 1 is listed explicitly with a rationale, not discovered later.

## Strategy & house conventions (binding)

- **Style**: Classicist (Chicago) — the suite's established style: real collaborators inside the boundary, state assertions, **hand-rolled fakes/spies** (`FakeLastBatchCache`, `FakeHubContext`, `WarningSpyLogger`, `FakeApplicationViewModel`). No mocking framework is introduced. Doubles follow the hierarchy: prefer stub < spy < fake; document any deviation in the test file.
- **Runner/naming**: xunit `[Fact]`/`[Theory]`; files `{Thing}Tests.cs`; methods `Scenario_ExpectedOutcome` PascalCase-with-underscores naming behaviour + expectation (house pattern: `Set_Then_Current_ContainsNonStaleVehicle`). Task-ID prefixes (`T0xx_`) and `// INV-…` invariant comments where a test pins a contract invariant, mirroring `WorkerTransitHubTests`.
- **AAA** visible in every test; builders as `static` helpers in the test class (house `MakeBatch`/`MakeRecord` pattern), not base classes.
- **Reliability rules** (from `reliability.md`, non-negotiable):
  - No assertion ever depends on `DateTime.UtcNow`. Where time matters, inject `TimeProvider` (already supported by `LastBatchCache(TimeProvider)`); envelope timestamps in builders are never asserted.
  - No `Thread.Sleep`/`Task.Delay` waits — async coordination via `TaskCompletionSource` gates held/released by the test (see Phase 2).
  - No randomness: all vectors are fixed literals; the Phase 3 size-budget batch is a deterministic generated sequence (`i => (33 + i * 0.00001, -84 - i * 0.00001)`), not random.
  - No retries, ever. A flaky 051 test is treated as a defect in the change itself.
- **One logical assertion** per unit test (multiple raw asserts jointly verifying one outcome are fine — e.g. "ETag present AND stable" is one concept).

### Execution tiers for this feature

| Tier | Contents | Gate |
|---|---|---|
| 0 (Always) | All new unit/component tests in the four `dotnet` test projects + `go test ./...` in `tools/telemetry-mcp` | Every commit, red/green locally, CI |
| 1 (Core) | In-process greybox tests via `Microsoft.AspNetCore.TestHost` (Phase 1 middleware/headers; Phase 3 hub version gate) | CI on every PR touching `src/Server` |
| 2 (Extended) | `quickstart.md` manual walkthroughs (browser DevTools, WS frames, Log Analytics KQL, bicep deploy checks) | Per-phase sign-off before merge |
| 3 (Perf/Scale) | Production `batch_wire_bytes` before/after comparison (SC-004/SC-005) | Post-deploy observation, not a test run |

**New dev dependency (Tier 1)**: `Microsoft.AspNetCore.TestHost` in `Server.WebAPI.Tests`. Justified by SMURF: header/middleware semantics (compression negotiation, `304`, hub method resolution) are only provable with a real HTTP/SignalR pipeline; TestHost keeps it in-process — no sockets, no deployed env — so Reliability stays unit-grade while Fidelity rises to what the behaviour needs. This is the *only* escalation above component level in the plan.

---

## Phase 0 — Measurement & observability

### Design-for-test seam (required)
The serialize-and-count must NOT live inline in `Worker`'s tick loop where only an E2E could reach it. Extract `static long WireSize.Measure(List<EventEnvelope> batch)` (pooled `ArrayBufferWriter`, returns written count) — a pure function, unit-testable, used by the Worker.

### Tests

| ID | Layer / project | Behaviour → expectation | Doubles / notes |
|---|---|---|---|
| P0-U1 | Unit / Worker.Tests | `Measure_KnownBatch_EqualsSerializerLength` — result equals `MessagePackSerializer.Serialize(batch).Length` for a fixed 3-record batch | none (pure fn). Guards the pooled-buffer implementation against drift from the canonical encoding |
| P0-U2 | Unit / Worker.Tests | `Measure_EmptyEnvelopeList_ReturnsSmallEnvelopeOverheadOnly` — boundary: empty list still measures (>0, < 64 B) | none |
| P0-U3 | Unit / Worker.Tests | Extend `TelemetryEventSchemaTests.ExpectedColumns` with `batch_wire_bytes`; parquet round-trip preserves a `long` value on PerCityCycle and `null` on a no-publish row | existing schema-freeze pattern; this is the parquet **contract** pinned at unit cost |
| P0-C1 | Component / Worker.Tests | Through the existing city-loop harness (`CityLoopTests` style, spy `ITransitHubPublisher`): a publishing tick posts a `PerCityCycle` `TelemetryEvent` whose `batch_wire_bytes` equals `WireSize.Measure` of the batch the spy captured | spy publisher records the exact batch; spy `IEventNotificationService` records the posted event — asserting the *observable* telemetry row, not Worker internals |
| P0-C2 | Component / Worker.Tests | Empty-feed tick (city fetch returns zero entities) → posted row has `batch_wire_bytes == null`, **never 0** (contract: `NULL ⇔ nothing published`) | same harness; negative path |
| P0-C3 | Component / Worker.Tests | FullCycle row's `batch_wire_bytes` equals the sum of that tick's PerCityCycle values (two fake cities) | same harness |
| P0-G1 | Contract / `tools/telemetry-mcp` (Go, table-driven) | validator accepts `batch_wire_bytes > 100000` and `batch_wire_bytes >= 0`; rejects `batch_wire_bytes = 'x'` (numeric kind) and `batch_wire_byte > 1` (unknown column) | extends the existing accept/reject vector tables — keeps the frozen cross-language allow-list sync honest |

**Not automated (Tier 2, declared)**: bicep changes (Log Analytics wiring, SWA Standard). No IaC test framework exists in the repo; adding one for two modules fails SMURF Maintainability. Compensations: `az bicep build` lint runs in the deploy lane; quickstart Phase 0 steps 4–5 are the acceptance check. Risk accepted: low — the `cae` conditional it exercises is pre-existing and the change is parameter plumbing.

---

## Phase 1 — Compression + route-response caching

### Design-for-test seams (required)
- `IRouteShapeResponseCache` is a plain singleton — fully unit-testable.
- The conditional-GET decision (`If-None-Match` vs ETag → 304?) is extracted as a pure helper so the matrix lives at unit level, with TestHost proving only the wiring once.

### Tests

| ID | Layer / project | Behaviour → expectation | Doubles / notes |
|---|---|---|---|
| P1-U1 | Unit / WebAPI.Tests | `SameBytes_ProduceSameETag` / `DifferentBytes_ProduceDifferentETag` — ETag is a deterministic strong hash of content (deliberately time-free: no clock in the contract → no clock flake class) | none |
| P1-U2 | Unit / WebAPI.Tests | `Repopulate_SwapsEntry_OldEntryUnchanged` — after a second populate, readers holding the old entry see intact bytes/ETag (immutability = the concurrency guarantee, asserted as state, not with a race) | none |
| P1-U3 | Unit / WebAPI.Tests | Conditional-GET helper matrix (`[Theory]`): exact match → 304; no header → 200; stale ETag → 200; multiple values incl. match → 304; `*` → 304 | pure fn; boundary/negative rows in one table |
| P1-U4 | Unit / WebAPI.Tests | `PrecomputedJson_EquivalentToLegacyPath` — cache-built bytes deserialize to the same `RouteShapeFeature` set the old per-request path produced from an identical fake repo (order-independent set comparison per reliability.md) | fake `IKeyValueRepository` with 3 fixed shapes; **the** regression guard for FR-016/no-content-change |
| P1-S1 | System (greybox, TestHost) / WebAPI.Tests | `GET` all-shapes with `Accept-Encoding: br` → `Content-Encoding: br` + intact body after decompression; with no `Accept-Encoding` → identity. One test per negotiation outcome | real pipeline, in-proc; the only place middleware order + `EnableForHttps` wiring is observable |
| P1-S2 | System (greybox, TestHost) / WebAPI.Tests | Cold `GET` → 200 + `ETag` + `Cache-Control: public, max-age=3600`; replayed `If-None-Match` → 304 with empty body; loader-not-ready → 503 | seeds the real cache via the loader seam; asserts the full header contract once end-to-end |

**Deliberately absent**: a multithreaded stress test on the cache swap. The design (immutable entry + reference assignment) makes the race unrepresentable; a looping race test would be Tier 4 cost for no added proof (SMURF: Reliability/Maintainability loss, zero marginal Fidelity). P1-U2 pins the invariant that makes it safe.

---

## Phase 2 — Hidden-tab pause

### Design-for-test seam (required)
The gate logic must be a plain class (`AttentionGate`) in `Client.Core/Services/` — no browser/JS dependency, so the desktop-runtime xUnit host exercises it directly (`Client.Shared.Tests` → `Client.Shared` → `Client.Core`; no new project reference required). Inputs `(hidden, audioEnabled)` events, output calls on an injected delivery-control interface (`PauseAsync`/`ResumeAsync`). JS interop and `TransitMap` wiring stay thin adapters. This is what makes the riskiest logic (async reconciliation) provable at Tier 0 instead of via browser automation.

### Tests

| ID | Layer / project | Behaviour → expectation | Doubles / notes |
|---|---|---|---|
| P2-U1 | Unit / Client.Shared.Tests | `HiddenWhileMuted_Pauses` — spy records exactly one `PauseAsync` | spy delivery control (house spy pattern) |
| P2-U2 | Unit / Client.Shared.Tests | `HiddenWhileAudioPlaying_DoesNotPause` — spy records zero calls (FR-008, the confirmed product decision) | spy |
| P2-U3 | Unit / Client.Shared.Tests | `VisibleAfterPause_Resumes`; `MuteWhileHidden_Pauses`; `UnmuteWhileHidden_Resumes` — the remaining data-model transition rows, one test each | spy |
| P2-U4 | Unit / Client.Shared.Tests | `ReconnectWhilePaused_DoesNotRejoin` — reconnect callback consults the gate; spy shows no resume (contract C3's reconnect rule — the regression most likely to slip in) | spy delivery control; the test sets `DesiredDelivery = false` (as the gate would), raises the reconnect callback, and asserts zero `ResumeAsync` calls — proving the rule at the seam, with no reference from control back to gate |
| P2-U5 | Unit / Client.Shared.Tests | `RapidToggleBurst_SettlesOnFinalDesiredState_WithSingleInFlightCall` — first `PauseAsync` blocks on a test-held `TaskCompletionSource`; N hidden/visible flips arrive; on release, reconciliation issues calls until actual == last desired, and at no point were two calls in flight | spy whose calls await TCSs **the test releases** — event-based sync per reliability.md, zero sleeps, deterministic |
| P2-U6 | Unit / WebAPI.Tests | `LeaveCity_RemovesConnectionFromCityGroup` (+ `JoinCity_ReplaysCachedSnapshotToCaller` / `JoinCity_EmptyCache_SendsNothing` if not already covered) — extends the `WorkerTransitHubTests` fake-hub pattern to `TransitHub` | `FakeHubContext`-style group-manager spy, `FakeLastBatchCache` |

**Not automated (Tier 2, declared)**: `page-visibility.js` itself and real `document.hidden` transitions — no JS test runner exists in the repo and the module is deliberately logic-free glue (fires current state; .NET re-derives). Covered by quickstart Phase 2 steps 1–3, 6. Mitigation: any conditional beyond "report `document.hidden`" is required to move into `AttentionGate` where P2-U* reach it.

---

## Phase 3 — Wire slimming (record v2 + version gate)

### Tests

| ID | Layer / project | Behaviour → expectation | Doubles / notes |
|---|---|---|---|
| P3-U1 | Unit / Shared.Tests | `[Theory]` round-trip vectors (contract C6): all-fields, steady-state (null prior + null category), boundary `±89.99999/±179.99999`, scaled values exactly equal `Math.Round(x,5) * 1e5` for representative 5-decimal fixtures | none; extends `EventEnvelopeMessagePackTests` for envelope polymorphism with v2 |
| P3-U2 | Unit / Shared.Tests | `SteadyStateBatch_1000Records_AtLeast40PercentSmallerThanV1Equivalent` — deterministic coordinate sequence; v1 baseline is a frozen local replica record type in the test (the old shape no longer exists in prod code) | the SC-004 proxy at Tier 0; deterministic, no randomness |
| P3-U3 | Unit / Worker.Tests | Emit-rule matrix through the city-loop harness + spy publisher: first-seen → prior==current & non-null & `DurationMs 0`; steady → prior null; route-change → prior non-null; stale → emitted with `IsStale` true; unknown-route-category → `Category=="unknown"`, all matched vehicles → `Category==null` (extends `CategoryFallbackTests`) | spy `ITransitHubPublisher` captures the batch — asserts the *published record*, the boundary, not builder internals |
| P3-U4 | Unit / WebAPI.Tests | `ReplayedNullPriorRecord_RetainsIsStale_AndSurvivesUpsert` — one new `LastBatchCacheTests` case with a v2 null-prior record; **all existing `LastBatchCacheTests` + `LastBatchCacheCrossingExclusionTests` pass with compile-only edits** (constructor-shape updates in builders; zero assertion changes — reviewed as such) | the "synthetically all moving" regression stays pinned by the *existing* suite; that's the point of not touching cache code |
| P3-U5 | Unit / Shared.Tests | `HubMethods_JoinCity_IsJoinCityV2` — freezes the gate constant the way `TelemetryEventSchemaTests.ExpectedColumns` freezes columns. Interaction-ish, but the string **is** the wire contract | none |
| P3-U6 | Unit / Shared.Tests | `HubMethods_LeaveCity_IsUnversioned` — `LeaveCity` stays `"LeaveCity"` (contract C5: not renamed with `JoinCity`). Pins the *negative* half of the gate: a blanket V2 rename satisfies P3-U5 while silently breaking Phase 2's pause path, and nothing else would catch it until manual testing | none |
| P3-S1 | System (greybox, TestHost) / WebAPI.Tests | Real MessagePack SignalR client invokes legacy `"JoinCity"` against the new hub → `HubException`, no group membership, no batch delivered; invoking `"JoinCityV2"` succeeds and receives the cache replay (FR-015's clean-failure semantics — unprovable below this layer, and too important for manual-only) | in-proc TestHost + `HubConnectionBuilder` with `AddMessagePackProtocol`; if MessagePack-over-TestHost proves infeasible in a timeboxed spike, fall back to quickstart step 3 **and record the gap here** |

**Not automated (declared)**: JS decode precedence (record-prior → retained → snap) and the retained-position store's eviction live in `vehicle-animator.js` (category catalog lookup in `map-interop.js`) — same no-JS-runner constraint as Phase 2; quickstart Phase 3 step 2 covers it. Mitigation: the C# payload builder passes nullables through untransformed (asserted implicitly by P3-U1's round-trips), keeping the JS surface minimal.

**Residual risk (accepted, declared)**: the retained-position store is genuinely *stateful* JS — the "pass nullables through" mitigation above does not reach its eviction path. The uncovered failure mode is a vehicle that leaves the rendered set and later reappears with a null prior: if its stale retained entry survived eviction, it animates from an obsolete origin (the motion artifact FR-012 forbids). Pinned only by quickstart Phase 3 step 2's leave-and-reappear vector. Escalate to a JS runner only if that vector fails in practice — standing up one for a single store fails SMURF Maintainability here.

---

## Traceability & review gate

| Spec/Contract | Pinned by |
|---|---|
| FR-001 / telemetry contract C1 | P0-U3, P0-C1–C3 |
| Validator sync (contract C2) | P0-G1 |
| FR-004–FR-006 / http-caching C1–C2 | P1-U1–U4, P1-S1–S2 |
| FR-007–FR-009 / visibility-pause C1, C3 | P2-U1–U6 |
| FR-010–FR-013 / wire-slimming C1–C3 | P3-U1–U3 (worker emit side) |
| FR-013a (client category resolution, never defaults to `"bus"`; catalog-load re-resolution) | **Tier 2 only** — quickstart Phase 3 step 2. Declared gap: the resolver lives in JS (no runner). Compensation: T044a scopes the change to one accessor + one call site; verify with a route deliberately absent from the catalog (must render `"unknown"`, not a bus dot) and by loading with the catalog delayed. |
| FR-012 + cache invariants (wire-slimming C4) | P3-U4 + existing cache suites unmodified |
| FR-015 / wire-slimming C5 | P3-U5 (join renamed), P3-U6 (leave NOT renamed), P3-S1 |
| FR-016 (no visible change) | P1-U4 (content), P3-U3 (semantics), quickstart Tier 2 (rendering/audio) |

**Definition of done per phase** (applies `review-criteria.md`'s checklist): all Tier 0/1 tests for the phase green; tests assert behaviour at a boundary (published batch, telemetry row, HTTP response, spy'd hub calls) — never private state; negative + boundary rows present where the tables above list them; no test reads a real clock, sleeps, retries, or depends on run order; names state behaviour + expectation; each new test fails for exactly one reason. Phase 3 additionally requires: existing cache/crossing suites untouched semantically (diff review), and the Tier 3 production measurement recorded before/after.
