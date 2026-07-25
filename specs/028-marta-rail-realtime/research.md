# Phase 0 Research: MARTA Rail Realtime

All findings below are consolidated from the design document
(`docs/MARTA_RAIL_REALTIME_DESIGN_DOCUMENT.md`), whose four open questions were resolved by a
live endpoint probe on 2026-06-23, and confirmed against the current worker code
(`Worker.cs`, `GtfsRtModels.cs`, `Program.cs`).

---

## R1 — Rail geometry is already loaded server-side

- **Decision**: Reuse the existing `_routeIndex`; do **no** GTFS-static or shape work.
- **Rationale**: `GtfsStaticLoader` ingests **all** routes (no `route_type` filter), and
  `Worker.BuildRouteIndex` (`Worker.cs:51-73`) keys each route by `RouteShortName ?? RouteId`.
  MARTA rail's `route_short_name` values are exactly `BLUE/GOLD/GREEN/RED`, so rail is already
  present in `_routeIndex` under those keys. The feed's `LINE: "RED"` maps with **zero
  translation** — consistent with Constitution Principle VI (join on `route_short_name`).
- **Alternatives considered**: A separate rail route index — rejected (duplicate plumbing,
  geometry already loaded).

## R2 — `LATITUDE`/`LONGITUDE` is the live train position (OQ-1, RESOLVED)

- **Decision**: Treat the coordinate as the live track position; pick **any one row per
  `TRAIN_ID`** after dedup. Retain a runtime assertion that all rows for a train share one
  coordinate.
- **Rationale**: 2026-06-23 probe (264 rows, 16 trains) showed `distinctCoords = 1` for every
  train, including one spanning 11 upcoming stations; geographic sanity check placed a train
  upstream of its first upcoming station — i.e. a live position, not a station coordinate.
- **Alternatives considered**: Averaging or per-station coordinates — rejected; the data
  disproves the station-coordinate hypothesis. The assertion (FR-013) is a cheap guard that
  fails loudly if MARTA changes the contract.

## R3 — Drop non-realtime rows (OQ-2, RESOLVED)

- **Decision**: Filter `IS_REALTIME != "true"` **before** dedup. Only genuine GPS positions
  enter the pipeline.
- **Rationale**: Rendering schedule-estimated positions is exactly the "people know when it's
  wrong" failure the feature avoids. Trivial single predicate; only ~16 trains at peak, so the
  occasional dropped non-realtime train doesn't empty the map. (The 2026-06-23 probe returned
  all rows `IS_REALTIME: "true"`, so the filter was a no-op on that sample but is applied
  defensively.)
- **Alternatives considered**: "Keep but flag" (dimmed marker / muted voice) — deferred as the
  escalation path only if a future cadence spike shows too few realtime trains.

## R4 — Feed cadence is coarse/irregular; reuse the route-aware animator (OQ-3, RESOLVED)

- **Decision**: Reuse the existing client animator (`wwwroot/js/vehicle-animator.js`)
  unchanged. ETA-paced interpolation is an **optional future refinement**, not a v1 prerequisite.
- **Rationale**: A 4× probe at ~11 s spacing measured per-train Haversine deltas of `0,0,0`
  (holds), `0,0,210`, `0,820,366` (820 m/11 s ≈ 168 mph — physically impossible), etc. The feed
  reports position in coarse, irregular catch-up steps, so **raw fixed-window lerp is
  insufficient** (it would freeze then teleport). The animator already (a) interpolates along
  the route polyline, (b) derives empirical speed and extrapolates forward along the route, and
  (c) caps coasting via `MAX_EXTRAPOLATION_MS = 30000` with per-sample re-anchoring — precisely
  the motion model the coarse cadence needs. During `0,0,0` holds it coasts and re-anchors on
  the next real step; an impossible catch-up step re-anchors rather than animating the dash.
- **Alternatives considered**: (1) Raw lerp — rejected (freeze/teleport). (2) A new client
  rail-motion subsystem — rejected (the animator already does it; keeps the "minimal client
  impact" thesis). (3) ETA-pacing now (`distance_remaining / WAITING_SECONDS`) — deferred to a
  tuning step only if empirical coasting looks noisy in the running app.

## R5 — Voices auto-assign by route key (OQ-4, RESOLVED)

- **Decision**: No manual rail voice data, no client code/data change.
- **Rationale**: `transit-synth.js` `instrumentFor(routeId)` computes
  `djb2(routeId) % PALETTE.length` (transit-synth.js:78-84) — deterministic for **any** route
  key, satisfying Constitution Principle VIII (deterministic, non-authored). `RED/GOLD/BLUE/
  GREEN` therefore each receive a trio voice automatically.
- **Caveats (recorded, non-blocking)**: (1) 3-voice cycle vs. 4 lines → at least two lines share
  a voice by pigeonhole; acceptable for v1 (buses already share voices). A rail-distinct voice
  family is a future enhancement. (2) `preload(routeIds)` (transit-synth.js:121) should receive
  rail keys; since trains flow through the same batch/path as buses this likely happens
  automatically — confirm during the build.

## R6 — Security & best-effort failure (FR-008, FR-012)

- **Decision**: Load `Marta:RailRealtime:BaseUrl` and `:ApiKey` from configuration/environment/
  user-secrets; never commit the key. Adapter is best-effort: on any failure, log a warning and
  return an **empty** entity list so the merged feed still carries buses.
- **Rationale**: Mirrors feature-012 FR-020 secret remediation and `FetchGtfsRtFeedAsync`'s
  null-on-failure behavior (`Worker.cs:550`). Use the standard `IHttpClientFactory` client with
  normal TLS validation (endpoint is HTTPS on port 18096; **no** cert override).
- **Bonus finding**: The endpoint returned HTTP 200 with **no** API key on 2026-06-23. The key
  may be optional for this deployment, but the design still treats it as configurable — do not
  hard-depend on keyless access.
- **Alternatives considered**: Hardcoding the key (committed) — rejected (Principle II / FR-020).
  A separate timer/cache for rail — rejected (duplicates the loop and telemetry for no benefit).

## R7 — Worker integration shape (grounded in current code)

- **Decision**: Merge both feeds on the existing `PeriodicTimer` tick in `ExecuteAsync`
  (`Worker.cs:41-48`); concat rail `FeedEntity` list into the bus `FeedMessage.Entities` before
  `ProcessSpatialReconciliationAsync`.
- **Rationale**: `FeedMessage.Entities` is a `List<FeedEntity>` (`GtfsRtModels.cs:13`); the
  reconciliation loop iterates `feed.Entities` reading only `entity.Id`, `entity.Vehicle.Trip
  .RouteId`, `entity.Vehicle.Position`, `entity.Vehicle.Vehicle.Id`, and
  `entity.Vehicle.Timestamp` — all of which the adapter populates. No reconciliation change
  needed. `Program.cs` registers HttpClients inline (`AddHttpClient(...)`) and uses primary-
  constructor DI on `Worker` — add a named `"RailRealtimeApi"` client and an
  `IRailRealtimeAdapter` singleton, then add the adapter to `Worker`'s constructor.
- **Alternatives considered**: A dedicated `Merge(busFeed, railFeed)` helper vs. inline concat —
  inline concat with null-guards is the minimal change; a tiny helper is acceptable for clarity.
