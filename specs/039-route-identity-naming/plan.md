# Implementation Plan: Route Identity Naming Unification

**Branch**: `039-route-identity-naming` | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/039-route-identity-naming/spec.md`

## Summary

`RouteId`/`routeId` is currently overloaded to mean three different things across the stack: the true GTFS static `route_id` (WebAPI), GTFS-RT's `Trip.RouteId` wire value (which for MARTA/WMATA is short-name-shaped, not the static id), and an internal `RouteShortName ?? RouteId` composite join key reimplemented independently in four places (`Worker.cs`, `TransitMap.razor.cs`, `ApplicationViewModel.cs`, `RouteFilterViewModel.cs`). This plan renames every site that holds the composite/RT-sourced join-key value to a new name, **`RouteJoinKey`**, reserves `RouteId` exclusively for the true GTFS static id, and introduces one shared computation — `RouteShapeProperties.JoinKey` — that replaces the four independent `??` expressions. The GTFS-RT wire model's `Trip.RouteId` protobuf field keeps its name (it mirrors the external GTFS-RT spec field name, which really is called `route_id` on the wire); only code that *consumes* that wire value as a lookup key is renamed. This is a pure rename + de-duplication with zero behavior change (FR-006) — confirmed by cross-referencing every occurrence via a full-repo grep pass (see Phase 0).

## Technical Context

**Language/Version**: C# / .NET 10.0 (all four touched projects); vanilla JavaScript (Client.Shared `wwwroot/js/`)
**Primary Dependencies**: None new. Touches: `ChefKnifeStudios.TransitJazz.Shared` (records/DTOs), `Server.TransitDataWorker` (`Worker.cs`, `VehicleState.cs`, `Cities/GtfsRtCity.cs`), `Client.Shared` (`ViewModels/RouteFilterViewModel.cs`, `ViewModels/ApplicationViewModel.cs`, `Data/RouteBlurbStore.cs`, `wwwroot/js/map-interop.js`, `wwwroot/js/vehicle-animator.js`), `Client.WebApp` (`Pages/TransitMap.razor.cs`)
**Storage**: N/A — no schema, no persisted data carries these field names (feature 038 confirmed telemetry parquet has no route-identity columns)
**Testing**: xUnit (existing `TransitDataWorker.Tests` and any Client test projects) — rename identifiers in test code/assertions; no new test infrastructure
**Target Platform**: Existing three deployables (WASM static site, WebAPI, TransitDataWorker Docker image) — unchanged
**Project Type**: Existing multi-project .NET solution + Blazor WASM frontend with JS interop
**Performance Goals**: N/A — no behavior or hot-path change; this is identifier renaming
**Constraints**: FR-006 — zero behavior/join-semantics/wire-format change. FR-005 — WebAPI's true-`route_id` usage (`GtfsEndpoints`, `GtfsStaticLoader`'s `{city}:{route_id}` store) is explicitly untouched. The GTFS-RT protobuf model's `Trip.RouteId`/`EntitySelector.RouteId` field names are untouched (they mirror the external wire spec) — only consumers of that value are renamed.
**Scale/Scope**: ~20 call sites across 4 projects + 2 JS files + 3 docs (constitution Principle VI, `docs/MULTI_CITY_TRANSIT_DESIGN.md`, `docs/WMATA_GTFS_COMPATIBILITY.md`), enumerated exhaustively in `data-model.md`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Assessment |
|---|---|---|
| I. Decoupled Cloud Architecture | Partial | Touches all three deployable units (WASM, WebAPI-adjacent Shared types, Worker) but changes no deployment topology, no new unit. ✅ |
| III. Two-Pass Pipeline | Direct | Principle VI (subsumed by III's V2 Pass description) is the doc this feature's FR-007 updates. The V2 pass's *behavior* (snap-to-nearest-point, `RouteShortName ?? RouteId` fallback) is unchanged — only the internal key's name and its doc description change. ✅ |
| IV. OpenTelemetry Observability | ✅ | Structured log message templates that reference `RouteId` in a join-key sense (e.g. `Worker.cs:578` `{SkippedNoRouteId}`) get relabeled for consistency but no logging behavior changes. |
| V. GitHub Actions CI/CD | N/A | No pipeline change. |
| VI. GTFS ID Mapping | ✅ Directly amended | This feature's FR-007 requires rewriting Principle VI's prose to name the join-key concept consistently with the renamed code (see Phase 1 constitution redline below). The underlying rule (`route_short_name ?? route_id`, `RailRouteIdMap` remap) is NOT changed, only its terminology. |
| VII–XIII (UX/audio/map/i18n/dark-mode) | N/A | No user-facing behavior, timing, color, or copy changes. The MapLibre GeoJSON `routeId` property rename (Client-side) is an internal data-contract rename between C# and JS, not a UX change — riders see identical rendering. |

**No violations requiring Complexity Tracking.** This is a rename/de-dup refactor; Principle VI is amended in wording only (a PATCH-level constitution change per its own Amendment Procedure), not redefined in substance.

**Post-Phase-1 re-check**: Data model and contracts (below) confirm the rename is mechanical and 1:1 — no new abstractions introduced beyond the single `RouteShapeProperties.JoinKey` helper the spec already calls for. Still no violations.

## Project Structure

### Documentation (this feature)

```text
specs/039-route-identity-naming/
├── plan.md              # This file
├── research.md          # Phase 0 output — confirms exhaustive site inventory, resolves naming
├── data-model.md         # Phase 1 output — full before/after rename table by file
├── contracts/
│   └── route-join-key-contract.md   # RouteShapeProperties.JoinKey contract + GeoJSON property rename
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md              # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/ChefKnifeStudios.TransitJazz.Shared/
├── GtfsData/RouteShapeFeature.cs          # ADD: JoinKey computed property on RouteShapeProperties
├── Geospatial/RoutePoint.cs               # RENAME: RouteId → RouteJoinKey
└── Events/
    ├── RouteNearestPointBatchEvent.cs     # RENAME: RouteNearestPointRecord.RouteId → RouteJoinKey
    └── RouteCrossingBatchEvent.cs         # RENAME: RouteCrossingRecord.RouteId → RouteJoinKey

src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── Worker.cs                              # RENAME: _routeIndex/_routeMode/_routeCumDist/_routeTriggerPoints
│                                           #   keys, local `routeId` variables, BuildRouteIndex,
│                                           #   log message templates (skippedNoRouteId → skippedNoJoinKey);
│                                           #   REPLACE the `RouteShortName ?? RouteId` literal (line 211)
│                                           #   with a call to RouteShapeProperties.JoinKey
├── VehicleState.cs                        # RENAME: RouteId → RouteJoinKey (param + doc comment)
└── Cities/GtfsRtCity.cs                   # UNCHANGED — ApplyRailRouteIdMap still mutates the GTFS-RT
                                            #   wire model's Trip.RouteId in place (wire field name
                                            #   is out of scope); only downstream consumers are renamed

src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
├── ViewModels/
│   ├── RouteFilterViewModel.cs            # RENAME: RouteItem.RouteId → RouteJoinKey; SelectedRouteId(s)/
│   │                                       #   HoveredRouteId → SelectedRouteJoinKey(s)/HoveredRouteJoinKey;
│   │                                       #   REPLACE 3x inline `??` expressions with RouteShapeProperties.JoinKey
│   └── ApplicationViewModel.cs            # RENAME: _routeShapes cache key/log message
├── Data/RouteBlurbStore.cs                # RENAME: GetForRoute(string routeId) param → routeJoinKey
└── wwwroot/js/
    ├── map-interop.js                     # RENAME: GeoJSON/JS property `routeId` → `routeJoinKey`
    │                                       #   (_routeColorsByRouteId, _triggerPointFeatures keys,
    │                                       #   MapLibre `['get', 'routeId']` match expressions)
    └── vehicle-animator.js                # RENAME: state.routeId / rec.routeId → routeJoinKey

src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/
└── Pages/TransitMap.razor.cs              # RENAME: _routeShapeCache key/log templates;
                                            #   REPLACE the `??` expression (line 576) with
                                            #   RouteShapeProperties.JoinKey; CrossingEventDto.RouteId
                                            #   → RouteJoinKey; interop calls pass routeJoinKey

.specify/memory/constitution.md            # AMEND: Principle VI prose to use RouteJoinKey terminology
docs/MULTI_CITY_TRANSIT_DESIGN.md          # AMEND: terminology consistency
docs/WMATA_GTFS_COMPATIBILITY.md           # AMEND: terminology consistency

*.Tests projects                            # RENAME: any test code referencing the above identifiers
```

**Structure Decision**: No new projects, no new files except the plan's own docs. All work is renames + one new computed property (`RouteShapeProperties.JoinKey`) inside existing files, spanning `Shared`, `Server.TransitDataWorker`, `Client.Shared`, and `Client.WebApp`. The WebAPI project (`Server.WebAPI`) and its `GtfsStaticLoader`/`GtfsEndpoints` are explicitly NOT touched (FR-005) since their `RouteId` already means the true GTFS id.

## Complexity Tracking

> No Constitution Check violations — table intentionally empty. Net change is a naming clarification plus one new shared helper; no new projects, layers, or indirection beyond that single property.
