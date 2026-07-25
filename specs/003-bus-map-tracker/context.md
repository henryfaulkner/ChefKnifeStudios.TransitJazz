# Feature Context: Real-Time Bus Map Tracker (003-bus-map-tracker)

**Purpose**: Self-contained knowledge transfer for a context-cleared agent continuing this feature.  
**Date**: 2026-05-05  
**Status**: Implementation complete, browser verification pending (T-25–T-30)

---

## What This Feature Does

Merges two test pages (`SignalRTest.razor` at `/signalr` and `AzureMapsTest.razor` at `/maps`) into a single production page at `/transit-map`. The new page renders an Azure Map centered on Atlanta and plots a live marker for every MARTA bus, updating position in real-time as SignalR events arrive. Old routes are retained but hidden from navigation (there is no nav menu component — routes are just accessible by direct URL).

---

## Repository Layout

```
C:\Projects\ChefKnifeStudios.TransitJazz\
├── .specify\                          # Spec-Kit config (spec tool, not speckit)
│   └── memory\constitution.md         # Project constitution — read this first
├── specs\
│   └── 003-bus-map-tracker\
│       ├── spec.md                    # Requirements & user stories
│       ├── plan.md                    # Technical plan with full pseudo-code
│       ├── tasks.md                   # 30 tasks, 23 complete, 7 browser-only remain
│       └── context.md                 # This file
└── src\
    ├── ChefKnifeStudios.TransitJazz.Shared\          # Shared DTOs/events (no external deps)
    │   ├── Events\
    │   │   ├── EventEnvelope.cs                      # record(EventType, Timestamp, ISignalREvent)
    │   │   ├── ISignalREvent.cs                      # marker interface
    │   │   └── VehiclePositionUpdatedEvent.cs        # record(VehicleData, PositionData, TripData?)
    │   └── EventData\
    │       ├── VehicleData.cs                        # record(Id, Label, LicensePlate, ...)
    │       ├── PositionData.cs                       # record(Latitude, Longitude, Bearing, SpeedMetersPerSec, ...)
    │       └── TripData.cs                           # record(TripId, RouteId, DirectionId, ...)
    └── Client\
        ├── ChefKnifeStudios.TransitJazz.Client.Core\
        │   └── Services\
        │       └── SignalRNotificationService.cs     # ISignalRNotificationService impl
        ├── ChefKnifeStudios.TransitJazz.Client.Shared\
        │   └── Components\
        │       ├── Map.razor                         # Map template (toolbar + map div)
        │       ├── Map.razor.cs                      # Map component base (ElementId, params, JSInvokable)
        │       ├── Map.razor.Helper.cs               # Map interop methods (CreateMapAsync, UpsertBusMarkerAsync, ...)
        │       └── Map.razor.css                     # .map { height: calc(100vh - 2rem) }
        └── ChefKnifeStudios.TransitJazz.Client.WebApp\
            ├── Pages\
            │   ├── TransitMap.razor                  # NEW — route /transit-map
            │   ├── TransitMap.razor.cs               # NEW — SignalR + map glue
            │   ├── SignalRTest.razor                  # RETAINED at /signalr (dev reference)
            │   ├── AzureMapsTest.razor                # RETAINED at /maps (dev reference)
            │   └── AzureMapsTest.razor.cs             # CLEANED — empty MapOnReadyAsync
            └── wwwroot\
                ├── js\
                │   └── azure-maps-interop.js          # REWRITTEN — bus-positions layer
                └── images\map-pins\
                    └── stop-pin-green.png             # Bus marker icon (confirmed present)
```

---

## Key Types

### `VehiclePositionUpdatedEvent` (Shared project)
```csharp
public sealed record VehiclePositionUpdatedEvent(
    VehicleData Vehicle,
    PositionData Position,
    TripData? Trip
) : ISignalREvent;
```

### `VehicleData`
```csharp
public sealed record VehicleData(
    string Id,          // ← used as marker key
    string? Label,
    string? LicensePlate,
    OccupancyStatus? OccupancyStatus,
    int? OccupancyPercentage
);
```

### `PositionData`
```csharp
public sealed record PositionData(
    float Latitude,
    float Longitude,
    float? Bearing,             // direction of travel — useful for interpolation
    float? SpeedMetersPerSec,   // useful for interpolation
    double? OdometerMeters,
    long? Timestamp,
    uint? CurrentStopSequence,
    string? CurrentStopId,
    VehicleStopStatus? CurrentStatus,
    CongestionLevel? CongestionLevel
);
```

### `TripData`
```csharp
public sealed record TripData(
    string? TripId,     // joins to GTFS static shapes for route pre-pathing
    string? RouteId,    // displayed in tooltip
    int? DirectionId,
    string? StartTime,
    string? StartDate,
    TripScheduleRelationship? ScheduleRelationship
);
```

### `EventEnvelope`
```csharp
public sealed record EventEnvelope(
    string EventType,
    DateTimeOffset Timestamp,
    ISignalREvent Payload        // cast to VehiclePositionUpdatedEvent
);
```

### `ISignalRNotificationService`
```csharp
public delegate Task SignalRNotificationHandler(List<EventEnvelope> batch);

public interface ISignalRNotificationService
{
    event SignalRNotificationHandler? NotificationReceived;
    Task InitAsync(CancellationToken ct = default);
}
```
- Hub URL: `/hubs/transit`
- Hub method: `"ReceiveBatch"` → `List<EventEnvelope>`
- Registered as `Scoped` in `Program.cs`
- Subscribe with a **named method** (not lambda) so `-=` works in `Dispose()`

---

## Implemented Files (verbatim-level summary)

### `TransitMap.razor`
```razor
@page "/transit-map"
@using ChefKnifeStudios.TransitJazz.Client.Shared.Components

<div class="transit-map-container">
    <div class="connection-status connection-status--@_connectionCssClass">
        @_connectionLabel
    </div>
    <Map CameraOptions="DefaultCameraOptions" OnMapReady="OnMapReadyAsync" />
</div>
```
No `@implements` directive needed — Blazor picks up `IDisposable` from the partial class.

### `TransitMap.razor.cs`
```csharp
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

    async Task OnMapReadyAsync(Map map) { _map = map; _mapReady = true; }

    async Task HandleBatchAsync(List<EventEnvelope> batch)
    {
        if (!_mapReady || _map is null) return;
        foreach (var envelope in batch)
        {
            if (envelope.Payload is not VehiclePositionUpdatedEvent evt) continue;
            if (evt.Position is null) continue;
            if (float.IsNaN(evt.Position.Latitude) || float.IsNaN(evt.Position.Longitude))
            {
                Logger.LogDebug("TransitMap: Skipping vehicle {VehicleId} — invalid coordinates", evt.Vehicle.Id);
                continue;
            }
            await _map.UpsertBusMarkerAsync(evt.Vehicle.Id, evt.Position.Latitude, evt.Position.Longitude);
        }
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose() => NotificationService.NotificationReceived -= HandleBatchAsync;
}
```

### `Map.razor.Helper.cs` — relevant public surface
```csharp
// Added this feature:
public async Task UpsertBusMarkerAsync(string vehicleId, float latitude, float longitude)
    // → JS: OvercastMap.upsertBusMarker(ElementId, vehicleId, latitude, longitude)

// Removed this feature:
// PlotJobSitesAsync  — deleted
// CenterJobsitePinAsync — deleted
```

### `azure-maps-interop.js` — architecture
```
window.OvercastMap = {
    shapes: {},         // { [vehicleId]: atlas.Shape } — O(1) lookup
    popups: [],
    maps: {},           // { [containerDivId]: atlas.Map }
    _busPopup: null,    // single shared atlas.Popup for hover tooltips

    createMap(containerDivId, mapComponent)   // init map, calls initDataSourceForBusPositions on ready
    setMapZoom(containerDivId, zoom)
    setMapStyle(containerDivId, mapStyle)
    toggleTraffic(containerDivId, showTraffic)
    upsertBusMarker(containerDivId, vehicleId, lat, lng)  // create-or-move by vehicleId key
    _showBusTooltip(map, props, position)
    _hideBusTooltip()
}

atlas.Map.prototype.initDataSourceForBusPositions(containerDivId)
    // DataSource id: "bus-positions"
    // SymbolLayer id: "bus-positions-layer"
    // Sprite: "bus-pin" from /images/map-pins/stop-pin-green.png
    // Text field: vehicleId (shown below marker)
    // Events: mouseover→tooltip, mouseout→close, dataremoved→clear shapes{}
```

Key invariant: **`vehicleId` is both the GeoJSON feature `id` and the key in `OvercastMap.shapes`**. `upsertBusMarker` checks `OvercastMap.shapes[vehicleId]` — if found, calls `shape.setCoordinates([lng, lat])`; if not, creates a new `atlas.Shape(atlas.data.Feature(...))` and adds to the data source.

---

## Patterns & Conventions to Follow

- **Delegate subscription**: always use named methods with `+=` / `-=`. Never lambdas — they can't be unsubscribed.
- **Blazor dispose**: implement `IDisposable` on the partial class (code-behind), not `IAsyncDisposable`. No `@implements` in `.razor` file.
- **SignalR thread safety**: SignalR callbacks are off the Blazor sync context. Always wrap UI updates in `await InvokeAsync(StateHasChanged)`.
- **JS interop error handling**: all `JsRuntime.InvokeVoidAsync` calls wrapped in try/catch writing to `Console.WriteLine`.
- **Coordinate order**: Azure Maps / GeoJSON uses `[longitude, latitude]` (x, y). C# `PositionData` uses `(Latitude, Longitude)`. Flip on the JS boundary.
- **No nav menu**: there is no `NavMenu.razor` in this project. Routes are discovered by Blazor's `@page` directive automatically.

---

## What Is NOT Done Yet (T-25–T-30)

All remaining tasks are browser verification — no code changes needed unless a bug is found:

| Task | What to check |
|------|--------------|
| T-08 | `/maps` loads map surface without JS errors |
| T-25 | `/transit-map` renders map, shows "Connected", bus markers appear |
| T-26 | Marker moves on update (no duplicates) — check `Object.keys(OvercastMap.shapes).length` in devtools |
| T-27 | Hover shows tooltip with `Vehicle: {id}` |
| T-28 | Navigate away and back — clean state, no duplicate SignalR handlers |
| T-29 | `/signalr` and `/maps` routes still resolve |
| T-30 | No uncaught JS or Blazor exceptions in console during a full session |

---

## Deferred Features (Out of Scope Here, Noted for Future Work)

### 1. Movement Interpolation
`PositionData` already carries `Bearing` and `SpeedMetersPerSec`. To interpolate:
- In `upsertBusMarker`, record previous coordinates + timestamp in a per-vehicle state object
- Use `requestAnimationFrame` to animate `shape.setCoordinates()` toward new position over the polling interval (~15–30s for MARTA)
- Use `Bearing` to rotate a directional icon
- Clamp max interpolation distance to avoid overshoot when a bus sits still

### 2. Route Layer (GTFS Static)
`TripData.TripId` and `TripData.RouteId` are already available in each event. MARTA publishes a GTFS Static feed (separate from the real-time feed) containing:
- `shapes.txt` — GPS polyline per route shape (hundreds of points)
- `trips.txt` — maps `trip_id` → `shape_id`
- `routes.txt` — route names, colors

To implement:
1. Backend: parse GTFS static zip, build `routeId → GeoJSON LineString`, expose as API endpoint
2. Client: add a `"route-shapes"` DataSource + `atlas.layer.LineLayer` to the map
3. On bus marker click/select, fetch and plot the route LineString
4. The constitution already scopes this as a future feature

---

## Build Notes

- **Client-only build**: `dotnet build Client\ChefKnifeStudios.TransitJazz.Client.WebApp\ChefKnifeStudios.TransitJazz.Client.WebApp.csproj` — 0 errors, 0 warnings ✅
- **Full solution build while AppHost is running**: produces `MSB3027` file-lock errors on server DLLs. These are **not compiler errors** — they are copy-lock failures because the running worker/API processes hold DLL file handles. Stop the AppHost before running a full solution build.
- **SDK**: .NET 10 preview (produces `NETSDK1057` informational messages — expected, not errors)
