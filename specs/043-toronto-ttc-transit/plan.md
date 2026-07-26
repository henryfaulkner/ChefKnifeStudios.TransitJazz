# Implementation Plan: Toronto TTC Transit City

**Branch**: `043-toronto-ttc-transit` | **Date**: 2026-07-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/043-toronto-ttc-transit/spec.md` + compatibility report `docs/city-compat/ttc.md`

## Summary

Add Toronto **TTC** as a live-vehicle city. TTC's surface feed (buses + streetcars) is ordinary GTFS-RT protobuf with real GPS, and its `route_id` matches the static `route_short_name` **verbatim** (99.4% alignment, zero transform). So this is the **cheapest possible city onboarding in the project — pure configuration, no new code**: `ttc` falls into the existing `else` arm of the Worker's city-registry factory (`Program.cs`) and is served by the config-driven `GtfsRtCity`, the same class already running WMATA and MBTA.

The complete change set is: one `Cities:` entry in the Worker `appsettings.json`, a mirrored entry in the WebAPI `appsettings.json` (static-zip loader parity), a `CityNames.Ttc = "ttc"` constant, and one city-picker button (`HandleTtcClicked` hash handler) in `CityFab.razor`. Both feeds are **keyless** — no `ApiKeyEnvVar`, no secret to provision. There is **no live subway feed**, so `RailRealtime` is omitted and no `RailRealtimeAdapter` work exists. Telemetry is `true` (real GPS, like every bus city).

**One reality-vs-doc correction** (see research R1): the compat report says streetcars "ride the bus palette," but the as-built WebAPI `GtfsStaticLoader` classifies GTFS `route_type` 0/1/2 all as `TransitMode.Rail` (`GtfsStaticLoader.cs:326`), and that mode flows onto every `RouteNearestPointRecord` and into client rendering/audio. TTC's ~20 streetcar routes (`route_type=0`) therefore voice/render as **Rail** in v1. Per user decision, this is **accepted as-is** (keeps the feature config-only); dedicated streetcar (tram) voicing is a tracked follow-up.

## Technical Context

**Language/Version**: C# / .NET 10.0 (config + one client `.razor` handler + one Shared constant)
**Primary Dependencies**: `protobuf-net` (GTFS-RT decode, existing), ASP.NET Core config binding (existing) — **no new dependency**
**Storage**: N/A (in-memory route index / KV store, unchanged)
**Testing**: Existing `TransitDataWorker.Tests` (xUnit). No new unit-testable code is introduced (config-only). Verification is integration/manual per quickstart (feed reachability + live-vehicle smoke test). The 037 city-integration-test framework covers new-city registration if in use.
**Target Platform**: Worker = Linux container (ACR image); WebAPI = Azure Container App; Client = Blazor WASM (Azure Static Web App)
**Project Type**: Web (decoupled Worker + WebAPI + Blazor WASM), per constitution Principle I
**Performance Goals**: Unchanged — one additional keyless surface feed (~93 KB/poll, ~916 route-attributed vehicles) processed by the existing snap pipeline; denser than MARTA but well within existing per-tick budget
**Constraints**: Additive only — no behavior change for `marta`/`wmata`/`mbta`/`nymta`. Both TTC feeds keyless. The static URL contains a literal space that MUST be URL-encoded/quoted in config.
**Scale/Scope**: 233 static routes (225 with shapes), 165 distinct RT route IDs, ~916 live route-attributed surface vehicles. Change = 4 config/constant/UI touch-points, zero new classes.

**On-disk naming note**: the solution's root namespace/folders are `ChefKnifeStudios.TransitJazz.*` (not `TransitJazz`). All references below use the `MartaJazz` convention. The `TransitJazz` name appears only in the repo path and product-facing docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status |
|-----------|-----------|--------|
| I. Decoupled Cloud Architecture | Worker fetches RT + WebAPI serves static; unchanged seam, no new deployable | ✅ Pass |
| III. Two-Pass Pipeline | `ttc` runs through the existing `GtfsRtCity` → `Worker.cs` V1/V2 passes unchanged | ✅ Pass |
| VI. GTFS ID Mapping / `RouteJoinKey` | RT `route_id` == static `route_short_name` verbatim → no `RailRouteIdMap`, no `RouteIdNormalization`. The existing `RouteShapeProperties.JoinKey` (`route_short_name` fallback `route_id`) matches with **empty** transform config. | ✅ Pass — zero transform |
| IV. OpenTelemetry / structured logging | Reuse existing per-cycle counters (`skippedNoRouteId`, `skippedUnknownRoute`); no new log surface | ✅ Pass |
| V. GitHub Actions CI/CD | No pipeline change; Worker image + WASM artifacts unchanged. Note the 3-lane wire/deploy constraint is N/A here — no wire-format change. | ✅ Pass |
| VII. OSM cartography / GeoJSON layers | TTC renders through the same route/vehicle GeoJSON layers as every city; no basemap change | ✅ Pass |
| XII. Internationalized presentation | New picker entry for Toronto. **Gate:** the *existing* `CityFab.razor` hardcodes inline labels (`Label="Atlanta, GA"` etc.) — pre-existing debt. Strict XII wants a resx key. | ⚠️ See caveat below |
| XIII. Dark-Mode Parity | New `CityFab` menu button reuses existing button styling — no new color-bearing CSS | ✅ Pass (no new CSS) |

**Localization caveat (Principle XII):** `CityFab.razor` currently hardcodes every city label inline (`Label="Atlanta, GA"`, `"Boston, MA"`, …) — pre-existing debt, not introduced by this feature. The strict-compliant path is to add a `CityToronto` key to `RouteFilterResources.resx` and bind the new button via `IStringLocalizer`. However, doing so for **only** the new button creates an inconsistent half-localized component. Recommendation (tracked in tasks): add the new label as a **matching inline label** to stay consistent with the four existing buttons (minimum change, no new debt asymmetry), and note the whole-component resx migration as a separate cleanup — OR, if strictness is preferred, localize all five in one pass. Default plan = matching inline label + tracked cleanup note, because this feature is scoped to city-onboarding, not a `CityFab` localization refactor. See research R5.

**Result: PASS** (one localization judgment call, documented for tasks). No Complexity Tracking entries — zero new classes, zero new deployables.

## Project Structure

### Documentation (this feature)

```text
specs/043-toronto-ttc-transit/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── city-config.md            # ttc Cities: entry (Worker + WebAPI) — the config contract
│   └── city-picker.md            # CityFab button + hash handler contract
└── checklists/
    └── requirements.md  # Spec quality checklist (from /speckit-specify)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.TransitJazz.Shared/
│   └── CityNames.cs                                      # + Ttc = "ttc"
│
├── Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
│   ├── Program.cs                                        # UNCHANGED (ttc hits existing else arm → GtfsRtCity)
│   └── appsettings.json                                  # + ttc Cities: entry (keyless, no rail, no normalization, telemetry true)
│
├── Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
│   └── appsettings.json                                  # + ttc Cities: entry (static-zip loader parity)
│
└── Client/ChefKnifeStudios.TransitJazz.Client.Shared/
    └── Components/FABs/CityFab.razor                     # + "Toronto, ON" menu button + HandleTtcClicked (#ttc) handler
```

**Structure Decision**: Reuse the existing decoupled Worker/WebAPI/Client layout (constitution Principle I). No new project, no new deployable, **no new source class**. The only C# touch-points are one Shared constant and one client component handler; everything else is JSON config. This is the config-only end of the city-onboarding spectrum (even lighter than 041, which added a `RouteIdNormalizer` class — TTC needs none).

## Complexity Tracking

*No constitution violations requiring justification.* This feature is the minimal city-onboarding path: config + one constant + one picker button. No new capability is introduced. The only judgment call (streetcar `route_type=0` → Rail vs Bus) is resolved by **accepting the as-built classifier** rather than adding code, deliberately avoiding a change to the city-shared `GtfsStaticLoader` mode rule.
