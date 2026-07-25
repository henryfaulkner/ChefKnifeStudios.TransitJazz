# Phase 1 Data Model: Dynamic Per-City Vehicle Categories

Entities and the concrete type changes that carry them across the four layers. "Before → After" shows the retype; new members are marked **NEW**.

## Entity: Vehicle Category

The core new concept: a named grouping of vehicles a rider sees, filters, and counts. Represented **as a plain lowercase string** everywhere (no enum, no wrapper type) — this is decision D1/D15.

- **Key** (string, e.g. `"bus"`, `"rail"`, `"streetcar"`, `"unknown"`): the single stable identity. Used simultaneously as: MessagePack wire value, GeoJSON property value, grouping key for counts, resx lookup key for the label, `RunningNoun_{key}` lookup key for the count phrase, and `data-category` CSS attribute value.
- **Display label** (derived, not stored): `IStringLocalizer<RouteFilterResources>[key]`; missing → raw key (D9/D11).
- **Running-count phrase** (derived, not stored): `Loc["RunningNoun_" + key]`; missing → `string.Format(Loc["VehiclesRunningTemplate"], Loc[key])` (D12).
- **Ordering rank** (derived, not stored): `min(route_type)` over routes in this category, ascending; ordinal-key tie-break (D8).

**Validation / invariants:**
- Category keys SHOULD be lowercase, whitespace-free (unenforced — deferred; mitigated by `downcase` map guard and optional `ClassifyCategory` lowercasing).
- Reserved fallbacks: `"bus"` (unmapped `route_type` within a configured city; WebAPI pre-init placeholder) and `"unknown"` (Worker route-join failure).
- An empty category (no routes / no active vehicles) is not rendered.

## Entity: City Category Configuration

Per-city, optional mapping from GTFS `route_type` (string key) to a category key. Authored **only** in WebAPI `appsettings.json` (D3/D4).

- Shape: `RouteTypeCategories: { "<route_type>": "<category-key>", ... }` — e.g. TTC `{ "0": "streetcar", "1": "rail", "3": "bus" }`.
- Absent ⇒ fallback classifier applies (`0/1/2 → "rail"`, else `"bus"`) — no behavior change (D5a).
- Present but a live `route_type` isn't a key ⇒ `"bus"` + warning log, city keeps loading (D5b).

**Binding target (WebAPI private record):**
```csharp
// GtfsStaticLoader — CityStaticEntry
// Before:
sealed record CityStaticEntry(string Name, string[] StaticZipUrls, string? ApiKeyEnvVar);
// After:
sealed record CityStaticEntry(
    string Name,
    string[] StaticZipUrls,
    string? ApiKeyEnvVar,
    IReadOnlyDictionary<string, string>? RouteTypeCategories);   // NEW
```

**Classifier (replaces the 3-line switch):**
```csharp
static string ClassifyCategory(string routeType,
    IReadOnlyDictionary<string, string>? cityMap, string cityName, ILogger logger)
{
    if (cityMap is not null)
    {
        if (cityMap.TryGetValue(routeType, out var category)) return category;   // D5 config hit
        logger.LogWarning("Unmapped route_type {RouteType} for city {City}, defaulting to bus", routeType, cityName);
        return "bus";                                                            // D5b
    }
    return routeType is "0" or "1" or "2" ? "rail" : "bus";                       // D5a fallback
}
```
*(Optional per open-item mitigation: `return category.ToLowerInvariant();` to normalize config casing.)*

## Entity: Route (category-relevant view) — shared shape contract

```csharp
// Shared/GtfsData/RouteShapeFeature.cs — RouteShapeProperties
// Before:
public sealed record RouteShapeProperties(
    string RouteId, string? RouteShortName, string? Color, string? TextColor,
    TransitMode Mode = TransitMode.Bus,
    string? City = null) { public string JoinKey => RouteShortName ?? RouteId; }
// After:
public sealed record RouteShapeProperties(
    string RouteId, string? RouteShortName, string? Color, string? TextColor,
    string Category = "bus",   // was Mode (enum) — D9
    int RouteType = 3,         // NEW — raw GTFS route_type, drives client display order (D8); default 3 = bus-ordered
    string? City = null)       // stays optional-last
    { public string JoinKey => RouteShortName ?? RouteId; }   // UNCHANGED — Principle VI
```
- `RouteType` is inserted **before** `City` so `City` remains the optional-last positional param. Default `3` means pre-existing stored GeoJSON without the field deserializes as bus-ordered (harmless).
- Hand-serialized in `BuildLineStringFeature`: swap `"mode":"{mode}"` → `"category":{JsonSerializer.Serialize(category)}` (quoted safely) and add `"routeType":{routeType}`. Deserialize leg no longer needs `JsonStringEnumConverter` for this type (verify no other enum relies on it before removing).

## Entity: Running Vehicle wire record

```csharp
// Shared/Events/RouteNearestPointBatchEvent.cs — RouteNearestPointRecord
// enum TransitMode { Bus=0, Rail=1 }   ← REMOVED entirely
// Key(0)–Key(9) UNCHANGED
// Before: [property: Key(10)] TransitMode TransitMode = TransitMode.Bus
// After:  [property: Key(10)] string Category = "bus"
```
- Same positional `Key(10)`; encoding changes packed-int → MessagePack string (~5–10 bytes for `"streetcar"`). Breaking wire change (D2/D14). `EventEnvelopeMessagePackTests` round-trip MUST update in lockstep.
- `RouteType` is deliberately **NOT** added here — display order is computed once from the route catalog, never per-tick, so the ordering int rides `RouteShapeProperties` only (zero per-tick cost).

## Entity: Worker route→category map

```csharp
// TransitDataWorker/Worker.cs
// _routeMode / modeMap:  Dictionary<string, TransitMode>  →  Dictionary<string, string>
// join-failure fallbacks (2 sites): TransitMode.Bus  →  "unknown"   (D6)
//   e.g. categoryMap != null && categoryMap.TryGetValue(routeJoinKey, out var c) ? c : "unknown"
// BuildRouteIndex tuple return + ProcessSpatialReconciliation param retyped enum→string; threaded through all §2.2 sites.
```
Built exclusively from the WebAPI shape JSON (`RouteShapeProperties.Category`); Worker never classifies (D3).

## Entity: Client ViewModel state

```csharp
// Client.Shared/ViewModels/RouteFilterViewModel.cs
public class RouteItem {
    public string RouteJoinKey { get; init; }
    public string Label { get; init; }
    public string Color { get; init; }
    public bool IsSelected { get; set; }
    public string Category { get; init; }   // was TransitMode Mode
    public int RouteType { get; init; }      // NEW — used only to compute CategoryOrder
}

public interface IRouteFilterViewModel : IViewModel, IDisposable {
    IEnumerable<RouteItem> RouteItems { get; }
    void SelectRoute(RouteItem routeItem);
    void SelectAll(string category);         // was TransitMode mode
    void ClearSelection(string category);    // was TransitMode mode
    void SetHoveredRoute(RouteItem? routeItem);
    bool HasSelectionFor(string category);   // was TransitMode mode
    IReadOnlyList<string> CategoryOrder { get; }                     // NEW — D8 display order
    IReadOnlyDictionary<string, int> ActiveCountsByCategory { get; } // was ActiveBusCount, ActiveRailCount
    // ... unchanged members ...
}
```

**Internal accumulator changes:**
- `HashSet<string> _railVehicleIds` → `Dictionary<string, string> _vehicleCategory` (vehicleId → category key). The binary rail/not partition no longer holds with 3+ categories.
- `RecomputeActiveTransitCounts()` groups `_vehicleCategory.Values` into a **freshly-built** `Dictionary<string,int>` and assigns it to the `[ObservableProperty]`-backed `ActiveCountsByCategory` (reassign the whole reference — never mutate in place, or `PropertyChanged` won't fire → stale label). This is the single most likely regression; guarded by a reactivity test.
- `CategoryOrder` built once in `BuildRouteItems`: group `RouteShapes` by `Category`, sort-key = `min(RouteType)` per group, ascending, ordinal tie-break; assign alongside `RouteItems` so the two never disagree. Needn't be per-tick reactive (only changes on catalog reload).

## State & flow (per route / per vehicle)

```text
GTFS static load (WebAPI, once per city):
  route_type string ──ClassifyCategory(cityMap)──► category string
                    └─int.Parse(route_type, default 3)──► RouteType int
  → stamped on RouteShapeProperties {Category, RouteType} → hand-serialized GeoJSON

Route catalog fetch (Worker + Client, once at startup):
  Worker:  shape JSON → _vehicleCategory-feeding route→category map (BuildRouteIndex)
  Client:  shape JSON → RouteItem{Category, RouteType} + CategoryOrder (BuildRouteItems)

Per GTFS-RT tick (Worker):
  vehicle → RouteJoinKey → categoryMap.TryGetValue ? category : "unknown"
  → RouteNearestPointRecord.Category (Key 10) → SignalR

Per tick (Client):
  record.Category → _vehicleCategory[vehicleId] = category
  → RecomputeActiveTransitCounts() → fresh ActiveCountsByCategory (reassigned)
  → PropertyChanged(nameof(ActiveCountsByCategory)) → TransitRunningLabel re-renders
  → map GeoJSON 'category' property → paint match ['downcase',['get','category']] 'rail' → dot size
```
