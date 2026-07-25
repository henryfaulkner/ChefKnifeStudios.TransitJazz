# City Integration-Test Scenarios — Catalog

**Feature:** 037-city-integration-test-framework
Companion to `design.md`. This is the exhaustive list of scenarios the
framework ships to **guarantee well-formatted output** for every city.

Legend:
- **Tier 1** = offline, deterministic, runs in CI on every PR (frozen fixtures).
- **Tier 2** = live, network, nightly/opt-in (`[Trait("tier","live")]`).
- Scenarios marked **[Theory]** run once per registered `CityContract`
  (marta, wmata, mbta, …) — so each is really N scenarios, one per city.
- IDs map to the invariants in `design.md §2` (A*, B*, C*).

---

## Group A — Static route-shape output (Surface A)

Driver: parse the frozen `static.zip` through the real
`GtfsStaticLoader.ParseRouteMetadata` / `ParseShapes` / `ParseRouteToShapeMap` /
`BuildLineStringFeature`, deserialize the resulting GeoJSON into
`RouteShapeFeature[]`, then assert.

| ID | Tier | Scenario | Pass condition |
|----|------|----------|----------------|
| A-01 | 1 | **[Theory]** City's fixture parses to ≥1 feature | `features.Count > 0` (a city that produces zero routes is silently dropped by `RefreshAllCitiesAsync` FR-005 — the test makes that visible). |
| A-02 | 1 | **[Theory]** Every feature is `Feature`/`LineString` (A1) | all `Type=="Feature"`, `Geometry.Type=="LineString"`. |
| A-03 | 1 | **[Theory]** Every geometry has ≥2 coordinates (A2) | no degenerate 0- or 1-point lines. |
| A-04 | 1 | **[Theory]** Every coordinate is `[lon,lat]`, both finite (A3) | each coord length 2; `double.IsFinite` on both. |
| A-05 | 1 | **[Theory]** Every coordinate lies inside city bounds (A4) | `contract.Bounds.Contains(lat, lon)` for all points — **catches lat/lon swap**, the single most common city-onboarding bug. |
| A-06 | 1 | **[Theory]** Every color is null or hex, never a URL (A5) | `Color`/`TextColor` match `^#[0-9A-F]{3}([0-9A-F]{3})?$` or are null. Directly guards the MBTA route-47 `route_url`→`route_color` regression. |
| A-07 | 1 | **[Theory]** Every feature has a non-empty `RouteId` (A6) | `!string.IsNullOrEmpty(Properties.RouteId)`. |
| A-08 | 1 | **[Theory]** Every feature stamped with expected city (A7) | `Properties.City == contract.CityName` (lowercase). |
| A-09 | 1 | **[Theory]** Every `Mode` is a defined enum value (A8) | `Enum.IsDefined(Properties.Mode)`. |
| A-10 | 1 | **[Theory]** Join key (RouteShortName ?? RouteId) non-empty (A9) | matches worker key derivation `Worker.cs:103`; empty key = route the worker can never index. |
| A-11 | 1 | **[Theory]** Sentinel routes present with expected color+mode | for each `contract.MustContain`, feature exists and `Color`/`Mode` match exactly (freezes the "tricky row" per city). |
| A-12 | 1 | **[Theory]** No duplicate route keys within a city | `features` grouped by join key have no group producing conflicting `Mode`/`Color` (detects the two-zip merge stomping metadata, cf. `allMeta.TryAdd`). |
| A-13 | 1 | **[Theory]** Simplification preserves endpoints | first and last coord of each simplified line equal the raw shape's endpoints (Douglas-Peucker must keep termini — `Simplify` keeps `[0]` and `[n-1]`). |

## Group A′ — Static parser edge cases (schema robustness)

These make the *per-city column layout* explicit so a new agency's ordering
can't silently break parsing. Extends the existing `GtfsStaticLoaderTests`.

| ID | Tier | Scenario | Pass condition |
|----|------|----------|----------------|
| A-20 | 1 | **[Theory]** City's real `routes.txt` header contains required columns | `route_id` present; log a warning (not fail) if `route_short_name`/`route_color` absent — parser tolerates it, test documents it. |
| A-21 | 1 | Quoted field containing comma stays one field | existing `SplitCsvLine_QuotedFieldWithComma…` — kept, now referenced by the framework as the canonical CSV-quoting guard. |
| A-22 | 1 | Doubled `""` inside quoted field unescapes | existing test, retained. |
| A-23 | 1 | BOM on header row stripped | header parse after `TrimStart('﻿')` still finds `route_id` (fixtures deliberately include a BOM city). |
| A-24 | 1 | **[Theory]** `route_type` → `TransitMode` mapping correct | rail types `0/1/2` → `Rail`, everything else → `Bus`, per city's actual types present in fixture. |
| A-25 | 1 | Missing `shapes.txt` yields empty (no throw) | city with no shapes produces 0 features, logged, doesn't crash the loader. |
| A-26 | 1 | Ragged row (fewer columns than header) skipped, not crashing | `cols.Length <= idx` guard holds. |

## Group B — GTFS-RT ↔ static join integrity (Surface B)

Driver: load frozen `vehicles.pb` into a `FeedMessage` (or run `GtfsRtCity`
against a stub `HttpClient` returning the fixture bytes), build the static index
from the frozen zip via the real `Worker.BuildRouteIndex`, then assert.

| ID | Tier | Scenario | Pass condition |
|----|------|----------|----------------|
| B-01 | 1 | **[Theory]** Join yield ≥ threshold | of RT entities with non-empty `route_id`, the fraction resolving in the static index ≥ `contract.MinJoinYield` (default 0.80). This is the core "does this city actually produce sound" test. |
| B-02 | 1 | **[Theory]** `RailRouteIdMap` values resolve to real static routes (B2) | every map *value* (e.g. `"B"`) exists as a static route id; fails if a rename orphans the map. |
| B-03 | 1 | **[Theory]** `RailRouteIdMap` keys appear in the RT feed | every map *key* (e.g. `"BLUE0"`) is seen at least once in `vehicles.pb`; flags dead map entries (warning-level for fixtures, since a trimmed fixture may not carry all). |
| B-04 | 1 | **[Theory]** `ApplyRailRouteIdMap` is idempotent + total | after mapping, no entity retains a mappable-but-unmapped `route_id`. |
| B-05 | 1 | **[Theory]** Every positioned vehicle is in city bounds (B3) | `Bounds.Contains` on each `Vehicle.Position` lat/lon — catches an RT feed for the wrong region. |
| B-06 | 1 | **[Theory]** RT vehicles carry a stable vehicle id | `Vehicle.Vehicle?.Id ?? entity.Id` non-empty for all (else the reconciliation cache key collapses). |
| B-07 | 1 | **[Theory]** Feed header timestamp present & sane | `Header.Timestamp` set and within a plausible range relative to fixture capture time (documents duplicate-feed detection, `Worker.cs:507`). |

## Group C — Published batch output (Surface C)

Driver: run **one** real reconciliation cycle — feed the frozen `vehicles.pb`
through `Worker.ProcessSpatialReconciliation` (or an extracted testable seam)
against the frozen static index, capturing the `RouteNearestPointBatchEvent`
that would be published (intercept `ITransitHubPublisher.PublishBatchAsync` with
a fake). Assert on the captured batch.

| ID | Tier | Scenario | Pass condition |
|----|------|----------|----------------|
| C-01 | 1 | **[Theory]** Batch is non-empty when RT feed is non-empty | if ≥1 vehicle joined, `batch.Records.Count > 0`. |
| C-02 | 1 | **[Theory]** Every snapped point within `MaxSnapMeters` of raw (C1) | `Haversine(raw, snapped) <= contract.MaxSnapMeters` — a snap that lands far away means wrong index or swapped coords. |
| C-03 | 1 | **[Theory]** Every batch `RouteId` exists in static index (C2) | no record references a route we can't draw. |
| C-04 | 1 | **[Theory]** Record `Mode` matches static route mode (C3) | rail vehicles carry `Rail`, bus carry `Bus`. |
| C-05 | 1 | **[Theory]** No NaN/Inf in any numeric field (C4) | `IsFinite` on all lat/lon; speed/bearing null or finite. |
| C-06 | 1 | **[Theory]** Second cycle marks unchanged feed as stale, not moved | feeding the *same* `vehicles.pb` twice yields `IsStale=true` records on cycle 2 (guards the stale-snapshot filter, feature 023). |
| C-07 | 1 | **[Theory]** First observation emits, prior lat==current lat | first-cycle records have `PriorLat==CurrentLat` (FirstObservation path, `Worker.cs:388`). |
| C-08 | 1 | **[Theory]** Batch serializes to valid JSON round-trip | `EventEnvelope`(batch) serializes with `JsonOptions.Get()` and deserializes back equal — the exact contract the SignalR client parses. |

## Group D — Endpoint contract (integration, in-process host)

Driver: `WebApplicationFactory`-style in-process host with a seeded in-memory
`IKeyValueRepository<string>` populated from the frozen features, hitting the
real minimal-API endpoints in `GtfsEndpoints`.

| ID | Tier | Scenario | Pass condition |
|----|------|----------|----------------|
| D-01 | 1 | `/gtfs/routes/shapes` returns 503 before ready key set | matches `ReadyKey` gate. |
| D-02 | 1 | `/gtfs/routes/shapes` returns all cities' features once ready | count == seeded features; each deserializes to `RouteShapeFeature`. |
| D-03 | 1 | **[Theory]** `/gtfs/routes/shapes?city=<c>` returns only that city | every returned feature `Properties.City == c`; prefix filter honored. |
| D-04 | 1 | **[Theory]** `/gtfs/routes?city=<c>` returns only `Properties` (no geometry) | shape properties present, no coordinates leaked; used by route-filter UI. |
| D-05 | 1 | `GetRouteShape` unknown route → 404 | matches endpoint behavior. |
| D-06 | 1 | **[Theory]** Every feature from the endpoint passes Group A invariants | re-run `CityOutputAssert.WellFormedShapes` on the HTTP payload — proves the *serialized* output (not just in-memory) is well-formed end-to-end. |

## Group E — Live smoke (Surface A + B against real upstreams)

Same assertions as Groups A and B, but sourced from the **real** feed URLs in
`appsettings.json Cities[]`. Opt-in, network-dependent, non-merge-blocking.

| ID | Tier | Scenario | Pass condition |
|----|------|----------|----------------|
| E-01 | 2 | **[Theory]** Real static zip downloads & parses to ≥1 feature | live fetch of `StaticZipUrls`, then Group A invariants A-02…A-10. |
| E-02 | 2 | **[Theory]** Real GTFS-RT feed downloads & deserializes | live fetch of `GtfsRtUrls`, `FeedMessage` non-null. |
| E-03 | 2 | **[Theory]** Live join yield ≥ threshold (B1 on live data) | catches upstream schema/route drift that fixtures miss. |
| E-04 | 2 | **[Theory]** Live vehicles in city bounds (B3) | region sanity on real feed. |
| E-05 | 2 | **[Theory]** API-key city fails clearly when key env var missing | if `ApiKeyEnvVar` set but unset in env, test is `Skipped` with a clear message (not a confusing 403). |
| E-06 | 2 | Feeds respond within timeout | each upstream returns 2xx within a generous cap; a hung agency is reported, not hung-on. |

## Group F — Framework meta-tests (guard the guards)

| ID | Tier | Scenario | Pass condition |
|----|------|----------|----------------|
| F-01 | 1 | Every `Cities[]` entry in `appsettings.json` has a `CityContract` | reflect over config, diff against registry — **a new city cannot ship without a contract** (the whole framework's enforcement point). |
| F-02 | 1 | Every `CityContract` has both fixtures present on disk | fail early with a helpful "run the trimmer" message. |
| F-03 | 1 | Every fixture zip contains `routes.txt`, `trips.txt`, `shapes.txt` | trimmer produced a valid minimal GTFS set. |
| F-04 | 1 | `GeoBounds` are self-consistent | `MinLat<MaxLat`, `MinLon<MaxLon`, and roughly earth-sized (guards a typo'd box that makes A-05/B-05 vacuously pass). |
| F-05 | 1 | Contract thresholds in sane ranges | `0 < MinJoinYield <= 1`, `MaxSnapMeters > 0`. |

---

## Coverage summary

| Output surface | Groups | Guarantees |
|----------------|--------|------------|
| Static shapes | A, A′, D | shape/geometry/color/bounds/mode/city-stamp well-formed; serialized payload matches. |
| RT↔static join | B, E | vehicles actually resolve to routes → the app produces sound, not silence. |
| Published batch | C | snapped output finite, in-range, correctly moded, stale-aware. |
| Onboarding enforcement | F | no city ships without a contract + fixtures + passing matrix. |

**Bottom line:** every registered city runs the full A/A′/B/C/D matrix offline in
CI on every PR (≈ 40 assertions × N cities), plus an opt-in live smoke (E). A new
city is "well-formatted-output-safe" the moment F-01 passes and its Theory rows
go green.

---

## Suggested build order (if implemented)

1. `Framework/` primitives: `CityContract`, `GeoBounds`, `FixtureLoader`,
   `CityOutputAssert`. (No behavior yet — just the harness.)
2. `tools/gtfs-fixture-trimmer` + freeze marta/wmata/mbta fixtures.
3. Group A + A′ (highest value, pure parse, no worker seam needed).
4. Group F meta-tests (locks the door behind every future city).
5. Group B (needs `BuildRouteIndex` access — already public-ish on `Worker`).
6. Group C (needs a small testable seam around `ProcessSpatialReconciliation`
   or a fake `ITransitHubPublisher` to capture the batch).
7. Group D (in-process host).
8. Group E (live tier, wire into a scheduled workflow).
