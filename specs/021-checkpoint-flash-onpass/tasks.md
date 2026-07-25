---

description: "Task list for feature 021 — Checkpoint Flash on Bus Pass & Bus-Visibility Toggle"
---

# Tasks: Checkpoint Flash on Bus Pass & Bus-Visibility Toggle

**Input**: Design documents from `/specs/021-checkpoint-flash-onpass/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: No automated tests requested. This project has no UI test harness (consistent with features 015–020); verification is manual via `quickstart.md`. No test tasks are generated.

**Organization**: Tasks are grouped by user story. US1 (checkpoint pulse, P1) is the MVP; US2 (bus-visibility toggle, P2) is independent and additive.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 = checkpoint pulse; US2 = bus-visibility toggle
- All paths are relative to repo root `C:\Projects\ChefKnifeStudios.TransitJazz\`

## Path Conventions

Frontend-only feature. All changes under `src/Client/`. Namespace root `ChefKnifeStudios.MartaJazz`.

- RCL (shared components/JS/resx): `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`
- WASM app (page): `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No project initialization needed — existing solution. This phase just confirms the working baseline.

- [X] T001 Verify the solution builds and the WebApp runs with live data (Aspire AppHost or WebApp+WebAPI+Worker), so buses animate and checkpoint crossings fire `OnCrossingsAsync`. Reference: `quickstart.md` Prerequisites.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Stand up the dedicated pulse overlay layer + JS module that US1 depends on. Nothing user-visible yet.

**⚠️ CRITICAL**: US1 (pulse) cannot function until this phase is complete.

- [X] T002 [P] Create the pulse ES module `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-pulse.js` per `contracts/pulse-interop.md`. Export `ensureLayer(map)`, `start(map, routeId, triggerIndex, coordinates, color)`, `reset(map)`. Maintain an internal active-pulse map keyed `"{routeId}::{triggerIndex}"` with `{coordinates, color, startTimeMs}` (data-model.md §5). Implement a single `requestAnimationFrame` loop that, each frame, computes eased `radius` (R_START≈4 → R_END≈24, ease-out cubic) and `opacity` (O_START≈0.6 → 0) over `DURATION_MS≈600`, builds one Point FeatureCollection with per-feature props `{radius, color, opacity}`, calls `source.setData(fc)` once, drops finished pulses (t≥1), and reschedules only while pulses remain. `ensureLayer` idempotently adds source `checkpoint-pulse` (empty FC) and layer `checkpoint-pulse-layer` (circle, data-driven paint: `circle-radius=['get','radius']`, `circle-color=['get','color']`, `circle-opacity=['get','opacity']`) inserted ABOVE `trigger-points-layer`. `reset` clears active pulses, sets source to empty FC, cancels the RAF.

- [X] T003 Wire the pulse module into `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`: add `ChefMap.pulseCheckpoint(containerDivId, routeId, triggerIndex)` that (a) no-ops if `trigger-points-layer` visibility is `'none'` (FR-008), (b) resolves the checkpoint coordinate from `ChefMap._triggerPointFeatures[routeId]` by matching `properties.triggerIndex === triggerIndex` (no-op + warn if not found), (c) resolves color `ChefMap._routeColorsByRouteId[routeId] || '#facc15'` (FR-004), (d) calls `CheckpointPulse.ensureLayer(map)` then `CheckpointPulse.start(map, routeId, triggerIndex, coords, color)`. Import/reference the `checkpoint-pulse.js` module consistent with how other JS modules are loaded in this file.

- [X] T004 Make the pulse layer survive a basemap style swap (FR-012) in `map-interop.js` `setMapStyle`: inside the existing `map.once('style.load', …)` callback (alongside the vehicles-layer re-add), call `CheckpointPulse.ensureLayer(map)` to re-add the empty `checkpoint-pulse` source+layer above `trigger-points-layer`, and `CheckpointPulse.reset(map)` to drop any in-flight pulses.

- [X] T005 Gate pulses on checkpoint visibility (FR-008) in `map-interop.js` `setCheckpointVisibility`: when `visible === false`, also hide `checkpoint-pulse-layer` and call `CheckpointPulse.reset(map)` (no orphaned animations); when `visible === true`, restore `checkpoint-pulse-layer` visibility (do NOT replay past pulses).

- [X] T006 Add the C# interop wrapper `PulseCheckpointAsync(string routeId, int triggerIndex)` to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Map.razor.Helper.cs` — invokes `ChefMap.pulseCheckpoint` with `ElementId`, wrapped in try/catch + console log, matching the sibling wrappers (e.g. `SetCheckpointVisibilityAsync`).

**Checkpoint**: Pulse plumbing exists end-to-end (C# → JS → overlay layer), but nothing calls it yet.

---

## Phase 3: User Story 1 - Checkpoints pulse when a bus passes (Priority: P1) 🎯 MVP

**Goal**: When a bus passes a checkpoint, an expanding, route-colored ring pulses at that checkpoint and fades out. Fires regardless of audio mute; honors the route-selection filter; suppressed when checkpoints are hidden.

**Independent Test**: With Checkpoints ON and buses moving, watch a checkpoint as a bus passes — confirm an expanding ring in the route's color appears and fades (~0.6s), and no dot is left altered. See `quickstart.md` Section A.

### Implementation for User Story 1

- [X] T007 [US1] Split `OnCrossingsAsync` in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` per `contracts/crossing-handler.md`: remove the top-level `if (!_audioEnabled) return;` early-return. For each crossing, keep the selection gate (`if (selected.Count > 0 && !selected.Contains(crossing.RouteId)) continue;`). Then ALWAYS call `await _map.PulseCheckpointAsync(crossing.RouteId, crossing.TriggerIndex)` (when `_map is not null`, wrapped in try/catch + `Logger.LogWarning`). Only call `TransitSynth.TriggerNoteAsync(...)` when `_audioEnabled` (its existing try/catch retained). Pulse fires independently of audio (research R4); anti-flicker stays upstream in `checkpoint-tracker.js` (FR-006).

**Checkpoint**: US1 fully functional — checkpoint pulses on passes, correct per-route color, audio-independent, selection-scoped, suppressed when checkpoints hidden, survives basemap swap. This is a shippable MVP.

---

## Phase 4: User Story 2 - Toggle bus visibility from settings (Priority: P2)

**Goal**: A persisted "Buses" toggle in the settings drawer (default OFF) controls whether bus markers are drawn; toggling applies immediately and is honored from first render and after a basemap swap.

**Independent Test**: Fresh load shows the Buses toggle OFF with no bus markers; toggling ON/OFF shows/hides markers without reload; the choice persists across reload. Bus passes still pulse checkpoints while buses are hidden. See `quickstart.md` Section B.

### Implementation for User Story 2

- [X] T008 [P] [US2] Add `BusVisibilitySettingChangedEventArgs : IEventArgs` (one `required bool IsBusesVisible { get; init; }`) at `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/EventArgs/BusVisibilitySettingChangedEventArgs.cs`, mirroring `GisSettingChangedEventArgs.cs`.

- [X] T009 [P] [US2] Add the resx entry `SettingBusesVisible` = `Buses` to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Resources/RouteFilterResources.resx` (EN only; `.es` deferred), matching the existing `SettingAudioEnabled` / `SettingCheckpointsVisible` / `SettingStreetMap` entries.

- [X] T010 [US2] Add the setting to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Models/Settings.cs`: `[ObservableProperty] [property: Description("SettingBusesVisible")] private bool _isBusesVisible = false;` (default OFF — FR-009a). The reflection-driven `SettingsBlade` will render the checkbox automatically (no `.razor` change). (Depends on T009 so the rendered label resolves.)

- [X] T011 [US2] Add the producer switch arm in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Blades/SettingsBlade.razor.cs` `HandleSettingPressed`: `nameof(Settings.IsBusesVisible) => new BusVisibilitySettingChangedEventArgs { IsBusesVisible = value },`. (Depends on T008, T010.)

- [X] T012 [US2] Add the consumer branch in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` `HandleSettingsEventReceived`: `if (e is BusVisibilitySettingChangedEventArgs buses) { InvokeAsync(async () => { if (_map is not null) await _map.SetVehiclesVisibleAsync(buses.IsBusesVisible); }); return; }` (FR-009b). (Depends on T008.)

- [X] T013 [US2] Honor the persisted setting on initial render and after basemap swap in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`: replace BOTH hardcoded `await _map.SetVehiclesVisibleAsync(true);` calls (one in `OnAfterRenderAsync`, one in the `GisSettingChangedEventArgs` handler) with `await _map.SetVehiclesVisibleAsync(SettingsService.GetSettings().IsBusesVisible);` (FR-009c, FR-011). (Depends on T010.)

**Checkpoint**: US1 AND US2 both work independently — pulses fire on passes; the Buses toggle hides/shows markers (default hidden, persisted, immediate, basemap-swap-safe) and never affects pulses.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Visual tuning and full manual validation.

- [X] T014 Tune pulse constants in `checkpoint-pulse.js` (`DURATION_MS`, `R_END`, `O_START`, easing, and whether to use a filled circle vs. a stroke ring) so the pulse reads as a clear, non-distracting expanding ping against live route lines (SC-001/SC-002). Confirm concurrent multi-route pulses don't visually interfere (SC-006, FR-013).
- [ ] T015 Run the full `quickstart.md` validation (Sections A and B) against live data; confirm all FR/SC checks pass and there are no console errors from `pulseCheckpoint` / `SetVehiclesVisibleAsync`, and no checkpoint is left in a non-resting state over 10+ minutes (SC-003).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: After Setup. BLOCKS US1 (the pulse path).
- **US1 (Phase 3)**: After Foundational (needs T002–T006).
- **US2 (Phase 4)**: After Setup. **Independent of Foundational and US1** — touches the settings/visibility path only. Can run in parallel with Phase 2/3.
- **Polish (Phase 5)**: After US1 (T014) and after both stories (T015).

### User Story Dependencies

- **US1 (P1)**: Depends on Phase 2 foundational pulse plumbing. No dependency on US2.
- **US2 (P2)**: No dependency on US1 or on Phase 2. Independently testable/deliverable.

### Within Each Story

- US1: single task (T007), depends on all of Phase 2.
- US2 task order: T008 + T009 (parallel) → T010 → T011 / T012 / T013. T012 needs T008; T011 needs T008+T010; T013 needs T010. T008 and T009 are the only true `[P]` pair (different files, no interdeps).

### Parallel Opportunities

- T002 (new `checkpoint-pulse.js`) is `[P]` vs. the rest of Phase 2 only at creation; T003–T005 all edit `map-interop.js` (same file → sequential among themselves). T006 (different file) can proceed once the JS contract from T002/T003 is settled.
- US2's T008 and T009 are independent files → parallel.
- Entire US2 (Phase 4) can be developed in parallel with Phase 2/Phase 3 by a second developer (no shared files: US2 touches `Settings.cs`, `SettingsBlade.razor.cs`, `RouteFilterResources.resx`, new EventArgs, and the settings-related lines of `TransitMap.razor.cs`; US1 touches only `OnCrossingsAsync` in `TransitMap.razor.cs` — coordinate the two `TransitMap.razor.cs` edits, otherwise disjoint).

---

## Parallel Example: User Story 2

```text
# T008 and T009 touch different files — launch together:
Task: "Create BusVisibilitySettingChangedEventArgs in .../EventArgs/BusVisibilitySettingChangedEventArgs.cs"
Task: "Add SettingBusesVisible resx entry in .../Resources/RouteFilterResources.resx"
# Then sequentially: T010 (Settings.cs) → T011 (SettingsBlade) / T012 (TransitMap consumer) / T013 (TransitMap honor-persisted)
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 (T001) — confirm baseline.
2. Phase 2 (T002–T006) — pulse plumbing (CRITICAL, blocks US1).
3. Phase 3 (T007) — wire pulse into the crossing handler.
4. **STOP and VALIDATE**: `quickstart.md` Section A — pulses fire, correct colors, audio-independent, selection-scoped, hidden when checkpoints off, survive basemap swap.
5. Demo MVP.

### Incremental Delivery

1. Setup + Foundational → pulse infrastructure ready.
2. Add US1 → validate Section A → demo (MVP: the pulsing checkpoints).
3. Add US2 → validate Section B → demo (the Buses toggle).
4. Polish (T014–T015) → tune + full validation.

---

## Notes

- `[P]` = different files, no incomplete-task dependency.
- Two tasks edit `TransitMap.razor.cs` (T007 in US1; T012/T013 in US2) — different methods/regions, but coordinate to avoid merge churn if worked in parallel.
- T003–T005 all edit `map-interop.js` — keep sequential.
- Anti-flicker (FR-006) is inherited from `checkpoint-tracker.js`'s existing 2000ms cooldown — do NOT add a second throttle.
- No server/worker/shared-project changes. No new SignalR events. No new automated tests (manual `quickstart.md` verification).
- Commit after each task or logical group.
