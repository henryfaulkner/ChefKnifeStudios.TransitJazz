# Quickstart: SEPTA Philadelphia Transit City

A mostly-config city onboarding **plus one narrowly-scoped new capability** (nested-zip
extraction in `GtfsStaticLoader`). No new class, no new dependency, no secret. Verification is
reachability + a live smoke test + the new unit tests for the nested-zip logic.

## Prerequisites

- Repo builds and runs today (Aspire AppHost or Worker + WebAPI + Client individually).
- Internet egress to `www3.septa.org`.

## Steps

### 1. Add the `Septa` constant
`src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs` → add `public const string Septa = "septa";`.

### 2. Add the Worker `Cities:` entry
`src/Server/…TransitDataWorker/appsettings.json` → append the canonical `septa` object
(see `contracts/city-config.md`).

### 3. Add the WebAPI `Cities:` entry
`src/Server/…WebAPI/appsettings.json` → append the **same** `septa` object (shape loading parity).

### 4. Add the city-picker button
`src/Client/…Client.Shared/Components/FABs/CityFab.razor` → add the Philadelphia `MatButton` +
`HandleSeptaClicked` handler (see `contracts/city-picker.md`).

### 5. Build
`dotnet build` the solution — expect success with no new warnings.

### 6. Add nested-zip extraction to `GtfsStaticLoader`
`src/Server/…WebAPI/GtfsStatic/GtfsStaticLoader.cs` → in `BuildCityShapeSetAsync`, add the
detect-root-else-unwrap-nested-zip step per `contracts/nested-zip-extraction.md`. This is
additive: every existing city's flat-zip path must remain byte-identical.

### 7. Add nested-zip unit tests
New tests covering: flat zip unchanged, nested zip unwrapped (non-"rail" entry selected), zip
with no `trips.txt` and no nested entry falls back to zero routes without throwing (see
research.md R4).

### 8. Map origin coordinate
`src/Client/…Client.WebApp/Pages/TransitMap.razor.cs` → `_cityCenter[CityNames.Septa] = (39.9526,
-75.1652)` (Center City / SEPTA's 15th & Market hub — see research.md R3).

### 9. Audio overlay + info panel copy
Invoke `create-audio-overlay-paragraphs` for Philadelphia/SEPTA to populate
`SeptaAudioOverlayHeader`/`Paragraph1-3` in `RouteFilterResources.resx`, wire the
`AudioUnlockOverlay.razor` switch arm (`CityNames.Septa => "SeptaAudioOverlay"`), add the
`SeptaOverlayParagraph1` info-panel key, and wire `InfoFab.razor`'s switch arm
(`CityNames.Septa => "SeptaOverlay"`).

### 10. Build again
`dotnet build` — expect success with no new warnings.

## Verification (10 checks)

1. **Static feed reachable**: `GET https://www3.septa.org/developer/gtfs_public.zip` → 200, a
   valid zip whose root does NOT contain `trips.txt`/`shapes.txt`/`routes.txt` directly (confirms
   the zip-of-zips structure the nested-extraction step depends on) but DOES contain
   `google_bus.zip` and `google_rail.zip` as entries.
2. **RT feed reachable**: `GET https://www3.septa.org/gtfsrt/septa-pa-us/Vehicle/rtVehiclePosition.pb`
   → 200, decodes as GTFS-RT protobuf (`FeedMessage`) with vehicle entities.
3. **Unit tests pass**: the new nested-zip extraction tests (flat/nested/fallback) pass in
   isolation, without needing network access (synthetic in-memory zip fixtures).
4. **Shapes load from the nested zip**: after WebAPI startup, the route-shapes endpoint returns
   SEPTA routes (expect ~145 with shapes, from `google_bus.zip`) — confirming the extraction step
   actually ran against the live SEPTA download, not just the unit-test fixtures.
5. **Live vehicles snap**: with the Worker running, SEPTA surface vehicles appear on the map at
   `#septa` and move over a few poll cycles on real Philadelphia streets.
6. **NHSL renders**: the Norristown High Speed Line (`M1`) appears and moves, rendering on the
   Rail treatment (`route_type=1`), matching or exceeding the 1-of-5 rail keys observed live in
   the compat report.
7. **Verbatim route match**: per-cycle counters show near-total matches (compat report measured
   100% RT route_id alignment); `skippedUnknownRoute` should be near-zero.
8. **No Regional Rail data present**: the WebAPI route-shapes endpoint for `septa` contains no
   `route_type=2` entries — confirms `google_rail.zip` was correctly skipped, not accidentally
   unwrapped instead of `google_bus.zip`.
9. **Picker works**: the city FAB lists Philadelphia; selecting it sets `#septa`, reloads, and
   shows Philadelphia. The Philadelphia button is disabled while active. Audio overlay and info
   panel show SEPTA-specific copy, not another city's text.
10. **No regression**: Atlanta / Boston / New York / Washington DC / Toronto each still load and
    behave exactly as before — in particular, re-run (or spot-check) their static-zip loading to
    confirm the new nested-zip detection step is a true no-op for flat zips (FR-004 / SC-004).

## Rollback

Remove the additions (constant, two `Cities:` entries, picker button, overlay copy, map origin).
The `GtfsStaticLoader` nested-zip step can be left in place even if `septa` is removed — it's a
no-op for every other city — but reverting it alongside the rest is equally safe since nothing
else depends on it.

## Operational follow-ups (not code, tracked separately)

- **Broad Street Subway / Market-Frankford live-vehicle gap** — re-fetch SEPTA's GTFS-RT feed at
  a different time of day to determine whether `B1`/`B2`/`B3`/`L1` ever populate it; no code
  change needed if/when they do (FR-008).
- **Regional Rail** (`google_rail.zip`, `route_type=2`) — out of scope for this feature; would
  need its own compatibility assessment (no live-vehicle GTFS-RT presence confirmed) before any
  future onboarding.
- **CityFab localization** — migrate all six inline city labels to `RouteFilterResources.resx`
  (Principle XII, carried over from 043's tracked follow-up).
