---

description: "Task list for Browser Memory Footprint Reduction"
---

# Tasks: Browser Memory Footprint Reduction

**Input**: Design documents from `/specs/024-browser-memory-investigation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (memory-probe.md, debug-flag.md, route-geometry-dedup.md), quickstart.md

**Tests**: This feature has **no automated test harness** for the WASM client (see research R6). Verification is **manual**, using the in-app `window.MemoryProbe` and the contract acceptance vectors. "Verify" tasks below execute those vectors — they are NOT automated test files.

**Organization**: Tasks are grouped by user story. All four stories are **frontend-only** (`src/Client/`) and **independently shippable**. Recommended ship order per quickstart: US1 → US3 → US2 → US4 (US3 is cheapest and fully independent), though phases are presented in spec priority order.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3, US4
- All paths are absolute-from-repo-root under `src/Client/`

## Path Conventions

- WebApp: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`
- Shared RCL: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the measurement baseline that every reduction slice is judged against. This MUST happen before any reduction so before/after comparison is possible.

- [ ] T001 Run the app (dev and, if available, prod), open DevTools console, run `await MemoryProbe.report()` and `MemoryProbe.wasmHeap()`; record baseline **RSS** (OS Task Manager), **wasmHeapMB**, and the **Canvas/WebGL bucket** (if `crossOriginIsolated`) into `specs/024-browser-memory-investigation/quickstart.md` (a "Baseline measurements" note at the bottom). Per quickstart §0.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single cross-cutting primitive multiple stories rely on — the runtime debug flag bootstrap. US3 gates JS logging on it; doing the bootstrap once here keeps the stories from colliding in `index.html`.

**⚠️ CRITICAL**: T002 must land before US3's JS-gating tasks. US1/US2/US4 do not depend on it.

- [X] T002 Bootstrap `window.__MJ_DEBUG = false` as the first inline `<script>` in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/index.html` (before the other script tags), so all JS modules can read it. Per data-model E5 / contract `debug-flag.md`.

**Checkpoint**: Baseline recorded + debug flag available — user story work can begin (US1/US2/US4 in parallel; US3 after T002).

---

## Phase 3: User Story 1 — Attribute the memory footprint (Priority: P1) 🎯 MVP

**Goal**: A maintainer can obtain a per-category memory breakdown (runtime/WASM vs. graphics/canvas vs. total) of the running app in under a minute, with no external tooling, resolving which heap owns the ~1.2 GB.

**Independent Test**: Open the running app, run `await MemoryProbe.report()`, and read off the runtime-heap share (`wasmHeap`) vs. the graphics/canvas bucket — satisfies SC-001, SC-002. Works even without cross-origin isolation (FR-003 fallback).

### Implementation for User Story 1

- [X] T003 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/memory-probe.js`, harden `measureUA()` and `wasmHeap()` to guarantee the no-throw contract: confirm `measureUA()` returns `{ error: <reason> }` when `performance.measureUserAgentSpecificMemory` is missing OR `!self.crossOriginIsolated`, and `wasmHeap()` returns `{ note }`/`{ error }` (never throws) when the runtime buffer is absent. Per contract `memory-probe.md` (A2–A4).
- [X] T004 [US1] In `memory-probe.js`, update the file header comment to mark `window.MemoryProbe` as a **supported attribution tool** (not "delete once solved"), and document the three-reading split (`wasmHeap` = runtime share; `measureUA` Canvas/WebGL bucket = graphics share; RSS = total). Keep zero steady-state cost (nothing runs until called).
- [X] T005 [US1] Document the optional cross-origin-isolation enhancement (COOP `same-origin` + COEP `require-corp`) in `specs/024-browser-memory-investigation/research.md` follow-up note OR a comment in `index.html`: it is required ONLY for the `measureUA()` GPU/canvas line, carries risk to the MapLibre CDN + MapTiler tile loads, and is deferred per research R2. Do **not** enable it on production in this slice.
- [ ] T006 [US1] **Verify** US1 against contract `memory-probe.md` vectors A1–A5: full breakdown when isolated (A1), graceful `{error}` when not isolated (A2) / non-Chromium (A3), `wasmHeap` note when buffer absent (A4), flat `watch()` line over a short run (A5). Record the runtime-vs-graphics attribution in the quickstart baseline note (SC-002).

**Checkpoint**: The 1.2 GB can now be attributed to a specific heap — US1 is independently shippable as the MVP.

---

## Phase 4: User Story 2 — Reduce the steady-state route-data footprint (Priority: P2)

**Goal**: Eliminate at least one full redundant copy of the route geometry from the .NET/WASM heap (the lever) without changing how routes look or breaking the basemap-toggle re-render.

**Independent Test**: Load full route set → all routes render; toggle GIS basemap repeatedly → all routes re-render correctly; `MemoryProbe.wasmHeap()` drops vs. baseline; routes pixel-identical. Satisfies FR-004, FR-005, SC-004.

### Implementation for User Story 2

- [X] T007 [US2] Decide and record the de-dup option (O1 compact-cache vs. O2 drop-and-re-render-from-JS) in `specs/024-browser-memory-investigation/contracts/route-geometry-dedup.md` (mark the chosen row), confirming the chosen path preserves: `RenderRoutesAsync` re-render, `ConfigureTrackerForRouteAsync` cumDist, and `TransitSynth.PreloadAsync(.Keys)`. Per research R3.
- [X] T008 [US2] Implement the chosen de-dup in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`:
  - **O1**: replace `_routeShapeCache` (`Dictionary<string,RouteShapeFeature>` at line 64) with a slim record holding only `{ coordinates, color, routeShortName }` consumed by `RenderRoutesAsync`/`ConfigureTrackerForRouteAsync`/`PreloadAsync`; drop the unused `RouteShapeFeature` sub-objects after `LoadRoutesAsync`.
  - **O2**: after initial render, release `_routeShapeCache`; rework `RenderRoutesAsync` to re-add the `routes` layer from the JS-resident `ChefMap._routesFeatureCollection`.
- [X] T009 [P] [US2] If O2 chosen: in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`, add/confirm a JS entry point that re-adds the `routes` source+layer from the already-cached `_routesFeatureCollection` after a `setStyle` swap (so the .NET side no longer needs the coordinate copy). Skip this task if O1 chosen.
- [ ] T010 [US2] **Verify** US2 against contract `route-geometry-dedup.md` vectors C1–C4: routes render + wasmHeap lower (C1), basemap toggle re-renders all routes repeatedly with no corruption (C2, Principle VII), tones/checkpoints unregressed (C3), routes pixel-identical (C4). Re-run `MemoryProbe.wasmHeap()` and log the drop (SC-004).

**Checkpoint**: One full route-geometry copy removed from the WASM heap; render + basemap toggle unaffected.

---

## Phase 5: User Story 3 — Stop verbose diagnostic logging in production (Priority: P2)

**Goal**: Production no longer runs verbose per-batch/per-frame logging; structured .NET logging and warnings/errors are preserved; hot-path diagnostics are recoverable via the debug flag.

**Independent Test**: Run production build → no `[ChefMapAnimator]`/per-batch/per-frame console output, no `LogDebug`; set `window.__MJ_DEBUG = true` → diagnostics reappear; real warnings/errors still show. Satisfies FR-006, FR-007, FR-008, SC-005. (Depends on T002.)

### Implementation for User Story 3

- [X] T011 [P] [US3] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json`, change `Logging.LogLevel.Default` from `"Debug"` to `"Information"` (production). Leave `appsettings.Development.json` at `"Debug"`. Per contract `debug-flag.md` (.NET side).
- [X] T012 [P] [US3] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs`, remove the hard-coded `builder.Logging.SetMinimumLevel(LogLevel.Debug)` (line 87) and instead let the minimum level come from `builder.Configuration` (the bound `Logging` section), so config — not code — sets the floor. Per research R4 (this is the bug that makes the prod appsettings level inert).
- [X] T013 [P] [US3] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/vehicle-animator.js`, gate `ChefMapAnimator._log` (line 13): early-return without calling `console[level]` when `!window.__MJ_DEBUG` and `level` ∈ {`debug`,`info`,`log`}; always emit when `level` ∈ {`warn`,`error`}.
- [X] T014 [P] [US3] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/transit-synth.js` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`, gate diagnostic `console.log`/`console.debug` calls behind `window.__MJ_DEBUG`; leave `console.warn`/`console.error` unconditional.
- [ ] T015 [US3] **Verify** US3 against contract `debug-flag.md` vectors B1–B4: silent prod hot path (B1), diagnostics return when flag on (B2), warnings/errors still emit (B3), dev still verbose (B4).

**Checkpoint**: Production console is quiet on the hot path; observability for real problems intact.

---

## Phase 6: User Story 4 — Reduce per-frame and per-batch churn (Priority: P3)

**Goal**: Cut redundant per-frame/per-batch allocation (the driver of the never-returned WASM high-water mark) without any visible change.

**Independent Test**: On live data, confirm `setData` is skipped when nothing visible changed, the double batch pass is collapsed, and `_pendingBatches` is bounded; animation stays smooth; `MemoryProbe.watch()` stays flat over 30–60 min. Satisfies FR-009, FR-010, FR-011, FR-013.

### Implementation for User Story 4

- [X] T016 [P] [US4] In `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/vehicle-animator.js` `tick()` (lines 173–254): add a "nothing visible changed" predicate and skip the `features` rebuild + `this._source.setData(...)` (line 241) when every vehicle is `idle` AND none changed phase/position since the last frame AND the rendered selection-emphasis set is unchanged. Resume immediately on any change. **Never skip on a selection change** (Principle IX guard). Per data-model E3 / research R5.
- [X] T017 [P] [US4] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` `HandleVehicleBatchAsync` (lines 404–410): collapse the `.Where().Select().SelectMany().ToArray()` + second `.Where(IsAllowedRoute).ToArray()` into a single materialized pass yielding the identical allowed-route record set. Per FR-010.
- [X] T018 [P] [US4] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` `_pendingBatches` (line 56) + `HandleVehicleBatchAsync` (line 399) + `OnMapReadyAsync` (drain): cap the buffer to the most-recent N batches (freshest only; preserve the initial-snapshot-not-clobbered property) and add a readiness watchdog that logs if `notifyMapReadyAsync` hasn't fired within a timeout. Per FR-011 / data-model E4.
- [ ] T019 [US4] **Verify** US4: animation smooth and visually unchanged (FR-012); idle frames skip `setData` (instrument behind `__MJ_DEBUG`); single-pass yields identical record count; `_pendingBatches` stays bounded when map-ready is delayed; `MemoryProbe.watch(5000)` flat over a 30–60 min session (FR-013).

**Checkpoint**: Transient churn reduced; WASM high-water peak lowered; behavior identical.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final attribution, flatness confirmation, and acceptance sign-off across all stories.

- [ ] T020 Re-measure post-change RSS + `MemoryProbe.wasmHeap()` + `measureUA()` (prod and dev) and compare against the T001 baseline; record the reduction in `specs/024-browser-memory-investigation/quickstart.md` (SC-003).
- [ ] T021 Run `MemoryProbe.watch(5000)` over a 30–60 min live session to confirm the reduced footprint is **flat** (no upward trend in the `wasm=` line) — FR-013 / SC-003.
- [ ] T022 Walk the full quickstart §5 acceptance table (SC-001…SC-006) and confirm each criterion; note any criterion not met for follow-up.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)**: No dependencies — baseline measurement, do first.
- **Foundational (T002)**: Depends on nothing structural; BLOCKS US3 JS-gating only (T013, T014). US1/US2/US4 do not depend on it.
- **User Stories (Phases 3–6)**: All depend only on Setup (T001) for the comparison baseline. They touch **mostly disjoint files** and can proceed in parallel.
- **Polish (Phase 7)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Independent. Touches only `memory-probe.js` (+ docs). MVP.
- **US2 (P2)**: Independent. Touches `TransitMap.razor.cs` (route cache) and optionally `map-interop.js`.
- **US3 (P2)**: Depends on T002 (the `__MJ_DEBUG` bootstrap) for the JS-gating tasks. Touches `appsettings.json`, `Program.cs`, `vehicle-animator.js`, `transit-synth.js`, `map-interop.js`.
- **US4 (P3)**: Independent. Touches `vehicle-animator.js` (`tick()`) and `TransitMap.razor.cs` (batch handling).

### ⚠️ Cross-story file overlaps (sequence these even though stories are "independent")

- `vehicle-animator.js`: US3 (T013, gate `_log`) and US4 (T016, `tick()` skip) edit the same file — land them sequentially or coordinate the diff.
- `map-interop.js`: US2 (T009, O2 only) and US3 (T014) — coordinate if both apply.
- `TransitMap.razor.cs`: US2 (T008) and US4 (T017, T018) edit the same file — sequence them.

### Within Each User Story

- Decision/record task → implementation task(s) → verify task (runs the contract vectors).

---

## Parallel Opportunities

- **T001 then T002** are quick and sequential-ish (T002 has no real dep but precedes US3 JS work).
- After Setup, **US1, US2, US4 can run fully in parallel** (largely disjoint files).
- Within **US3**, T011 / T012 / T013 / T014 are all `[P]` (different files) — but T013/T014 must respect the `vehicle-animator.js`/`map-interop.js` overlaps with US2/US4 above.
- Within **US4**, T016 (JS) / T017 (C#) / T018 (C#) are `[P]` — but T017 and T018 share `TransitMap.razor.cs`, so sequence those two.

## Parallel Example: maximum-parallelism cut after Setup

```bash
# After T001 (baseline) + T002 (debug flag):
Developer A — US1: T003, T004, T005, T006   (memory-probe.js)
Developer B — US2: T007, T008, T010         (TransitMap.razor.cs route cache)
Developer C — US3: T011, T012               (appsettings.json, Program.cs) — .NET only, no file overlap
# Then serialize the shared-file edits: US3 T013 + US4 T016 (vehicle-animator.js);
#                                       US2 T008 + US4 T017/T018 (TransitMap.razor.cs)
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. T001 (baseline) → T002 (flag, optional for US1) → US1 (T003–T006).
2. **STOP and VALIDATE**: attribute the 1.2 GB. This alone resolves the investigation's open question and is shippable.

### Recommended incremental order (per quickstart)

1. **US1** — attribution (know which heap to target).
2. **US3** — logging quiet-down (cheapest, lowest risk, independent .NET + flag-gated JS).
3. **US2** — route de-dup (the WASM-heap lever; guard the Principle VII re-render).
4. **US4** — churn reduction (lower the never-returned peak).
5. **Phase 7** — re-measure, confirm flat, sign off the SC table.

Each story adds value and is independently verifiable via its contract vectors without breaking the others.

---

## Notes

- **No automated tests** — "Verify" tasks execute the contract acceptance vectors manually via `window.MemoryProbe` and visual parity (research R6). Verify behavior parity before considering a story done.
- **Out of scope** (spec assumptions): `PublishTrimmed`/AOT/lazy-assembly (prod==dev proved build flags aren't the lever) and stale-vehicle eviction (symptom is flat, not climbing).
- **Hard gate**: US2 must not break the basemap-toggle re-render of all routes (Principle VII / FR-005).
- Commit after each task or logical group; coordinate the shared-file edits called out above.
