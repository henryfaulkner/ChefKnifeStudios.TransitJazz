# Quickstart: NYC Subway Position Interpolation

Verifies the feature end-to-end: server-side offset table → worker fetch/cache →
per-entity synthesis → normalized entities visible on the map. Ordered so each step is
independently checkable.

## Prerequisites

- .NET 10 SDK; solution builds (`dotnet build ChefKnifeStudios.TransitJazz.sln`).
- The `nymta` `Cities:` entry added to **both** `appsettings.json` files (Worker + WebAPI)
  with the subway static zip in `StaticZipUrls` and the 8 line-group RT URLs in `GtfsRtUrls`,
  `EmitsTelemetry: false`.

## 1. Build & unit tests (no live feeds)

```powershell
dotnet build ChefKnifeStudios.TransitJazz.sln
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests
```

**Expect**: `SubwayStopOffsetBuilderTests` (INV-E1–E6) and `SubwaySynthesisTests`
(INV-A1–A7, INV-N2) green, alongside the existing suites.

## 2. Server-side offset table (WebAPI)

Run the WebAPI, wait for `GtfsStaticLoader` to finish a refresh, then:

```powershell
curl "https://localhost:<port>/gtfs/subway/stop-offsets?city=nymta" | jq 'length'
curl "https://localhost:<port>/gtfs/subway/stop-offsets?city=nymta" | jq '.[0]'
```

**Expect**: a non-empty array; `.[0]` has `routeJoinKey`, `direction` (`"N"`/`"S"`),
`coordinates`, `cumulativeDistanceMeters` (starts at 0, non-decreasing, same length as
coordinates), and an ordered `stops` array. **No** raw `stop_times` rows anywhere (INV-E4).
Before static load completes, the endpoint returns `503` (INV-E5).

## 3. Worker fetch & cache (Principle VII)

Run the Worker against the WebAPI. In the logs:

**Expect**:
- One "fetched subway stop-offsets" line on startup (or first NYC tick), **not** per tick.
- Each 10 s tick logs `synthesizedStopped`/`synthesizedInTransit`/`skippedUnknownStation`
  counters for `nymta`, but **no** re-fetch line (the table is read from cache). INV-N1.

## 4. Trains appear & move (the visible outcome)

With the map pointed at `nymta`:

**Expect (SC-001, SC-002)**: subway trains render — not an empty map. Trains reported
`StoppedAt`/`IncomingAt` sit exactly on station coordinates.

**Expect (SC-003, US2)**: an `InTransitTo` train sits on the drawn line between two stations
and advances toward the target across successive 10 s ticks; it coincides with the station
coordinate at both segment ends. On a curved line (e.g. the 7 through Queens) it follows the
curve, not a straight chord.

## 5. Downstream indistinguishability (SC-004)

**Expect**: synthesized trains cross trigger points and sound (Principle VIII) exactly like
MARTA buses; a code search confirms **zero** NYC-specific conditionals in `Worker.cs`,
`RouteSnapper`, `CrossingDetector`, or the synth path. All NYC logic lives in
`Cities/NymtaCity.cs` + `Subway/*`.

## 6. Fault isolation (SC-006)

Temporarily point one of the 8 RT URLs at a dead host.

**Expect**: that line group's trains are absent for the tick; every other line group still
renders (per-feed try/catch, INV-N2). Restore the URL → trains return next tick.

## 7. Edge cases (spot-check)

| Case | Expected |
|------|----------|
| Train with unknown `stop_id` | dropped, `skippedUnknownStation` increments, others unaffected (INV-A5) |
| Train at a line terminal, `InTransitTo`, no previous | pinned to target station (INV-A7) |
| Train with no `CurrentStatus` | treated as `StoppedAt` (INV-A4) |
| `elapsed` ≫ `NominalRunSeconds` | sits at target platform, no overshoot (INV-A6) |

## Rollback

Feature is additive and NYC-scoped: remove the `nymta` `Cities:` entry (or set an
unreachable RT URL set) and NYC simply produces no trains — MARTA/WMATA/MBTA are unaffected
because `Worker.cs` is unchanged. No migration, no persisted state.
