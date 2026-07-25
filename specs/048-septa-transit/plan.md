# Implementation Plan: SEPTA Philadelphia Transit City

**Branch**: `048-septa-transit` | **Date**: 2026-07-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/048-septa-transit/spec.md` + compatibility report `docs/city-compat/septa.md`

## Summary

Add Philadelphia **SEPTA** as a live-vehicle city. This is a **two-part fork**, not a single
pattern — stated explicitly because it differs from every prior config-only city (WMATA, MBTA,
TTC) and from the two bespoke-adapter cities (MARTA, NYMTA):

1. **Live-vehicle path — pure config.** SEPTA's buses, trackless trolleys, streetcars, and the
   Norristown High Speed Line (`route_type=1`, `route_id=M1`) all ride a single keyless GTFS-RT
   feed with 100% route_id/lat/lon coverage and a verbatim `route_id == route_short_name` match.
   `septa` falls into the existing `else` arm of the Worker's city-registry factory
   (`Program.cs`) and is served by the config-driven `GtfsRtCity` — **zero new classes**, same as
   TTC.
2. **Static-GTFS path — one new, narrowly-scoped capability.** SEPTA's static zip
   (`gtfs_public.zip`) is a **zip-of-zips**: the top-level download contains `google_bus.zip`
   (the bus/trolley/streetcar/NHSL data this feature needs) and `google_rail.zip` (Regional Rail,
   out of scope). `GtfsStaticLoader.cs` currently opens every configured `StaticZipUrls` entry as
   a single flat `ZipArchive` and looks for `trips.txt`/`shapes.txt`/`routes.txt` at the archive
   root — it has no concept of a nested zip. This plan adds a small, additive
   detect-and-unwrap step to that one loader method: if a downloaded zip's entries don't contain
   the expected GTFS text files but do contain a `.zip` entry, extract and process that nested
   entry as the effective GTFS zip. Every existing flat-zip city's behavior is byte-for-byte
   unchanged (the new step is a no-op whenever `trips.txt` etc. are found at the top level, which
   is true for every city configured today).

This is a **smaller, narrower fork than MARTA** (which merges a separate rail-realtime API) or
**NYMTA** (which synthesizes positions and merges multiple live feeds): no new `ICity`
implementation, no new adapter class, no new background service — just one self-contained
extraction-step change inside the existing static loader's zip-opening logic, plus the same four
config-only registration touch-points every city gets.

The Broad Street Subway / Market-Frankford Line (`B1`/`B2`/`B3`/`L1`) ride this same feed/ID
scheme but showed zero live vehicles in the compatibility report. Per the report's own
recommendation this is treated as a known, accepted open question — **no bespoke rail adapter is
built for them**; they flow through the same generic path NHSL (`M1`) already uses and will
appear automatically if SEPTA ever emits live positions under those IDs.

## Technical Context

**Language/Version**: C# / .NET 10.0 (config + one loader method change + one client `.razor`
handler + one Shared constant)
**Primary Dependencies**: `System.IO.Compression.ZipArchive` (existing, already used by
`GtfsStaticLoader`), `protobuf-net` (GTFS-RT decode, existing), ASP.NET Core config binding
(existing) — **no new dependency**
**Storage**: N/A (in-memory route index / KV store, unchanged)
**Testing**: Existing `TransitDataWorker.Tests` / WebAPI test project (xUnit). The nested-zip
detection/extraction logic in `GtfsStaticLoader` is genuinely new code and IS unit-testable
(unlike TTC's pure-config change) — new tests cover: flat zip unchanged, nested zip unwrapped,
nested zip with no matching inner archive falls back gracefully. Live-vehicle registration
remains config-only, verified via quickstart (feed reachability + live-vehicle smoke test).
**Target Platform**: Worker = Linux container (ACR image); WebAPI = Azure Container App; Client =
Blazor WASM (Azure Static Web App)
**Project Type**: Web (decoupled Worker + WebAPI + Blazor WASM), per constitution Principle I
**Performance Goals**: Unchanged for the live path — one additional keyless surface feed (~33 KB
/poll, 448 route-attributed vehicles). The static path does one extra in-memory zip-extraction
step during the existing 24-hour refresh cycle for SEPTA only — negligible, bounded by a single
city's zip size, and off the live polling hot path entirely.
**Constraints**: Additive only — no behavior change for `marta`/`wmata`/`mbta`/`nymta`/`ttc`.
Both SEPTA feeds keyless. Regional Rail (`google_rail.zip`, `route_type=2`) is out of scope and
MUST NOT be loaded. No bespoke rail adapter for `B1`/`B2`/`B3`/`L1`.
**Scale/Scope**: 147 static routes (145 with shapes), 118 distinct RT route IDs, 448 live
route-attributed vehicles. Change = 4 config/constant/UI touch-points (same as TTC) + 1 new
additive method-level change in `GtfsStaticLoader.cs` + its unit tests.

**On-disk naming note**: the solution's root namespace/folders are `ChefKnifeStudios.MartaJazz.*`
(not `TransitJazz`). All references below use the `MartaJazz` convention. The `TransitJazz` name
appears only in the repo path and product-facing docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status |
|-----------|-----------|--------|
| I. Decoupled Cloud Architecture | Worker fetches RT + WebAPI serves static; unchanged seam, no new deployable | ✅ Pass |
| III. Two-Pass Pipeline | `septa` runs through the existing `GtfsRtCity` → `Worker.cs` V1/V2 passes unchanged | ✅ Pass |
| VI. GTFS ID Mapping / `RouteJoinKey` | RT `route_id` == static `route_short_name` verbatim → no `RailRouteIdMap`, no `RouteIdNormalization`. Empty transform config. | ✅ Pass — zero transform |
| IV. OpenTelemetry / structured logging | Reuse existing per-cycle counters; the new nested-zip extraction step logs a warning (not silent failure) when it can't find GTFS files at either level, consistent with existing per-zip try/catch logging | ✅ Pass |
| V. GitHub Actions CI/CD | No pipeline change; Worker image + WASM artifacts unchanged | ✅ Pass |
| VII. OSM cartography / GeoJSON layers | SEPTA renders through the same route/vehicle GeoJSON layers as every city; no basemap change | ✅ Pass |
| XII. Internationalized presentation | New picker entry for Philadelphia. Same pre-existing `CityFab.razor` inline-label debt as TTC (043) — not this feature's job to fix | ⚠️ See caveat below (mirrors 043) |
| XIII. Dark-Mode Parity | New `CityFab` menu button reuses existing button styling — no new color-bearing CSS | ✅ Pass (no new CSS) |

**Localization caveat (Principle XII):** Identical situation and identical resolution to
043-toronto-ttc-transit — `CityFab.razor` hardcodes every city label inline. Default plan =
matching inline label (`"Philadelphia, PA"`) consistent with the five existing buttons, no new
localization debt asymmetry introduced. A whole-component resx migration remains a separate,
tracked cleanup outside this feature's scope.

**New-code caveat (unique to this feature, not present in 043):** unlike every prior config-only
city, this feature DOES introduce new production code — the nested-zip detection/extraction step
in `GtfsStaticLoader.BuildCityShapeSetAsync`. This is scoped as tightly as possible (one method,
additive-only, unit tested) precisely so it doesn't compromise the "config-only city" pattern's
simplicity for the *next* config-only city — SEPTA is the exception that proves the rule, not a
precedent for scope creep in ordinary onboardings.

**Result: PASS** (one localization judgment call inherited from 043, one narrowly-scoped and
justified new-code addition documented above). See Complexity Tracking below for the formal
justification entry.

## Project Structure

### Documentation (this feature)

```text
specs/048-septa-transit/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   ├── city-config.md            # septa Cities: entry (Worker + WebAPI) — the config contract
│   ├── city-picker.md            # CityFab button + hash handler contract
│   └── nested-zip-extraction.md  # GtfsStaticLoader detect/unwrap contract — accept/reject vectors
└── checklists/
    └── requirements.md  # Spec quality checklist (from /speckit-specify)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.MartaJazz.Shared/
│   └── CityNames.cs                                      # + Septa = "septa"
│
├── Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/
│   ├── Program.cs                                        # UNCHANGED (septa hits existing else arm → GtfsRtCity)
│   └── appsettings.json                                  # + septa Cities: entry (keyless, no rail, no normalization, telemetry true)
│
├── Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/
│   ├── appsettings.json                                  # + septa Cities: entry (static-zip loader parity)
│   └── GtfsStatic/GtfsStaticLoader.cs                     # + nested-zip detect/unwrap step in BuildCityShapeSetAsync (additive, city-agnostic — NOT a septa-specific branch)
│
└── Client/ChefKnifeStudios.MartaJazz.Client.Shared/
    ├── Components/FABs/CityFab.razor                     # + "Philadelphia, PA" menu button + HandleSeptaClicked (#septa) handler
    ├── Components/FABs/InfoFab.razor                      # + SeptaOverlay switch arm
    ├── Components/AudioUnlockOverlay.razor                 # + SeptaAudioOverlay switch arm
    └── Resources/RouteFilterResources.resx                 # + Septa* overlay/info copy keys

src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs   # + _cityCenter[CityNames.Septa] entry (Philadelphia's Center City core)

tests/
└── Server.WebAPI.Tests (or equivalent existing test project)/
    └── GtfsStaticLoaderTests.cs                            # + nested-zip unit tests (flat unchanged, nested unwrapped, nested-missing-fallback)
```

**Structure Decision**: Web application (decoupled Worker + WebAPI + Blazor WASM), matching the
project's existing structure. The `GtfsStaticLoader.cs` change is implemented as a
**city-agnostic** capability (detect nested zip → unwrap → process), not a `if (city.Name ==
"septa")` branch — consistent with constitution Principle VI/III's insistence that per-city
behavior be data-driven where possible, and because a future city could plausibly ship the same
zip-of-zips packaging.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|---------------------------------------|
| New production code in `GtfsStaticLoader.cs` (nested-zip unwrap), breaking the "config-only city" no-new-code pattern established by WMATA/MBTA/TTC | SEPTA's only public static GTFS download is genuinely packaged as a zip-of-zips (confirmed in `docs/city-compat/septa.md`) — there is no alternate flat-zip URL for the bus/trolley/streetcar/NHSL data | Rejected: (1) skipping static data entirely would leave live vehicles with no route shape/color context, failing User Story 1; (2) a SEPTA-specific branch/adapter class was rejected in favor of a generic detect-and-unwrap step, since it costs no more code and stays reusable for any future zip-of-zips city rather than adding a second special case |
