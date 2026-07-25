# Contract: Map Interaction Behavior

This is a **UI interaction contract** (the appropriate contract type for a Blazor WASM frontend per the
plan workflow). It is not a network/API contract — this feature exposes no new endpoints, JS-invokable
methods, or event-bus messages. The contract below is the observable behavior the implementation MUST
satisfy and the quickstart verifies.

## Surface

- Component: `Map` (`Client.Shared/Components/Map.razor` + `.cs`)
- Interop: `window.ChefMap.createMap(containerDivId, dotNetRef)` in `map-interop.js`
- Caller default: `TransitMap.DefaultCameraOptions`

No method signatures change. `createMap`, `getMapSettings`, `setMapZoom`, and `plotFeatures` keep their
current signatures. The contract is purely behavioral.

## Behavioral guarantees

### Default view (FR-001, FR-002)
- **GIVEN** a fresh load with no overriding camera state
- **WHEN** the map finishes initializing
- **THEN** the map is centered at `(33.749, -84.388)` at zoom `8.5` (wider than the prior `9.5`),
  with the visible extent strictly larger than the prior default on the same viewport.

### Touch zoom (FR-003)
- **GIVEN** a touch device
- **WHEN** the user performs a two-finger pinch
- **THEN** the map zooms about the pinch midpoint, bounded by `[7, 18]`, and the bearing/pitch remain 0.

### Desktop zoom (FR-004)
- **GIVEN** a pointer device
- **WHEN** the user scrolls or double-clicks over the map
- **THEN** the map zooms about the cursor, bounded by `[7, 18]`.

### On-screen zoom controls (FR-005)
- **GIVEN** any device
- **WHEN** the user taps/clicks the zoom-in or zoom-out control
- **THEN** the map zoom changes by MapLibre's standard step (≈1 level), bounded by `[7, 18]`, and the
  control does NOT overlap the route filter grid or the settings gear FAB.

### Pan / drag (FR-006)
- **GIVEN** a touch device (one finger) or pointer device (click-drag)
- **WHEN** the user drags across the map
- **THEN** the visible center moves to follow the drag; no rotation/tilt occurs.

### No rotation/tilt (FR-007)
- **GIVEN** any rotate or twist gesture (two-finger twist on touch, right-drag on desktop)
- **WHEN** performed and released
- **THEN** map bearing == 0 and pitch == 0 (north-up, flat) at all times.

### Zoom bounds (FR-008)
- **GIVEN** any zoom path
- **WHEN** the user attempts to exceed `minZoom: 7` or `maxZoom: 18`
- **THEN** the zoom clamps to the bound; no over-zoom.

### Manual interaction precedence (FR-009)
- **GIVEN** the user has manually panned or zoomed
- **WHEN** a recurring vehicle-position update is plotted
- **THEN** the map does NOT auto-recenter/`fitBounds` over the user's view (recurring plots pass
  `centerMap: false`; only an explicit user action like a bus-marker click may move the camera).

## Out of scope (explicitly NOT part of this contract)
- Persisting the last-used view across sessions.
- Enabling map rotation or 3D pitch.
- Any change to vehicle animation cadence, audio, or the basemap style.
