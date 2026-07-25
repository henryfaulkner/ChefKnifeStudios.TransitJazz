# Implementation Plan: NYC MTA Bus Support

**Branch**: `041-nymta-bus-support` (authored on `040-nymta-subway-interpolation` per user instruction — no branch switch) | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/041-nymta-bus-support/spec.md`

## Summary

Add NYC MTA **bus** as a live-vehicle city. Unlike the bespoke `NymtaCity` subway adapter (feature 040, which *synthesizes* positions), bus positions are real GPS in the ordinary GTFS-RT protobuf shape, so this reuses the existing config-driven `GtfsRtCity` — the same class already running WMATA and MBTA — with **zero** `Program.cs` change (`nymta-bus` falls into the existing `else` arm of the city-registry factory).

The only genuinely new code is a small, pure, unit-testable **`RouteIdNormalizer`** — an ordered pipeline of named string-transform steps (`uppercase` → `plusToSbs` → `stripLeadingZeros`) applied to each RT `Trip.RouteId` before the merged feed leaves `GtfsRtCity`, so live route IDs line up with the static route registry. Everything else is config: two new `Cities:` entries (Worker + WebAPI `appsettings.json`), a `CityNames.NymtaBus` constant, and a second city-picker button. Telemetry is `true` (real GPS, like every other bus city). Subway (`nymta`) is untouched.

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: `protobuf-net` (GTFS-RT decode, existing), ASP.NET Core config binding, xUnit (existing `TransitDataWorker.Tests`)
**Storage**: N/A (in-memory route index / KV store, unchanged)
**Testing**: xUnit — new `RouteIdNormalizerTests` in `ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests` (pure unit tests, no HTTP/host)
**Target Platform**: Worker = Linux container (ACR image); WebAPI = Azure Container App; Client = Blazor WASM
**Project Type**: Web (decoupled Worker + WebAPI + Blazor WASM), per constitution Principle I
**Performance Goals**: Unchanged — normalization is O(route-ID length) per vehicle per tick, negligible vs. existing per-tick snap work
**Constraints**: Additive only — no behavior change for `marta`/`wmata`/`mbta`/`nymta`; normalization must never throw (bad config degrades match rate, never crashes a tick)
**Scale/Scope**: ~266 NYC bus route IDs, one citywide RT feed, 6 static zips; one new ~40-line class + config + one picker button

**On-disk naming note**: the solution's root namespace/folders are `ChefKnifeStudios.MartaJazz.*` (not `TransitJazz`). All new files follow the existing `MartaJazz` namespace convention. The `TransitJazz` name appears only in the repo path and product-facing docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status |
|-----------|-----------|--------|
| I. Decoupled Cloud Architecture | Worker fetches RT + WebAPI serves static; unchanged seam | ✅ No new deployable, no architecture change |
| III. Two-Pass Pipeline | Normalization runs inside `GtfsRtCity.FetchVehiclesAsync` before `Worker.cs` sees entities; V1/V2 passes unchanged | ✅ Pass |
| VI. GTFS ID Mapping / `RouteJoinKey` | Normalization operates on the **GTFS-RT wire value** (`Trip.RouteId`) — exactly like the existing `RailRouteIdMap`, which the constitution already sanctions as operating "on the GTFS-RT wire value, not `RouteJoinKey`". `RouteIdNormalizer` is the transform-pipeline sibling of that static map. | ✅ Pass — consistent with VI |
| IV. OpenTelemetry / structured logging | Reuse existing per-cycle counters (`skippedUnknownRoute`); no new log surface required | ✅ Pass |
| V. GitHub Actions CI/CD | No pipeline change; Worker image + WASM artifacts unchanged | ✅ Pass |
| VII. OSM cartography / GeoJSON layers | Bus renders through the same route/vehicle GeoJSON layers as every city; no basemap change | ✅ Pass |
| XII. Internationalized presentation | New picker label ("New York Buses") MUST come from `RouteFilterResources.resx` via `IStringLocalizer`, **no inline copy** | ⚠️ Gate: label must be a resx key, not a hardcoded `Label="New York Buses"` |
| XIII. Dark-Mode Parity | Second `CityFab` button reuses the existing button styling — no new color-bearing CSS | ✅ Pass (no new CSS) |

**Localization caveat (Principle XII):** the *existing* `CityFab.razor` currently hardcodes `Label="Atlanta, GA"` etc. inline (pre-existing debt, not introduced here). Strict reading of XII requires the **new** label to be a resx string. Plan: add `CityNymtaBus` (and, to avoid worsening debt asymmetrically, optionally the four existing city labels) to `RouteFilterResources.resx` and bind via `IStringLocalizer`. Minimum-compliant scope = the one new label; the existing inline labels are noted as pre-existing debt and are out of this feature's required scope. See research.md R5.

**Result: PASS** (one localization gate, handled in tasks). No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/041-nymta-bus-support/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── route-id-normalizer.md   # Apply() step contract + accept vectors
│   └── city-config.md           # nymta-bus Cities: entry + RouteIdNormalization field
└── checklists/
    └── requirements.md  # Spec quality checklist (from /speckit-specify)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.MartaJazz.Shared/
│   └── CityNames.cs                                      # + NymtaBus = "nymta-bus"
│
├── Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/
│   ├── Cities/
│   │   ├── CityConfig.cs                                 # + string[] RouteIdNormalization = []
│   │   ├── GtfsRtCity.cs                                 # + ApplyRouteIdNormalization(merged) call + method
│   │   └── RouteIdNormalizer.cs                          # NEW — pure static class
│   ├── Program.cs                                        # UNCHANGED (nymta-bus hits existing else arm)
│   └── appsettings.json                                  # + nymta-bus Cities: entry
│
├── Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/
│   └── RouteIdNormalizerTests.cs                         # NEW — xUnit [Theory] accept vectors
│
├── Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/
│   └── appsettings.json                                  # + nymta-bus Cities: entry (static-zip loader parity)
│
└── Client/ChefKnifeStudios.MartaJazz.Client.Shared/
    ├── Components/FABs/CityFab.razor                     # + "New York Buses" button (#nymta-bus)
    └── Resources/RouteFilterResources.resx               # + CityNymtaBus label key
```

**Structure Decision**: Reuse the existing decoupled Worker/WebAPI/Client layout (constitution Principle I). No new project, no new deployable. The one new source file (`RouteIdNormalizer.cs`) and its test live in the existing Worker + Worker.Tests projects.

## Complexity Tracking

*No constitution violations requiring justification.* The feature is deliberately the cheapest city-onboarding path in the project (config + one pure class); the only new capability, `RouteIdNormalizer`, exists solely to close the route-ID mismatch that `RailRouteIdMap`'s static dictionary structurally cannot express (regex-shaped transforms vs. a fixed pair lookup).
