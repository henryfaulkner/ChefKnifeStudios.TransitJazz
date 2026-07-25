# Feature Specification: Map Render Performance — Tranche 2

**Feature Branch**: `022-map-render-performance`
**Created**: 2026-06-20
**Status**: Draft
**Input**: Design document: `specs/022-map-render-performance/design-tranche-2.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Fast Map Load on Low-Power Devices (Priority: P1)

A transit rider opens the app on a mid-range or throttled device. Currently the map freezes for several seconds while 86 bus routes (111,627 coordinate pairs) load and render. After this feature, routes appear quickly and the map becomes interactive before all background processing finishes.

**Why this priority**: The freeze is the dominant UX defect — users experience a non-responsive map and cannot pan, zoom, or select routes while route data loads. Fixing raw data volume eliminates the root cause.

**Independent Test**: Throttle CPU 4–6× in browser DevTools, reload the app, and measure time from page load to first interactive map state. Routes should be visible and pannable within a dramatically shorter window than before.

**Acceptance Scenarios**:

1. **Given** the app loads on a CPU-throttled device, **When** the map initializes and fetches route shapes, **Then** routes appear on the map within a noticeably shorter time than before tranche 2, and the map responds to pan/zoom while routes are still populating.
2. **Given** the server re-ingests GTFS data, **When** a client requests route shapes, **Then** total coordinate count has dropped roughly 5–10× and the payload size is well under 1 MB.
3. **Given** a user views the map at zoom levels 9–14, **When** routes are displayed using simplified geometry, **Then** route lines look visually identical to the original dense geometry — no visible corner-cutting at stops or turns.

---

### User Story 2 — Route Focus and Hover Remain Correct (Priority: P2)

A user hovers over or selects one or more bus routes. The focused routes highlight and unfocused routes dim, as implemented in feature 020. This behavior must continue correctly after the rendering changes consolidate 86 separate route layers into a single layer.

**Why this priority**: Focus/hover is a core interaction for the route-selection feature (020). Regression here would make the multi-select experience non-functional.

**Independent Test**: With the single routes layer in place, hover over individual routes and use multi-select. Confirm focused routes emphasize and others dim, matching pre-tranche-2 behavior.

**Acceptance Scenarios**:

1. **Given** the single consolidated route layer is active, **When** a user hovers over a route, **Then** that route emphasizes and all others dim.
2. **Given** multi-route selection is active (feature 020), **When** a user selects multiple routes, **Then** selected routes remain emphasized and unselected routes dim.
3. **Given** the user clears route focus, **When** no routes are selected, **Then** all routes return to equal default styling.

---

### User Story 3 — Basemap Toggle Still Shows Routes (Priority: P2)

A user toggles the Street Map setting (feature 017). After the style swap, all routes and checkpoint markers remain visible on the new basemap. This must continue correctly after routes are collapsed from 86 layers into one.

**Why this priority**: The basemap toggle (017) wipes and restores all custom sources/layers after a MapLibre style swap. With the new single routes source, the restore path must be updated or routes will disappear on toggle.

**Independent Test**: Toggle Street Map on and off while routes are displayed. Confirm routes, vehicle dots, and checkpoint markers all reappear correctly on both basemap styles.

**Acceptance Scenarios**:

1. **Given** routes are displayed on the map, **When** the user toggles the Street Map setting, **Then** all routes reappear on the new basemap without requiring a page reload.
2. **Given** routes are displayed with some routes focused, **When** the basemap toggles, **Then** routes restore with their correct colors and the focus state can be reapplied.

---

### User Story 4 — Checkpoint Pulses Still Fire at Correct Positions (Priority: P3)

A bus crosses a checkpoint. The transit soundscape (feature 009) triggers a note and the checkpoint pulses on the map. After geometry simplification, trigger points along simplified routes must remain at sensible positions — not bunched together or missing long segments.

**Why this priority**: The soundscape is the app's signature experience. If simplification pushes trigger-point spacing from ~200 m to multi-kilometer gaps or sub-100 m clusters, the soundscape breaks. This is a verification story, not a new feature.

**Independent Test**: With simplified route geometry, observe checkpoint pulse positions on the map at zoom 9–14. Confirm spacing looks regular (approximately every ~200 m). Ride a simulated or live bus position through several checkpoints and confirm notes trigger.

**Acceptance Scenarios**:

1. **Given** route geometry has been simplified, **When** trigger points are generated from the simplified coordinates, **Then** checkpoint markers appear at regular spacing along routes (not bunched or absent on long segments).
2. **Given** a bus crosses a checkpoint, **When** the crossing event fires, **Then** the soundscape note triggers and the checkpoint pulse animates, same as before simplification.

---

### Edge Cases

- What happens when a route has fewer than 3 coordinates? Simplification is skipped; the route is stored and served as-is.
- What happens if the simplification tolerance is too aggressive and checkpoint spacing degrades? The tolerance constant is lowered and the server re-ingests.
- What happens when the basemap toggles before routes finish rendering? Routes restore correctly once the render completes — the restore path operates on whatever data has been loaded.
- What if a user toggles all-checkpoints visibility before checkpoint configuration finishes (deferred math scenario)? The checkpoint layer fills in progressively; toggling visibility works on what's present, and the rest appear as configuration catches up.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The server MUST simplify route geometry using Ramer–Douglas–Peucker at approximately 10-meter tolerance before storing and serving route shapes.
- **FR-002**: Simplified geometry MUST always preserve the first and last coordinate of each route; routes with fewer than 3 points MUST be served without simplification.
- **FR-003**: The simplification tolerance MUST be a named constant so it can be adjusted without hunting for magic numbers.
- **FR-004**: The client MUST render all 86 routes from a single map source and a single map layer, not 86 individual layers.
- **FR-005**: Route focus and hover effects (feature 020) MUST continue to work correctly on the single consolidated layer.
- **FR-006**: All route geometry MUST cross the browser's WASM–JS boundary in a single bulk call, replacing per-route interop calls.
- **FR-007**: The per-route yielding loop (tranche 1) MUST be replaced by the single bulk call; per-route forced timer round-trips are removed.
- **FR-008**: Trigger-point generation and checkpoint tracker configuration MUST be deferred until after routes are visible and the map is interactive.
- **FR-009**: The basemap Street Map toggle (feature 017) MUST correctly restore the single consolidated routes source and layer after a style swap.
- **FR-010**: Vehicle dot colors per route MUST remain correct after routes collapse to a single layer.
- **FR-011**: Route z-order MUST be preserved: routes render beneath vehicle dots, which render beneath trigger-point/pulse markers.
- **FR-012**: All-checkpoints visibility toggle MUST work correctly even while deferred checkpoint configuration is still in progress.

### Key Entities

- **Route Shape**: An ordered sequence of geographic coordinates representing one bus route. After this feature, routes are stored and served in simplified form (fewer coordinates, same shape at transit zoom levels).
- **Simplification Tolerance**: A named constant controlling how aggressively RDP removes points. Expressed in meters; tunable without code restructuring.
- **Routes Source**: A single GeoJSON FeatureCollection containing all route LineStrings with per-feature `routeId` and `color` properties. Replaces 86 individual sources.
- **Routes Layer**: A single data-driven map line layer consuming the Routes Source. Replaces 86 individual line layers.
- **Trigger Point**: A geographic position along a route where the soundscape fires when a bus passes. Generated from route coordinates after simplification; spacing must remain approximately 200 m.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Total route coordinate count served by the API drops roughly 5–10× after server re-ingest (from ~111,627 to approximately 10,000–20,000).
- **SC-002**: Route shapes API payload size drops to well under 1 MB (from 2.4 MB).
- **SC-003**: The number of distinct map sources created for routes drops from 86 to 1; the number of map layers created for routes drops from 86 to 1.
- **SC-004**: The number of WASM–JS boundary crossings for route geometry drops from approximately 258 (3× per route × 86 routes) to approximately 1–2.
- **SC-005**: Route lines at zoom levels 9–14 are visually indistinguishable from pre-simplification geometry — no visible corner-cutting at stops, turns, or intersections.
- **SC-006**: Trigger-point spacing along simplified routes remains approximately 200 m (within tolerance), consistent with the soundscape design from feature 009.
- **SC-007**: Route hover emphasis, multi-select focus (feature 020), basemap toggle route restore (feature 017), and vehicle dot per-route coloring all pass manual regression checks after the single-layer change.
- **SC-008**: Routes are visible and the map is interactive before checkpoint tracker configuration finishes (deferred math).

## Assumptions

- Tranche 1 is already implemented and merged — the overlay spinner, `FlushTriggerPointsAsync` single-flush, and per-route progress counter from tranche 1 are present in the working tree.
- Feature 017 (basemap toggle) and feature 020 (multi-select route focus) are already implemented and merged; tranche 2 must not regress them.
- Feature 009 (transit soundscape, ~200 m trigger-point spacing) is already implemented; the trigger-point generator runs on whatever coordinates the client receives.
- Server GTFS ingest is a single code path (`GtfsStaticLoader.StartAsync`); simplifying there covers all clients.
- The RDP algorithm will be hand-rolled in C# (~30 lines, no new package dependencies).
- Planar (equirectangular) perpendicular distance is adequate for city-scale geographic simplification at the target tolerance.
- The simplification is implemented and measured (#1) before proceeding to the layer collapse (#2), single-marshal interop (#3), and deferred math (#4) — if #1 alone resolves the UX problem, subsequent changes become optional polish.
- No server/worker API contract changes: `RouteShapeFeature` JSON shape is unchanged, just with fewer coordinates per feature.
- No new NuGet packages, no new npm/JS libraries.
