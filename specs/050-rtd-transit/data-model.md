# Phase 1 Data Model: RTD Denver Transit City

No new persistent entities, schema changes, or transient processing concepts. This feature reuses
every existing data shape (`CityConfig`, `RouteShapeFeature`, `VehiclePositionBatchEvent`,
`RouteNearestPointBatchEvent`) unchanged, and is a strict subset of what WMATA's `CityConfig`
already exercises.

## Existing entities reused (unchanged)

- **CityConfig** (`Cities:` array entry, Worker + WebAPI `appsettings.json`): `Name`,
  `GtfsRtUrls[]`, `StaticZipUrls[]`, `RailRouteIdMap`, `EmitsTelemetry`. RTD's entry uses these
  five fields — no `ApiKeyEnvVar`, `RailRealtime`, or `RouteIdNormalization` (all omitted, same
  keyless shape as TTC/SEPTA, but with `RailRouteIdMap` populated like WMATA).
- **RouteShapeFeature** (GeoJSON, WebAPI KV store, keyed `{city}:{displayKey}`): produced by
  `GtfsStaticLoader` from RTD's static zip exactly as for any other flat-zip city — no loader
  change.
- **VehiclePositionBatchEvent / RouteNearestPointBatchEvent**: Worker's live-vehicle pipeline,
  unchanged — RTD vehicles flow through the identical V1/V2 passes as every other `GtfsRtCity`.
  Rail vehicles' `Trip.RouteId` is rewritten by the existing `GtfsRtCity.ApplyRailRouteIdMap` step
  before the V2 join, exactly as WMATA's rail vehicles already are.

## Key Entities (from spec.md, restated with implementation grounding)

- **RTD city configuration**: `CityConfig` entry, `Name = "rtd"`, one `GtfsRtUrls` entry, one
  `StaticZipUrls` entry, no credential fields, `RailRouteIdMap` populated with 8 entries.
- **Rail route-ID map entry**: an existing `Dictionary<string, string>` entry
  (`RailRouteIdMap[rtRouteId] = staticRouteShortName`) — RTD contributes 8 of these
  (`101C`→`C`, `101E`→`E`, `101T`→`T`, `103W`→`W`, `107R`→`R`, `113B`→`B`, `113G`→`G`,
  `117N`→`N`), the same shape as WMATA's 12 entries, just a different data set on the same
  pre-existing field.
