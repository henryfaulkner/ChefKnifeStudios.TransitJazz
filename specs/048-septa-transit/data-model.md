# Phase 1 Data Model: SEPTA Philadelphia Transit City

No new persistent entities or schema changes. This feature reuses every existing data shape
(`CityConfig`, `RouteShapeFeature`, `VehiclePositionBatchEvent`, `RouteNearestPointBatchEvent`)
unchanged. The only structural addition is a **transient, in-memory processing concept** inside
the static loader — not a stored entity.

## Existing entities reused (unchanged)

- **CityConfig** (`Cities:` array entry, Worker + WebAPI `appsettings.json`): `Name`,
  `GtfsRtUrls[]`, `StaticZipUrls[]`, `EmitsTelemetry`. SEPTA's entry uses only these four fields —
  no `ApiKeyEnvVar`, `RailRealtime`, `RailRouteIdMap`, or `RouteIdNormalization` (all omitted,
  same as TTC).
- **RouteShapeFeature** (GeoJSON, WebAPI KV store, keyed `{city}:{displayKey}`): produced by
  `GtfsStaticLoader` from SEPTA's (unwrapped) static zip exactly as for any other city.
- **VehiclePositionBatchEvent / RouteNearestPointBatchEvent**: Worker's live-vehicle pipeline,
  unchanged — SEPTA vehicles flow through the identical V1/V2 passes as every other `GtfsRtCity`.

## New transient concept: nested-zip resolution (not stored)

Exists only for the duration of one `BuildCityShapeSetAsync` call per configured zip URL, per
refresh cycle. Not serialized, not cached, not exposed via any API.

| Step | Input | Output |
|------|-------|--------|
| 1. Fetch | `zipUrl` (from `CityStaticEntry.StaticZipUrls`) | raw zip bytes |
| 2. Open | raw zip bytes | `ZipArchive` (the "outer" archive) |
| 3. Detect | outer `ZipArchive` | does `trips.txt` exist at root? |
| 4a. (root has GTFS files) | outer `ZipArchive` | used directly as the "effective" archive |
| 4b. (root lacks GTFS files) | outer `ZipArchive` entries | select the non-"rail"-named `.zip` entry, if any, as the nested archive; open its stream as the "effective" `ZipArchive` |
| 5. Process | effective `ZipArchive` | `routeToShape`, `shapes`, `meta` — same as every existing flat-zip city, feeding into the existing `BuildZipRouteFeatures` |

No new field is added to `CityStaticEntry` or any config shape — detection is purely structural
(zip entry inspection), not config-driven, per research.md R1.

## Key Entities (from spec.md, restated with implementation grounding)

- **SEPTA city configuration**: `CityConfig` entry, `Name = "septa"`, two `GtfsRtUrls`/
  `StaticZipUrls` (well, one each — SEPTA is single-feed), no credential fields.
- **Nested static archive**: the transient resolution described above; conceptually a "GTFS
  static source" but never itself a stored/named entity — it collapses into ordinary
  `RouteShapeFeature` records by the end of the same processing call.
