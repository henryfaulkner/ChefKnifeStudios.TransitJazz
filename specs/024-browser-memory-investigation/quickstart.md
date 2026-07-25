# Quickstart: Browser Memory Footprint Reduction

Frontend-only. All work is under `src/Client/`. Verification is manual via the in-app
`window.MemoryProbe` (no external profiling tooling — SC-001).

## 0. Establish the baseline (do this FIRST — US1)

Run the app (prod or dev — the prior investigation proved they're equal), open DevTools console:

```js
await MemoryProbe.report();   // records wasmHeap MB, measureUA breakdown (if isolated), app objects
MemoryProbe.wasmHeap();       // the .NET/WASM linear-memory size = the runtime-heap share
```

Also note process RSS from OS Task Manager (the snapshot/probe never shows reserved WASM pages or GPU).
Record: **RSS**, **wasmHeapMB**, and (if `crossOriginIsolated`) the **Canvas/WebGL bucket** from
`measureUserAgentSpecificMemory`. This is the number every later slice is judged against (SC-002, SC-003).

> If `MemoryProbe.measureUA()` returns `{ error: ...crossOriginIsolated... }`, that's expected (FR-003).
> `wasmHeap()` still gives the runtime-heap share, which answers the open question on its own.

## 1. US1 — Attribution (P1, ships first)

- Keep `memory-probe.js` (already wired in `index.html`); document it as the supported attribution path.
- (Optional enhancement) Enable cross-origin isolation for the GPU/canvas line — see research R2; only
  pursue if the WebGL bucket is needed and MapLibre/MapTiler loads survive COOP/COEP.
- **Verify** with contract `memory-probe.md` vectors A1–A5.

## 2. US3 — Quiet production logging (P2, cheap, independent)

- `appsettings.json`: `Logging.LogLevel.Default` `Debug` → `Information`.
- `Program.cs:87`: drop the hard-coded `SetMinimumLevel(LogLevel.Debug)`; drive level from config.
- `index.html`: bootstrap `window.__MJ_DEBUG = false`.
- Gate `ChefMapAnimator._log` (`vehicle-animator.js:13`) and `transit-synth.js` / `map-interop.js`
  diagnostic `console.*` behind `__MJ_DEBUG`; leave `warn`/`error` unconditional.
- **Verify** with contract `debug-flag.md` vectors B1–B4.

## 3. US2 — Route-geometry de-dup (P2)

- Choose O1 (compact `_routeShapeCache`) or O2 (drop it, re-render from JS `_routesFeatureCollection`) —
  see `route-geometry-dedup.md`.
- Preserve: initial render, **basemap-toggle re-render of ALL routes** (Principle VII / FR-005),
  `TransitSynth.PreloadAsync` keys, checkpoint `cumDist`.
- **Verify** with contract vectors C1–C4. Re-run `MemoryProbe.wasmHeap()` — expect a drop (SC-004).

## 4. US4 — Churn reduction (P3)

- `vehicle-animator.js` `tick()`: skip `features` rebuild + `setData` when nothing visible changed
  (all idle, no phase/pos change, no selection-emphasis change). Never skip on selection change (IX).
- `TransitMap.razor.cs` `HandleVehicleBatchAsync` (404–410): collapse the double `.ToArray()` into one pass.
- `TransitMap.razor.cs` `_pendingBatches` (56): cap to most-recent-N + readiness watchdog (FR-011).
- **Verify**: animation still smooth (FR-012); `MemoryProbe.watch(5000)` over 30–60 min stays flat (FR-013).

## 5. Final acceptance

| Success criterion | How to confirm |
|-------------------|----------------|
| SC-001 | breakdown obtained in <1 min via `MemoryProbe.report()`, no external tools |
| SC-002 | state runtime-heap (wasmHeap) vs. graphics (Canvas/WebGL bucket) share of total |
| SC-003 | post-change RSS measurably < ~1.2 GB baseline; flat over 30–60 min (`watch`) |
| SC-004 | `wasmHeap()` lower by the route-geometry share; routes pixel-identical before/after basemap toggle |
| SC-005 | no verbose per-batch/per-frame console output in prod; warnings/errors still shown |
| SC-006 | maintainer sees no visible change in routes/animation/audio/basemap |

## Notes
- **Do not** spend effort on `PublishTrimmed`/AOT for *this* number — prod==dev proved build flags aren't
  the lever (spec Assumptions; design doc §0a). They remain valid general hygiene, out of scope here.
- Stale-vehicle eviction is **out of scope** (spec assumptions): the symptom is flat, not climbing.
