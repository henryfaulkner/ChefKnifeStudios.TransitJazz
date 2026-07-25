# Implementation Plan: Real-Time Bus Map Tracker

**Feature Branch**: `003-bus-map-tracker`  
**Created**: 2026-05-05  
**Spec**: [spec.md](spec.md)  
**Status**: Draft

---

## Constitution Check

| Principle | Compliance |
|-----------|-----------|
| I. Decoupled Cloud Architecture | ✅ Frontend connects via SignalR; no backend coupling |
| II. No Frontend Secrets | ✅ Azure Maps token fetched via existing `tokenApiUrl`; no secrets in client |
| III. Real-time Data Processing Pipeline | ✅ Consuming `VehiclePositionUpdatedEvent` from the existing pipeline |
| IV. OpenTelemetry Observability | ✅ Existing OTEL setup unchanged; new page adds no new observability concerns |
| V. Azure DevOps CI/CD | ✅ No pipeline changes needed; this is a client-only change |

---

## Overview

This feature merges `SignalRTest.razor` and `AzureMapsTest.razor` into a new `TransitMap.razor` page that shows live bus positions on an Azure Map. It involves:

1. A new Blazor page (`TransitMap.razor` + `TransitMap.razor.cs`)
2. Cleanup of the `Map` shared component (remove jobsite-specific methods)
3. A rewrite of the `azure-maps-interop.js` data source and layer (replace jobsite layer with bus-positions layer; add `upsertBusMarker`)

There is no nav menu component in this project — no nav changes are required.

No new NuGet packages, no backend changes, no new Shared project types — `VehiclePositionUpdatedEvent`, `VehicleData`, `PositionData`, and `TripData` are already in the `Shared` project.

---

## Affected Files

### New Files
| File | Purpose |
|------|---------|
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor` | Page template: Map component + connection status badge |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` | Code-behind: SignalR subscription, event dispatch, IAsyncDisposable |

### Modified Files
| File | Change Summary |
|------|---------------|
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/azure-maps-interop.js` | Replace jobsite layer with bus-positions layer; add `upsertBusMarker`; remove dead jobsite functions |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs` | Remove `PlotJobSitesAsync`, `CenterJobsitePinAsync`; add `UpsertBusMarkerAsync` |
### Retained (unchanged — routes kept, no nav to update)
| File | Change |
|------|--------|
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/SignalRTest.razor` | Nav entry removed only |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/AzureMapsTest.razor` | Nav entry removed only |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/AzureMapsTest.razor.cs` | Unchanged |

---

## Phase 1 — JavaScript: Replace Jobsite Layer with Bus-Positions Layer

**Goal**: `azure-maps-interop.js` initializes a `"bus-positions"` data source and `"bus-positions-layer"` symbol layer, and exposes `upsertBusMarker`.

### 1.1 — Remove Jobsite Infrastructure

Delete or replace the following from `azure-maps-interop.js`:
- Top-level constants: `defaultPinState`, `hoverPinState`, `activePinState`, `fallbackPinIcon`, all `transitPin*` icon constants and paths
- `OvercastMap.centerJobsitePin`
- `OvercastMap.plotFeatures`
- `OvercastMap.toggleJobSiteMarkerActiveState`
- `atlas.Map.prototype.initDataSourceForTransitPins` — replace entirely (see 1.2)

Keep:
- `OvercastMap.maps`, `OvercastMap.shapes`, `OvercastMap.popups`
- `OvercastMap.createMap` (update the `ready` handler call from `initDataSourceForTransitPins` → `initDataSourceForBusPositions`)
- `OvercastMap.setMapZoom`, `OvercastMap.setMapStyle`, `OvercastMap.toggleTraffic`

### 1.2 — Add `initDataSourceForBusPositions`

Replace `atlas.Map.prototype.initDataSourceForTransitPins` with `atlas.Map.prototype.initDataSourceForBusPositions`:

```javascript
atlas.Map.prototype.initDataSourceForBusPositions = async function (containerDivId) {
    let map = OvercastMap.maps[containerDivId];
    if (map == null) return;

    let sourceId = 'bus-positions';
    if (map.sources.getById(sourceId) != null) return;

    let ds = new atlas.source.DataSource(sourceId);
    map.sources.add(ds);

    // Load bus pin sprite — use existing stop-pin-green as the bus icon
    try {
        await map.imageSprite.add('bus-pin', '/images/map-pins/stop-pin-green.png');
    } catch (err) {
        console.warn('[OvercastMap] Could not load bus-pin sprite:', err);
    }

    let busLayer = new atlas.layer.SymbolLayer(ds, 'bus-positions-layer', {
        iconOptions: {
            image: 'bus-pin',
            size: 0.8,
            anchor: 'center',
            allowOverlap: true,
            ignorePlacement: true
        },
        textOptions: {
            textField: ['get', 'vehicleId'],
            offset: [0, 1.2],
            color: 'white',
            size: 11,
            haloColor: '#1a1a2e',
            haloWidth: 2
        },
        filter: ['==', ['geometry-type'], 'Point']
    });

    map.layers.add(busLayer);

    // Hover tooltip
    map.events.add('mouseover', busLayer, (e) => {
        map.getCanvasContainer().style.cursor = 'pointer';
        if (!e.shapes || e.shapes.length === 0) return;
        let p = e.shapes[0].getProperties();
        OvercastMap._showBusTooltip(map, p, e.position);
    });

    map.events.add('mouseout', busLayer, () => {
        map.getCanvasContainer().style.cursor = 'grab';
        OvercastMap._hideBusTooltip();
    });

    map.events.add('dataremoved', ds, () => {
        OvercastMap.shapes = {};
    });
};
```

### 1.3 — Add `upsertBusMarker`

```javascript
upsertBusMarker: function (containerDivId, vehicleId, latitude, longitude) {
    if (latitude == null || longitude == null || isNaN(latitude) || isNaN(longitude)) {
        console.warn('[OvercastMap] upsertBusMarker: invalid coordinates for vehicle', vehicleId);
        return;
    }

    let map = OvercastMap.maps[containerDivId];
    if (map == null) return;

    let ds = map.sources.getById('bus-positions');
    if (ds == null) return;

    let existing = OvercastMap.shapes[vehicleId];
    if (existing) {
        existing.setCoordinates([longitude, latitude]);
    } else {
        let feature = new atlas.data.Feature(
            new atlas.data.Point([longitude, latitude]),
            { vehicleId: vehicleId },
            vehicleId
        );
        let shape = new atlas.Shape(feature);
        ds.add(shape);
        OvercastMap.shapes[vehicleId] = shape;
    }
},
```

### 1.4 — Add Tooltip Helpers

```javascript
_busPopup: null,

_showBusTooltip: function (map, props, position) {
    if (!OvercastMap._busPopup) {
        OvercastMap._busPopup = new atlas.Popup({ closeButton: false });
    }
    let routeText = props.routeId ? `<br/>Route: ${props.routeId}` : '';
    let tsText = props.timestamp
        ? `<br/>${new Date(props.timestamp * 1000).toLocaleTimeString()}`
        : '';
    OvercastMap._busPopup.setOptions({
        content: `<div style="padding:4px 8px;font-size:12px">
                    <b>Vehicle: ${props.vehicleId}</b>${routeText}${tsText}
                  </div>`,
        position: position,
        pixelOffset: [0, -10]
    });
    OvercastMap._busPopup.open(map);
},

_hideBusTooltip: function () {
    if (OvercastMap._busPopup) OvercastMap._busPopup.close();
},
```

### 1.5 — Update `createMap` Ready Handler

Change the `ready` event callback from:
```javascript
await map.initDataSourceForTransitPins(containerDivId);
```
to:
```javascript
await map.initDataSourceForBusPositions(containerDivId);
```

---

## Phase 2 — Shared Component: Clean Up `Map.razor.Helper.cs`

**Goal**: Remove jobsite-specific methods and add `UpsertBusMarkerAsync`.

### 2.1 — Remove Dead Methods

Delete from `Map.razor.Helper.cs`:
- `PlotJobSitesAsync(object? mapFeatureCollection, bool centerMap)`
- `CenterJobsitePinAsync(int jobsiteId)`

### 2.2 — Add `UpsertBusMarkerAsync`

```csharp
public async Task UpsertBusMarkerAsync(string vehicleId, float latitude, float longitude)
{
    try
    {
        await JsRuntime.InvokeVoidAsync("OvercastMap.upsertBusMarker",
            ElementId, vehicleId, latitude, longitude);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.ToString());
    }
}
```

---

## Phase 3 — New Blazor Page: `TransitMap.razor` + `TransitMap.razor.cs`

### 3.1 — `TransitMap.razor` Template

```razor
@page "/transit-map"
@using ChefKnifeStudios.TransitJazz.Client.Shared.Components

<div class="transit-map-container">
    <div class="connection-status connection-status--@_connectionCssClass">
        @_connectionLabel
    </div>
    <Map CameraOptions="DefaultCameraOptions"
         OnMapReady="OnMapReadyAsync" />
</div>
```

**Notes:**
- The connection badge overlays the top-left corner via CSS (absolute positioning, z-index above map).
- `_connectionCssClass` drives a CSS modifier: `connecting` / `connected` / `disconnected`.

### 3.2 — `TransitMap.razor.cs` Code-Behind

```csharp
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.WebApp.Pages;

public partial class TransitMap : ComponentBase, IDisposable
{
    [Inject] ISignalRNotificationService NotificationService { get; set; } = null!;
    [Inject] ILogger<TransitMap> Logger { get; set; } = null!;

    Map? _map;
    bool _mapReady;

    string _connectionLabel = "Connecting…";
    string _connectionCssClass = "connecting";

    static CameraOptions DefaultCameraOptions
        => new() { Center = new Position(33.749, -84.388), Zoom = 10 };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await NotificationService.InitAsync();
            _connectionLabel = "Connected";
            _connectionCssClass = "connected";
            NotificationService.NotificationReceived += HandleBatchAsync;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: Failed to connect to SignalR hub");
            _connectionLabel = "Disconnected";
            _connectionCssClass = "disconnected";
        }
    }

    async Task OnMapReadyAsync(Map map)
    {
        _map = map;
        _mapReady = true;
    }

    // Named method (not lambda) so -= unsubscription works correctly in Dispose.
    // Signature matches: public delegate Task SignalRNotificationHandler(List<EventEnvelope> batch)
    async Task HandleBatchAsync(List<EventEnvelope> batch)
    {
        if (!_mapReady || _map is null) return;

        foreach (var envelope in batch)
        {
            if (envelope.Payload is not VehiclePositionUpdatedEvent evt) continue;
            if (evt.Position is null) continue;

            await _map.UpsertBusMarkerAsync(
                evt.Vehicle.Id,
                evt.Position.Latitude,
                evt.Position.Longitude);
        }

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        NotificationService.NotificationReceived -= HandleBatchAsync;
    }
}
```

**Key design notes:**
- `IDisposable` with synchronous `Dispose()` — matches the project's established pattern (see `LobbyViewModel`). Blazor detects `IDisposable` on the partial class; no `@implements` directive needed in the `.razor` file.
- `HandleBatchAsync` is a named method — required so that `-=` in `Dispose` refers to the same delegate instance. A lambda subscribed with `+=` cannot be unsubscribed.
- Delegate type is `SignalRNotificationHandler` → `Task (List<EventEnvelope> batch)` — confirmed from the service definition.
- Guard `_mapReady` prevents marker calls before the JS layer is initialized (SignalR can deliver batches before `OnAfterRenderAsync` fires).
- `InvokeAsync(StateHasChanged)` is required because `SignalRNotificationHandler` callbacks arrive on a non-Blazor sync context.

---

## Phase 4 — Cleanup: `AzureMapsTest.razor.cs`

Remove the `SampleDataHelper` static class and `JobsiteData` record from `AzureMapsTest.razor.cs` — these are dead code now that `PlotJobSitesAsync` is removed from the `Map` component. The `AzureMapsTest.razor` and `AzureMapsTest.razor.cs` files themselves remain (routes are kept).

If `AzureMapsTest.razor.cs` still references `PlotJobSitesAsync` after Phase 2 removes it, update `AzureMapsTest.razor.cs` to compile cleanly (e.g., empty `MapOnReadyAsync`).

---

## Data Flow Summary

```
Backend Worker
  └─► SignalR Hub (/hubs/transit)
        └─► SignalRNotificationService.NotificationReceived
              └─► TransitMap.HandleBatchAsync(batch)
                    └─► foreach VehiclePositionUpdatedEvent in batch
                          └─► Map.UpsertBusMarkerAsync(vehicleId, lat, lng)
                                └─► JS: OvercastMap.upsertBusMarker(divId, vehicleId, lat, lng)
                                      ├─► shape exists? → shape.setCoordinates([lng, lat])
                                      └─► new?          → ds.add(new atlas.Shape(feature))
```

---

## Key Technical Decisions

| Decision | Rationale |
|----------|-----------|
| Replace jobsite layer entirely (not extend) | Jobsite concepts don't exist in this domain; coexisting layers would leave dead code permanently |
| `vehicleId` as GeoJSON feature `id` | Enables O(1) lookup in `OvercastMap.shapes` dict; avoids scanning the data source |
| `atlas.Shape` wrapper around `atlas.data.Feature` | `Shape` is mutable — `setCoordinates()` updates the position without remove/re-add, which avoids flicker |
| Guard `_mapReady` flag in `HandleBatchAsync` | Batches can arrive from SignalR before `OnMapReady` fires; without the guard, `JsRuntime.InvokeVoidAsync` throws because the JS map doesn't exist yet |
| `IDisposable` (not `IAsyncDisposable`) on `TransitMap` | Matches the project's established pattern (e.g. `LobbyViewModel`). Blazor detects `IDisposable` on the partial class automatically — no `@implements` directive needed in the `.razor` file. |
| Named method for `+=` / `-=` (not lambda) | A lambda subscribed with `+=` cannot be unsubscribed with `-=` — the delegate instance differs. Named method is the only way `Dispose` can correctly remove the handler. |
| Tooltip via `atlas.Popup` (not Blazor markup) | Tooltip must respond to JS mouseover events; a Blazor component can't intercept Azure Maps canvas hover events efficiently |

---

## Open Questions / Pre-Implementation Checks

1. **Bus pin image asset**: The plan reuses the existing `/images/map-pins/stop-pin-green.png` as the bus icon. Verify this file exists in `wwwroot/images/map-pins/` before Phase 1. If a custom bus icon is preferred, drop the PNG in that directory and update the sprite path in `initDataSourceForBusPositions`.

---

## Success Criteria Traceability

| SC | Covered By |
|----|-----------|
| SC-001: Map renders in <5s | Phase 1 (JS init) + Phase 3 (page) |
| SC-002: Bus marker visible <10s after connect | Phase 3 (HandleBatchAsync) |
| SC-003: No duplicate markers on update | Phase 1.3 (upsertBusMarker upsert logic) |
| SC-004: Status badge transitions in <3s | Phase 3 (OnInitializedAsync sets status) |
| SC-005: Clean state on re-navigation | Phase 3 (IAsyncDisposable) |
| SC-006: Null coords skipped silently | Phase 1.3 (upsertBusMarker guard) + Phase 3 (Position null check) |
| SC-007: Old routes reachable, not in nav | Phase 4 (nav update only) |
