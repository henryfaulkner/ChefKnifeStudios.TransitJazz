# Phase 1 Data Model: Neighborhood Routes

Entities are in-memory during the join and serialized to two JSON files. No database, no .NET models. Field meanings trace to `docs/NEIGHBORHOOD_ROUTES_DESIGN_DOCUMENT.md` §2–§3 and the research decisions (D4–D9).

---

## Entity: Neighborhood (in-memory, source = GeoJSON feature)

Loaded from each feature of the City-of-Atlanta GeoJSON via `shapely.geometry.shape(feature["geometry"])`.

| Field | Type | Source property | Notes |
|-------|------|-----------------|-------|
| `object_id` | int | `OBJECTID_1` | Stable primary/join key (D4). Required. |
| `name` | str | `NAME` | e.g. `"Old Fourth Ward"`. Required. |
| `npu` | str | `NPU` | Planning unit letter, e.g. `"M"`. |
| `sq_miles` | float \| None | `SQMILES` | Area in square miles. |
| `population` | int \| None | `population` | 2024 estimate. |
| `median_household_income` | int \| None | `householdi` | USD. |
| `transit_commute_percent` | float \| None | `commute__3` | % commuting by transit (D7). |
| `car_alone_percent` | float \| None | `commute__1` | % driving alone (D7). |
| `work_from_home_percent` | float \| None | `commute__5` | % WFH (D7). |
| `all_properties` | dict | (entire `properties`) | Verbatim copy for the full dump; geometry excluded. |
| `geometry` | shapely geometry | `geometry` | `Polygon` or `MultiPolygon`; join-only, never serialized. |

**Validation / rules**
- `object_id` and `name` MUST be present; a feature missing either is a data error (log and skip, or abort — implementer choice, but never emit a keyless entry).
- Any numeric field MAY be `None`; preserve as `null`, never `0` (D5, FR-011).
- `geometry` MUST be excluded from both output files (FR-009).

---

## Entity: Route (in-memory, source = API)

Loaded from each `RouteShapeFeature` in `GET /gtfs/routes/shapes`.

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| `route_id` | str | `properties.routeId` | GTFS static id, e.g. `"26922"`. |
| `route_short_name` | str | `properties.routeShortName` | Human label, e.g. `"39"` (GTFS-RT join key, constitution VI). |
| `linestring` | shapely LineString | `geometry.coordinates` | Built from `[[lon,lat],…]`; join-only, never serialized. |

**Validation / rules**
- A feature whose geometry is not a usable LineString (empty/missing coordinates) is skipped with a diagnostic count.
- `color` / `textColor` from the API are not needed for this feature and are dropped.

---

## Relationship: Neighborhood ⨯ Route (the spatial join)

- **Cardinality**: many-to-many. Each neighborhood matches 0..N routes; each route matches 0..M neighborhoods.
- **Match rule**: route is matched to neighborhood iff `neighborhood.geometry.intersects(route.linestring)` (D2).
- **Stored on**: the lean neighborhood entry as a `routes` array of `{routeId, routeShortName}`; empty `[]` when none (FR-006).
- **Not stored**: the reverse index (route → neighborhoods) is not materialized; consumers derive it by scanning the lean array (the data-explorer skill does this for "what neighborhoods does route X serve").

---

## Serialized: Lean File — `neighborhood_routes.json`

```jsonc
{
  "generatedAt": "2026-06-14T00:00:00Z",     // UTC ISO-8601, run time (FR-010)
  "sourceGeoJson": "Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson", // basename
  "neighborhoods": [                          // flat array, sorted by name asc (FR-006)
    {
      "objectId": 13,                         // int, join key (FR-012)
      "name": "Ridgewood Heights",
      "npu": "C",
      "sqMiles": 0.42,                         // null if missing
      "population": 2191,                      // int, rounded; null if missing (D6)
      "medianHouseholdIncome": 87000,          // int, rounded; null if missing (D6)
      "transitCommutePercent": 9.1,            // 1 dp; null if missing (D6)
      "carAlonePercent": 30.2,                 // 1 dp; null if missing
      "workFromHomePercent": 58.7,             // 1 dp; null if missing
      "routes": [
        { "routeId": "26922", "routeShortName": "39" }
      ]
    }
  ]
}
```

**Rules**: percentages rounded to 1 dp, income/population to nearest int (FR-007); `null` for missing (FR-011); no geometry (FR-009); array sorted by `name` (FR-006). Sized to stay LLM-context-friendly (SC-006).

---

## Serialized: Full File — `neighborhood_routes_full.json`

```jsonc
{
  "generatedAt": "2026-06-14T00:00:00Z",
  "sourceGeoJson": "Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson",
  "neighborhoods": {                          // dict keyed by str(objectId) (FR-008)
    "13": {
      "OBJECTID_1": 13,
      "NAME": "Ridgewood Heights",
      "NPU": "C",
      "SQMILES": 0.42,
      "population": 2.191,
      "commute__1": 30.2,
      "commute__3": 0.0,
      "commute__5": 58.72,
      "householdi": 87000,
      "HasData": true
      // …all remaining GeoJSON properties verbatim (no rename, no round); geometry excluded
    }
  }
}
```

**Rules**: every source property verbatim, original names, no rounding (FR-008); geometry excluded (FR-009); key is `str(objectId)`; `full["neighborhoods"][str(lean["objectId"])]` round-trips (FR-012, SC-003). Not loaded speculatively by skills (FR-016).

---

## Run Summary (stdout, not serialized) — FR-013

Printed at end of a successful run:
- Total neighborhoods processed.
- Count with ≥1 matched route.
- Names of neighborhoods with 0 matched routes (the likely edge/rural list).
- Total count of unique routes matched across all neighborhoods.
- (Recommended) counts of skipped features (bad geometry) for both inputs.
