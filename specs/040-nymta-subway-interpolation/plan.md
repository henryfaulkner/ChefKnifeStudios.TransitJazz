# Implementation Plan: NYC Subway Position Interpolation

**Branch**: `040-nymta-subway-interpolation` | **Date**: 2026-07-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/040-nymta-subway-interpolation/spec.md`

## Summary

NYC subway GTFS-RT never carries `Position.lat/lon` — it is a stop-arrival-prediction
feed (`trip.route_id`, `current_stop_sequence`, `current_status`, `stop_id`, `timestamp`).
Run through the generic `GtfsRtCity`, every subway entity dies at the
`entity.Vehicle?.Position == null` guard in `Worker.ProcessSpatialReconciliationAsync`,
so the NYC map stays empty. This feature adds a **bespoke `NymtaCity : ITransitCity`**
(sibling of `MartaCity`) that **synthesizes** a `Position` per train inside
`FetchVehiclesAsync`: stopped/arriving trains snap to their target station coordinate;
in-transit trains are placed on the route **shape polyline** at an elapsed-time fraction
of the segment between the previous and target stations. By the time an entity leaves
`FetchVehiclesAsync` it is an ordinary `FeedEntity` with a real `Position`, so the shared
loop and every downstream stage (snap → lerp → crossing → synth) are untouched
(Principle: quarantine the city-specific algorithm, no `if (city == "nymta")` in the loop).

Two static lookups the pipeline does not produce today — `stop_id → (lat,lon)` and
per-route `stop_id → distanceAlongShapeMeters` — are computed **server-side once at
static-load time** (co-located with `GtfsStaticLoader`'s existing `Simplify` +
Haversine work, reusing the same cumulative-distance math the worker already builds in
`_routeCumDist`) and served via a new `GET /gtfs/subway/stop-offsets` endpoint. `NymtaCity`
fetches it once on startup and on the worker's existing 24 h refresh cadence, caches it,
and reads it on every 10 s tick — never re-fetched, never recomputed (Principle VII).
Subway RT is ~8 line-group feeds fetched/decoded/synthesized/merged into one
`FeedMessage`, with per-feed try/catch so one dead line group doesn't blank the others
(same shape as `MartaCity` merging bus + rail, and `GtfsRtCity` looping `GtfsRtUrls`).

**Scope:** subway/rail only. NYC bus is a separate, cheap `GtfsRtCity` config entry and is
out of scope. `NymtaCity.EmitsTelemetry => false` (telemetry stays MARTA-only per the
multi-city decision); local counters (`synthesizedStopped`, `synthesizedInTransit`,
`skippedUnknownStation`) are still logged.

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: `protobuf-net` (GTFS-RT decode, already referenced), ASP.NET Core
Minimal API (WebAPI endpoint), `System.IO.Compression.ZipArchive` (static zip parse, already
used by `GtfsStaticLoader`). No new NuGet packages.
**Storage**: In-memory only. Station/offset data is served from the WebAPI's existing
`IKeyValueRepository<string>` (same store as route shapes) and cached in-process by
`NymtaCity`. No database, no persisted artifact.
**Testing**: xUnit (`ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests` and
`...WebAPI.Tests`), following the existing `CityLoopTests` / `FailureIsolationTests` style
(pure unit tests over synthesis math and fault isolation; no live-feed dependency).
**Target Platform**: Linux container (`TransitDataWorker` Docker image + WebAPI container).
**Project Type**: Backend — .NET Worker Service + ASP.NET Core WebAPI. No frontend/WASM/JS
changes (Principle VII holds: synthesized trains are ordinary `RouteNearestPointRecord`s
downstream; the map already renders them).
**Performance Goals**: Synthesis stays within the existing 10 s tick budget for ~8 line
groups; the two lookups are O(1) dictionary reads per entity. `pointOnShapeAtDistance` is a
binary search over the cumulative-distance array (O(log n) per in-transit train).
**Constraints**: NO per-tick re-fetch or recompute of station/offset data (Principle VII);
NO NYC-specific branch outside `NymtaCity`; NO raw `stop_times.txt` shipped to the worker
(collapsed server-side); NO `.Content`-string read of the protobuf (stream-decode only, per
the feed-evaluation `text/plain` binary-mangling gotcha).
**Scale/Scope**: ~28 subway routes, ~470 stations, ~8 RT feeds. `stop_times.txt` is millions
of rows — parsed once server-side, collapsed to per-route ordered stop+offset lists, raw
rows discarded.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Impact | Verdict |
|-----------|--------|---------|
| **I. Decoupled Cloud Architecture** | Changes confined to WebAPI (new endpoint + static parse) and TransitDataWorker (new city adapter). SignalR contract unchanged; synthesized trains flow through the existing `RouteNearestPointBatchEvent`. | ✅ PASS |
| **II. No Frontend Secrets** | No frontend change. Subway RT feeds are keyless; the static zip needs no key. | ✅ PASS |
| **III. Two-Pass Pipeline** | Untouched. `NymtaCity` emits normalized `FeedEntity`s that enter the existing V2 pass exactly like a MARTA bus. `RouteJoinKey` semantics unchanged (single-letter/number line labels serve as both `route_id` and join key for NYC subway — no short-name divergence). | ✅ PASS |
| **IV. OpenTelemetry / structured logging** | New per-cycle counters (`synthesizedStopped`, `synthesizedInTransit`, `skippedUnknownStation`) logged structurally, mirroring `skippedUnknownRoute`. | ✅ PASS |
| **V. GitHub Actions CI/CD** | No pipeline change; both artifacts (WASM unchanged, Worker Docker image) still build. | ✅ PASS |
| **VI. GTFS ID Mapping / `RouteJoinKey`** | `NymtaCity` sets `Trip.RouteId` to the line label; the worker's `_routeIndex` already aliases `route_id` and `JoinKey`, so lookup succeeds. No `RailRouteIdMap` needed for subway (labels align). | ✅ PASS |
| **VII. OSM Cartography / data-layer persistence / no re-fetch** | **Load-bearing.** Station/offset data computed once server-side and fetched once by the worker on the existing refresh cadence; read from cache per tick, never recomputed. Reuses existing shape geometry + Haversine cumulative-distance math. | ✅ PASS (verified in design) |
| **VIII. Generative Music** | Untouched — synthesized trains cross the same procedurally-generated trigger points and sound identically to buses. | ✅ PASS |
| **IX–XIII (UX, filter, zoom, overlays, i18n, dark-mode)** | No frontend surface touched; no new user-facing copy or CSS. | ✅ N/A |

**Result: PASS. No violations. Complexity Tracking table not required.**

The one genuinely new algorithm (`pointOnShapeAtDistance` + elapsed-time fraction) and the
one new endpoint are the irreducible core of making a missing coordinate appear; they are
justified by FR-001–FR-006 and cannot be met by config alone (that is exactly why `NymtaCity`
is bespoke, not a `GtfsRtCity` entry).

## Project Structure

### Documentation (this feature)

```text
specs/040-nymta-subway-interpolation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── stop-offsets-endpoint.md      # GET /gtfs/subway/stop-offsets contract
│   ├── nymta-city-adapter.md         # NymtaCity ITransitCity behavior contract
│   └── interpolation-algorithm.md    # Per-entity synthesis + pointOnShapeAtDistance
└── checklists/
    └── requirements.md  # Spec quality checklist (already created by /speckit.specify)
```

### Source Code (repository root)

Real project layout (note: root namespace is `ChefKnifeStudios.TransitJazz`, projects live
under `src/Server/`; the constitution's tree is aspirational — trust these paths):

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── Cities/
│   ├── ITransitCity.cs              # unchanged (contract already fits)
│   ├── MartaCity.cs                 # precedent to copy (bus + rail merge)
│   ├── GtfsRtCity.cs                # precedent for per-feed try/catch fan-out
│   ├── CityConfig.cs               # + subway static/RT config fields consumed by NymtaCity
│   └── NymtaCity.cs                 # NEW — the adapter (fan-out + synthesis)
├── Subway/                          # NEW folder for the NYC-specific helpers
│   ├── StopOffsetTable.cs           # NEW — cached station coords + per-route offsets DTO
│   ├── ShapeInterpolator.cs         # NEW — pointOnShapeAtDistance + stationBefore + frac
│   └── SubwaySynthesisOptions.cs    # NEW — NominalRunSeconds constant + RT feed URLs
├── GtfsRtModels.cs                  # unchanged (VehiclePosition already has StopId/CurrentStatus/CurrentStopSequence)
├── Worker.cs                        # UNCHANGED (the whole point)
└── Program.cs                       # + one registration branch: NymtaCity for CityNames.Nymta

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
├── GtfsStatic/
│   ├── GtfsStaticLoader.cs          # + parse stops.txt + stop_times.txt → offset table (subway city only)
│   └── SubwayStopOffsetBuilder.cs   # NEW — the stop→shape-offset derivation, keyed by {city}
├── EndpointGroups/
│   └── GtfsEndpoints.cs             # + MapGet /gtfs/subway/stop-offsets
└── Program.cs                       # unchanged (endpoint auto-mapped via MapGtfsEndpoints)

src/ChefKnifeStudios.TransitJazz.Shared/
├── CityNames.cs                     # + public const string Nymta = "nymta";
├── ApiEndpoints.cs                  # + Gtfs.GetSubwayStopOffsets = "/gtfs/subway/stop-offsets"
└── GtfsData/
    └── SubwayStopOffset.cs          # NEW — shared DTO for the endpoint payload (WebAPI↔Worker)

src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
└── SubwaySynthesisTests.cs          # NEW — synthesis math, endpoint-cache, fault isolation

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/
└── SubwayStopOffsetBuilderTests.cs  # NEW — stops.txt/stop_times.txt → offset table

appsettings.json (Worker + WebAPI)   # + "nymta" Cities: entry (subway static zip + 8 RT URLs)
```

**Structure Decision**: Backend-only, two-project change (WebAPI produces the static data;
TransitDataWorker consumes it and synthesizes). The Shared project gains only the two DTOs and
the two constants that cross the WebAPI↔Worker boundary. `Worker.cs` is deliberately untouched
— the adapter seam (`ITransitCity`) already accepts a fully-normalized `FeedMessage`, so all
new behavior lands in `NymtaCity` + its `Subway/` helpers and the server-side builder.

## Complexity Tracking

> No constitution violations — table intentionally empty.
