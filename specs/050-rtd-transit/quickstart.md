# Quickstart: RTD Denver Transit City

A pure config-only city onboarding — the simplest shape in the codebase. No new class, no new
production code, no new dependency, no secret. Verification is reachability + a live smoke test;
no new unit tests are needed (research.md R4).

## Prerequisites

- Repo builds and runs today (Aspire AppHost or Worker + WebAPI + Client individually).
- Internet egress to `open-data.rtd-denver.com` and `www.rtd-denver.com`.

## Steps

### 1. Add the `Rtd` constant
`src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs` → add `public const string Rtd = "rtd";`.

### 2. Add the Worker `Cities:` entry
`src/Server/…TransitDataWorker/appsettings.json` → append the canonical `rtd` object, including
the 8-entry `RailRouteIdMap` (see `contracts/city-config.md`).

### 3. Add the WebAPI `Cities:` entry
`src/Server/…WebAPI/appsettings.json` → append the **same** `rtd` object (shape loading parity —
must be byte-identical to the Worker's entry, including `RailRouteIdMap`).

### 4. Add the city-picker button
`src/Client/…Client.Shared/Components/FABs/CityFab.razor` → add the Denver `MatButton` +
`HandleRtdClicked` handler (see `contracts/city-picker.md`).

### 5. Build
`dotnet build` the solution — expect success with no new warnings.

### 6. Map origin coordinate
`src/Client/…Client.WebApp/Pages/TransitMap.razor.cs` → `_cityCenter[CityNames.Rtd] = (39.7539,
-105.0009)` (Denver Union Station / downtown transit core — see research.md R3).

### 7. Audio overlay + info panel copy
Invoke `create-audio-overlay-paragraphs` for Denver/RTD to populate
`RtdAudioOverlayHeader`/`Paragraph1-3` in `RouteFilterResources.resx`, wire the
`AudioUnlockOverlay.razor` switch arm (`CityNames.Rtd => "RtdAudioOverlay"`), add the
`RtdOverlayParagraph1` info-panel key (mentioning buses, light rail, and commuter rail), and wire
`InfoFab.razor`'s switch arm (`CityNames.Rtd => "RtdOverlay"`).

### 8. Build again
`dotnet build` — expect success with no new warnings.

## Verification (9 checks)

1. **Static feed reachable**: `GET https://www.rtd-denver.com/files/gtfs/google_transit.zip` →
   follows the 308 redirect automatically → 200, a valid flat zip with `trips.txt`/`shapes.txt`/
   `routes.txt` at the root (confirms no nested-zip handling is needed, unlike SEPTA).
2. **RT feed reachable**: `GET https://open-data.rtd-denver.com/files/gtfs-rt/rtd/VehiclePosition.pb`
   → 200, decodes as GTFS-RT protobuf (`FeedMessage`) with vehicle entities.
3. **Shapes load**: after WebAPI startup, the route-shapes endpoint returns RTD routes (expect
   ~125 routes, 125 with shapes, per the compat report).
4. **Live vehicles snap**: with the Worker running, RTD buses appear on the map at `#rtd` and move
   over a few poll cycles on real Denver streets.
5. **Rail remap works**: light rail and commuter rail vehicles (reporting `101C`/`101E`/`101T`/
   `103W`/`107R`/`113B`/`113G`/`117N`) render on their correct static route (`C`/`E`/`T`/`W`/`R`/
   `B`/`G`/`N`) rather than as unknown; a vehicle reporting `A` also resolves correctly with no
   remap applied. Expect all 8 rail lines represented across a few poll cycles (54 of 357 vehicles
   were rail in the compat snapshot).
6. **Route match rate**: per-cycle counters show high match rates once the rail remap is applied
   (compat report: 89.2% verbatim + 8 remapped rail IDs); `skippedUnknownRoute` should be small,
   accounted for almost entirely by the known `BOND`/`FREE` residual (FR-008 — not a regression).
7. **Picker works**: the city FAB lists Denver; selecting it sets `#rtd`, reloads, and shows
   Denver. The Denver button is disabled while active. Audio overlay and info panel show
   RTD-specific copy, not another city's text.
8. **No regression**: Atlanta / Boston / New York / Washington DC / Toronto / Philadelphia each
   still load and behave exactly as before — in particular, confirm WMATA's existing
   `RailRouteIdMap` entries still resolve correctly (this feature doesn't touch shared matching
   code, only adds a second city's config data).
9. **Config parity**: Worker and WebAPI `rtd` `Cities:` entries are byte-identical (diff the two
   JSON blocks) — divergence here is the most common config-only onboarding mistake.

## Rollback

Remove the additions (constant, two `Cities:` entries, picker button, overlay copy, map origin).
Nothing else depends on RTD's presence — the `RailRouteIdMap` mechanism itself predates this
feature (WMATA) and is unaffected by removing RTD's entry.

## Operational follow-ups (not code, tracked separately)

- **`BOND`/`FREE` bus route-ID gap** — decide whether a fourth generic `RouteIdNormalization`
  transform or a small per-city ad-hoc dictionary is worth adding later; both routes currently
  fall into "unknown route" handling, a small (2-of-93) residual, not a blocking defect (FR-008).
- **CityFab localization** — migrate all seven inline city labels to `RouteFilterResources.resx`
  (Principle XII, carried over from 043's/048's tracked follow-up).
