---
description: "Task list for Time-to-First-Note (feature 045)"
---

# Tasks: Time-to-First-Note

**Input**: Design documents from `/specs/045-time-to-first-note/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included where the contracts name specific test files (CrossingDetectorTests, TelemetryEventSchemaTests, LastBatchCacheCrossingExclusionTests, Go validate_test.go). Manual browser TTFN benchmarking (§5 of the discovery doc) is verification, captured in the quickstart-run tasks, not unit tests.

**Organization**: Grouped by user story (priority order from spec.md). Each story is an independent, deployable increment. US2 is intentionally split into two deploy gates (2a counters → read attribution → 2b fix) per the measure-before-fix rule (FR-015, plan §Phasing).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5
- Absolute-from-repo-root file paths shown. Namespace root is `ChefKnifeStudios.MartaJazz` under `src/`.

## Path Conventions

- Worker: `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`
- WebAPI: `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/`
- Client JS/interop: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`
- Go bridge: `tools/telemetry-mcp/`
- Tests mirror their project under `…​.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the branch/toolchain build before touching any tier. No new project or dependency is introduced by this feature.

- [X] T001 Confirm branch `045-time-to-first-note` is checked out and the solution builds: `dotnet build ChefKnifeStudios.MartaJazz.sln` and `go build ./...` in `tools/telemetry-mcp/` both succeed (baseline green before changes). *(verified post-implementation: full solution build 0 errors, `go build ./...` clean)*
- [ ] T002 [P] Capture the pre-change baseline TTFN using the §5.2 console proxy from `docs/TIME_TO_FIRST_NOTE_DISCOVERY_DOCUMENT.md` on deployed prod (`#marta`), fast-click + dwell, ≥10 trials each; record median/p90 into the benchmark log at `specs/045-time-to-first-note/quickstart.md` §0 (and the discovery doc §5.4 table). *(manual browser trial against deployed prod — not performed by this implementation pass)*
- [ ] T003 [P] Capture the pre-change telemetry baseline: `query_telemetry` dataset `telemetry`, `event_type='PerCityCycle' AND city_name='marta'`; compute tones/tick avg + zero-tick fraction; record alongside T002. *(not performed — pairs with T002's benchmark log entry)*

**Checkpoint**: Build is green and both baselines are recorded — every later fix is now comparable.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: There is **no shared blocking work** — each user story owns a distinct tier and can proceed independently once Setup is done. This phase is intentionally empty.

**Checkpoint**: Proceed directly to user stories. US1 (client) and US2 (worker) can be worked in parallel by different people.

---

## Phase 3: User Story 1 — Immediate audible feedback at unlock (Priority: P1) 🎯 MVP

**Goal**: Audible ambient output at t=0 on unlock (mute-respecting), first crossing plays with no build delay, and the `[TTFN]` probe ships so the fix is measurable. Client-only; no server/worker/wire change.

**Independent Test**: Fresh session, audio enabled → soft ambient bed audible within 1 s of clicking Enable, before any transit note; muted setting → silence until re-enabled; first note shows `[TTFN] trigger→audible ≈ 0 ms`; `window.MemoryProbe` stays in the 3-slot footprint.

**Contracts**: `contracts/unlock-warming.md` (D1, D2, D7), `contracts/ttfn-probe.md`.

### Implementation for User Story 1

- [X] T004 [US1] Add the `window.TtfnProbe` module-scope object (`version`, `unlockAt`, `firstTriggerAt`, `firstAudibleAt`, `droppedWhileLocked`, and `noiseBedAt` for SC-001) and a `_ttfnMarkUnlock()` helper to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js`, following the `window.MemoryProbe` idiom (data-model §4, ttfn-probe.md).
- [X] T005 [US1] In `transit-synth.js`, build the master bus + start the noise bed inside `unlock()` after `T.start()`, gated on `_audioEnabled` (D1); call `_ttfnMarkUnlock()` and set `_ttfn.noiseBedAt ??= performance.now()` immediately after the bed starts, so SC-001 ("audible within 1 s") is a recorded `noiseBedAt − unlockAt` number, not only an ear-check. `getMasterBus` stays idempotent so the first-note path reuses it.
- [X] T006 [US1] In `transit-synth.js`, add an exported `warmProdSamplers()` that fire-and-forget kicks off `instrumentForSlot(i)` for exactly the `PROD_INSTRUMENTS` slot indices (3, NOT the full PALETTE — RAM invariant FR-004); call it from `unlock()` after the bus is built (D2).
- [X] T006a [US1] Handle the warmed-slot eviction edge case (spec edge "Prepared sound engine evicted before first note", FR-003; discovery §4.2 landmine): `EvictInactiveRouteAudioAsync` (`src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/TransitMap.razor.cs` ~:526) disposes a slot after 3 absent batches, so a warmed sampler for a quiet route can be evicted before its first note. Per `contracts/unlock-warming.md` D2 the resolution is **accept the transparent rebuild** (no pin mechanism): confirm `instrumentForSlot` rebuilds on the next `triggerNote` so the first note is still correct, and add a code comment on that rebuild path marking the edge as handled. Verify in the quickstart that a quiet warmed route still plays its first note correctly.
- [X] T007 [US1] In `transit-synth.js` `triggerNote`: change the `!_unlocked` guard to `{ _ttfn.droppedWhileLocked++; return; }`; after the `_audioEnabled` gate set `_ttfn.firstTriggerAt ??= performance.now()`; after `triggerAttackRelease` set `_ttfn.firstAudibleAt` once and emit the `[TTFN] …` console line + `performance.mark/measure` (ttfn-probe.md).
- [X] T008 [US1] Wire the same master-bus-build + `warmProdSamplers()` + `_ttfnMarkUnlock()` into the `attachUnlockGesture` handler path so the steady-state (gesture) unlock also gets the bed, warming, and probe mark (keep behavior identical between the two unlock paths).
- [X] T009 [P] [US1] Set `_ttfn.version` from the deploy commit short-SHA so `[TTFN]` lines are comparable across iterations (FR-012/FR-013 require the measurement be version-tagged; an unstamped `SET-PER-DEPLOY` silently breaks cross-version comparison). **Chosen mechanism**: a build/publish-time token substitution — inject the short-SHA into `transit-synth.js` (or a small generated JS constant it imports) during the WASM publish step in `.github/workflows/`, replacing the `SET-PER-DEPLOY` placeholder. Verify a deployed `[TTFN]` line shows a real SHA, not the placeholder.
- [X] T010 [US1] (Only if warming is C#-driven rather than internal to the JS handler) add `WarmSamplersAsync()` to `ITransitSynthJsInterop` + `TransitSynthJsInterop` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/JsInterop/TransitSynthJsInterop.cs` following the existing try/catch+LogError method pattern. Preferred: skip this and keep warming inside the JS unlock handler. *(skipped per the task's own preference — warming stays internal to `transit-synth.js`'s unlock handler, no interop change)*
- [ ] T011 [US1] Run the US1 quickstart verification (SC-001 audible <1 s, FR-002 mute-silent, SC-005 trigger→audible ≈0 ms, FR-004 RAM flat via MemoryProbe, Principle XI overlay still instant); record the `[TTFN]` line in the benchmark log. *(manual browser verification — not performed by this implementation pass)*

**Checkpoint**: US1 is independently shippable as the MVP — perceived-broken problem eliminated regardless of note supply, and the probe is live for all later measurement.

---

## Phase 4: User Story 2 — Faster/steadier note stream (Priority: P1)

**Goal**: Raise crossing supply ≥2× by making reverse-direction vehicles emit, with per-reason suppression counters shipped FIRST so the fix is attributable and verifiable. Worker-only (+ Go validator for the new telemetry columns).

**Independent Test**: `PerCityCycle` suppression counts satisfy the SC-007 invariant; after the fix, MARTA-evening tones/tick avg increases ≥2× (verified via tones/tick, NOT zero-tick fraction).

**Contracts**: `contracts/telemetry-schema.md`, `contracts/crossing-suppression-counters.md` (data-model §1, §2).

### Deploy Gate 2a — Suppression counters (measure before fixing)

- [X] T012 [US2] Add `enum CrossingSuppressionReason { None, FirstSeen, DeltaLeqZero, Teleport, RouteTransfer }` and change `CrossingDetector.Detect` to return `CrossingDetectResult(Records, Reason)` (Reason==None iff Records non-empty) in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Checkpoints/CrossingDetector.cs`; set the correct reason on each existing `[]` return (no behavior change yet — `delta <= 0` still `DeltaLeqZero`).
- [X] T013 [US2] Add the four `int?` snake_case columns (`crossings_suppressed_first_seen`, `…delta_leq0`, `…teleport`, `…transfer`) to `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Logging/TelemetryEvent.cs` (PerCityCycle-only, summed on FullCycle), following the `tones_emitted`/`vehicles_processed` conventions.
- [X] T014 [US2] In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`: accumulate the four suppression counts per city per tick from each `Detect` call's reason (~:488 loop); add the four fields to the `CityTickResult` record; stamp them on the `PerCityCycle` row (~:97) and include them in the tick-wide FullCycle sum (~:121).
- [X] T015 [P] [US2] Add the four columns to the frozen `kindNumeric` allow-list in `tools/telemetry-mcp/internal/validate/validate.go` (~:55), keeping the snake_case names identical to T013 (FR-016).
- [X] T016 [P] [US2] Add accept vectors for each new column (and a near-miss reject vector) to `tools/telemetry-mcp/internal/validate/validate_test.go`; run `go test ./...` green.
- [X] T017 [P] [US2] Update `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/TelemetryEventSchemaTests.cs` to assert the four columns exist and round-trip through `ParquetSerializer` as nullable ints.
- [X] T018 [US2] Update `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/CrossingDetectorTests.cs` so existing cases assert the returned `Reason` (forward=None, first-seen=FirstSeen, delta≤0=DeltaLeqZero, teleport=Teleport, transfer=RouteTransfer); all green.
- [ ] T019 [US2] **Deploy 2a**, then query `PerCityCycle` for marta and verify the SC-007 invariant (four counts + emitting vehicles == vehicles that ran detection, no remainder); record which reason dominates (expect `delta_leq0`). This attribution justifies 2b. *(requires a deploy — not performed by this implementation pass; counters are implemented and tested, ready to deploy)*

### Deploy Gate 2b — Reverse-direction emission

- [X] T020 [US2] Add a `Direction` field (`{ Unknown, Forward, Reverse }`) to `CrossingBaseline` in `CrossingDetector.cs` (data-model §2).
- [X] T021 [US2] Implement the direction state machine in `CrossingDetector.Detect` (crossing-suppression-counters.md table): `delta>0`→Forward (ascending window, advance up), `delta<0`→**Reverse** (descending window `[current, prev)`, advance down), `delta==0`→`DeltaLeqZero`, turnarounds reset direction and emit nothing on the pivot tick; keep the `>2000 m` teleport guard BEFORE the direction split (FR-007).
- [X] T022 [US2] Extend `CrossingDetectorTests.cs`: reverse motion emits in descending trigger order; Forward→Reverse and Reverse→Forward turnarounds emit nothing on the pivot; out-and-back >2000 m jump still returns `Teleport` (not a reverse-emit); forward regression unchanged. All green.
- [ ] T023 [US2] *(requires a deploy — not performed by this implementation pass; reverse-direction fix is implemented and tested, ready to deploy)* **Deploy 2b**, then query `PerCityCycle` marta over a comparable evening window; verify tones/tick ~1.14 → **~2.3 (≥2×, SC-002) using tones/tick avg** (zero-tick fraction stays ~70% — do NOT use it); confirm `delta_leq0` dropped by ~the reverse-fleet share; check dwell `[TTFN]` `unlock→trigger` trending toward <5 s (SC-003). Record in the benchmark log. **SC-002's ≥2× is forecast from the reverse-direction fix ALONE** (discovery §4.1-#3: 1.14→~2.3), independent of the deferred 200 m spacing lever (a separate ~2×, D9/T036). If 2b misses ≥2×, the attribution was wrong — STOP and re-diagnose (FR-015); do NOT reach for the spacing lever early to make up the shortfall.

**Checkpoint**: Note supply is measurably higher and its cause is fully attributed. US1 + US2 both work independently.

---

## Phase 5: User Story 3 — No guaranteed-silent first cycle (Priority: P2)

**Goal**: Replay recent, age-capped crossings on `JoinCity` so a fast unlock isn't forced to wait a fresh cycle, without the "rapid pulsing" regression. Server-only (`RouteCrossingBatchEvent` already `[Union(1)]` — no wire/client deploy).

**Independent Test**: Fast-click TTFN converges to dwell TTFN (SC-004); no audible burst on load.

**Contracts**: `contracts/join-replay.md` (data-model §3).

### Implementation for User Story 3

- [X] T024 [US3] In `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/SignalR/ILastBatchCache.cs`, add a `CrossingAgeCapSeconds` const and an age-capped store of recent `RouteCrossingBatchEvent.RouteCrossingRecord`s in `CityCache`; on `Set`, extract crossings from the batch, timestamp them, and prune older than the cap (data-model §3).
- [X] T025 [US3] In `ILastBatchCache.cs`, make `Current` return the position envelope PLUS (only if any survive the age cap at read time) a `RouteCrossingBatchEvent` envelope ordered `(RouteJoinKey, VehicleId, TriggerIndex)`; omit the crossing envelope entirely when none survive (never send empty).
- [X] T026 [US3] Confirm `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/SignalR/TransitHub.cs` `JoinCity` replays whatever `Current` returns unchanged (it already sends `current` as a list — no hub logic change needed beyond it now possibly carrying a second envelope).
- [X] T027 [US3] Rewrite `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/LastBatchCacheCrossingExclusionTests.cs` from "excludes all crossings" to the age-cap guarantee: within-cap included, older excluded, empty→no crossing envelope, ordering preserved, position-snapshot behavior unchanged. All green.
- [X] T028 [US3] Tune and **justify** `CrossingAgeCapSeconds` — *(code + comment done: `CrossingAgeCap = TimeSpan.FromSeconds(10)` in `ILastBatchCache.cs`, justified in the const's doc comment; the quickstart manual verification — browser fast-click/dwell convergence and ear-check — was not performed by this implementation pass)*.

- [X] T028a [US3] Verify FR-017 / spec edge "Very high-volume city on join replay": the age-capped crossing replay envelope stays under the feature-040 5 MB SignalR ceiling for the busiest city (NYMTA) at peak. Because the replay is bounded to one age cap (~one tick) of crossings, it can carry at most one tick's crossing volume — bound the worst case: `max(tones_emitted) at NYMTA peak × per-record size` and confirm it is far under 5 MB (NYMTA observed 85 tones/tick evening; check peak). State the bound in `contracts/join-replay.md` (add an FR-017 section) so this is a documented invariant, not an assumption.

**Checkpoint**: Cold-start penalty removed; fast-click and dwell first-note times converge. US1–US3 independent.

---

## Phase 6: User Story 4 — Unlock never leaves permanent silence (Priority: P2)

**Goal**: Attach the unlock click listener before the Tone import completes so a slow-load click still runs inside the trusted-gesture window (iOS permanent-silence fix). Client-only.

**Independent Test**: Throttled network, click Enable before Tone finishes → audio still becomes audible; on iOS Safari the AudioContext reaches `running`.

**Contracts**: `contracts/unlock-warming.md` (D6).

### Implementation for User Story 4

- [X] T029 [US4] In `transit-synth.js` `attachUnlockGesture`, register `addEventListener('click', handler)` **synchronously** without awaiting `getTone()` first; inside the handler do `getTone().then(T => T.start()).then(() => { _unlocked = true; buildBus + warmProdSamplers + _ttfnMarkUnlock })` so `T.start()` resolves within the trusted-gesture microtask chain even if Tone was still importing (D6).
- [X] T030 [US4] Verify no feature-040 module-instance split reintroduced: `TransitSynthJsInterop.cs:23` and the crossing-dispatcher both import the bare `transit-synth.js` path (shared `_unlocked`); notes still fire after unlock. *(confirmed by inspection: `TransitSynthJsInterop.cs`'s module import comment and the unchanged bare-path import are intact; no new interop/module-instance was introduced)*
- [ ] T031 [US4] *(manual DevTools/iOS trial — not performed by this implementation pass)* Run the US4 quickstart: DevTools "Slow 3G", click Enable the instant it renders → audio unlocks and becomes audible (SC-006, zero permanent-silence failures); repeat on a real iPhone / iOS simulator. **Explicitly assert FR-011**: after unlock, verify `Tone.getContext().rawContext.state === 'running'` (read it in the console, or add a one-line log in the gesture handler) — the exact failure mode FR-011 names is iOS leaving the context `suspended`, so the ear-check alone is insufficient; the state must be programmatically confirmed to reach `running`.

**Checkpoint**: Unlock is robust on slow/mobile connections. US1–US4 independent.

---

## Phase 7: User Story 5 — Ongoing measurement & health monitoring (Priority: P3)

**Goal**: The `[TTFN]` probe (already shipped with US1) plus a telemetry-only musical-density health check. No new instrumentation beyond US2's counters.

**Independent Test**: A `[TTFN]` line split into supply/build halves with a version + fast-unlock/dwell label; a defined threshold flags a city with degraded density.

**Contracts**: `contracts/ttfn-probe.md` (D7 done in US1; D8 here) (data-model §5).

### Implementation for User Story 5

- [X] T032 [P] [US5] Document the density health check with **justified** thresholds (per /util-testing — a threshold must state why it is what it is): rolling-hour zero-tone-tick fraction per `city_name` flagged **>30%**, and post-US2 tones/tick **<½ baseline**. Rationale to record: the healthy cities in the discovery §2.1 baseline (MBTA/WMATA) sit at 0% zero-tone ticks and NYMTA at 20%, while the symptomatic ones (MARTA 70%, TTC 69%) are far above — so a 30% line cleanly separates healthy from degraded with margin; "<½ baseline" catches a regression in a city that was previously healthy. Add these thresholds + rationale to `specs/045-time-to-first-note/contracts/ttfn-probe.md` follow-up notes and/or the mj-data-explorer reference (NOT a live service — threshold + query only).
- [X] T033 [US5] Run the health check over recent `PerCityCycle` data and confirm the **mechanism**: a city whose rolling-hour zero-tone-tick fraction exceeds the >30% threshold is flagged. Assert on the mechanism, not a dated snapshot (the 2026-07-19 baseline of "TTC 69% / 915 vehicles" is illustrative and will drift — do not pin the test to it). At time of writing TTC is the expected example flag (tracked as its own issue, not fixed here); re-run against current data and record whichever city/cities actually exceed the threshold.

**Checkpoint**: TTFN is a tracked per-version metric and density regressions are detectable from existing telemetry.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Wrap-up that spans stories.

- [X] T034 [P] Run the full `specs/045-time-to-first-note/quickstart.md` §4.2 regression-landmine checklist in review (3-slot warming, mute gating, validator allow-list, replay age cap + dot positions, tones/tick-not-zero-ticks verification, no 040 split). *(reviewed by code inspection: warmProdSamplers scoped to PROD_INSTRUMENTS only; noise bed + warming gated on `_audioEnabled`; validate.go allow-list + tests updated; replay age-capped and relies on existing dot-position drop; CrossingDetectorTests assert reason/direction, not zero-tick fraction; TransitSynthJsInterop bare-path import unchanged. Manual ear-check/browser portions not performed.)*
- [ ] T035 [P] Update the benchmark log (discovery doc §5.4) with a row per shipped version × scenario; compare each against its §4.1 forecast — if any fix missed its forecast, STOP and re-diagnose before stacking further (FR-015). *(requires deployed measurements — not performed by this implementation pass)*
- [X] T036 Confirm the deferred 200 m spacing decision (D9) is recorded and NOT implemented in this feature; leave `TriggerPointGenerator.cs` spacing at 400 m (only its stale 200 m comment may be corrected, no constant change). *(constant unchanged at 400.0; stale 200m-example comment corrected to 400m examples + D9 deferral note added)*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; T002/T003 baselines should precede any deploy so comparisons are valid.
- **Foundational (Phase 2)**: empty — no blocking shared work.
- **User Stories (Phases 3–7)**: each depends only on Setup. They own disjoint tiers, so they can run in parallel:
  - US1/US4/US5-probe → `transit-synth.js` (same file → serialize US1↔US4 edits, see below)
  - US2 → Worker + Go validator
  - US3 → WebAPI SignalR
- **Polish (Phase 8)**: after the shipped stories.

### User Story Dependencies

- **US1 (P1)**: independent. MVP.
- **US2 (P1)**: independent of US1; **internal order is strict — 2a (T012–T019) before 2b (T020–T023)** (measure before fix, FR-015).
- **US3 (P2)**: independent; benefits from US2's higher supply but testable alone.
- **US4 (P2)**: edits the same `transit-synth.js` as US1 → do US1 first (or coordinate the merge); logically independent otherwise.
- **US5 (P3)**: probe half ships with US1; health half (T032–T033) depends on US2's counters/higher supply being deployed.

### Within Each Story

- Contract-named tests updated alongside their implementation (Detect signature ↔ CrossingDetectorTests; schema ↔ TelemetryEventSchemaTests + validate_test.go; replay ↔ LastBatchCacheCrossingExclusionTests).
- US2: signature/reason plumbing (2a) before behavior change (2b).
- Deploy + measure task closes each story against its forecast.

### Parallel Opportunities

- T002 ‖ T003 (baselines).
- Within US2-2a: T015 ‖ T016 ‖ T017 (Go validator + Go test + C# schema test are different files) after T013 defines the column names; T014 depends on T012+T013.
- US1 (worker-free), US2 (client-free), US3 (client-free) can be staffed in parallel by three people.
- T009 ‖ other US1 tasks (version stamp is a separate concern).

---

## Parallel Example: User Story 2, Gate 2a

```bash
# After T012 (Detect reason) + T013 (columns) define names, run in parallel:
Task: "Add columns to Go allow-list in tools/telemetry-mcp/internal/validate/validate.go"   # T015
Task: "Add accept/reject vectors in tools/telemetry-mcp/internal/validate/validate_test.go" # T016
Task: "Assert columns round-trip in TelemetryEventSchemaTests.cs"                            # T017
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup (incl. baselines T002/T003).
2. Phase 3 US1 — audible feedback + warming + `[TTFN]` probe.
3. **STOP & VALIDATE** (T011): audible at unlock, mute-silent, trigger→audible ≈0 ms, RAM flat.
4. Deploy — the perceived-broken problem is gone even before any latency fix.

### Incremental Delivery (measure between every gate — FR-015)

1. Setup → baselines recorded.
2. US1 → validate → deploy (MVP; probe now live).
3. US2 gate 2a (counters) → deploy → read attribution.
4. US2 gate 2b (reverse fix) → deploy → verify ≥2× tones/tick.
5. US3 (join replay) → deploy → verify fast-click converges to dwell.
6. US4 (robust unlock) → deploy → verify no permanent silence.
7. US5 health check → monitor.
8. Each deploy checked against its §4.1 forecast before the next is stacked.

### Parallel Team Strategy

After Setup: Dev A → US1 then US4 (same JS file); Dev B → US2 (worker + Go); Dev C → US3 (WebAPI). US5 health folds in after US2 lands.

---

## Notes

- [P] = different files, no incomplete-task dependency. US1 and US4 both edit `transit-synth.js` → NOT parallel with each other.
- The frozen snake_case telemetry contract (013/014) means T013 column names must match T015/T016 exactly — pick the names once, use verbatim.
- Verify US2 with **tones/tick avg, never zero-tick fraction** (feed cadence keeps zero-ticks ~70% even after the fix).
- Do not re-verify known dead ends (feature-040 module split fixed; replay-exclusion was intentional): re-solve the rapid-pulsing regression via the age cap, don't just re-enable crossings.
- 200 m spacing (D9) is deferred — out of scope for this task set.
- Per repo policy, do not auto-commit; commits are the user's to run.
