# Phase 0 Research: Browser Memory Footprint Reduction

All Technical Context unknowns resolved below. No blocking NEEDS CLARIFICATION remain.

---

## R1 — How to obtain a runtime-heap vs. graphics/canvas memory split in-app

**Decision**: Use the already-shipped `window.MemoryProbe` (in `Client.Shared/wwwroot/js/memory-probe.js`) as the supported attribution path. Its three readings together produce the split required by FR-001/FR-002:
- `MemoryProbe.measureUA()` → `performance.measureUserAgentSpecificMemory()` — per-type breakdown including a `Canvas`/`WebGL`-attributable bucket and a `Wasm`/JS bucket. **Requires `self.crossOriginIsolated === true`.**
- `MemoryProbe.wasmHeap()` → `Blazor.runtime.Module.HEAPU8.buffer.byteLength` — the .NET WASM linear-memory size directly, independent of cross-origin isolation.
- `MemoryProbe.jsHeap()` → `performance.memory` (Chromium) — coarse JS-only number for the `watch()` poller.

**Rationale**: The probe already exists and already implements the exact graceful-degradation FR-003 demands (returns `{ error: '...needs crossOriginIsolated...' }` rather than throwing). `wasmHeap()` gives the .NET-heap share even when `measureUA()` is unavailable, so the WASM-vs-rest split is obtainable on any Chromium build; the full per-type breakdown (adding the GPU/canvas line) needs cross-origin isolation. This satisfies SC-001 (under a minute, no external tooling) and SC-002 (state the runtime vs. graphics share).

**Alternatives considered**:
- DevTools heap snapshot only — rejected: the prior investigation proved the JS snapshot collapses the WASM heap to one ArrayBuffer and never shows GPU/VRAM or WASM reservation, so it can't attribute the 1.2 GB RSS. The in-app probe + RSS comparison is the correct instrument.
- A new bespoke measurement service — rejected: duplicates the existing probe; the probe is the right primitive, it just needs to be treated as supported (kept, documented) rather than "delete once cause found."

---

## R2 — Enabling cross-origin isolation for the full per-type breakdown

**Decision**: Cross-origin isolation (COOP `same-origin` + COEP `require-corp`) is **required only for the GPU/canvas line of `measureUA()`**, not for the core WASM-vs-rest attribution (which `wasmHeap()` provides unconditionally). Treat COOP/COEP as an **optional, isolatable enhancement**: document how to enable it (Static Web App `staticwebapp.config.json` response headers, or a dev-server header set) and the known risk that it breaks cross-origin subresource loads (the MapLibre GL JS + CSS loaded from `cdn.jsdelivr.net`, and MapTiler tile/style fetches) unless those respond with `Cross-Origin-Resource-Policy`/CORS-compatible headers. Mitigation if isolation is pursued: load MapLibre `crossorigin`/from same origin (vendor it) and confirm MapTiler tiles send CORS headers (they do for documented usage). **Default plan path: ship the probe usable without isolation (WASM split works), and enable isolation only as a follow-on if the GPU line is needed to close attribution.**

**Rationale**: Avoids coupling the P1 attribution slice to a risky global header change. FR-002 ("runtime-heap share vs. graphics/map share distinguishable") is met by `wasmHeap()` (WASM bytes) compared against total RSS even without the canvas line; the canvas line is a refinement. This keeps US1 independently shippable per the spec.

**Alternatives considered**:
- Force COOP/COEP on production immediately — rejected: highest risk to the live MapLibre/MapTiler loads, and not needed to answer the open question.
- Skip `measureUA()` entirely — rejected: it's the only source of the explicit per-type GPU/canvas bucket, valuable for confirming the renderer/GPU share the prior doc flagged as invisible to snapshots.

---

## R3 — Which redundant route-geometry copy to drop (and what must be preserved)

**Decision**: Keep MapLibre's `routes` GeoJSON source (+ `ChefMap._routesFeatureCollection`) and the animator's `routeGeometry` as the live render/animation copies. Target the **.NET `_routeShapeCache`** (`TransitMap.razor.cs:64`) for reduction, but **do not delete it outright** — it has two real consumers:
1. `RenderRoutesAsync` (re-add the `routes` layer after a basemap `setStyle` swap — Principle VII, FR-005).
2. `ConfigureAllTrackersAsync`/`ConfigureTrackerForRouteAsync` (builds per-route `cumDist` for checkpoint trackers) and `TransitSynth.PreloadAsync(_routeShapeCache.Keys)`.

Chosen reduction: **store the cache more compactly** rather than dropping it — retain only the coordinate arrays + the small properties (`routeId`, `color`, `routeShortName`) actually used by its consumers, discarding any unused `RouteShapeFeature` sub-objects after render, OR (stronger) drop the .NET cache and have `RenderRoutesAsync` re-read the already-resident MapLibre `_routesFeatureCollection` on the JS side for the re-render, eliminating the .NET copy entirely. The contract (`route-geometry-dedup.md`) pins which option ships and proves the basemap-toggle re-render still works.

**Rationale**: The prior heap snapshot measured route geometry as <0.2 MB in the **JS** heap — but the .NET copy lives in WASM linear memory (the heap that owns the 1.2 GB) and is held for the page lifetime, so eliminating it lowers the WASM high-water mark, which is the actual lever per the investigation's conclusion. Preserving the re-render path is a hard constitutional gate (VII), so "compact/relocate" is safer than "delete and hope."

**Alternatives considered**:
- Drop the animator `cumDist` duplication — rejected as the primary target: it's needed every frame for interpolation; recomputing per frame trades memory for CPU and risks the 60 fps goal.
- Drop MapLibre's source — rejected: that *is* the render layer; dropping it removes the routes from the map.

---

## R4 — Quieting production logging without losing observability (Principle IV)

**Decision**: Three coordinated changes:
1. `appsettings.json`: `Logging.LogLevel.Default` `Debug` → `Information` (production default). Leave `appsettings.Development.json` at `Debug` for local work.
2. `Program.cs:87`: replace the hard-coded `builder.Logging.SetMinimumLevel(LogLevel.Debug)` with the level read from `builder.Configuration` (the `Logging` section the WASM host already binds), so config — not code — controls the floor. This is the bug that makes the prod `appsettings.json` level irrelevant today.
3. JS hot paths: introduce a single runtime flag `window.__MJ_DEBUG` (default `false`, bootstrapped in `index.html`); gate `ChefMapAnimator._log` and the `transit-synth.js` / `map-interop.js` `console.*` diagnostic calls behind it. `console.warn`/`console.error` for genuine problems stay unconditional (FR-008).

**Rationale**: Keeps structured .NET logging intact (Principle IV) — only the Debug-severity floor drops in prod — while removing the per-frame/per-batch console retention the prior doc flagged. The constitution explicitly exempts logging/console messages from the resx rule, so the debug flag needs no localized copy. Driving level from config is the minimal correct fix and matches standard .NET logging configuration.

**Alternatives considered**:
- Strip the `_log` calls entirely — rejected: loses the ability to diagnose on demand; the flag preserves it at zero steady-state cost.
- A compile-time `#if DEBUG` for .NET — rejected: prod is a Release build but the *level* is the issue, not a compile constant; config-driven level is the idiomatic .NET answer and matches the existing `appsettings` binding.

---

## R5 — Reducing per-frame / per-batch churn safely

**Decision**:
- **`tick()` setData skip**: track a per-frame "anything visible changed" flag. Skip the `_source.setData(...)` call (and the `features` rebuild) when **all** vehicles are `idle` AND none changed phase/position since the last frame AND no selection-emphasis change occurred. Resume immediately on any change. Never skip when the rendered selection/filter set changed (Principle IX guard).
- **`HandleVehicleBatchAsync` double pass**: collapse the `.Where(...).Select(...).SelectMany(...).ToArray()` followed by `.Where(IsAllowedRoute).ToArray()` into a single LINQ pipeline materialized once.
- **`_pendingBatches` bound**: cap to the most recent N batches (stale position data is worthless once superseded) and/or add a readiness watchdog that logs if `notifyMapReadyAsync` hasn't fired within a timeout (FR-011). Given the mobile context, a small cap (e.g. keep last 1–2) is sufficient since only the freshest snapshot matters once the map readies.

**Rationale**: Churn inflates the never-returned WASM high-water mark (the lever). Each change is allocation-reducing and behavior-preserving: skip-when-idle is invisible because an idle frame renders identical pixels; single-pass LINQ produces the identical array; bounding `_pendingBatches` only discards superseded stale data. All three honor FR-012 (no visible change) and FR-013 (stays flat).

**Alternatives considered**:
- Throttle the RAF loop to <60 fps — rejected: violates the 60 fps animation goal and is visible.
- Object-pool the `features` array — rejected as premature; skip-when-unchanged removes the allocation on the common idle path with far less complexity and risk.

---

## R6 — How "reduced and still flat" is verified (no automated test harness)

**Decision**: Verification is manual, using the P1 probe as the instrument, per quickstart.md:
1. Record baseline RSS + `MemoryProbe.wasmHeap()` + `measureUA()` (if isolated) on the current build, prod and dev.
2. Apply slices; re-measure after each. Compare WASM-heap MB and RSS.
3. Run `MemoryProbe.watch(5000)` over a 30–60 min live session to confirm flatness (FR-013): the WASM line and vehicle count should not trend upward.
4. Eyeball parity: routes render, basemap toggle re-renders all routes, vehicle animation smooth, audio unaffected (FR-012).

**Rationale**: No test project exists for the WASM client; the spec's success criteria are explicitly measurement/observation-based (SC-001..SC-006). The probe makes this repeatable and external-tool-free (SC-001).

**Alternatives considered**:
- Add an automated Playwright + CDP memory test — rejected for this feature's scope: large new harness, out of scope; noted as possible future hardening.
