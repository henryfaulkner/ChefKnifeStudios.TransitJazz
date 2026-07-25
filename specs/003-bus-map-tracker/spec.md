# Feature Specification: Real-Time Bus Map Tracker

**Feature Branch**: `003-bus-map-tracker`  
**Created**: 2026-05-05  
**Status**: Draft  
**Input**: User description: "Merge the SignalRTest page and the AzureMapsTest page. Create markers on an Azure map that represent each bus's current location and update when the location changes."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Live Bus Positions on a Map (Priority: P1)

A user navigates to the Transit Map page. An Azure Map renders centered on the Atlanta metro area. As the SignalR connection receives bus position event batches, each bus appears as a marker on the map at its reported latitude/longitude.

**Why this priority**: This is the core product experience. Without it, the feature has no value.

**Independent Test**: Start the full stack (AppHost), navigate to `/transit-map`, confirm the Azure Map loads, confirm bus markers appear within a few seconds as SignalR events arrive.

**Acceptance Scenarios**:

1. **Given** the page loads, **When** the Azure Map initializes, **Then** the map renders centered on Atlanta (approx. lat 33.749, lng -84.388) at a zoom level that shows the metro area.
2. **Given** the SignalR connection is established, **When** a batch of `EventEnvelope` events arrives containing `VehiclePositionUpdatedEvent` payloads with valid `latitude`/`longitude`, **Then** a marker is created on the map for each bus ID not yet represented.
3. **Given** a bus marker already exists on the map, **When** a new `VehiclePositionUpdatedEvent` arrives for the same vehicle ID, **Then** the marker moves to the new coordinates rather than a duplicate being created.
4. **Given** the page is open, **When** SignalR sends multiple batches in sequence, **Then** all bus markers reflect the latest known position for each bus.

---

### User Story 2 - SignalR Connection Status Indicator (Priority: P2)

The page shows a small connection status badge (e.g., "Connected" / "Connecting…" / "Disconnected"). The user can see at a glance whether live data is flowing.

**Why this priority**: Without a status indicator, users have no way to distinguish a working map from a stalled one.

**Independent Test**: Load the page while the API is offline — verify "Disconnected" is shown. Start the API — verify the status transitions to "Connected".

**Acceptance Scenarios**:

1. **Given** the page is loading, **When** `SignalRNotificationService.InitAsync` has not yet completed, **Then** the badge shows "Connecting…".
2. **Given** the SignalR connection is active, **When** the hub is reachable and a heartbeat or first message is received, **Then** the badge shows "Connected".
3. **Given** the SignalR hub is unreachable, **When** `InitAsync` fails or the connection drops, **Then** the badge shows "Disconnected" without crashing the page.

---

### User Story 3 - Bus Marker Tooltip on Hover (Priority: P3)

When a user hovers over a bus marker on the map, a tooltip or popup appears showing the bus's vehicle ID, route ID (if available), and last-updated timestamp.

**Why this priority**: Adds context to the map but is not required for the core real-time tracking experience.

**Independent Test**: Hover over any bus marker and verify the popup appears with at minimum the vehicle ID and timestamp.

**Acceptance Scenarios**:

1. **Given** a bus marker is on the map, **When** the user hovers over it, **Then** a tooltip shows `Vehicle: {vehicleId}`.
2. **Given** a bus marker's event payload includes `route_id`, **When** the tooltip is shown, **Then** the route ID is included in the tooltip text.
3. **Given** a bus marker's event payload includes `timestamp`, **When** the tooltip is shown, **Then** the timestamp is displayed in a human-readable format.

---

### Edge Cases

- What happens when a `VehiclePositionUpdatedEvent` arrives with `null` or missing `latitude`/`longitude`? The marker MUST NOT be created or moved; the invalid event is silently skipped with a debug-level log entry.
- What happens when hundreds of buses are on the map simultaneously? The JavaScript data source should handle bulk updates without blocking the UI thread — use batch updates where possible.
- What happens when the same batch contains multiple `VehiclePositionUpdatedEvent` entries for the same vehicle ID? Only the last event for that vehicle ID in the batch is applied.
- What happens when the user navigates away and back? The SignalR connection should be re-established and all markers re-rendered from fresh data (no stale state from prior session).
- What happens when the Azure Maps token fetch fails? The map fails to initialize; the page shows an error state rather than a blank container.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Page & Routing
- **FR-001**: The new page MUST be routable at `/transit-map` and registered in the Blazor navigation structure.
- **FR-002**: The `SignalRTest` and `AzureMapsTest` pages MUST have their nav menu entries removed but their routes (`/signalr`, `/maps`) MUST remain accessible — they are retained as developer reference pages, not deleted.

#### Azure Maps Integration
- **FR-003**: The page MUST embed the existing `Map` shared component and pass a `CameraOptions` centered on Atlanta (lat 33.749, lng -84.388, zoom 10).
- **FR-004**: The page MUST call `Map.CreateMapAsync()` during `OnAfterRenderAsync` (first render only) and wait for the `OnMapReady` callback before rendering any bus markers.
- **FR-005**: The existing jobsite pin infrastructure (`"job-sites"` data source, `"transit-pins-layer"` symbol layer, jobsite-specific icon states) in `azure-maps-interop.js` MUST be replaced entirely with a bus-position-focused implementation. Jobsite concepts do not exist in this domain.
- **FR-006**: Bus markers MUST be rendered on a `"bus-positions-layer"` symbol layer backed by a `"bus-positions"` data source.
- **FR-007**: Each bus marker MUST use the bus's vehicle ID as its GeoJSON feature `id` to enable efficient upsert (add-or-move) behavior.

#### SignalR Integration
- **FR-008**: The page MUST inject `ISignalRNotificationService` and call `InitAsync` during `OnInitializedAsync`.
- **FR-009**: The page MUST subscribe to `NotificationService.NotificationReceived` and process events where the payload is a `VehiclePositionUpdatedEvent`.
- **FR-010**: For each valid `VehiclePositionUpdatedEvent` in a batch, the page MUST call a JavaScript interop function to upsert the bus marker on the map (create if new, update coordinates if existing).
- **FR-011**: The page MUST unsubscribe from `NotificationReceived` and dispose the SignalR connection in `IAsyncDisposable.DisposeAsync`.

#### JavaScript Interop
- **FR-012**: `azure-maps-interop.js` MUST expose `OvercastMap.upsertBusMarker(containerDivId, vehicleId, latitude, longitude)`.
- **FR-013**: `upsertBusMarker` MUST check whether a shape with the given `vehicleId` already exists in the `"bus-positions"` data source. If it does, update its coordinates; if not, create a new `atlas.data.Feature` (Point geometry) and add it.
- **FR-014**: `upsertBusMarker` MUST silently no-op (and log a `console.warn`) if `latitude` or `longitude` is `null`, `undefined`, or `NaN`.
- **FR-015**: The bus marker visual style (icon, color, size) MUST be clearly readable against the map background. A future route-layer will be added separately; no route visualization is in scope for this feature.

#### Event Payload Mapping
- **FR-016**: The Blazor client MUST deserialize the `EventEnvelope.Payload` as `VehiclePositionUpdatedEvent` — the existing concrete `ISignalREvent` implementation that carries vehicle position data from the backend.
- **FR-017**: The page MUST read `VehiclePositionUpdatedEvent.VehicleId`, `Latitude`, `Longitude`, `RouteId` (nullable), `Timestamp`, `Bearing` (nullable), and `Speed` (nullable) from the event.

#### Connection Status
- **FR-018**: The page MUST track a local connection state (`Connecting`, `Connected`, `Disconnected`) and update it based on the outcome of `InitAsync` and any subsequent hub state changes.
- **FR-019**: The connection status MUST be rendered visibly on the page and trigger `InvokeAsync(StateHasChanged)` on change (since SignalR callbacks arrive off the Blazor sync context).

### Key Entities

- **VehiclePositionUpdatedEvent**: Existing `ISignalREvent` implementation in the `Shared` project carrying vehicle position data from the backend.
- **TransitMapPage** (`TransitMap.razor` + `TransitMap.razor.cs`): New Blazor page owning the Map component, SignalR subscription, and connection status indicator.
- **OvercastMap.upsertBusMarker**: New JavaScript function in `azure-maps-interop.js` for add-or-move bus marker behavior keyed by vehicle ID, backed by a dedicated `"bus-positions"` data source.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Navigating to `/transit-map` renders an Azure Map within 5 seconds of page load under normal network conditions.
- **SC-002**: Within 10 seconds of the SignalR connection being established, at least one bus marker is visible on the map (assuming the backend is actively publishing events).
- **SC-003**: When a `VehiclePositionUpdatedEvent` arrives for an existing vehicle ID, the marker moves to the new position without a duplicate marker being created — verifiable by inspecting the `"bus-positions"` data source feature count.
- **SC-004**: The connection status badge transitions from "Connecting…" to "Connected" within 3 seconds of a successful hub handshake.
- **SC-005**: Navigating away from and back to `/transit-map` results in a clean state with no duplicate SignalR subscriptions or stale markers.
- **SC-006**: A `VehiclePositionUpdatedEvent` with a null/missing coordinate is skipped without a JavaScript or Blazor exception in the browser console.
- **SC-007**: The `SignalRTest` (`/signalr`) and `AzureMapsTest` (`/maps`) routes remain reachable but are absent from the nav menu.

---

## Assumptions

- `VehiclePositionUpdatedEvent` already exists as a concrete `ISignalREvent` in the `Shared` project and is the type the backend worker publishes for bus position updates.
- The existing `Map` shared component (`Map.razor` / `Map.razor.cs`) requires no structural changes — only `azure-maps-interop.js` needs modification to replace the jobsite layer with the bus-positions layer.
- The jobsite pin concepts (`PlotJobSitesAsync`, `CenterJobsitePinAsync`, `"job-sites"` data source, `"transit-pins-layer"`) in `azure-maps-interop.js` are dead code in the current domain and will be replaced rather than extended.
- A future route-layer feature will add route visualization on top of bus markers; this spec intentionally defers that work.
- No authentication or user-specific state is required for this page — any connected client sees all buses.
- The Azure Maps token endpoint (`tokenApiUrl`) is already configured and functional from the existing implementation.
- Mobile/responsive layout is out of scope for this feature; the map fills available desktop viewport space.
