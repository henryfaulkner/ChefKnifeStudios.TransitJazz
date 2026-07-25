# Quickstart: NYC MTA Bus Support

End-to-end verification, from unit test to buses on the map. Assumes the solution builds and the Worker/WebAPI run locally (Aspire AppHost).

## Prerequisites

- `NYMTA_BUS_API_KEY` obtained (register at the obanyc developer portal) and set in the **Worker's** environment (user secrets / launch env — never committed).
- Solution builds on .NET 10.

## 1. Unit test — `RouteIdNormalizer` (fastest inner loop)

```pwsh
dotnet test src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests `
  --filter "FullyQualifiedName~RouteIdNormalizerTests"
```

**Expect**: all accept-vector rows green (see `contracts/route-id-normalizer.md`), including `Q06→Q6`, `M15+→M15-SBS`, `bx3→BX3`, unknown-step no-op, empty-steps passthrough.

## 2. Regression guard — existing cities unchanged

```pwsh
dotnet test src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests
```

**Expect**: all pre-existing tests (`CityLoopTests`, `NoNymtaLeakageTests`, `FailureIsolationTests`, etc.) still green — proves the new normalization step is inert for `marta`/`wmata`/`mbta`/`nymta` (SC-004).

## 3. Config load — `nymta-bus` registers as a `GtfsRtCity`

Run the Worker. In logs, confirm the city registry built a `GtfsRtCity` for `nymta-bus` (no exception, no `Program.cs` special-casing). A missing/invalid `NYMTA_BUS_API_KEY` logs a warning and yields an empty feed for the tick (graceful — FR-011), not a crash.

## 4. Static data — 6 zips merge in the WebAPI

Run the WebAPI. Confirm `GtfsStaticLoader` downloads and merges the 6 `nymta-bus` zips; per-zip failures log-and-continue (FR-010). Hit the all-shapes route endpoint for `nymta-bus` and confirm bus route shapes exist across boroughs.

## 5. End-to-end — buses on the map

1. Launch the AppHost (Worker + WebAPI + WASM client).
2. In the client, open the city FAB → click **New York Buses** → app reloads at `#nymta-bus`.
3. **Expect**: live bus markers appear and move over successive ticks (SC-001), positioned on real streets.
4. Watch the Worker's per-cycle counters: `skippedUnknownRoute` should be a small fraction of vehicles (≥98% match, target ~100% — SC-002). A high skip rate means normalization isn't matching — recheck the `RouteIdNormalization` order and the `?key=` credential path (R4).
5. Confirm both operators render: at least one MTA Bus Company-only route (e.g. a QM/BXM express) shows buses (SC-003).

## 6. Telemetry

Confirm `nymta-bus` vehicles flow into telemetry (`EmitsTelemetry: true`), consistent with other bus cities (FR-013). Subway (`nymta`) remains excluded — untouched.

## Rollback

Remove the two `nymta-bus` `Cities:` entries and the `CityFab` button. `RouteIdNormalizer.cs` + `CityConfig.RouteIdNormalization` can remain (inert with no city using them). No data migration, no schema change.
