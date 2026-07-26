# Quickstart: Toronto TTC Transit City

A config-only city onboarding. No new class, no new dependency, no secret. Verification is
reachability + a live smoke test (there is no new unit-testable code).

## Prerequisites

- Repo builds and runs today (Aspire AppHost or Worker + WebAPI + Client individually).
- Internet egress to `bustime.ttc.ca` and `ckan0.cf.opendata.inter.prod-toronto.ca`.

## Steps

### 1. Add the `Ttc` constant
`src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs` → add `public const string Ttc = "ttc";`.

### 2. Add the Worker `Cities:` entry
`src/Server/…TransitDataWorker/appsettings.json` → append the canonical `ttc` object
(see `contracts/city-config.md`). Confirm the static URL space is `%20`.

### 3. Add the WebAPI `Cities:` entry
`src/Server/…WebAPI/appsettings.json` → append the **same** `ttc` object (shape loading parity).

### 4. Add the city-picker button
`src/Client/…Client.Shared/Components/FABs/CityFab.razor` → add the Toronto `MatButton` +
`HandleTtcClicked` handler (see `contracts/city-picker.md`).

### 5. Build
`dotnet build` the solution — expect success with no new warnings (config + one constant + one handler).

## Verification (9 checks)

1. **Static feed reachable**: `GET` the `%20`-encoded TTC static zip URL → 200, a valid GTFS zip.
2. **RT feed reachable**: `GET https://bustime.ttc.ca/gtfsrt/vehicles` → 200, decodes as GTFS-RT protobuf (`FeedMessage`) with vehicle entities.
3. **Shapes load**: after WebAPI startup, the route-shapes endpoint returns TTC routes (expect ~225 with shapes); subway lines `1`/`2`/`4` present as `route_type=1` (Rail).
4. **Live vehicles snap**: with the Worker running, TTC surface vehicles appear on the map at `#ttc` and move over a few poll cycles on real streets.
5. **Verbatim route match**: per-cycle counters show near-total matches; `skippedUnknownRoute` reflects only the odd internal id (e.g. `600`), `skippedNoRouteId` reflects the normal ~⅓ deadhead share. No mass drop.
6. **No rail-realtime fetch**: Worker logs show **no** attempt to hit a TTC rail-realtime endpoint (there is none). No error about a missing rail feed (SC-006).
7. **Streetcars = Rail (v1)**: a 500-series streetcar (e.g. `504`, `501`) renders/voices on the **Rail** treatment, not Bus — confirming the accepted as-built classification (research R1). This is expected, not a bug.
8. **Picker works**: the city FAB lists Toronto; selecting it sets `#ttc`, reloads, and shows Toronto. The Toronto button is disabled while active.
9. **No regression**: Atlanta / Boston / New York / Washington DC each still load and behave exactly as before (additive-only, FR-013 / SC-004).

## Rollback

Remove the four additions (constant, two `Cities:` entries, picker button). No migrations, no
state, no deploy-order constraint (no wire-format change).

## Operational follow-ups (not code, tracked separately)

- **Pin/mirror the CKAN static zip** — the resource id can rotate on schedule updates (research R4).
- **Dedicated streetcar (tram) voicing** — give `route_type=0` its own treatment instead of Rail (research R1). This is a wire-contract change (`TransitMode` enum) and a separate feature.
- **CityFab localization** — migrate all five inline city labels to `RouteFilterResources.resx` (Principle XII, research R5).
