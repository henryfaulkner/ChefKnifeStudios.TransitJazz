# Implementation Plan: RTD Denver Transit City

**Branch**: `050-rtd-transit` | **Date**: 2026-07-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/050-rtd-transit/spec.md` + compatibility report `docs/city-compat/rtd.md`

## Summary

Add Denver **RTD** as a live-vehicle city. This is a **single-part fork, pure config-only** —
simpler than SEPTA (which needed a new nested-zip-extraction capability) and structurally
identical to WMATA, MBTA, and TTC:

RTD's buses, light rail (`route_type=0`), and commuter rail (`route_type=2`) all ride a single
keyless GTFS-RT feed with 100% route_id/lat/lon coverage. `rtd` falls into the existing `else`
arm of the Worker's city-registry factory (`Program.cs`) and is served by the config-driven
`GtfsRtCity` — **zero new classes, zero new code**.

Bus route IDs match statically at 89.2% verbatim (83/93). Of the 10 unmatched IDs, 8 are rail
lines using a numeric-prefix scheme (`101C`, `101E`, `101T`, `103W`, `107R`, `113B`, `113G`,
`117N`) that don't match static's plain line letters (`C`, `E`, `T`, `W`, `R`, `B`, `G`, `N`).
This is resolved entirely by `CityConfig.RailRouteIdMap` — an existing mechanism introduced for
WMATA (Metro's `BLUE`/`BLUE0` → `B` style remaps) and, until now, used by exactly one city. RTD's
onboarding is the first proof that this config-only remap generalizes to a second, independent
agency with **no code changes** — an 8-entry dictionary in config is the entire "new capability"
this feature exercises.

The remaining 2 unmatched bus IDs (`BOND`, `FREE`) are an explicit non-goal, per the spec's edge
cases — they fall into the platform's existing "unknown route" handling, same as any unmatched
route on any city today.

The static GTFS zip is a normal flat zip (its download URL 308-redirects to another endpoint,
which `HttpClient` follows by default) — unlike SEPTA's zip-of-zips, `GtfsStaticLoader.cs` needs
**no changes**.

This is the simplest onboarding shape in the codebase: 4 config/constant/UI touch-points, no new
production code, no new tests beyond the standard live-verification quickstart.

## Technical Context

**Language/Version**: C# / .NET 10.0 (config + one client `.razor` handler + one Shared constant
— no new methods, no new classes)
**Primary Dependencies**: `protobuf-net` (GTFS-RT decode, existing), ASP.NET Core config binding
(existing) — **no new dependency**
**Storage**: N/A (in-memory route index / KV store, unchanged)
**Testing**: No new unit tests required — `RailRouteIdMap` and `GtfsRtCity` are already
unit-covered from the WMATA onboarding; this feature only adds a second config consumer of an
existing, already-tested mechanism. Verification is via `quickstart.md` (feed reachability, live
vehicle rendering across poll cycles, rail-remap correctness, route-match counters, picker/audio
overlay checks, existing-city regression pass).
**Target Platform**: Worker = Linux container (ACR image); WebAPI = Azure Container App; Client =
Blazor WASM (Azure Static Web App)
**Project Type**: Web (decoupled Worker + WebAPI + Blazor WASM), per constitution Principle I
**Performance Goals**: Unchanged. One additional keyless feed (~52.6 KB/poll, 357 route-attributed
vehicles, 54 of them rail). No new hot-path work — `ApplyRailRouteIdMap` already runs per-poll for
WMATA and is O(vehicle count) dictionary lookups.
**Constraints**: Additive only — no behavior change for `marta`/`wmata`/`mbta`/`nymta`/`ttc`/
`septa`. Both RTD feeds keyless. `BOND`/`FREE` bus IDs intentionally left unresolved (spec FR-008).
**Scale/Scope**: 125 static routes (125 with shapes), 93 distinct RT route IDs, 357 live
route-attributed vehicles (54 rail). Change = 4 config/constant/UI touch-points (same shape as
TTC/SEPTA's live-vehicle path), zero new production code, zero new tests.

**On-disk naming note**: the solution's root namespace/folders are `ChefKnifeStudios.TransitJazz.*`
(not `TransitJazz`). All references below use the `MartaJazz` convention. The `TransitJazz` name
appears only in the repo path and product-facing docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status |
|-----------|-----------|--------|
| I. Decoupled Cloud Architecture | Worker fetches RT + WebAPI serves static; unchanged seam, no new deployable | ✅ Pass |
| III. Two-Pass Pipeline | `rtd` runs through the existing `GtfsRtCity` → `Worker.cs` V1/V2 passes unchanged | ✅ Pass |
| VI. GTFS ID Mapping / `RouteJoinKey` | Bus IDs match verbatim at 89.2%; rail IDs resolved via existing `RailRouteIdMap` config (8 entries) — a second consumer of an already-built mechanism, no new transform code | ✅ Pass — config reuse, zero new code |
| IV. OpenTelemetry / structured logging | Reuse existing per-cycle counters; no new logging paths | ✅ Pass |
| V. GitHub Actions CI/CD | No pipeline change; Worker image + WASM artifacts unchanged | ✅ Pass |
| VII. OSM cartography / GeoJSON layers | RTD renders through the same route/vehicle GeoJSON layers as every city; no basemap change | ✅ Pass |
| XII. Internationalized presentation | New picker entry for Denver. Same pre-existing `CityFab.razor` inline-label debt as TTC (043) / SEPTA (048) — not this feature's job to fix | ⚠️ See caveat below (mirrors 043/048) |
| XIII. Dark-Mode Parity | New `CityFab` menu button reuses existing button styling — no new color-bearing CSS | ✅ Pass (no new CSS) |

**Localization caveat (Principle XII):** Identical situation and identical resolution to
043-toronto-ttc-transit and 048-septa-transit — `CityFab.razor` hardcodes every city label
inline. Default plan = matching inline label (`"Denver, CO"`) consistent with the six existing
buttons, no new localization debt asymmetry introduced. A whole-component resx migration remains
a separate, tracked cleanup outside this feature's scope.

**Result: PASS** — no new-code caveat needed this time (unlike SEPTA): this is a pure
config-and-copy change reusing two already-built, already-tested mechanisms (`GtfsRtCity`,
`RailRouteIdMap`). No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/050-rtd-transit/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/            # Phase 1 output
│   ├── city-config.md            # rtd Cities: entry (Worker + WebAPI), incl. RailRouteIdMap — the config contract
│   └── city-picker.md            # CityFab button + hash handler contract
└── checklists/
    └── requirements.md  # Spec quality checklist (from /speckit-specify)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.TransitJazz.Shared/
│   └── CityNames.cs                                      # + Rtd = "rtd"
│
├── Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
│   ├── Program.cs                                        # UNCHANGED (rtd hits existing else arm → GtfsRtCity)
│   └── appsettings.json                                  # + rtd Cities: entry (keyless, RailRouteIdMap, telemetry true)
│
├── Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
│   ├── appsettings.json                                  # + rtd Cities: entry (static-zip loader parity, byte-identical to Worker's)
│   └── GtfsStatic/GtfsStaticLoader.cs                    # UNCHANGED (RTD's zip is a normal flat zip behind a followed 308 redirect)
│
└── Client/ChefKnifeStudios.TransitJazz.Client.Shared/
    ├── Components/FABs/CityFab.razor                     # + "Denver, CO" menu button + HandleRtdClicked (#rtd) handler
    ├── Components/FABs/InfoFab.razor                      # + RtdOverlay switch arm
    ├── Components/AudioUnlockOverlay.razor                 # + RtdAudioOverlay switch arm
    └── Resources/RouteFilterResources.resx                 # + Rtd* overlay/info copy keys

src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs   # + _cityCenter[CityNames.Rtd] entry (Denver Union Station / downtown transit core)
```

**Structure Decision**: Web application (decoupled Worker + WebAPI + Blazor WASM), matching the
project's existing structure. No new source files or methods — every change is either JSON
configuration, a resx string table entry, or a `.razor` switch-arm/button addition following the
exact shape of the five prior config-only cities (WMATA, MBTA, TTC, SEPTA's live-vehicle half,
and now RTD).

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

*No entries — Constitution Check passed cleanly with no new-code violations.*
