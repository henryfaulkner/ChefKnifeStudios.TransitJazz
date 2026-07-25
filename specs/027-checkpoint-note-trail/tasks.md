---

description: "Task list for Checkpoint Crossing Trail (feature 027)"
---

# Tasks: Checkpoint Crossing Trail

**Input**: Design documents from `/specs/027-checkpoint-note-trail/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: No automated test tasks are generated. The project has no JS/Blazor UI test harness (consistent with features 016/017/021); the spec requests manual verification via `quickstart.md`. Test tasks would be wrong to fabricate here.

**Organization**: Tasks are grouped by user story (P1 → P2 → P3) so each can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 — maps to the user stories in spec.md
- Exact file paths are included in every task

## Path Conventions

Frontend-only Blazor WASM feature. Two project roots are touched:

- RCL: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`
- WebApp: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new trail module file and its lazy-import plumbing — the scaffold every story builds on.

- [X] T001 Create the new ES module file `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-trail.js` with the five tuning constants declared at the very top per contracts/trail-module.md (`MIN_SPEED=2.0`, `MAX_SPEED=30.0`, `LENGTH_SCALE=1.0`, `MAX_LEN_M=600`, `TRAIL_WIDTH=12`), the source/layer id constants (`SOURCE_ID='crossing-trail'`, `LAYER_ID='crossing-trail-layer'`), an empty module-scoped active-trails `Map`, and a `null` RAF handle. Export empty `ensureLayer/start/reset/setVisible` stubs and assign `window.CheckpointTrail`. (Use `checkpoint-pulse.js` as the structural reference.)
- [X] T002 Add the lazy importer `_getCheckpointTrail()` to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`, mirroring the existing `_getCheckpointPulse()` (import path `/_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/checkpoint-trail.js`, cached in a module-level variable).

**Checkpoint**: The module exists and is importable; no behavior yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Stand up the trail layer on the map and make it survive a basemap swap, plus the audio-independent duration helper. These are shared by all three stories and MUST be complete before any story behavior works.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Implement `ensureLayer(map)` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-trail.js`: idempotently add `SOURCE_ID` (empty FeatureCollection) and `LAYER_ID` as a MapLibre `line` layer with paint `'line-color': ['get','color']`, `'line-width': TRAIL_WIDTH` and layout `'line-cap':'round'`, `'line-join':'round'`, `'visibility':'visible'` (no `beforeLayer`). Guard with `map.getSource`/`getLayer` existence checks.
- [X] T004 In `map-interop.js` map-load handler, call `_getCheckpointTrail().then(t => t.ensureLayer(map))` right next to the existing `_getCheckpointPulse().then(p => p.ensureLayer(map))` so the trail layer exists before the first crossing. File: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`.
- [X] T005 Register the trail layer in BOTH `setMapStyle` restore paths (the `style.load` handler and the timed fallback) in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js` by calling `_getCheckpointTrail().then(t => t.ensureLayer(map))` after the existing vehicles/trigger-points/routes restoration, wrapped in try/catch (Principle VII — trail survives basemap swap; active trails need not be preserved).
- [X] T006 [P] Add the audio-independent duration helper to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js` per contracts/duration-helper.md: `export function durationSecondsFor(vehicleId)` using the same `durations[djb2(String(vehicleId)) % durations.length]` selection mapped to seconds via `{ '8n':0.25, '8n.':0.375, '4n':0.5 }` (120 BPM default). No `_unlocked` guard, no AudioContext, no Tone import. Add `durationSecondsFor` to the `window.TransitSynth = {...}` export.
- [X] T007 Refactor `triggerNote` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js` to derive its `duration` token from the same selection logic (a shared local that both `triggerNote` and `durationSecondsFor` use) so the audible note and the trail always agree on duration. Confirm pitch/instrument/cadence behavior is otherwise unchanged.
- [X] T008 Expose the duration getter to C#: add `DurationSecondsForAsync(string vehicleId)` to the existing TransitSynth JS-interop wrapper used by `TransitMap` (the lazy-module wrapper for `transit-synth.js`), invoking `durationSecondsFor` and returning a `double`, with a `0.25` fallback on interop error. File: the existing `TransitSynth*JsInterop` class under `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/` (locate the wrapper that already exposes `TriggerNoteAsync`).

**Checkpoint**: An empty trail layer is present on the map, survives style swaps, and a correct per-vehicle note duration (in seconds) is fetchable from C# regardless of audio state. No trail is drawn yet.

---

## Phase 3: User Story 1 - See a bus "sing" as it crosses a checkpoint (Priority: P1) 🎯 MVP

**Goal**: On a checkpoint crossing (checkpoints visible), a route-colored line anchored at the checkpoint grows forward along the route over the note's duration and disappears immediately when the note ends. Concurrent crossings on different routes render independently.

**Independent Test**: Run the app with checkpoints visible and audio unlocked; confirm each crossing produces a route-colored trail anchored at the checkpoint that grows along the route and vanishes when the note ends, and that two routes crossing at once each show their own color with no interference.

### Implementation for User Story 1

- [X] T009 [US1] Implement the RAF tick + `start(map, routeId, vehicleId, triggerIndex, anchorCoord, anchorDistanceM, color, speedMps, durationSec)` core in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-trail.js`: register an ACTIVE trail keyed `` `${vehicleId}::${triggerIndex}::${performance.now()}` `` storing `{routeId, color, anchorDistanceM, finalLengthM, durationMs, startTimeMs}`, and start the shared RAF loop if idle. For US1, set `finalLengthM` to a simple `speedMps * durationSec` (length refinement/clamps land in US2). No-op if `ChefMapAnimator.routeGeometry[routeId]` is missing.
- [X] T010 [US1] Implement the per-frame growth + geometry build in the RAF tick of `checkpoint-trail.js`: compute `t = clamp((now-startTimeMs)/durationMs,0,1)`; on `t>=1` delete the entry (immediate removal); else compute `headDistanceM = min(anchorDistanceM + finalLengthM*t, routeTotalLengthM)` and build a LineString by slicing `ChefMapAnimator.routeGeometry[routeId].coords` between `anchorDistanceM` and `headDistanceM`, interpolating both endpoints within their segments using `cumDist`. Push one `{geometry:LineString, properties:{color}}` feature per active trail and call `source.setData(...)` once per tick; null the RAF handle when no trails remain. File: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-trail.js`.
- [X] T011 [US1] Add `startCrossingTrail(containerDivId, routeId, vehicleId, triggerIndex, durationSec)` to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js` per contracts/trail-interop.md: look up the trigger feature in `ChefMap._triggerPointFeatures[routeId]` by `triggerIndex` (no-op if absent), resolve `anchorCoord`, `anchorDistanceM` (`properties.alongDistanceM`), `color` (`ChefMap._routeColorsByRouteId[routeId] || '#facc15'`), and `speedMps` from `ChefMapAnimator.vehicles[vehicleId].empiricalSpeed ?? .speed ?? 0`; then `await _getCheckpointTrail()`, `ensureLayer(map)`, and call `start(...)`.
- [X] T012 [US1] Add the `StartCrossingTrailAsync(string routeId, string vehicleId, int triggerIndex, double durationSeconds)` interop wrapper to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Map.razor.Helper.cs`, mirroring `PulseCheckpointAsync` (fire-and-forget, try/catch, `ElementId` first).
- [X] T013 [US1] Wire the trail into `OnCrossingsAsync` in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`: inside the existing `if (_checkpointsVisible && _map is not null)` block (right after `PulseCheckpointAsync`), fetch `durationSec` via the TransitSynth interop `DurationSecondsForAsync(crossing.VehicleId)` and call `await _map.StartCrossingTrailAsync(crossing.RouteId, crossing.VehicleId, crossing.TriggerIndex, durationSec)`, wrapped in try/catch with `Logger.LogWarning`. Do NOT place it inside the `if (_audioEnabled)` block — the trail must fire independent of audio (FR-001).

**Checkpoint**: User Story 1 is fully functional — crossings draw route-colored, route-following trails that disappear on note end, including while audio is muted/locked. This is the MVP.

---

## Phase 4: User Story 2 - Speed reads as trail length (Priority: P2)

**Goal**: Final trail length scales with speed × note duration, floored so a stopped bus still marks and capped at `MAX_LEN_M`.

**Independent Test**: Compare a fast vs. slow bus crossing checkpoints with equal-duration notes — the faster bus's final trail is visibly longer, a stopped/below-floor bus still produces a visible mark, and no trail exceeds ~600 m.

### Implementation for User Story 2

- [X] T014 [US2] Refine `finalLengthM` in `start(...)` of `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-trail.js` to the full FR-003 formula: `finalLengthM = Math.min(MAX_LEN_M, clamp(speedMps, MIN_SPEED, MAX_SPEED) * durationSec * LENGTH_SCALE)`, replacing the simple US1 product. Add a small local `clamp(v,lo,hi)` helper. This guarantees a non-zero mark via the `MIN_SPEED` floor (SC-004) and bounds length via the `MAX_SPEED`/`MAX_LEN_M` caps (SC-003).

**Checkpoint**: Trail length now encodes speed, with floor and cap enforced. User Stories 1 and 2 both work.

---

## Phase 5: User Story 3 - Trail respects checkpoint visibility (Priority: P3)

**Goal**: Trails are suppressed while checkpoint pulses are hidden, and toggling visibility off clears any active trail immediately.

**Independent Test**: With checkpoints hidden, no trails appear on crossings; with a trail actively growing, toggling checkpoint visibility off clears it immediately.

### Implementation for User Story 3

- [X] T015 [US3] Implement `reset(map)` and `setVisible(map, visible)` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-trail.js` mirroring `checkpoint-pulse.js`: `reset` clears the active-trails Map, cancels the RAF handle, and empties the source; `setVisible(false)` calls `reset(map)` then sets `LAYER_ID` layout `visibility:'none'`; `setVisible(true)` sets it back to `'visible'` (guarded by `getLayer` checks/try-catch).
- [X] T016 [US3] Add `setCrossingTrailVisibility(containerDivId, visible)` to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js` (await `_getCheckpointTrail()`, call `trail.setVisible(map, visible)`, try/catch with console.warn) per contracts/trail-interop.md.
- [X] T017 [US3] Add the `SetCrossingTrailVisibilityAsync(bool visible)` wrapper to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Map.razor.Helper.cs`, mirroring `SetCheckpointVisibilityAsync`.
- [X] T018 [US3] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`, wherever the checkpoint-visibility setting currently drives `SetCheckpointVisibilityAsync(visible)` (and wherever `_checkpointsVisible` is updated from the settings/event handler), also call `await _map.SetCrossingTrailVisibilityAsync(visible)` so one toggle suppresses and clears both the pulse and the trail (FR-006). The crossing-time suppression already holds because `StartCrossingTrailAsync` is only called inside the `_checkpointsVisible` block (from T013).

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verification and final consistency checks across the feature.

- [X] T019 Build the solution (`dotnet build ChefKnifeStudios.TransitJazz.sln`) and confirm no compile errors in the edited C# files (`Map.razor.Helper.cs`, `TransitMap.razor.cs`, the TransitSynth interop wrapper).
- [ ] T020 Run the full manual `quickstart.md` walkthrough (steps 1–10 + regression checks): confirm trail-on-crossing, route-following growth, speed→length, immediate disappearance, hidden-checkpoints suppression, clear-on-toggle-off, muted-audio-still-shows-trail, two-route independence, 12px width match, and survival across a GIS basemap swap; and confirm the existing pulse + audible notes are unchanged.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. T001 → T002 (T002 imports the file from T001).
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS all user stories.
  - T003 depends on T001. T004/T005 depend on T002+T003. T006/T007 are in `transit-synth.js` (T007 depends on T006's shared selection). T008 depends on T006.
- **User Stories (Phase 3+)**: All depend on Foundational completion.
  - US1 is the MVP and lands the core trail-drawing path.
  - US2 refines one line in `start()` (depends on US1's `start`).
  - US3 adds visibility/reset (depends on the module + interop existing from US1/Foundational).
- **Polish (Phase 6)**: After all desired stories.

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational. Self-contained MVP.
- **US2 (P2)**: Depends on US1 (edits `finalLengthM` inside the `start` written in T009). Independently testable (length behavior).
- **US3 (P3)**: Depends on Foundational + the module/interop from US1. Independently testable (visibility behavior). Note: US3's `reset`/`setVisible` could technically be authored right after Foundational, but its visible effect (clearing active trails) is only observable once trails draw (US1).

### Within Each User Story

- JS module behavior before interop wrapper before `TransitMap` wiring (matches T009→T010→T011→T012→T013 in US1).

### Parallel Opportunities

- T006 (transit-synth duration helper) is `[P]` — a different file from the `checkpoint-trail.js`/`map-interop.js` work, so it can proceed alongside T003–T005.
- Within US1, T009 and T010 touch the same file (`checkpoint-trail.js`) and must be sequential; T011/T012 are separate files but logically follow the module.
- US2 (T014) and US3 (T015–T018) touch largely separate concerns and could be done in parallel by two developers once US1 lands (T014 edits `start`; T015 adds new functions — both in `checkpoint-trail.js`, so coordinate that one file).

---

## Parallel Example: Foundational Phase

```bash
# T006 is in transit-synth.js — independent of the checkpoint-trail.js / map-interop.js work:
Task: "Add durationSecondsFor() to transit-synth.js (T006)"
# ...can run alongside:
Task: "Implement ensureLayer + map-load/style-swap registration in checkpoint-trail.js / map-interop.js (T003–T005)"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup (T001–T002)
2. Phase 2 Foundational (T003–T008) — CRITICAL, blocks all stories
3. Phase 3 User Story 1 (T009–T013)
4. **STOP and VALIDATE**: trails draw on crossings, follow the route, vanish on note end, work while muted. Demo-able MVP.

### Incremental Delivery

1. Setup + Foundational → trail layer present, duration available
2. US1 → core trail (MVP) → validate via quickstart steps 1, 2, 4, 7, 8, 9
3. US2 → speed→length → validate step 3
4. US3 → visibility gating/clear → validate steps 5, 6
5. Polish → build + full quickstart (steps 1–10) + basemap-swap step 10

---

## Notes

- No automated tests are included by design (no harness; spec asks for manual quickstart verification). Verification is T020.
- [P] = different file, no dependency on an incomplete task.
- The trail must NEVER be gated on `_audioEnabled`/`_unlocked` (FR-001) — only on `_checkpointsVisible`.
- All five tuning constants live at the top of `checkpoint-trail.js` (FR-010); no magic numbers elsewhere.
- Commit after each task or logical group.
