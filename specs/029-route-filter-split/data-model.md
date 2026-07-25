# Data Model: RouteFilter Rail / Bus Split

## Existing type (reused, not created)

### `TransitMode` (enum) — `Shared/Events/RouteNearestPointBatchEvent.cs`

```csharp
public enum TransitMode { Bus = 0, Rail = 1 }
```

Already exists. No change. Serializes as `"Bus"` / `"Rail"` under the project's
`JsonStringEnumConverter`.

## Modified entities

### `RouteShapeProperties` (Shared) — `GtfsData/RouteShapeFeature.cs`

Add one field. Default `Bus` so older serialized payloads (and routes with no `route_type`) classify
as bus.

```csharp
public sealed record RouteShapeProperties(
    string RouteId,
    string? RouteShortName,
    string? Color,
    string? TextColor,
    TransitMode Mode = TransitMode.Bus   // NEW — from GTFS route_type
);
```

| Field | Type | Source | Notes |
|---|---|---|---|
| `Mode` | `TransitMode` | GTFS `routes.txt` `route_type` | `1 → Rail`, else `Bus` |

### `RouteItem` (Client.Shared) — `ViewModels/RouteFilterViewModel.cs`

Add one init-only field. Set in `BuildRouteItems()` from `Properties.Mode`.

```csharp
public class RouteItem
{
    public string RouteId { get; init; }
    public string Color { get; init; }
    public bool IsSelected { get; set; }
    public TransitMode Mode { get; init; }   // NEW
}
```

> `SelectRoute` / `ClearSelection` rebuild `RouteItem`s — they MUST copy `Mode` through, same as
> `RouteId`/`Color`, or pills would lose their section on selection.

## Validation rules

- `Mode` defaults to `Bus`; a route is `Rail` only when GTFS `route_type == 1` (FR-010).
- Classification is set at route-build time, independent of vehicle data (FR-002).

## State / derivation

- **Rail section list** = `RouteItems.Where(r => r.Mode == Rail)`.
- **Bus section list** = `RouteItems.Where(r => r.Mode == Bus)`.
- A section renders only when its list is non-empty (FR-006).
- Ordering within each section: existing `OrderByDescending(vehicle count)` from `BuildRouteItems`
  carries through the `.Where` filter (no separate sort needed).
- Selection set spans both sections (one global pool, FR-007).

## Out of scope (explicitly unchanged)

- `_railVehicleIds`, `_routeVehicles`, `RecomputeActiveTransitCounts`, `ActiveBusCount`,
  `ActiveRailCount` — the active-count split is a separate, already-shipped concern.
