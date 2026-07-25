# Phase 0 Research: Mobile Map Controls & Wider Default Zoom

All "NEEDS CLARIFICATION" items from Technical Context resolved below. This feature is a small,
configuration-level change to an existing MapLibre map; research focuses on the exact MapLibre APIs
and the one non-obvious gotcha (the `touchZoomRotate` flag).

## Decision 1 — Wider default zoom value

- **Decision**: Lower `DefaultCameraOptions.Zoom` from `9.5` to **`8.5`** in
  `Client.WebApp/Pages/TransitMap.razor.cs` (center unchanged at `33.749, -84.388`).
- **Rationale**: MapLibre/Web-Mercator zoom is logarithmic — each whole level halves/doubles the
  visible span. `9.5 → 8.5` roughly **doubles** the visible map extent, which satisfies SC-001
  ("wider extent than the previous default") on both phone and desktop while staying well above the
  existing `minZoom: 7` floor. Going to `8.0` or lower risks the metro area reading as too small/empty
  on a phone (spec US1 acceptance #2). `8.5` is the conservative one-level-wider choice; final value is
  a tuning decision validated against SC-001/SC-003, not a hard contract.
- **Alternatives considered**:
  - `9.0` — only marginally wider; may not be perceptibly "wider by default."
  - `7.5`/`7.0` — too wide for a phone; metro detail becomes hard to read and pushes against `minZoom`.
  - Compute a `fitBounds` over all routes at load — heavier, depends on route data being loaded first,
    and overrides the simple "default view" model; rejected as scope creep.

## Decision 2 — Enabling pinch-to-zoom without enabling rotation

- **Decision**: In `createMap`, stop passing `touchZoomRotate: false`. Instead let touch zoom be on
  (MapLibre default) and explicitly disable only the rotation sub-behavior:
  ```js
  map.touchZoomRotate.enable();
  map.touchZoomRotate.disableRotation();
  ```
  Keep `dragRotate: false` (already present) so two-finger/right-drag rotation stays off on desktop too.
- **Rationale**: This is the load-bearing finding. MapLibre's `touchZoomRotate` is a **single combined
  handler** for pinch-zoom AND two-finger rotate. Passing `touchZoomRotate: false` (current code)
  disables BOTH — which is exactly why pinch-to-zoom is broken on mobile today (spec US2). The handler
  exposes `disableRotation()` to keep pinch-zoom while suppressing rotation, satisfying FR-003 (pinch
  zoom works) and FR-007 (map stays north-up) simultaneously. `dragRotate: false` independently covers
  the desktop rotate gesture.
- **Alternatives considered**:
  - Leave `touchZoomRotate` default-on without `disableRotation()` — would allow accidental rotation,
    violating FR-007.
  - Custom pinch handling on the canvas — unnecessary reinvention; MapLibre already provides the exact
    knob needed.

## Decision 3 — On-screen zoom controls & drag-pan

- **Decision**: Add a MapLibre `NavigationControl` configured as zoom-only and add it to the map after
  creation:
  ```js
  map.addControl(new maplibregl.NavigationControl({ showCompass: false, showZoom: true, visualizePitch: false }), 'bottom-right');
  ```
  Leave `dragPan` at its default (enabled) for both touch one-finger drag and desktop click-drag —
  no flag change needed; confirm via quickstart. Retain the existing bespoke ctrl+drag pan handler
  (it augments, does not conflict).
- **Rationale**: `NavigationControl` gives FR-005's tap/click zoom buttons for free with built-in,
  already-localized button titles (satisfies Principle XII without new resx strings). `showCompass:false`
  keeps it consistent with the no-rotation rule (FR-007) and avoids a control that would do nothing.
  `dragPan` is on by default in MapLibre, so FR-006 is already met for standard gestures; the explicit
  task is to verify, not to add code. Anchoring **bottom-right** keeps it clear of the zoom-adaptive
  route filter grid (top-left/top-right per Principle X), satisfying the non-occlusion gate. Note the
  gear settings FAB also lives bottom-right — placement must avoid overlapping it (quickstart check;
  fall back to `bottom-left` if they collide).
- **Alternatives considered**:
  - Custom HTML zoom buttons calling `ChefMap.setMapZoom` — more code, must re-localize, must re-style;
    rejected in favor of the native control.
  - `top-right` anchor — collides with the route filter grid when zoomed in (Principle X); rejected.

## Decision 4 — Manual interaction vs. automatic camera moves (FR-009)

- **Decision**: No new "user interacted" state machine is required for MVP. Audit the two existing
  automatic camera moves and ensure neither fights the user:
  - `centerVehiclePin` (`easeTo`) — only fires on an explicit user bus-marker click; intentional, keep.
  - `plotFeatures` `fitBounds` (when `centerMap === true`) — the only involuntary recenter. Confirm how
    `PlotVehiclesAsync(centerMap:)` is invoked; if vehicles are plotted with `centerMap: true` on a
    recurring basis it would override a manual pan. Resolution: ensure recurring vehicle updates pass
    `centerMap: false` (one-time initial fit only), or gate the fit to first load.
- **Rationale**: The vehicle animation runs on a `requestAnimationFrame` loop that rebuilds the GeoJSON
  source per tick but does NOT recenter per tick (verified: no `easeTo`/`setCenter` in the animation
  path). The only recurring-override risk is `fitBounds` via `centerMap`. Scoping the fix to "fit once,
  then leave the camera to the user" satisfies FR-009 with minimal change and no new state.
- **Alternatives considered**:
  - Track a `userHasInteracted` flag set on `dragstart`/`zoomstart` and suppress all auto-moves after —
    more robust but more code; deferred unless the audit shows recurring `centerMap: true` plotting.

## Resolved Unknowns Summary

| Unknown | Resolution |
|---------|-----------|
| Exact wider zoom value | `8.5` (one level wider; tunable, validated by SC-001) |
| Why pinch-zoom is broken | `touchZoomRotate: false` disables pinch+rotate together |
| How to keep north-up while enabling pinch | `touchZoomRotate.enable()` + `disableRotation()`; keep `dragRotate:false` |
| On-screen zoom control | MapLibre `NavigationControl`, zoom-only, `bottom-right` (clear of filter grid) |
| New resx strings needed? | No — native control titles; no new app-authored visible copy |
| FR-009 mechanism | Audit `centerMap`/`fitBounds`; fit once, don't recenter on recurring vehicle plots |
