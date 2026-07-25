# Contract: ChefMap route-focus interop

JS interop surface added to `window.ChefMap` (`Client.Shared/wwwroot/js/map-interop.js`) and the C#
wrappers on the `Map` component (`Map.razor.Helper.cs`). Mutates the appearance of the existing
per-route line layers (`route-layer-<routeId>`) without touching their GeoJSON sources.

## JS: `ChefMap.focusRoute(containerDivId, routeId)`

**Purpose**: Emphasize the focused route; grey + lower-opacity every other route.

**Behavior**:
1. Resolve `map = ChefMap.maps[containerDivId]`; if absent → no-op.
2. Lazily init `ChefMap._preFocusColors = {}` (module-level cache of original `line-color` per layerId).
3. Enumerate `map.getStyle().layers` where `id` starts with `route-layer-`. For each layer:
   - On first focus pass, stash original color: `ChefMap._preFocusColors[id] ??= map.getPaintProperty(id, 'line-color')`.
   - If `id === 'route-layer-' + routeId` (the focused route): `setPaintProperty(id, 'line-opacity', 0.95)` and restore/keep its stashed `line-color`.
   - Else (non-focused): `setPaintProperty(id, 'line-opacity', 0.15)` and `setPaintProperty(id, 'line-color', '#9ca3af')`.
4. Guard every `getLayer`/`setPaintProperty` so a missing focused layer (route with no rendered
   geometry) does not throw and does not prevent the other layers from being greyed.

**Idempotent**: calling `focusRoute` with a new `routeId` while already focused re-evaluates all layers
to the new target (supports direct route→route focus changes).

## JS: `ChefMap.clearRouteFocus(containerDivId)`

**Purpose**: Restore all routes to their normal appearance — instantly (no transition).

**Behavior**:
1. Resolve `map`; if absent → no-op.
2. For each `route-layer-*`: `setPaintProperty(id, 'line-opacity', 0.85)` (the creation default) and
   `setPaintProperty(id, 'line-color', ChefMap._preFocusColors[id] ?? <existing>)`.
3. Clear `ChefMap._preFocusColors = {}`.

**Note on timing**: MapLibre paint changes are not animated unless a `line-opacity-transition` is set;
leaving transitions unset yields the constitution-required *immediate* teardown. The 100ms "in" feel is
carried by the blurb bar (CSS), not by the line repaint.

## C#: `Map.FocusRouteAsync(string routeId)` / `Map.ClearRouteFocusAsync()`

Added to `Map.razor.Helper.cs`, mirroring existing interop wrappers (try/catch + `Console.WriteLine`):

```csharp
public async Task FocusRouteAsync(string routeId)
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.focusRoute", ElementId, routeId); }
    catch (Exception ex) { Console.WriteLine($"[Map] FocusRoute failed for routeId={routeId}: {ex}"); }
}

public async Task ClearRouteFocusAsync()
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.clearRouteFocus", ElementId); }
    catch (Exception ex) { Console.WriteLine($"[Map] ClearRouteFocus failed: {ex}"); }
}
```

## Accept / reject vectors

| Scenario | Call | Expected |
|----------|------|----------|
| Focus an existing route | `focusRoute(c, "110")` | `route-layer-110` full opacity + own color; all other `route-layer-*` opacity 0.15, grey | 
| Switch focus directly | `focusRoute(c,"110")` then `focusRoute(c,"5")` | `route-layer-5` emphasized, `route-layer-110` greyed, no flicker to "all normal" between |
| Unfocus | `clearRouteFocus(c)` | every `route-layer-*` back to opacity 0.85 + original color; `_preFocusColors` empty |
| Focus a route with no layer | `focusRoute(c,"999")` | no throw; all real layers greyed; nothing emphasized |
| Unknown container | `focusRoute("bad", "110")` | no-op, no throw |
| Basemap style swap while focused | (GIS toggle re-applies data layers) | focus state on data layers is unaffected; layers persist (Principle VII) |
