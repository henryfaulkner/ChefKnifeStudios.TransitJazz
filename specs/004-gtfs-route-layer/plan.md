# Implementation Plan: GTFS Static Route Layer (004-gtfs-route-layer)

**Feature Branch**: `feature/gtfs-route-layer`
**Created**: 2026-05-05
**Spec**: [spec.md](spec.md)
**Status**: Draft
**Depends on**: 003-bus-map-tracker (complete)

---

## Constitution Check

| Principle | Compliance |
|-----------|-----------|
| I. Decoupled Cloud Architecture | ✅ New backend API endpoint; client fetches via HTTPS. No tight coupling. |
| II. No Frontend Secrets | ✅ GTFS Static fetch happens on backend worker; no credentials needed (public feed). |
| III. Real-time Data Processing Pipeline | ✅ GTFS Static loading added to `TransitDataWorker` alongside existing RT pipeline. |
| IV. OpenTelemetry Observability | ✅ Existing OTEL setup unchanged; new worker phase uses existing structured logging pattern. |
| V. Azure DevOps CI/CD | ✅ No pipeline changes; client and server changes compile into existing artifacts. |

---

## Overview

This feature adds a route polyline layer to the transit map. When a user clicks a bus marker, the bus's full route shape is fetched from a new backend endpoint and drawn as a colored `LineString` on the Azure Map. Route shapes come from MARTA's GTFS Static feed, parsed at worker startup.

The work breaks into four phases:

1. **Backend — GTFS Static loader** in `TransitDataWorker`: download zip, parse `trips.txt` / `shapes.txt` / `routes.txt`, build per-route GeoJSON, store in `IKeyValueRepository<string>`.
2. **Backend — new API endpoint** `GET /gtfs/routes/{routeId}/shape` in `Server.WebAPI`.
3. **Client — JavaScript interop**: new `"route-shapes"` DataSource + LineLayer, `showRouteShape` / `clearRouteShape` functions, bus marker click → JS→C# callback.
4. **Client — Blazor**: `Map.razor.Helper.cs` new methods, `TransitMap.razor.cs` click handler, route cache, vehicleId→routeId tracking.

No new NuGet packages are required. No new Shared project types are needed (GeoJSON is returned as a raw JSON string / `IResult` from the endpoint; the client passes it straight to JS).

---

## Affected Files

### New Files
| File | Purpose |
|------|---------|
| `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/GtfsStatic/GtfsStaticLoader.cs` | Downloads and parses GTFS Static zip; builds `routeId → GeoJSON` map |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/GtfsEndpoints.cs` | `GET /gtfs/routes/{routeId}/shape` endpoint |

### Modified Files
| File | Change |
|------|--------|
| `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` | Call `GtfsStaticLoader.LoadAsync()` once in `ExecuteAsync` before the polling loop |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` | Register `GtfsEndpoints`; inject `IKeyValueRepository<string>` (already registered as open generic) |
| `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs` | Add `Gtfs` nested class with `GetRouteShape` format string |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/azure-maps-interop.js` | Add `showRouteShape`, `clearRouteShape`, route DataSource+LineLayer init, bus click callback |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.cs` | Add `[JSInvokable] BusMarkerClickedAsync(string vehicleId)` |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs` | Add `ShowRouteShapeAsync(string geoJson)`, `ClearRouteShapeAsync()` |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` | Add route cache, vehicleId→routeId map, `OnBusMarkerClickedAsync`, route fetch+display logic |

---

## Phase 1 — Backend: GTFS Static Loader

### 1.1 — `GtfsStaticLoader`

New class in the `TransitDataWorker` project. Called once from `Worker.ExecuteAsync` before the polling loop starts.

```csharp
// src/Server/TransitDataWorker/GtfsStatic/GtfsStaticLoader.cs

public class GtfsStaticLoader(
    IHttpClientFactory httpClientFactory,
    IKeyValueRepository<string> routeShapeRepo,
    ILogger<GtfsStaticLoader> logger)
{
    const string GtfsStaticUrl = "https://itsmarta.com/google_transit_feed/google_transit.zip";

    // Key stored in the repo to signal load completion (value = "ready")
    const string ReadyKey = "__gtfs_static_ready__";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        logger.LogInformation("GtfsStaticLoader: downloading GTFS Static zip...");

        try
        {
            var client = httpClientFactory.CreateClient();
            var zipBytes = await client.GetByteArrayAsync(GtfsStaticUrl, ct);

            using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

            // Parse trips.txt  →  tripId → (routeId, shapeId)
            var trips = ParseTrips(archive);

            // Parse shapes.txt →  shapeId → List<(lat, lon, seq)>
            var shapes = ParseShapes(archive);

            // Parse routes.txt →  routeId → (routeColor, textColor)
            var routeColors = ParseRouteColors(archive);

            // Build routeId → GeoJSON LineString Feature
            // One shape_id per route_id (use the first trip encountered per route → its shape_id)
            var routeShapeIds = trips.Values
                .GroupBy(t => t.RouteId)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().ShapeId
                );

            int stored = 0;
            foreach (var (routeId, shapeId) in routeShapeIds)
            {
                if (!shapes.TryGetValue(shapeId, out var points) || points.Count == 0) continue;

                var color = routeColors.TryGetValue(routeId, out var c) ? c.RouteColor : null;
                var textColor = routeColors.TryGetValue(routeId, out var tc) ? tc.TextColor : null;

                var geoJson = BuildLineStringFeature(routeId, points, color, textColor);
                await routeShapeRepo.SetAsync(routeId, geoJson, ct);
                stored++;
            }

            await routeShapeRepo.SetAsync(ReadyKey, "ready", ct);

            logger.LogInformation("GtfsStaticLoader: loaded {Count} route shapes.", stored);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GtfsStaticLoader: failed to load GTFS Static data.");
        }
    }

    static Dictionary<string, (string RouteId, string ShapeId)> ParseTrips(ZipArchive archive)
    {
        // trips.txt columns: route_id, service_id, trip_id, trip_headsign, direction_id, block_id, shape_id
        var result = new Dictionary<string, (string, string)>();
        var entry = archive.GetEntry("trips.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = reader.ReadLine()?.Split(',') ?? [];
        int tripIdIdx = Array.IndexOf(header, "trip_id");
        int routeIdIdx = Array.IndexOf(header, "route_id");
        int shapeIdIdx = Array.IndexOf(header, "shape_id");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = line.Split(',');
            if (cols.Length <= Math.Max(tripIdIdx, Math.Max(routeIdIdx, shapeIdIdx))) continue;
            var tripId = cols[tripIdIdx].Trim();
            var routeId = cols[routeIdIdx].Trim();
            var shapeId = cols[shapeIdIdx].Trim();
            if (!string.IsNullOrEmpty(tripId) && !string.IsNullOrEmpty(routeId) && !string.IsNullOrEmpty(shapeId))
                result[tripId] = (routeId, shapeId);
        }
        return result;
    }

    static Dictionary<string, List<(double Lat, double Lon, int Seq)>> ParseShapes(ZipArchive archive)
    {
        // shapes.txt columns: shape_id, shape_pt_lat, shape_pt_lon, shape_pt_sequence
        var result = new Dictionary<string, List<(double, double, int)>>();
        var entry = archive.GetEntry("shapes.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = reader.ReadLine()?.Split(',') ?? [];
        int shapeIdIdx = Array.IndexOf(header, "shape_id");
        int latIdx = Array.IndexOf(header, "shape_pt_lat");
        int lonIdx = Array.IndexOf(header, "shape_pt_lon");
        int seqIdx = Array.IndexOf(header, "shape_pt_sequence");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = line.Split(',');
            if (cols.Length <= Math.Max(shapeIdIdx, Math.Max(latIdx, Math.Max(lonIdx, seqIdx)))) continue;
            var shapeId = cols[shapeIdIdx].Trim();
            if (!double.TryParse(cols[latIdx].Trim(), out var lat)) continue;
            if (!double.TryParse(cols[lonIdx].Trim(), out var lon)) continue;
            if (!int.TryParse(cols[seqIdx].Trim(), out var seq)) continue;

            if (!result.TryGetValue(shapeId, out var pts))
                result[shapeId] = pts = [];
            pts.Add((lat, lon, seq));
        }

        foreach (var pts in result.Values)
            pts.Sort((a, b) => a.Seq.CompareTo(b.Seq));

        return result;
    }

    static Dictionary<string, (string? RouteColor, string? TextColor)> ParseRouteColors(ZipArchive archive)
    {
        // routes.txt columns: route_id, ..., route_color, route_text_color
        var result = new Dictionary<string, (string?, string?)>();
        var entry = archive.GetEntry("routes.txt");
        if (entry == null) return result;

        using var reader = new StreamReader(entry.Open());
        var header = reader.ReadLine()?.Split(',') ?? [];
        int routeIdIdx = Array.IndexOf(header, "route_id");
        int colorIdx = Array.IndexOf(header, "route_color");
        int textColorIdx = Array.IndexOf(header, "route_text_color");

        if (routeIdIdx < 0) return result;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var cols = line.Split(',');
            var routeId = cols[routeIdIdx].Trim();
            var color = colorIdx >= 0 && cols.Length > colorIdx ? NormalizeColor(cols[colorIdx].Trim()) : null;
            var textColor = textColorIdx >= 0 && cols.Length > textColorIdx ? NormalizeColor(cols[textColorIdx].Trim()) : null;
            if (!string.IsNullOrEmpty(routeId))
                result[routeId] = (color, textColor);
        }
        return result;
    }

    static string? NormalizeColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.StartsWith('#') ? raw : $"#{raw}";
    }

    static string BuildLineStringFeature(
        string routeId,
        List<(double Lat, double Lon, int Seq)> points,
        string? color,
        string? textColor)
    {
        // Produces: {"type":"Feature","geometry":{"type":"LineString","coordinates":[[lon,lat],...]},"properties":{"routeId":"...","color":"...","textColor":"..."}}
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"LineString\",\"coordinates\":[");

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"[{points[i].Lon.ToString("G17", CultureInfo.InvariantCulture)},{points[i].Lat.ToString("G17", CultureInfo.InvariantCulture)}]");
        }

        sb.Append("]},\"properties\":{");
        sb.Append($"\"routeId\":{JsonSerializer.Serialize(routeId)}");
        sb.Append($",\"color\":{(color != null ? JsonSerializer.Serialize(color) : "null")}");
        sb.Append($",\"textColor\":{(textColor != null ? JsonSerializer.Serialize(textColor) : "null")}");
        sb.Append("}}");
        return sb.ToString();
    }
}
```

**Key decisions:**
- One shape per route: multiple trips share a `shape_id`; we take the first trip per `route_id` to get a representative shape. This is correct for route visualization (all trips on a route follow the same shape or a close variant).
- GeoJSON built as a raw string with `StringBuilder` — avoids a `System.Text.Json` serialization roundtrip for a large coordinate array; keeps the output lean and avoids any serializer configuration concerns for nested arrays.
- `__gtfs_static_ready__` sentinel key: the endpoint checks this key to distinguish "not loaded yet" (503) from "loaded but unknown routeId" (404).

### 1.2 — Wire `GtfsStaticLoader` into `Worker.cs`

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    logger.LogInformation("TransitDataWorker started.");

    await transitHubPublisher.StartAsync(stoppingToken);
    await gtfsStaticLoader.LoadAsync(stoppingToken);    // ← new line

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        await ProcessGtfsRtFeedAsync(stoppingToken);
    }
}
```

`GtfsStaticLoader` is injected via constructor parameter (same pattern as `ITransitHubPublisher`).

---

## Phase 2 — Backend: New API Endpoint

### 2.1 — `GtfsEndpoints.cs`

```csharp
// src/Server/WebAPI/EndpointGroups/GtfsEndpoints.cs

public static class GtfsEndpoints
{
    const string ReadyKey = "__gtfs_static_ready__";

    public static IEndpointRouteBuilder MapGtfsEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName(nameof(ApiEndpoints.Gtfs))
            .WithTags(nameof(ApiEndpoints.Gtfs));

        group.MapGet(ApiEndpoints.Gtfs.GetRouteShape, async (
            string routeId,
            [FromServices] IKeyValueRepository<string> repo,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(GtfsEndpoints));

            var readyResult = await repo.GetAsync(ReadyKey, ct);
            if (!readyResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: GTFS Static data not yet loaded.");
                return Results.StatusCode(503);
            }

            var shapeResult = await repo.GetAsync(routeId, ct);
            if (!shapeResult.IsSuccess)
            {
                logger.LogWarning("GtfsEndpoints: Route shape not found for routeId {RouteId}.", routeId);
                return Results.NotFound();
            }

            return Results.Text(shapeResult.Value, "application/json");
        })
        .WithName(nameof(ApiEndpoints.Gtfs.GetRouteShape))
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return group;
    }
}
```

### 2.2 — Register in `Program.cs`

Add `.MapGtfsEndpoints()` to the chain:

```csharp
app.MapTestEndpoints()
    .MapMapsEndpoints()
    .MapGtfsEndpoints();    // ← new
```

### 2.3 — Update `ApiEndpoints.cs`

```csharp
public static class ApiEndpoints
{
    public static class Maps { ... }   // unchanged
    public static class Test { ... }   // unchanged

    public static class Gtfs
    {
        public const string GetRouteShape = "/gtfs/routes/{routeId}/shape";
    }
}
```

---

## Phase 3 — Client: JavaScript Interop

### 3.1 — Add `"route-shapes"` DataSource and LineLayer

Extend `atlas.Map.prototype.initDataSourceForBusPositions` to also initialize the route shapes layer. This keeps all layer setup in one place (called once on map ready).

Add at the end of `initDataSourceForBusPositions`, **before** `map.layers.add(busLayer)`:

```javascript
// Route shapes layer — rendered below bus markers
let routeDs = new atlas.source.DataSource('route-shapes');
map.sources.add(routeDs);

let routeLayer = new atlas.layer.LineLayer(routeDs, 'route-shapes-layer', {
    strokeColor: ['coalesce', ['get', 'color'], '#0078D4'],
    strokeWidth: 4,
    strokeOpacity: 0.85,
    lineJoin: 'round',
    lineCap: 'round'
});

// Add route layer BEFORE bus layer so buses render on top
map.layers.add(routeLayer);
// busLayer added after → renders on top
map.layers.add(busLayer);
```

> Note: remove the existing `map.layers.add(busLayer)` that's currently at the end — it's now inside this block in order.

### 3.2 — Add `showRouteShape` and `clearRouteShape`

Add to `window.OvercastMap`:

```javascript
showRouteShape: function (containerDivId, geoJsonString) {
    let map = OvercastMap.maps[containerDivId];
    if (map == null) return;

    let ds = map.sources.getById('route-shapes');
    if (ds == null) return;

    ds.clear();

    try {
        let feature = JSON.parse(geoJsonString);
        ds.add(feature);
    } catch (err) {
        console.warn('[OvercastMap] showRouteShape: failed to parse GeoJSON', err);
    }
},

clearRouteShape: function (containerDivId) {
    let map = OvercastMap.maps[containerDivId];
    if (map == null) return;

    let ds = map.sources.getById('route-shapes');
    if (ds == null) return;

    ds.clear();
},
```

### 3.3 — Bus Marker Click → JS→C# Callback

The `mapComponent` (`DotNetObjectReference`) is already stored in the closure of `createMap`. We need to store it for use inside `initDataSourceForBusPositions`.

**Change in `createMap`:** pass `mapComponent` through to `initDataSourceForBusPositions`:

```javascript
map.events.add('ready', async function () {
    await map.initDataSourceForBusPositions(containerDivId, mapComponent);  // ← pass mapComponent
    ...
});
```

**Change `initDataSourceForBusPositions` signature:**

```javascript
atlas.Map.prototype.initDataSourceForBusPositions = async function (containerDivId, mapComponent) {
    ...
    // Bus marker click event
    map.events.add('click', busLayer, (e) => {
        if (!e.shapes || e.shapes.length === 0) return;
        let props = e.shapes[0].getProperties();
        if (props && props.vehicleId) {
            mapComponent.invokeMethodAsync('BusMarkerClickedAsync', props.vehicleId);
        }
    });
    ...
```

**Also update** the existing map-level `click` handler in `createMap` to clear the route when the background is clicked (already calls `mapBodyClickedAsync` — that's sufficient; `TransitMap` will handle the clear in the Blazor callback).

---

## Phase 4 — Client: Blazor

### 4.1 — `Map.razor.cs` — new `[JSInvokable]` method

```csharp
[Parameter] public EventCallback<(Map Map, string VehicleId)> OnBusMarkerClicked { get; set; }

[JSInvokable("BusMarkerClickedAsync")]
public async Task BusMarkerClickedAsync(string vehicleId)
{
    await OnBusMarkerClicked.InvokeAsync((this, vehicleId));
}
```

### 4.2 — `Map.razor.Helper.cs` — two new interop methods

```csharp
public async Task ShowRouteShapeAsync(string geoJson)
{
    try
    {
        await JsRuntime.InvokeVoidAsync("OvercastMap.showRouteShape", ElementId, geoJson);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.ToString());
    }
}

public async Task ClearRouteShapeAsync()
{
    try
    {
        await JsRuntime.InvokeVoidAsync("OvercastMap.clearRouteShape", ElementId);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.ToString());
    }
}
```

### 4.3 — `TransitMap.razor` — wire `OnBusMarkerClicked`

```razor
<Map CameraOptions="DefaultCameraOptions"
     OnMapReady="OnMapReadyAsync"
     OnBusMarkerClicked="OnBusMarkerClickedAsync" />
```

### 4.4 — `TransitMap.razor.cs` — route cache and click handler

New fields:

```csharp
// vehicleId → routeId, updated on every VehiclePositionUpdatedEvent
readonly Dictionary<string, string> _vehicleRouteMap = new();

// routeId → GeoJSON string (client-side cache, lives for page lifetime)
readonly Dictionary<string, string> _routeShapeCache = new();

// Track the currently selected routeId so we don't re-fetch on re-click
string? _selectedRouteId;
```

Update `HandleBatchAsync` to maintain the vehicle→route mapping:

```csharp
async Task HandleBatchAsync(List<EventEnvelope> batch)
{
    if (!_mapReady || _map is null) return;
    foreach (var envelope in batch)
    {
        if (envelope.Payload is not VehiclePositionUpdatedEvent evt) continue;
        if (evt.Position is null) continue;
        if (float.IsNaN(evt.Position.Latitude) || float.IsNaN(evt.Position.Longitude))
        {
            Logger.LogDebug("TransitMap: Skipping {VehicleId} — invalid coordinates", evt.Vehicle.Id);
            continue;
        }

        // Keep the vehicleId → routeId map current
        if (evt.Trip?.RouteId is { } routeId)
            _vehicleRouteMap[evt.Vehicle.Id] = routeId;

        await _map.UpsertBusMarkerAsync(evt.Vehicle.Id, evt.Position.Latitude, evt.Position.Longitude);
    }
    await InvokeAsync(StateHasChanged);
}
```

New `OnBusMarkerClickedAsync` handler:

```csharp
async Task OnBusMarkerClickedAsync((Map Map, string VehicleId) args)
{
    if (_map is null) return;

    if (!_vehicleRouteMap.TryGetValue(args.VehicleId, out var routeId))
    {
        Logger.LogDebug("TransitMap: No routeId for vehicle {VehicleId}", args.VehicleId);
        await _map.ClearRouteShapeAsync();
        _selectedRouteId = null;
        return;
    }

    if (routeId == _selectedRouteId) return; // same route already shown
    _selectedRouteId = routeId;

    if (!_routeShapeCache.TryGetValue(routeId, out var geoJson))
    {
        try
        {
            var url = string.Format(ApiEndpoints.Gtfs.GetRouteShape, routeId);
            // HttpClient is injected — see §4.5
            var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("TransitMap: Route shape fetch failed for {RouteId}: {Status}", routeId, response.StatusCode);
                await _map.ClearRouteShapeAsync();
                _selectedRouteId = null;
                return;
            }
            geoJson = await response.Content.ReadAsStringAsync();
            _routeShapeCache[routeId] = geoJson;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: Exception fetching route shape for {RouteId}", routeId);
            _selectedRouteId = null;
            return;
        }
    }

    await _map.ShowRouteShapeAsync(geoJson);
}
```

Also wire `OnMapBodyClicked` to clear the route:

```csharp
async Task OnMapBodyClickedAsync(Map map)
{
    if (_map is null) return;
    await _map.ClearRouteShapeAsync();
    _selectedRouteId = null;
}
```

And add to `.razor`:
```razor
<Map ... OnMapBodyClicked="OnMapBodyClickedAsync" />
```

### 4.5 — `HttpClient` injection in `TransitMap.razor.cs`

```csharp
[Inject] HttpClient Http { get; set; } = null!;
```

`HttpClient` is already registered in `Client.WebApp/Program.cs` as a pre-configured instance pointing at the API base URL (standard Blazor WASM setup via `builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })`). Verify this is present — if not, add it.

---

## Data Flow Summary

```
[User clicks bus marker]
  └─► JS: map click event on 'bus-positions-layer'
        └─► mapComponent.invokeMethodAsync('BusMarkerClickedAsync', vehicleId)
              └─► Map.BusMarkerClickedAsync(vehicleId)
                    └─► OnBusMarkerClicked.InvokeAsync((map, vehicleId))
                          └─► TransitMap.OnBusMarkerClickedAsync(args)
                                ├─► _vehicleRouteMap[vehicleId] → routeId
                                ├─► _routeShapeCache hit?  ──► ShowRouteShapeAsync(geoJson)
                                └─► cache miss?
                                      └─► GET /gtfs/routes/{routeId}/shape
                                            ├─► GtfsEndpoints → IKeyValueRepository<string>.GetAsync(routeId)
                                            └─► 200 OK: geoJson string
                                                  └─► cache + ShowRouteShapeAsync(geoJson)
                                                        └─► JS: OvercastMap.showRouteShape(divId, geoJson)
                                                              └─► ds.clear() + ds.add(JSON.parse(geoJson))
```

---

## Key Technical Decisions

| Decision | Rationale |
|----------|-----------|
| Load GTFS Static at worker startup (not on first request) | Keeps the endpoint fast (<500ms SC-007); startup cost is paid once. |
| Store pre-built GeoJSON strings in `IKeyValueRepository<string>` | Avoids re-serializing per request; the repo already holds strings; no new type needed. |
| One shape per route (first trip's shape_id) | All trips on a route share the same shape or close variants. Good enough for v1; overridable in v2 with trip-specific shapes. |
| `Results.Text(geoJson, "application/json")` instead of `Results.Ok(obj)` | The GeoJSON is already a pre-built string; avoids double-serialization through `System.Text.Json`. |
| Pass `mapComponent` into `initDataSourceForBusPositions` | `mapComponent` is the `DotNetObjectReference`; the click callback needs it. Passing as a parameter keeps the prototype method self-contained without a module-level variable. |
| `EventCallback<(Map, string)>` for bus click | Tuple EventCallback keeps the `Map` reference accessible to the page without a separate field lookup. |
| Client-side `Dictionary<string, string>` route cache | Prevents repeated API calls for the same route. Lives for the page component lifetime; cleared on dispose/re-navigate. This matches SC-004. |
| `_selectedRouteId` guard | Clicking the same bus twice should not re-fetch or re-draw. Early-return if `routeId == _selectedRouteId`. |
| `OnMapBodyClicked` → `ClearRouteShapeAsync` | Re-uses the existing `mapBodyClickedAsync` callback already wired in JS. No new JS event needed. |

---

## Open Questions / Pre-Implementation Checks

1. **GTFS Static zip URL**: Confirm `https://itsmarta.com/google_transit_feed/google_transit.zip` is publicly reachable from the worker host. If behind a firewall, an alternate fetch strategy is needed.
2. **`HttpClient` base address in `Client.WebApp/Program.cs`**: Verify a pre-configured `HttpClient` with base address pointing to the API is already registered. If not, add it before T-26.
3. **CSV quoting**: GTFS files can have quoted fields (e.g., `"route_long_name"` with commas). The simple `Split(',')` in the parser will break on quoted fields. For `shapes.txt` this is not a concern (lat/lon/seq are never quoted), but `routes.txt` `route_long_name` may contain commas. Since we only use `route_id`, `route_color`, and `route_text_color` — and these are never quoted — `Split(',')` is safe. Document this assumption.
4. **`System.IO.Compression` namespace**: Already available in .NET 10 BCL — no extra NuGet needed for `ZipArchive`.
