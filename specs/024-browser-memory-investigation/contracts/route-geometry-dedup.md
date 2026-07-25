# Contract: Route-geometry de-duplication

Addresses US2 / FR-004, FR-005. Hard gate: Principle VII (route layers persist/re-render across
basemap style swap). The `_routeShapeCache` is the de-dup target; the basemap-toggle re-render is
the property that MUST survive.

## Current copies (steady-state, page lifetime)

1. **`_routeShapeCache`** — `TransitMap.razor.cs:64`, **.NET/WASM heap** (the heap that owns the 1.2 GB).
2. `ChefMapAnimator.routeGeometry[id]` — `coords` + `cumDist`, JS heap (hot animation path).
3. `routes` source + `ChefMap._routesFeatureCollection` — JS/WebGL (the render layer).

## Required outcome

- **At least one full copy of the route coordinate data eliminated from steady-state residency** (FR-004),
  and that copy MUST be on the **.NET/WASM heap** (copy #1) — that is the heap the investigation
  identified as the lever.
- All routes still render on initial load.
- **Toggling the basemap style (street ↔ blank) still re-renders ALL routes** with no missing/corrupted
  lines (FR-005, Principle VII).
- `TransitSynth.PreloadAsync(...)` still receives the full set of route keys (Principle VIII).
- Checkpoint trackers still receive per-route `cumDist` (no checkpoint regression).

## Implementation options (one MUST be chosen in tasks/plan)

| Option | What changes | Preserves re-render via | **Chosen** |
|--------|--------------|-------------------------|------------|
| **O1 — Compact the cache** | Replace `Dictionary<string,RouteShapeFeature>` with a slim record holding only `{ coordinates, color, routeShortName }` used by consumers; drop unused `RouteShapeFeature` sub-objects after first render | the slim cache still feeds `RenderRoutesAsync` | ✅ **CHOSEN** |
| **O2 — Relocate to JS (drop the .NET copy)** | After initial render, do not retain `_routeShapeCache`; `RenderRoutesAsync` re-reads the already-resident `ChefMap._routesFeatureCollection` on the JS side to re-add the `routes` layer after `setStyle` | the JS render layer becomes the single source of truth for re-render | |

**Chosen: O1** — lower risk; keeps the re-render path entirely in .NET, just smaller. Replaces
`Dictionary<string,RouteShapeFeature>` with `Dictionary<string,RouteShapeSlim>` holding only
`{ Coordinates, Color }`. The key (routeShortName) is preserved as the dictionary key.

## Acceptance vectors

| # | Action | Expected |
|---|--------|----------|
| C1 | Load app with full route set | all routes render; `MemoryProbe.wasmHeap()` lower than pre-change baseline by the route-geometry share (FR-004, SC-004) |
| C2 | Open settings → toggle GIS basemap (street ↔ blank), repeatedly | every route re-renders correctly each time; no missing/blank/corrupted routes (FR-005) |
| C3 | After de-dup | tones still preload for all routes; checkpoints still flash on pass (no regression) |
| C4 | Visual diff before/after | rendered routes pixel-identical (FR-012) |
