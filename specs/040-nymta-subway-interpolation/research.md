# Phase 0 Research: NYC Subway Position Interpolation

All decisions below are grounded in the actual codebase (paths verified) and the design
doc `docs/nymta-subway-interpolation-design.md`. No `NEEDS CLARIFICATION` markers remained
after spec validation; the items here resolve the design-level "how" choices.

---

## R1. Where does the synthesized coordinate come from, and where does synthesis run?

**Decision**: A bespoke `NymtaCity : ITransitCity` synthesizes `Position` inside
`FetchVehiclesAsync`, before the entity ever reaches `Worker`.

**Rationale**: `Worker.ProcessSpatialReconciliationAsync` skips any entity with
`entity.Vehicle?.Position == null` (`Worker.cs:358`). The `ITransitCity` contract
(`ITransitCity.cs`) returns a "fully-normalized merged `FeedMessage`", so the loop never
asks whether a city has rail. Emitting a `FeedEntity` with a real `Position` (identical in
shape to `MartaCity.FetchRailEntitiesAsync`, `MartaCity.cs:97-107`) means the shared loop,
snap (`RouteSnapper`), lerp, crossing detection (`CrossingDetector`), and synth all treat the
train as a bus with zero NYC-specific code.

**Alternatives considered**:
- *Config-driven `GtfsRtCity`*: rejected — `GtfsRtCity` only decodes and re-emits; it cannot
  invent a coordinate. NYC subway needs computed data, not decoded data.
- *Patch `Worker.cs` to interpolate when Position is null*: rejected — leaks `if city==nymta`
  into the shared pipeline (the anti-drift smell the design explicitly quarantines against).

---

## R2. The three RT inputs already exist on the decode model — confirm no proto change

**Decision**: No change to `GtfsRtModels.cs`. `VehiclePosition` already carries
`CurrentStopSequence` (field 3), `CurrentStatus` (`VehicleStopStatus?`, field 4), `StopId`
(field 7), and `Timestamp` (field 5). `TripDescriptor.RouteId` (field 5) carries the line label.

**Rationale**: Verified in `GtfsRtModels.cs:56-88` and `:109-129`. The `VehicleStopStatus`
enum (`IncomingAt=0, StoppedAt=1, InTransitTo=2`) is defined in
`Shared/EventData/PositionData.cs:16-21` — exactly the enum the design references. The
`protobuf-net` `[ProtoMember]` numbering matches the GTFS-RT spec, so a standard
`ProtoBuf.Serializer.Deserialize<FeedMessage>(stream)` (as in `MartaCity`/`GtfsRtCity`)
populates all four fields with no new decode work.

**Note (feed-evaluation gotcha)**: keep the stream-decode path
(`ReadAsStreamAsync` → `Deserialize`); never introduce a `.Content` string read, which
mangles the binary protobuf (documented in `docs/city-compat/nymta.md`). Both existing city
adapters already do it right — copy them verbatim.

---

## R3. Where are the station coords + stop-distance-along-shape computed?

**Decision**: Server-side in the WebAPI's `GtfsStaticLoader`, in a new
`SubwayStopOffsetBuilder`, only for the subway city. Parse `stops.txt`
(`stop_id → lat/lon`) and `stop_times.txt` (per-trip ordered `stop_id` sequence), collapse to
a per-route ordered stop list, and compute each stop's cumulative distance along that route's
shape by reusing the same Haversine cumulative-distance logic the code already runs.

**Rationale**:
- `GtfsStaticLoader` already downloads and `ZipArchive`-parses `trips.txt`, `shapes.txt`,
  `routes.txt` (`GtfsStaticLoader.cs:198-292`) and simplifies/measures shapes. Adding two more
  entries (`stops.txt`, `stop_times.txt`) reuses the existing `SplitCsvLine`, header-index, and
  cumulative-distance machinery — one place for "static → geometry", per the design's
  "why server-side" argument.
- The worker already builds `_routeCumDist` (`Worker.cs:35`, `:259-262`) with
  `HaversineCalculator.DistanceMeters` — identical math; the server produces the *stop-anchored*
  version of the same array. Keeping the worker free of GTFS-zip parsing (it has none today) is
  the constitution's separation of concerns.
- `stop_times.txt` for NYC subway is millions of rows; the design mandates parsing it **once**
  server-side and discarding raw rows (ship only the collapsed offset table). FR-013.

**Distance-along-shape method**: for each ordered stop, find the nearest point on the route's
(pre-`Simplify`) shape polyline and take its cumulative distance. Use the **raw** (un-simplified)
shape points for offset accuracy, then the endpoint serves offsets against the polyline the
worker will interpolate on. (See open decision D1 in data-model: simplified-vs-raw geometry
alignment — resolved to "serve the polyline used for interpolation alongside its offsets" so the
two never drift.)

**Alternatives considered**:
- *Compute in `NymtaCity`*: rejected — forces GTFS-zip parsing into the worker, duplicates
  `Simplify`/Haversine, and re-parses `stop_times.txt` on the worker's cadence. Violates the
  single-transformation-site rule.
- *Straight line between stations (chord)*: rejected by FR-005 — subway lines curve (e.g. the 7
  through Queens); a chord cuts across blocks. Must walk the shape.

---

## R4. How does the worker get the offset table without re-fetch/recompute?

**Decision**: New `GET /gtfs/subway/stop-offsets?city=nymta` endpoint (mirrors
`/gtfs/routes/shapes`). `NymtaCity` fetches it once on first `FetchVehiclesAsync` (lazy, guarded
by a cached flag) and refreshes on the worker's existing 24 h `RefreshRouteIndexAsync` cadence
by re-fetching; between refreshes it reads the in-memory cache on every tick.

**Rationale**: Directly mirrors `_routeIndex` discipline (`Worker.cs:275-319`, `:681-714`):
fetched once, refreshed on a slow cadence, read per tick. Principle VII forbids per-tick
re-fetch/recompute. Serving from the WebAPI's `IKeyValueRepository<string>` (same store as route
shapes) means the builder runs inside the existing `GtfsStaticLoader` refresh cycle and the data
is ready when the worker asks.

**Caching detail**: `NymtaCity` holds a `volatile StopOffsetTable? _table`. First tick with a
null table triggers a fetch; a background refresh (piggybacking the worker's existing 24 h loop
is out of `NymtaCity`'s reach, so `NymtaCity` self-schedules a lazy TTL re-fetch, e.g. every 24 h
tracked by a `DateTime _fetchedAt`). This keeps the "no per-tick fetch" guarantee without
coupling to `Worker`'s private timers. See contract `nymta-city-adapter.md`.

**Alternatives considered**:
- *Ship the offset table in the route-shapes payload*: rejected — bloats the shared,
  every-city `/gtfs/routes/shapes` response with NYC-only data; a separate endpoint keeps the
  concern isolated (only NYC fetches it).

---

## R5. In-transit fraction and shape walk

**Decision**: `frac = clamp(elapsedSeconds / NominalRunSeconds, 0, 1)` where `elapsedSeconds =
now - entity.timestamp`; `NominalRunSeconds` is a **constant** (90 s, per design §3.3). Position
= `pointOnShapeAtDistance(route, dPrev + frac*(dTarget - dPrev))`, a binary search over the
route's cumulative-distance array + linear interpolation between the two bracketing shape points.

**Rationale**: The feed pins the train only at stations; `frac` is a smoothing estimate that is
*exactly right at both endpoints* (frac 0 = prev station, frac 1 = target station) and
approximate in between — which is what makes it read as correct (design §3.3). Constant-first is
the YAGNI path (endpoints anchor the motion); refining `NominalRunSeconds` from `stop_times.txt`
scheduled deltas is explicitly deferred (spec out-of-scope). `pointOnShapeAtDistance` is the one
genuinely new geometric helper; the cumulative-distance array is exactly what the server serves
per route.

**Direction / `stationBefore`**: the `stop_id` suffix (`N`/`S`) disambiguates the previous
neighbour in the ordered stop list; at a terminal (`prev == null`) return the target coord
(FR-007, edge case). Direction is carried in the offset table's ordered-per-direction lists.

**Alternatives considered**:
- *Interpolate by distance from `stop_times` schedule*: deferred (see above).
- *Straight-line lerp between station coords*: rejected (FR-005).

---

## R6. RT fan-out across ~8 line-group feeds

**Decision**: `NymtaCity.FetchVehiclesAsync` loops the 8 configured feed URLs, each in its own
try/catch, decodes each protobuf, runs synthesis, merges into one `FeedMessage`.

**Rationale**: Structurally identical to `GtfsRtCity` looping `config.GtfsRtUrls` with per-URL
try/catch (`GtfsRtCity.cs:23-39`) and `MartaCity` merging bus + rail (`MartaCity.cs:23-31`).
Per-feed isolation satisfies FR-010 / SC-006 (one dead line group doesn't blank the others). The
8 URLs live in the `nymta` `Cities:` config entry's `GtfsRtUrls` (already an array field on
`CityConfig`).

---

## R7. Registration, config, telemetry

**Decision**:
- `CityNames.Nymta = "nymta"` in `Shared/CityNames.cs`.
- `Program.cs` gains one branch: `else if (cfg.Name == CityNames.Nymta) cities.Add(sp.GetRequiredService<NymtaCity>());` + `AddSingleton<NymtaCity>()` (mirrors the MARTA special-case at `Program.cs:36-38`).
- `appsettings.json` (Worker + WebAPI) gains a `nymta` `Cities:` entry: subway static zip in
  `StaticZipUrls`, the 8 RT line-group URLs in `GtfsRtUrls`, `EmitsTelemetry: false`.
- `NymtaCity.EmitsTelemetry => false` (telemetry stays MARTA-only per the multi-city Q6
  decision). The Worker's telemetry gate already keys on `city.EmitsTelemetry` (`Worker.cs:94`),
  so no PerCityCycle row is posted for NYC — no code change needed there.

**Rationale**: Reuses the exact registration pattern already in `Program.cs`. `EmitsTelemetry
=> false` avoids multiplying per-city parquet blobs; local counters (logged, not telemetered)
cover diagnostics.

---

## R8. Testing approach

**Decision**: Pure unit tests, no live feeds — matching `CityLoopTests` / `FailureIsolationTests`.
- `SubwayStopOffsetBuilderTests` (WebAPI.Tests): feed small in-memory `stops.txt` +
  `stop_times.txt` + `shapes.txt` strings → assert per-route ordered stops with correct
  monotonic offsets, both directions.
- `SubwaySynthesisTests` (TransitDataWorker.Tests): `StoppedAt`/`IncomingAt` → exact station
  coord; `InTransitTo` at frac 0/0.5/1 → correct point on a known curved polyline; terminal
  (null prev) → target coord; missing status → treated as `StoppedAt`; unknown `stop_id` →
  skipped + counter incremented; one throwing feed among several → others still synthesize
  (fault isolation, mirrors `CityLoopTests.FaultIsolation`).

**Rationale**: The synthesis math and the offset derivation are the risk; both are pure
functions testable with hand-built fixtures. Endpoint wiring and DI registration are low-risk and
covered by the existing city-loop harness + a build.

---

## Resolved constants & config summary

| Item | Value | Source |
|------|-------|--------|
| `NominalRunSeconds` | `90` (constant, configurable via `SubwaySynthesisOptions`) | design §3.3, R5 |
| Offset-table TTL / refresh | 24 h (matches worker route-index refresh) | R4, `Worker.cs:683` |
| RT feed count | 8 line-group URLs (config array) | design §5, R6 |
| `EmitsTelemetry` | `false` | design §7, R7 |
| Endpoint | `GET /gtfs/subway/stop-offsets?city=nymta` | design §4, R4 |
| No new NuGet | ✅ | R-all |
