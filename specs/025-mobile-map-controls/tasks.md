---
description: "Task list for Mobile Map Controls & Wider Default Zoom"
---

# Tasks: Mobile Map Controls & Wider Default Zoom

**Input**: Design documents from `/specs/025-mobile-map-controls/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/map-interaction.md, quickstart.md

**Tests**: No automated test tasks. The repo has no UI/interop test harness for the map, and the spec did not request TDD. Verification is the manual `quickstart.md` matrix (see Phase 6).

**Organization**: Tasks are grouped by user story. US1 (wider default) is independent (edits `TransitMap.razor.cs`). US2 (touch zoom) and US3 (drag + on-screen zoom + FR-009) both edit `createMap` in `map-interop.js`, so they share a file and must be done sequentially (not in parallel with each other).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3
- Exact file paths included.

## Path Conventions

Front-end-only feature. Two edit sites:
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the build runs before changing map behavior.

- [x] T001 Build the solution to establish a clean baseline: run `dotnet build ChefKnifeStudios.TransitJazz.sln` from repo root and confirm it succeeds.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Locate the exact change sites so all stories edit the right lines. No code change here.

- [x] T002 [P] Confirm the default zoom site: open `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` and verify `DefaultCameraOptions => new() { Center = new Position(33.749, -84.388), Zoom = 9.5 }` (the value to change in US1).
- [x] T003 [P] Confirm the interop site: open `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js` and verify the `ChefMap.createMap` `new maplibregl.Map({...})` options include `minZoom: 7, maxZoom: 18, dragRotate: false, touchZoomRotate: false` and the existing ctrl+drag handler below it (the lines US2/US3 modify).

**Checkpoint**: Both edit sites located; user stories can proceed.

---

## Phase 3: User Story 1 - Wider map view on first load (Priority: P1) 🎯 MVP

**Goal**: Map opens at a wider zoom so more of the MARTA network is visible on first load.

**Independent Test**: Hard-reload the app on desktop and a phone-sized viewport; confirm the initial extent is noticeably wider than before, still centered on Atlanta (quickstart row 1).

### Implementation for User Story 1

- [x] T004 [P] [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`, change `DefaultCameraOptions` `Zoom = 9.5` to `Zoom = 8.5` (center unchanged). This is within `CameraOptions`' `[1,24]` clamp and above `minZoom: 7`, so no model change is needed (data-model.md).

**Checkpoint**: First load shows a wider extent (FR-001, FR-002, SC-001). MVP deliverable on its own.

---

## Phase 4: User Story 2 - Zoom the map on touch and desktop (Priority: P1)

**Goal**: Pinch-to-zoom works on touch; scroll/double-click works on desktop; map stays north-up.

**Independent Test**: On a touch device / emulation, pinch apart then together and confirm zoom in/out about the pinch with no rotation (quickstart rows 2, 7); desktop scroll/double-click zooms (row 3).

### Implementation for User Story 2

- [x] T005 [US2] In `map-interop.js` `ChefMap.createMap`, remove the `touchZoomRotate: false` option from the `new maplibregl.Map({...})` config (keep `dragRotate: false`, `minZoom: 7`, `maxZoom: 18`). This re-enables MapLibre's default pinch-zoom (research Decision 2 — the single `touchZoomRotate` flag was disabling pinch AND rotate together).
- [x] T006 [US2] In `map-interop.js` `ChefMap.createMap`, immediately after `let map = new maplibregl.Map(...)` (and before/around the existing ctrl+drag handler), add `map.touchZoomRotate.enable(); map.touchZoomRotate.disableRotation();` so pinch-zoom is on but touch rotation stays disabled — keeping the map north-up (FR-003, FR-007).

**Checkpoint**: Pinch + scroll + double-click zoom work, bounded by `[7,18]`, with no rotation (FR-003, FR-004, FR-007, FR-008). Depends on US2 file edits being applied in order T005 → T006.

---

## Phase 5: User Story 3 - Pan/drag and on-screen zoom controls (Priority: P2)

**Goal**: On-screen zoom buttons exist and don't occlude the filter grid/gear; drag-pan works; manual moves aren't overridden by auto-recenter.

**Independent Test**: Tap the +/− buttons (zoom changes), drag to pan (view follows), confirm the control clears the route filter grid and gear FAB, and confirm a manual pan survives several vehicle update cycles (quickstart rows 4, 5, 6, 9).

> Note: T007 edits the same `createMap` function as US2 (T005/T006) — sequence after Phase 4 to avoid edit conflicts.

### Implementation for User Story 3

- [x] T007 [US3] In `map-interop.js` `ChefMap.createMap`, after the map is created, add a zoom-only navigation control: `map.addControl(new maplibregl.NavigationControl({ showCompass: false, showZoom: true, visualizePitch: false }), 'bottom-left');` (FR-005). Placed bottom-left (not bottom-right) to avoid collision with the settings gear FAB. `showCompass: false` keeps it consistent with no-rotation (FR-007); native button titles avoid new resx strings (Principle XII).
- [x] T008 [US3] Verify drag-pan needs no code: confirm `dragPan` is not disabled anywhere in `createMap` (MapLibre default is on) so one-finger touch drag and desktop click-drag already pan (FR-006). No edit unless a `dragPan: false` is found.
- [x] T009 [US3] Audit FR-009 auto-recenter: trace callers of `Map.PlotVehiclesAsync(... centerMap:)` in `src/Client` and confirm recurring vehicle-position plots pass `centerMap: false` (only a one-time initial fit or explicit `centerVehiclePin` bus-click may move the camera). If a recurring caller passes `centerMap: true`, change it to `false` so a manual pan/zoom is not overridden by `fitBounds` (research Decision 4).

**Checkpoint**: On-screen zoom + drag-pan work, control is non-occluding (Principle X), manual interaction wins over auto-camera (FR-009).

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify placement, north-up invariant, and run the full quickstart.

- [x] T010 Confirm control placement does not collide with the settings gear FAB (also bottom-right) or the route filter grid (top-left/top-right, zoom-adaptive); if the zoom control overlaps the gear FAB, move it to `'bottom-left'` (research Decision 3, Principle X).
- [ ] T011 Rebuild (`dotnet build`) and run the app; execute the full `quickstart.md` verification matrix (rows 1–9) on a desktop viewport and a phone-sized emulated viewport. Pay special attention to row 2 (pinch-to-zoom, the previously broken case) and row 7 (no rotation/tilt).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: After Setup. Read-only location confirmation; blocks nothing technically but should precede edits.
- **US1 (Phase 3)**: After Phase 2. Independent file (`TransitMap.razor.cs`) — can run fully in parallel with US2/US3.
- **US2 (Phase 4)**: After Phase 2. Edits `map-interop.js` `createMap`.
- **US3 (Phase 5)**: After Phase 2. Edits the same `createMap` — sequence after US2 to avoid conflicts.
- **Polish (Phase 6)**: After all desired stories complete.

### User Story Dependencies

- **US1 (P1)**: Independent. Different file from US2/US3.
- **US2 (P1)**: Independent of US1; shares `map-interop.js` with US3.
- **US3 (P2)**: Independent of US1; shares `createMap` with US2 → do US2 first.

### Parallel Opportunities

- T002 and T003 (Foundational location checks) are `[P]` — different files.
- T004 (US1) is `[P]` — `TransitMap.razor.cs` is a different file from the interop, so US1 can be done in parallel with US2/US3.
- US2 (T005, T006) and US3 (T007) all touch `createMap` in `map-interop.js` → NOT parallel with each other; do them in order.

---

## Parallel Example

```text
# After Phase 2, US1 can run alongside the US2→US3 interop chain:
Track A (different file):  T004  (TransitMap.razor.cs — zoom 9.5 → 8.5)
Track B (one file, in order): T005 → T006 (US2) → T007 → T008 → T009 (US3)
# Then converge on Phase 6 (T010, T011).
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 (build baseline) → Phase 2 (locate sites) → Phase 3 (T004).
2. **STOP and VALIDATE**: hard-reload, confirm wider default extent (quickstart row 1). Shippable MVP.

### Incremental Delivery

1. US1 → wider default (MVP).
2. US2 → pinch/scroll/double-click zoom (closes the core mobile gap).
3. US3 → on-screen zoom buttons + drag-pan verification + FR-009 audit.
4. Polish → placement + full quickstart pass.

---

## Notes

- [P] = different files, no dependencies. US2/US3 are intentionally NOT [P] with each other (same `createMap`).
- Front-end only — no server/worker/shared/resx changes (plan Constitution Check).
- The load-bearing fix is T005+T006: `touchZoomRotate: false` was disabling pinch-zoom and rotate together.
- Keep the map north-up/flat at all times (FR-007, Principle VII): `dragRotate:false` + `disableRotation()` + compass-less control.
- Commit after each story; validate each against its quickstart rows before moving on.
