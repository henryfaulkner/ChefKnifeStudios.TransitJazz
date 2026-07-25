# Contract: `/gtfs/routes/shapes` (city-scoped) + KV keying

## Endpoint

**Before**: `GET /gtfs/routes/shapes` → all shapes (KV keyed by bare `routeId`).
**After**: `GET /gtfs/routes/shapes?city={city}` → only that city's shapes.

| Param | Type | Required | Default | Notes |
|---|---|---|---|---|
| `city` | query string | ✖ | `marta` | Filters KV keys with prefix `{city}:`. Unknown city → empty list (client falls back per Q5/FR-004). |

Response shape is unchanged: `IEnumerable<RouteShapeFeature>`, but each
`RouteShapeProperties` now carries `City`.

## KV store keying (Q4)

| | Before | After |
|---|---|---|
| Shape key | `routeId` (e.g. `26932`) | `{city}:{routeId}` (e.g. `marta:26932`, `wmata:B`) |
| Ready sentinel | `GtfsStaticLoader.ReadyKey` | unchanged (single sentinel) |

`GetRouteShape` (single) and `GetAllRoutes` follow the same `{city}:` prefix scoping. The
ready-sentinel filter (`kvp.Key != ReadyKey`) is retained.

## GtfsStaticLoader (Q4)

- Loop the city registry; for each city load its `StaticZipUrls` (multi-zip per city merged).
- Seed each shape under `{city}:{routeId}`.
- Set `RouteShapeProperties.City = city` on every shape it builds.

## RouteShapeProperties (Shared, additive)

```csharp
public sealed record RouteShapeProperties(
    string RouteId,
    string? RouteShortName,
    string? Color,
    string? TextColor,
    TransitMode Mode = TransitMode.Bus,
    string? City = null);          // NEW — populated by GtfsStaticLoader
```

## Client consumption

- Shape-fetch service appends `?city={city}` (city from URL, default `marta`).
- `RouteFilterViewModel` reads `RouteShapeProperties.City` (already flows through naturally).
- **Principle VII**: shapes fetched once at init; layers re-added after basemap style swaps,
  never re-fetched.

## Invariants (testable)

- **INV-S1**: `?city=wmata` returns only `wmata:*` shapes; zero `marta:*`. (SC-001)
- **INV-S2**: Worker partitions one shapes response into per-city indexes via `City` — no N HTTP
  calls. (Q4)
- **INV-S3 (MARTA unchanged)**: With default `city=marta`, the client renders the identical route
  set as before the refactor. (FR-017 / SC-004)
