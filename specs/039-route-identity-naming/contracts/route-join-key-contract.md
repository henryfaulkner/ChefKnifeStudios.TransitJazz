# Contract: `RouteShapeProperties.JoinKey` and the `routeJoinKey` GeoJSON property

## C# contract

```csharp
namespace ChefKnifeStudios.TransitJazz.Shared.GtfsData;

public sealed record RouteShapeProperties(
    string RouteId,
    string? RouteShortName,
    string? Color,
    string? TextColor,
    TransitMode Mode = TransitMode.Bus,
    string? City = null)
{
    /// <summary>
    /// The value used to correlate this route across GTFS-RT real-time data and the
    /// static route index. Prefers the public-facing short name (matching GTFS-RT
    /// Trip.RouteId for most cities); falls back to the true GTFS static RouteId when
    /// no short name is present. This is NOT the same as <see cref="RouteId"/> whenever
    /// a short name exists — see constitution Principle VI.
    /// </summary>
    public string JoinKey => RouteShortName ?? RouteId;
}
```

**Pre-conditions**: `RouteId` is non-null (record's existing constraint — it's a non-nullable `string`). `RouteShortName` may be null.

**Post-conditions**: `JoinKey` is never null (guaranteed by `RouteId` being non-nullable). `JoinKey == RouteShortName` when `RouteShortName` is non-null/non-empty; `JoinKey == RouteId` otherwise. This exactly reproduces every existing call site's current behavior — see the four sites in `data-model.md` that are replaced by calls to this property.

**Callers** (all four current independent reimplementations, per FR-003):
- `Worker.cs:211` — `BuildRouteIndex`'s per-city index-key computation
- `TransitMap.razor.cs:576` — `_routeShapeCache` key computation
- `ApplicationViewModel.cs:131` — `_routeShapes` key computation
- `RouteFilterViewModel.cs:226-232` — `RouteItem.RouteJoinKey`/`Label` projection (both fields keep using `JoinKey`; they happen to be equal today, per existing behavior — this contract does not change that coincidence)

## JS interop contract (GeoJSON feature property rename)

The route GeoJSON `Feature.properties` object sent from C# to `map-interop.js` currently carries:

```json
{ "routeId": "74", "color": "#facc15" }
```

After this feature:

```json
{ "routeJoinKey": "74", "color": "#facc15" }
```

**Value is unchanged** — only the JSON property key changes, from `routeId` to `routeJoinKey`. Consumers requiring update, all in `Client.Shared/wwwroot/js/`:

- `map-interop.js`: `addTriggerPointMarkers` (property assignment at line 306), `_routeColorsByRouteId` → `_routeColorsByRouteJoinKey` (state object rename, lines 367, 297, 435, 471, 517), the MapLibre `['match', ['get', 'routeId'], ...]` expressions (lines 377, 394-395) → `['get', 'routeJoinKey']`, the feature construction at line 523-525 (`id: route.routeId` / `properties: { routeId: route.routeId, ... }`) → `id: route.routeJoinKey` / `properties: { routeJoinKey: route.routeJoinKey, ... }`.
- `vehicle-animator.js`: `state.routeId`/`rec.routeId` throughout (lines 101-459) → `routeJoinKey`; `this.routeGeometry` dictionary keying (lines 153-156, 382) follows the same rename.

**Backward compatibility**: None required — this is a same-deploy, coupled C#+JS change (both live in the same Blazor WASM bundle, built and shipped together). No versioning/negotiation needed since there's no independent deploy of JS vs. C# in this architecture.

## SignalR wire contract (Shared event records)

`RouteNearestPointBatchEvent.RouteNearestPointRecord` and `RouteCrossingBatchEvent.RouteCrossingRecord` are `record` types serialized by SignalR's default (MessagePack or JSON, per existing hub config — unaffected by this change) protocol. Renaming the C# property from `RouteId` to `RouteJoinKey` changes the serialized field name identically on both ends (Worker publishes, Client consumes) since **both sides are redeployed together** as part of this feature — there is no rolling/mixed-version deployment concern because Worker and Client are versioned and released from the same repo/branch.

**Verification**: after the rename, confirm no stale references to the old field name remain in either `Worker.cs`'s event construction (`RouteId: nearest.RouteId` → `RouteJoinKey: nearest.RouteJoinKey`) or the Client's consumption (`TransitMap.razor.cs` `crossing.RouteId` → `crossing.RouteJoinKey`).
