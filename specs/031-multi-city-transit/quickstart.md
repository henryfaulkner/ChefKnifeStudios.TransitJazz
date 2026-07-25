# Quickstart: Multi-City Transit Targets

Two things this feature makes possible, plus the verification that proves it works.

## A. View a city

- Atlanta (default, unchanged): open the app normally.
- Washington DC: open `…/?city=wmata` (shareable/bookmarkable link).
- Unknown city: `…/?city=nope` falls back to Atlanta — never a blank map (FR-004).

## B. Add a standard-GTFS-RT city (config only — zero C#)

1. Append a `Cities:` entry (see `contracts/city-config.md`) with `Name`, `GtfsRtUrls`,
   `StaticZipUrls`, optional `RailRouteIdMap`, optional `ApiKeyEnvVar`, and `EmitsTelemetry: false`.
2. If the feed needs a key, add a Container Apps secret surfaced as the env var named in
   `ApiKeyEnvVar`. **Never** put the key in `appsettings.json`.
3. Restart the worker. The city is live at `…/?city={name}`.

That commit contains **no new application source** (SC-002).

## C. Add a bespoke-feed city (one isolated class)

1. Add `Cities/{Name}City.cs : ITransitCity` that fetches + normalizes the feed into a merged
   `FeedMessage` (route_ids already matching the static index).
2. Register it by name so the registry picks it over the generic `GtfsRtCity`.
3. Add the matching `Cities:` config entry.

Nothing in the loop, hub, client, or any other city changes (SC-003).

## Verification checklist (maps to Success Criteria)

| # | Check | Maps to |
|---|---|---|
| 1 | Open `?city=wmata` and `?city=marta` in two tabs; neither shows the other's vehicles. | SC-001 / FR-002 |
| 2 | Open with no param → Atlanta renders identically to pre-refactor (routes, vehicles, audio, telemetry). | SC-004 / FR-017 |
| 3 | The WMATA-add commit diff contains only config (+ a secret), no `.cs`. | SC-002 / FR-005 |
| 4 | The bespoke-city diff touches exactly one new `*City.cs` and zero shared/loop/hub/client lines. | SC-003 / FR-007 |
| 5 | Induce one city's feed to fail (bad URL); other cities keep updating; error logged with `{City}`. | SC-005 / FR-009 |
| 6 | Deployed container/process count is identical with 1 city vs N cities. | SC-006 / FR-016 |
| 7 | Join a city mid-stream → current vehicles appear within a few seconds (cache replay), not after a full tick. | SC-007 / FR-012 |
| 8 | Grep `appsettings*.json` and source: no agency API key value present. | SC-008 / FR-014 |
| 9 | Route `1` (ATL) and a same-named route elsewhere never cross-render; same vehicleId across cities never collides. | FR-010 / FR-011 |

## Slice order (build + ship in this order, per plan §"Implementation Order")

1. **Slice 1 — MARTA refactor, no behavior change**: `ITransitCity` + `MartaCity`, loop-over-one,
   per-city index/cache, `city` param threaded publisher → hub → client `JoinCity`. Verify checks
   #2, #7, #9 pass for MARTA before adding a second city. (spec US1 + US3)
2. **Slice 2 — WMATA as config only**: `GtfsRtCity` + `Cities:` + `RailRouteIdMap` + CA secret +
   multi-zip loader. Verify checks #1, #3, #5, #8. (spec US2)

## Existing tests to extend

- `Server.WebAPI.Tests/LastBatchCacheTests.cs` — assert per-city isolation (INV-T3).
- `Server.WebAPI.Tests/WorkerTransitHubTests.cs` — assert `Clients.Group(city)` routing + `Set(city,…)`.
- `Server.TransitDataWorker.Tests` — assert loop fault isolation (INV-2) and telemetry gate (INV-3).
