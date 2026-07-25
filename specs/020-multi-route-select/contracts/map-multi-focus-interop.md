# Contract: Map Multi-Route Focus Interop

**Feature**: 020-multi-route-select | Surfaces:
`Client.Shared/Components/Map.razor.Helper.cs` (C# wrapper) + `wwwroot/js/map-interop.js` (`ChefMap`)

Generalizes the existing single-route `focusRoute` / `clearRouteFocus` to a **set** of selected routes.
The existing single-route methods stay; this adds a set-aware variant.

## C# wrapper

```csharp
// Map.razor.Helper.cs — NEW, alongside FocusRouteAsync / ClearRouteFocusAsync
public async Task FocusRoutesAsync(IEnumerable<string> routeIds)
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.focusRoutes", ElementId, routeIds); }
    catch (Exception ex) { Console.WriteLine($"[Map] FocusRoutes failed: {ex}"); }
}
```

## JS function

```js
// map-interop.js — NEW, mirrors focusRoute but tests SET membership
focusRoutes: function (containerDivId, routeIds) {
    let map = ChefMap.maps[containerDivId];
    if (!map) return;
    let style = map.getStyle();
    if (!style) return;

    let selected = new Set(routeIds || []);
    (style.layers || []).forEach(function (layer) {
        if (!layer.id || !layer.id.startsWith('route-layer-')) return;
        let id = layer.id;
        if (!map.getLayer(id)) return;
        let routeId = id.substring('route-layer-'.length);
        if (selected.has(routeId)) {
            map.setPaintProperty(id, 'line-opacity', 0.95);
            map.setPaintProperty(id, 'line-color', ChefMap._routeColors[id] || '#22c55e');
        } else {
            map.setPaintProperty(id, 'line-opacity', 0.3);
            map.setPaintProperty(id, 'line-color', '#d1d5db');
        }
    });
},
```

> Note: derive `routeId` from the layer id (`route-layer-<routeId>`) to match the set. The existing
> `_routeColors` map is keyed by **layer id** (`id`), so emphasis uses `ChefMap._routeColors[id]` exactly as
> `focusRoute` does today.

## Caller wiring (`TransitMap.OnRouteFilterPropertyChanged`)

```
if (e.PropertyName is not (nameof(IRouteFilterViewModel.RouteItems)
                           or nameof(IRouteFilterViewModel.HasSelection))) return;
if (!_mapReady || _map is null) return;

var selected = RouteFilterViewModel.SelectedRouteIds;
if (selected.Count > 0)
    InvokeAsync(() => _map.FocusRoutesAsync(selected));
else
    InvokeAsync(() => _map.ClearRouteFocusAsync());
```

## Guarantees

| Selection | Result on map |
|-----------|---------------|
| {A,B} | route-layer-A, route-layer-B emphasized; all other route layers opacity 0.3, grey |
| {} | `ClearRouteFocusAsync` → all routes restored to default appearance |
| includes a routeId with no layer | that id is a no-op; other selected routes still emphasized; no throw |

- **Idempotent / last-write-wins**: one `focusRoutes` call fully expresses the current set, so a burst of
  selection changes resolves to a single correct final paint.
- **Principle VII**: operates only on the persistent `route-layer-*` GeoJSON layers via `setPaintProperty`;
  no re-fetch. After a basemap `style.load` (GIS toggle, #17), `TransitMap` re-renders routes and MUST
  re-apply the current focus (call `FocusRoutesAsync(SelectedRouteIds)` after the post-`style.load`
  re-render if a selection is active) so the blur survives the swap.
- Must not throw when a selected route has no rendered layer (the `map.getLayer(id)` guard + set test handle
  this).

## Why not loop `focusRoute`

Calling `focusRoute(A)` then `focusRoute(B)` would blur A on the second call (each call greys every
non-target layer). A single set-aware pass is required to emphasize multiple routes at once.
