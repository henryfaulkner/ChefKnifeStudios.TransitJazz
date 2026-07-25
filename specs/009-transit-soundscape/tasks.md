# Tasks: Emergent Transit Soundscape v1

**Feature**: 009-transit-soundscape
**Input**: Design documents from `specs/009-transit-soundscape/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interop-surface.md, quickstart.md

**Tests**: Per plan.md, verification is **manual** via `quickstart.md` (seven observation sessions mapped to SC-001 … SC-007). No automated test tasks are generated.

**Organization**: Tasks are grouped by user story to enable independent implementation and verification of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- RCL: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/` (folder name renamed; assembly remains `ChefKnifeStudios.TransitJazz.Client.Shared`)
- WebApp: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify baseline assumption that 008 artifacts do not exist (FR-013 / SC-008), so the feature can be implemented as pure additions.

- [x] T001 Verify absence of 008 artifacts by searching the repo for `Checkpoint*.cs`, `checkpoints.json`, `checkpoint-audio.js`, and any crossing-detection code inside `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/vehicle-animator.js`; document the empty result in a one-line note appended to `specs/009-transit-soundscape/plan.md` § Summary if not already present (per plan.md line 10, this should already be verified — task is a re-check)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The C# trigger-point model and generator are blocking prerequisites for both crossing detection (US2) and instrument/pitch assignment (US1). They must exist before any JS module work.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T002 [P] Create `TriggerPoint` record in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Models/TriggerPoint.cs` per data-model.md § TriggerPoint (`public sealed record TriggerPoint(int Index, double AlongDistanceM)` in namespace `ChefKnifeStudios.TransitJazz.Client.Shared.Models`)
- [x] T003 [P] Create `ITriggerPointGenerator` interface in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/ITriggerPointGenerator.cs` exposing `IReadOnlyList<TriggerPoint> Generate(double[][] coords, double[] cumDist)` (spacing is internal to the impl)
- [x] T004 Implement `TriggerPointGenerator` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/TriggerPointGenerator.cs` per data-model.md § "Trigger-point generation" (const `TriggerSpacingMeters = 200` at top of class with SC-005-derivation comment from research.md § R3; first trigger at `spacing` not 0; binary search over `cumDist`; warn-log for routes shorter than spacing). Depends on T002.

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 - Hear emergent music from live transit (Priority: P1) 🎯 MVP

**Goal**: A first-time visitor clicks once on `/transit-map` and within 30 seconds begins hearing route-distinct, pitch-distinct, harmonically-compatible notes driven by real bus movement.

**Independent Test**: Run quickstart.md Test 1 (SC-001 first-note latency ≤ 30 s), Test 2 (SC-002 distinct timbres for ≥ 3 routes over 2 min), and Test 3 (SC-003 same-route harmonic compatibility over 2 min). Together these prove the elevator-pitch: instrument-per-route + tone-per-vehicle + emergent harmony.

### Implementation for User Story 1

- [x] T005 [P] [US1] Create `ITransitSynthJsInterop` interface in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/JsInterop/ITransitSynthJsInterop.cs` with `Task UnlockAsync()`, `Task<bool> IsUnlockedAsync()`, `Task TriggerNoteAsync(string routeId, string vehicleId)`, and `IAsyncDisposable` per contracts/interop-surface.md § 2
- [x] T006 [P] [US1] Create `transit-synth.js` ES module in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js`: lazy `import('https://esm.sh/tone@15')` inside `getTone()` (research.md § R1); `_unlocked` flag with `unlock()` calling `await Tone.start()`; `PALETTE` array of 6 voices and `instrumentFor(routeId)` per data-model.md § "Instrument assignment" (apply `volume: -12` to MembraneSynth + MetalSynth per research.md § R4 implementer notes); `SCALE` const `[48, 51, 53, 55, 58, 60, 63, 65, 67, 70]` and `pitchFor(vehicleId)` per data-model.md § "Pitch derivation"; `djb2` hash; `triggerNote(routeId, vehicleId)` calling `instrument.triggerAttackRelease(Tone.Frequency(pitch, 'midi').toFrequency(), '8n')` only when `_unlocked` (silent no-op otherwise — no buffering); exports `unlock`, `isUnlocked`, `triggerNote`, `dispose`; expose under `window.TransitSynth` namespace per contracts/interop-surface.md § "Out-of-scope" note. Console-log `[TransitSynth] unlocked` on first successful unlock (quickstart Test 1).
- [x] T007 [US1] Implement `TransitSynthJsInterop` class in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/JsInterop/TransitSynthJsInterop.cs` mirroring `AudioPlayerJsInterop.cs` pattern: lazy `IJSObjectReference` to `./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/transit-synth.js?v={Guid}`; implements `IAsyncDisposable` (calls module `dispose` then disposes JS reference); each method wraps the corresponding JS export in try/catch logging via existing logger pattern. Depends on T005, T006.
- [x] T008 [US1] Register `ITransitSynthJsInterop` → `TransitSynthJsInterop` as Scoped in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Program.cs` alongside the existing JsInterop registrations. Depends on T007.
- [x] T009 [US1] Edit `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor` to add a top-level "Click to enable audio" hint overlay element (single `<div>` with inline style or minimal class — no styling contract per contracts/interop-surface.md "Out-of-scope") that is removed once the synth reports unlocked.
- [x] T010 [US1] Edit `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` to: inject `ITransitSynthJsInterop`; bind a window-level click handler (or use the existing page click capture) that calls `UnlockAsync()` once on first user gesture; after unlock, set a flag that hides the hint overlay (T009); dispose the interop in `DisposeAsync`. Depends on T008, T009.

**Checkpoint**: Audio output path is wired but no notes will fire until US2 detection lands. Manual partial verification: clicking the page should produce one `[TransitSynth] unlocked` console log and no errors (quickstart Test 7 pre-condition).

---

## Phase 4: User Story 2 - Audio reflects real movement, not stationary noise (Priority: P1)

**Goal**: Crossing detection fires exactly one note per genuine forward crossing, suppresses stopped/jittering/teleporting vehicles, and handles mid-route appearances and missing-geometry cases per the spec edge cases.

**Independent Test**: Run quickstart.md Test 4 (SC-004 stopped-bus suppression: ≤ 1 note in 60 s) and the edge-case spot checks (vehicle teleport produces no burst; mid-route appearance produces no retroactive notes; missing route geometry silently drops events).

### Implementation for User Story 2

- [x] T011 [P] [US2] Create `ICheckpointTrackerJsInterop` interface in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/JsInterop/ICheckpointTrackerJsInterop.cs` with `Task ConfigureRouteAsync(string routeId, TriggerPoint[] triggerPoints, DotNetObjectReference<object> dotNetRef)`, `Task ClearAsync()`, and `IAsyncDisposable` per contracts/interop-surface.md § 1
- [x] T012 [P] [US2] Create `checkpoint-tracker.js` ES module in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-tracker.js`: `routeTriggerPoints = new Map()`, `vehicleState = new Map()`, `_dotNetRef = null`; `configureRoute(routeId, triggerPoints, dotNetRef)` stores trigger points (sorted by index) and captures dotNetRef on first non-null; install single `_tickHook` on `ChefMapAnimator` only on first configureRoute call; implement `onTick(positionEvents)` per research.md § R2 algorithm — for each `{vehicleId, routeId, currIndex}`: route-change → reset state, no fire; first observation → baseline state, no fire; `cumDist[currentIndex] - cumDist[lastTriggeredIndex] > 2000` teleport → snap baseline, no fire; `delta <= 0` → no fire; `delta > 0` → emit CrossingEvent for triggers with `lastTriggeredIndex < triggerIndex <= currentIndex` honoring `cooldownMs = 2000`; batch all crossings for the tick and call `_dotNetRef.invokeMethodAsync('OnCrossingsAsync', batch)` only when batch is non-empty; ordering `(routeId, vehicleId, triggerIndex)` per contracts/interop-surface.md § 1 callbacks; `clear()` empties maps, releases dotNetRef, detaches tick hook; expose under `window.CheckpointTracker` namespace. Console-log `[CheckpointTracker]`-prefixed lines per plan.md § Constitution Check IV.
- [x] T013 [US2] Implement `CheckpointTrackerJsInterop` class in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/JsInterop/CheckpointTrackerJsInterop.cs` mirroring `AudioPlayerJsInterop.cs` pattern: lazy `IJSObjectReference` to `./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/checkpoint-tracker.js?v={Guid}`; implements `IAsyncDisposable` (calls `clear` then disposes JS reference); methods wrap JS exports in try/catch. Depends on T011, T012.
- [x] T014 [US2] Edit `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/vehicle-animator.js`: at the end of `tick()` after the existing `setData(...)` call, build `positionEvents` array (one entry per vehicle whose `currentPos` advanced this tick, omit idle/unchanged vehicles) using existing `findNearestIndex(routeData.coords, state.currentPos)` for `currIndex`; emit `window.CheckpointTracker?.onTick?.(positionEvents)` per contracts/interop-surface.md § 3. No detection logic in this file.
- [x] T015 [US2] Register `ICheckpointTrackerJsInterop` → `CheckpointTrackerJsInterop` as Scoped in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Program.cs` alongside `ITriggerPointGenerator` (also register here as Scoped) and the synth interop from T008. Depends on T004, T013.
- [x] T016 [US2] Edit `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` to: inject `ITriggerPointGenerator` and `ICheckpointTrackerJsInterop`; on each route geometry load (hook the existing route-geometry-load callback), call `_triggerPointGenerator.Generate(coords, cumDist)` and `_tracker.ConfigureRouteAsync(routeShortName, triggerPoints, _dotNetRef)` where `_dotNetRef = DotNetObjectReference.Create(this)`; add `[JSInvokable] public Task OnCrossingsAsync(CrossingEventDto[] crossings)` that iterates ordered batch and calls `_synth.TriggerNoteAsync(c.RouteId, c.VehicleId)` for each, swallowing per-call exceptions with a `console.warn` (via logger) per contracts/interop-surface.md § 1 "Error contract"; declare a private `CrossingEventDto` record `(string VehicleId, string RouteId, int TriggerIndex)` inside the file; dispose `_dotNetRef`, `_tracker`, and `_synth` in `DisposeAsync`. Depends on T010, T015.

**Checkpoint**: End-to-end audio works. Run quickstart Tests 1–4 + 7 to verify SC-001, SC-002, SC-003, SC-004, SC-007.

---

## Phase 5: User Story 3 - Soundscape rhythm tracks bus speed (Priority: P2)

**Goal**: Per-vehicle note cadence sits inside the SC-005 band (≥ 1 note / 30 s, ≤ 1 note / 5 s) during continuous motion at typical urban bus speeds.

**Independent Test**: Run quickstart.md Test 5 (SC-005 cadence band, median of 5 successive intervals from one moving vehicle in [5 s, 30 s]).

### Implementation for User Story 3

- [ ] T017 [US3] Run quickstart.md Test 5 with the default `TriggerSpacingMeters = 200` (set in T004). If median cadence is out of band, tune the constant in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/TriggerPointGenerator.cs` per research.md § R3 implementer notes (try 150 if intervals too long, 250 if too short) and re-run Test 5. Update the constant's derivation comment to record the final tuned value and the observation that justified it.

**Checkpoint**: All three user stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: SC-006 regression verification, full SC-007 sweep, console-log housekeeping, and memory/spec sign-off.

- [ ] T018 [P] Run quickstart.md Test 6 (SC-006 no time-to-first-vehicle regression). Verify in browser devtools Network tab that no `tone` request fires before the unlock click. If regression > 10%, profile and address the cause (most likely: trigger-point generation running per tick instead of once per route load).
- [ ] T019 [P] Run quickstart.md Test 7 (SC-007 zero console errors over 5 min spanning pre-interaction, click transition, and steady state).
- [ ] T020 [P] Run quickstart.md edge-case spot checks (no active vehicles silent; vehicle teleport no burst; route geometry not yet loaded silently dropped).
- [x] T021 Audit console-log noise across `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js` and `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-tracker.js`; keep the one-time `[TransitSynth] unlocked` line plus warnings (per-tick crossing logs only if explicitly enabled by a `_debug` flag, default off) so a 5 min session does not flood devtools.
- [x] T022 Update `CLAUDE.md` SPECKIT marker block to point to `specs/009-transit-soundscape/plan.md` as the active feature plan (replace any prior 008 reference) per plan.md § "Agent Context".
- [ ] T023 Update `MEMORY.md` (`C:\Users\hfaul\.claude\projects\C--Projects-ChefKnifeStudios-TransitJazz\memory\MEMORY.md`) with measured cadence and palette feedback from the listening sessions; remove the stale `project_008_checkpoint_audio.md` note per quickstart.md § Sign-off.
- [ ] T024 Annotate `specs/009-transit-soundscape/spec.md` Status from "Draft" to "Verified" after all 7 quickstart tests pass.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: No real dependency on Setup beyond confirming the baseline; BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational completion (specifically T002 record exists; T004 not strictly required for US1 alone but already done by phase order)
- **User Story 2 (Phase 4)**: Depends on Foundational completion AND on US1's `ITransitSynthJsInterop` registration (T008) being present so `TransitMap.razor.cs` can call `TriggerNoteAsync` from `OnCrossingsAsync`. In practice US1 and US2 are co-P1 and ship together to deliver the elevator pitch; US1 alone produces silence (no detection), US2 alone has no audio backend.
- **User Story 3 (Phase 5)**: Depends on US1+US2 complete (tuning task, observational only)
- **Polish (Phase 6)**: Depends on all user stories complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2. Standalone deliverable = audible synth wired but silent until US2 lands.
- **US2 (P1)**: Logically depends on US1 because `OnCrossingsAsync` invokes the synth interop. If staffing demands true parallel work, US2's tracker + animator edit can be developed against a stub synth and re-wired when US1's synth lands.
- **US3 (P2)**: Pure tuning + observation; must come after US1+US2.

### Within Each User Story

- Models before services
- Interfaces before implementations
- JS modules and C# interop wrappers can be developed in parallel ([P] tasks)
- DI registration after the implementation it registers
- Page wiring after DI registration

### Parallel Opportunities

- **Phase 2**: T002 and T003 are different files with no dependency → run in parallel.
- **Phase 3 (US1)**: T005 (C# interface) and T006 (JS module) are different files → run in parallel.
- **Phase 4 (US2)**: T011 (C# interface) and T012 (JS module) are different files → run in parallel.
- **Phase 6**: T018, T019, T020 are independent manual observation runs (can be done in the same session) → marked [P].
- **Cross-story**: With multiple developers, after Phase 2 completes, US1's T005/T006 and US2's T011/T012 can be developed simultaneously (four different files).

---

## Parallel Example: User Story 1 + User Story 2 kickoff after Foundational

```text
# After T004 completes, four independent file creations can run in parallel:
Task T005 [US1]: Create ITransitSynthJsInterop.cs
Task T006 [US1]: Create transit-synth.js
Task T011 [US2]: Create ICheckpointTrackerJsInterop.cs
Task T012 [US2]: Create checkpoint-tracker.js
```

```text
# Within Phase 6, three observation runs can be folded into a single browser session:
Task T018: SC-006 time-to-first-vehicle measurement
Task T019: SC-007 5-min console error sweep
Task T020: Edge-case spot checks
```

---

## Implementation Strategy

### MVP First (US1 + US2 together)

The elevator pitch requires both stories — US1 alone is a silent synth, US2 alone has no audio backend. So the MVP is **Phases 1 + 2 + 3 + 4**, delivered together:

1. Phase 1: Setup (one re-check task)
2. Phase 2: Foundational (TriggerPoint + generator)
3. Phase 3: US1 (synth + unlock)
4. Phase 4: US2 (tracker + detection + page wiring)
5. **STOP and VALIDATE**: quickstart Tests 1, 2, 3, 4, 7
6. Deploy/demo if all pass

### Incremental Delivery After MVP

1. Add US3 tuning pass → quickstart Test 5 → re-deploy
2. Polish pass → quickstart Tests 6, 7 (full), edge-case spot checks → final sign-off

### Single-Developer Sequencing (this project)

This is a one-person project. Strict sequential order is the simplest path:

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011 → T012 → T013 → T014 → T015 → T016 → quickstart Tests 1–4 + 7 → T017 → quickstart Test 5 → T018 → T019 → T020 → T021 → T022 → T023 → T024.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- All verification is manual per `quickstart.md` — no automated test tasks
- Commit after each task or logical group
- Stop at the MVP checkpoint (after T016) to validate the elevator pitch before tuning
- Avoid: introducing test frameworks (not in scope), per-route mute UI (FR-015), visible trigger-point markers (FR-014), bundling Tone.js (research.md § R1 alternatives rejected)
