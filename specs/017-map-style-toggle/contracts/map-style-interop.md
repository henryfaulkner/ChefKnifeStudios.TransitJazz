# Contract: Basemap Style Swap Interop

Bridges C# → JS to perform the hot basemap swap with `map.setStyle`, preserving domain GeoJSON layers
(Principle VII). Replaces the existing no-op `ChefMap.setMapStyle` stub.

## C# wrapper — Map.razor.Helper.cs (MODIFY)

```csharp
public async Task SetBasemapStyleAsync(string styleUrl)
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.setMapStyle", ElementId, styleUrl); }
    catch (Exception ex) { Console.WriteLine($"[Map] SetBasemapStyle failed: {ex}"); }
}
```

(Signature change vs. the legacy stub wrapper, which is currently unused. The JS param goes from a `styleName`
to a full `styleUrl` — the caller resolves the URL from config.)

## JS — ChefMap.setMapStyle (REPLACE the no-op in map-interop.js)

Contract:

```text
ChefMap.setMapStyle(containerDivId, styleUrl):
  1. map = ChefMap.maps[containerDivId]; if (!map) return.
  2. CAPTURE current custom state:
     - For each source id in { 'vehicles', 'trigger-points', every id starting 'route-' }:
         save its GeoJSON data (from map.getSource(id)._data or getStyle().sources[id].data).
     - For each layer whose id is 'vehicles-layer', 'trigger-points-layer', or starts 'route-layer-':
         save the full layer definition (getStyle().layers entry) INCLUDING its current
         layout.visibility (read via getLayoutProperty(id,'visibility') ?? 'visible').
  3. map.setStyle(styleUrl).
  4. map.once('style.load', () => re-add every captured source then every captured layer,
     in their original order, preserving the saved visibility; insert route/trigger layers
     beneath 'vehicles-layer' as the original code does).
```

Notes:
- Re-add order matters: sources first, then layers; route + trigger layers are inserted **below**
  `vehicles-layer` (the existing code uses the `'vehicles-layer'` beforeId), so buses stay on top.
- Use `map.once('style.load', …)` (one-shot) so repeated swaps don't stack handlers.
- No `fetch`, no SignalR, no `dotNetRef.invokeMethodAsync` for data — restoration is purely from captured
  in-memory GeoJSON (Principle VII: never re-fetch).

## Initial-load selection — Map.GetMapSettings (MODIFY)

`createMap` calls `getMapSettings` on startup; it MUST return the style URL chosen from the persisted setting:

```csharp
[Inject] ISettingsService SettingsService { get; set; } = null!;   // NEW injection

[JSInvokable("getMapSettings")]
public Task<object> GetMapSettings()
{
    var settings = SettingsService.GetSettings();
    var key = settings.IsStreetMapEnabled ? "MapTiler:StyleUrls:LightOn" : "MapTiler:StyleUrls:LightOff";
    var styleUrl = Configuration.GetValue<string>(key)
                   ?? Configuration.GetValue<string>("MapTiler:StyleUrl")
                   ?? string.Empty;
    // … existing center/zoom/language/apiKey assembly, with styleUrl substituted …
}
```

## Behavioral requirements

| ID | Requirement |
|----|-------------|
| IO-1 | After a swap, every route line, the vehicles layer, and the trigger-points layer are present on the new basemap (FR-006). |
| IO-2 | Re-added layers keep their pre-swap visibility — a hidden checkpoint layer stays hidden (FR-007). |
| IO-3 | No network request (HTTP or SignalR) is made to restore data layers (Principle VII). |
| IO-4 | Swap completes within MapLibre's normal `style.load` cycle; map is never left blank on a valid URL (FR-005). |
| IO-5 | `setMapStyle` on a missing/unknown container is a safe no-op. |
| IO-6 | Initial map load paints in the persisted style from first render (FR-009); LightOff when unset (FR-001). |
