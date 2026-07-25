# Phase 0 Research: MapLibre + MapTiler POC

**Feature**: 006-maplibre-poc | **Date**: 2026-05-17

This document resolves the open research questions identified in [plan.md](plan.md) before Phase 1 design begins.

---

## R1 — MapLibre marker-update strategy at ~200 markers, 60 Hz

**Question**: MapLibre GL JS does not expose per-marker mutation in the way Azure Maps does (`shape.setCoordinates(newPos)`). The idiomatic update path is `map.getSource('vehicles').setData(featureCollection)`, which replaces the entire source. With ~200 markers updated at 60 FPS in the animator's RAF loop, will this rebuild the entire source 60 times per second and create a performance cliff?

**Decision**: Use a **single GeoJSON source containing all vehicles, updated via `setData()` once per RAF tick**, with the entire `FeatureCollection` rebuilt from the in-memory `vehicles` map. Do not attempt `feature-state`-based positioning or a custom WebGL layer for the POC.

**Rationale**:

- MapLibre's renderer is built on the assumption that GeoJSON source updates are the high-frequency mutation primitive. The internal pipeline diffs the source data and updates only changed features in the WebGL buffers; it is not, as a naive reading might suggest, a full re-tessellation from scratch on every `setData` call. Published benchmarks and community reports indicate ~200–500 point features updated at 60 Hz is well within the comfortable performance envelope on modern WebGL hardware.
- `feature-state` is designed for *style* changes (color, opacity, icon-image), not geometric position changes. It does not move features.
- Custom WebGL layers offer the most control but require implementing the rendering pipeline manually — out of scope for a 1-day POC. If gate (b) fails with the GeoJSON source approach, the appropriate response is "don't migrate" (per spec), not "extend the POC to try a custom layer."
- The animator's per-frame work — computing the new lon/lat for each vehicle from cumulative-distance interpolation — does not change. Only the *delivery* of those new positions to the renderer changes.

**Alternatives considered**:

| Alternative | Why rejected |
|-------------|--------------|
| `feature-state` for position updates | Doesn't support position updates; designed for styling only. |
| One MapLibre `Marker` (DOM) per vehicle | DOM-based; 200 DOM markers moving at 60 Hz triggers reflow/repaint storms. Rejected on first principles. |
| Custom WebGL layer | Maximum performance ceiling but multi-day implementation cost; not justified for a POC. |
| One GeoJSON source per vehicle | Source registration overhead is significant; MapLibre's design pushes you toward batched sources. |

**POC implication**: The ported animator's `processNearestPointBatch` updates the in-memory `vehicles` map (unchanged logic), and the RAF tick replaces with `map.getSource('vehicles').setData({ type: 'FeatureCollection', features: [...] })` once per frame. If gate (b) fails on this approach, the decision is don't-migrate.

---

## R2 — MapTiler free tier, API-key model, and style choice

**Question**: What are MapTiler's free-tier usage limits, what auth model does its web SDK use (server-issued tokens vs client-embedded keys), and which style is appropriate for a soundscape-themed transit visualization?

**Decision**:

- **Tier**: MapTiler Cloud free tier — 100,000 map tile loads / month, sufficient for the POC measurement window and for early production traffic.
- **Auth**: Use a **URL-restricted public API key** embedded in `wwwroot/appsettings.json`. Restrict the key at the MapTiler side to the project's localhost dev origins and (post-deploy) `www.martajazz.com`.
- **Style**: Use MapTiler's **"streets-v2"** or **"basic-v2"** vector style as the default for the POC. Either is appropriate for transit visualization; final aesthetic choice is the soft gate (e) judgment, made on the POC day.

**Rationale**:

- The URL-restricted public key model is MapTiler's documented and recommended pattern for web/SPA clients. The key is not a secret; it is a usage attribution token bounded by origin enforcement on MapTiler's side. This is the same model used by Mapbox, Google Maps JS API, and most modern web map providers.
- Standing up a server-side `/maptiler/auth/token` endpoint paralleling the existing `MapsEndpoints.GetMapsAuthToken` would be wasted effort for the POC (see Constitution Check § II in plan.md).
- Style choice does not affect any hard gate. It only affects the soft aesthetic gate, which is evaluated by inspection on POC day.

**Alternatives considered**:

| Alternative | Why rejected |
|-------------|--------------|
| Server-issued token endpoint | Adds ≥1 day server work to a 1-day POC; satisfies letter not intent of Principle II. |
| Self-hosted Protomaps PMTiles on Azure Blob Storage | Right long-term answer for spike-proofness, but multi-day setup; defer to migration phase if POC passes. |
| MapTiler with `client.maptiler.com` SDK | The MapTiler-specific JS SDK wraps MapLibre and adds vendor lock-in. Using MapLibre directly against MapTiler tiles is more portable. |
| Stadia Maps free tier | Comparable, but non-commercial-only license is awkward for the project's potential future. MapTiler's tier is unrestricted by use case at the same volume. |

**POC implication**: One `appsettings.json` entry (`MapTiler:ApiKey`, `MapTiler:StyleUrl`), one MapTiler account signup, one URL restriction configured. Total setup time: ≤15 minutes on POC morning.

---

## R3 — Performance measurement protocol

**Question**: What specific measurements does the spec's "Performance Measurement Set" entity consist of, how are they recorded identically on both pages, and how are they written into the decision record?

**Decision**: Adopt the following measurement protocol, applied identically to `TransitMap.razor` (baseline) and `MapLibreTest.razor` (POC):

**Measurement 1 — Cold-load time** (gate a)
- Method: Chrome DevTools → Network panel → Disable cache. Hard reload. Read "Largest Contentful Paint" (LCP) from the Performance panel.
- Recorded value: LCP in milliseconds.
- Repeat: 3 runs per page; record median.

**Measurement 2 — Sustained animation FPS** (gate b)
- Method: Chrome DevTools → Performance panel → Record. Start recording, wait ~5 seconds for SignalR data to flow, stop after a 10-second window.
- Recorded values:
  - Median FPS over the 10-second window
  - Worst-case (minimum) FPS during the window
  - Per-frame timing p50, p95, p99 (read from the Performance panel summary)

**Measurement 3 — Long-task count** (gate b corollary, spec SC-005)
- Method: A `PerformanceObserver` registered for `entryType: 'longtask'` on both pages dumps any task >50ms to `console.log` with timestamp. During the same 10-second measurement window, count the logged entries.
- Recorded value: integer count of long tasks in the 10-second window.

**Measurement 4 — Multi-route polyline rendering** (gate c)
- Method: Navigate to a view with ≥5 simultaneously visible MARTA routes. Visually confirm no rendering defects (gaps, jagged segments, mis-projected lines). Capture a screenshot for the decision record.
- Recorded value: pass/fail + screenshot reference.

**Measurement 5 — Click handlers** (gate d)
- Method: With page open and live data, click one vehicle marker; observe that the Blazor-side `OnBusMarkerClicked` event fires (verify via `console.log` in the handler). Click an empty area of the map; observe `OnMapBodyClicked` fires.
- Recorded value: pass/fail.

**Measurement 6 — Transferred bytes** (supporting evidence, not gated)
- Method: Chrome DevTools → Network panel → reload with cache disabled → read "Transferred" total at the bottom of the panel.
- Recorded value: total kilobytes transferred for the page's first load.

**Rationale**:

- All six measurements are obtainable from a standard developer browser without additional tooling. This matches the spec's assumption that browser-native instrumentation is sufficient.
- Measurements 1, 2, 6 use Chrome DevTools panels directly — no source code changes required on either page.
- Measurement 3 (long tasks) does require a small JS snippet on each page; the snippet is identical on both and lives in the page's `OnInitializedAsync` equivalent.
- Measurements 4, 5 are qualitative pass/fail but documented with screenshots/logs for the decision record.
- Median of 3 runs for cold load (Measurement 1) is the minimum reasonable rigor; single runs are too noisy to compare.

**Alternatives considered**:

| Alternative | Why rejected |
|-------------|--------------|
| Lighthouse-only | Captures cold-load metrics well but doesn't observe live-data animation FPS; needs to be paired with DevTools Performance. Use Lighthouse as a supplemental cross-check, not the primary tool. |
| Custom `requestAnimationFrame` self-timing with CSV dump | More precise than DevTools but adds animator-side changes that could themselves affect timing. Reserved for tie-breaking if a hard gate is borderline. |
| Synthetic 200-marker stress test | Faster and reproducible, but spatial distribution differs from live data (downtown clustering matters for renderer stress). Use only as a fallback if MARTA service is sleepy on POC day. |

**POC implication**: Add an identical 10-line `PerformanceObserver` snippet to both pages (or a shared `wwwroot/js/perf-observer.js` referenced by both). Everything else is read from DevTools panels — no further code changes.

---

## R4 — Mapping the four Azure-specific touch points in `vehicle-animator.js`

**Question**: The existing animator has exactly four call sites that touch Azure Maps APIs. What are the MapLibre equivalents?

**Decision**: The four call sites map as follows:

| Site (azure) | Azure Maps API | MapLibre equivalent |
|--------------|----------------|---------------------|
| `vehicle-animator.js:213` — `ChefMap.maps[containerDivId]` | Map registry (your wrapper) | `ChefMapLibre.maps[containerDivId]` — same pattern, new namespace |
| `vehicle-animator.js:219` — `map.sources.getById('vehicles')` | Azure `DataSource` lookup | `map.getSource('vehicles')` — returns a MapLibre `GeoJSONSource` |
| `vehicle-animator.js:164–168` (in `tick`) — `ds.getShapeById(...)` + `shape.setCoordinates(newPos)` | Per-shape position mutation | **Removed.** Replaced by accumulating new positions in the in-memory `vehicles` map; once per RAF tick, build a full `FeatureCollection` and call `source.setData(fc)`. |
| `vehicle-animator.js:296–300` — `new atlas.data.Feature(new atlas.data.Point(startPos), props, id)` + `ds.add(...)` | Azure Feature construction and add | **Removed.** Feature objects are plain GeoJSON literals; "adding" a new vehicle just means it shows up in the next `setData` call. |

**Rationale**:

- The two `tick`-loop touch points (sites 3 and 4 in the existing code) are the highest-frequency call sites. Eliminating per-shape mutation in favor of once-per-tick source replacement is the central architectural change.
- The `processNearestPointBatch` flow is restructured: instead of creating Azure Feature objects on first-seen vehicles and mutating them thereafter, the new animator only ever updates the in-memory `vehicles` map. The map is the source of truth; MapLibre's source is rebuilt from it each frame.
- All non-Azure logic (haversine, cumulative distances, sub-path extraction, wrap-around handling, interpolation math, extrapolation, mid-animation handoff, route-transfer teleport detection) ports unchanged.

**Alternatives considered**:

| Alternative | Why rejected |
|-------------|--------------|
| Keep per-feature mutation by tracking GeoJSON feature objects and patching their coordinates in place, then `setData(fc)` with the patched collection | Same network of object references; no behavioral difference. Adds complexity without benefit. |
| Use MapLibre `Marker` (DOM) wrappers | DOM-based; rejected in R1. |

**POC implication**: `maplibre-vehicle-animator.js` is a near-mechanical port of `vehicle-animator.js`. The non-Azure logic (lines 19–86 of the source: `haversineMeters`, `buildCumulativeDistances`, `findNearestIndex`, `extractSubPath`, `interpolateAlongPath`) is copied verbatim. The `tick`, `processNearestPointBatch`, `start`, `stop`, and `loadRouteGeometry` functions are rewritten only at the four touch points identified above. Estimated port effort: ~2 hours including testing.

---

## Summary of Phase 0 Decisions

1. **MapLibre source update**: Single `vehicles` GeoJSON source, replaced once per RAF tick via `setData`. (R1)
2. **MapTiler auth**: URL-restricted public API key in `appsettings.json`. (R2)
3. **Measurement protocol**: 6 measurements (cold load, FPS median/min/p95/p99, long tasks, polyline rendering, click handlers, transferred bytes), captured identically on both pages via Chrome DevTools + a shared `PerformanceObserver` snippet. (R3)
4. **Animator port**: 4 Azure-specific call sites mapped to MapLibre equivalents; non-provider logic copied verbatim. ~2 hours of port work. (R4)

No NEEDS CLARIFICATION items remain.
