# Implementation Plan: Multi-City Transit Targets

**Branch**: `031-multi-city-transit` | **Date**: 2026-06-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/031-multi-city-transit/spec.md`
**Design source**: [`docs/MULTI_CITY_TRANSIT_DESIGN.md`](../../docs/MULTI_CITY_TRANSIT_DESIGN.md) (authoritative decision record, Q1–Q8)

## Summary

Extend TransitJazz from a single hardcoded agency (MARTA/Atlanta) to N transit cities. The
anti-drift mechanism is an `ITransitCity` strategy interface — one implementation per city —
that the worker loop iterates **without ever branching on a city name**. Standard-GTFS-RT cities
(e.g. WMATA) are added with **config only** via a generic `GtfsRtCity`; bespoke feeds (e.g.
MARTA's JSON rail API) get **one isolated named class** (`MartaCity`). The pair `(city, routeId)`
becomes the universal key everywhere (worker index, server KV store, client fetch). Per-city
fan-out uses **SignalR Groups** (`Clients.Group(city)`), the city travels as a **method parameter**
on `PublishBatch`, and the per-vehicle `LastBatchCache` is **keyed by city**. The client reads its
city from the **URL/query param**, defaulting to `marta`. Telemetry stays MARTA-only via a declared
`EmitsTelemetry` capability flag (never a city-name check). Deployment stays **one worker process /
one Azure Container App** (Q3); per-city container isolation is a deferred, reversible `.Where(name
== CITY)` filter. Implementation lands in two pure slices: (1) refactor MARTA onto the pattern with
zero behavior change; (2) add WMATA as config only.

## Technical Context

**Language/Version**: C# / .NET 10.0 (all projects)
**Primary Dependencies**: ASP.NET Core (Minimal API + SignalR), Blazor WebAssembly, MatBlazor,
SignalR client, protobuf-net (GTFS-RT), MapLibre GL JS / MapTiler (client), Parquet.Net +
DefaultAzureCredential (telemetry sidecar, unchanged)
**Storage**: In-memory `IKeyValueRepository<string>` (route shapes, keyed `{city}:{routeId}`);
in-memory per-city vehicle caches; Azure Blob for MARTA-only telemetry (unchanged)
**Testing**: xUnit (existing `*.Tests` projects: `Server.WebAPI.Tests`,
`Server.TransitDataWorker.Tests`)
**Target Platform**: Blazor WASM (Azure Static Web App) + ASP.NET Core WebAPI + .NET Worker
(Azure Container App), per constitution Principle I
**Project Type**: Web application — decoupled frontend (WASM) + backend (WebAPI) + background worker
**Performance Goals**: Per-city work is I/O-bound HTTP every 10 s (small protobuf payloads,
sequential awaits). One process handles a dozen cities trivially. New joiner sees current vehicles
within seconds (cache replay on `JoinCity`, FR-012/SC-007).
**Constraints**: No new deployed infrastructure per city (FR-016/SC-006); no city access key in
committed config (FR-014/SC-008/Principle II); MARTA behavior byte-identical post-refactor
(FR-017/SC-004); shared pipeline must not branch on city name (FR-008, Principle anti-drift §3).
**Scale/Scope**: 2 cities at delivery (MARTA + WMATA); design supports an arbitrary configured set.
Touches Shared (2 files), Worker (new `Cities/` folder + loop), WebAPI (3 SignalR files +
GtfsStaticLoader + GtfsEndpoints), Client (SignalR service + shape fetch + RouteFilterViewModel),
worker config.

**Note on namespaces**: the design doc uses the logical product name `TransitJazz`; the actual
source root namespace is `ChefKnifeStudios.MartaJazz` under `src/`. All file references below use the
real paths.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Impact | Status |
|---|---|---|
| **I. Decoupled Cloud Architecture** | One worker process iterates cities; three deployable units unchanged. | ✅ PASS |
| **II. No Frontend Secrets** | WMATA `api_key` is a Container Apps secret referenced by env-var name, never in `appsettings.json`. Client gains only a city string (not a secret). | ✅ PASS |
| **III. Two-Pass Real-Time Pipeline** | V2 spatial reconciliation logic is unchanged; it is parameterized by a per-city index. Vehicle state cache becomes per-city. | ✅ PASS |
| **IV. OpenTelemetry Observability** | Per-city try/catch logs `{City}` on failure; existing structured logging retained. | ✅ PASS |
| **V. Azure DevOps CI/CD** | No new artifacts; same WASM + Docker image. | ✅ PASS |
| **VI. GTFS ID Mapping** | `(city, routeId)` generalizes the `route_short_name` join; WMATA `RailRouteIdMap` is config-declared, not branched. | ✅ PASS |
| **VII. No re-fetch of static data** | Client fetches per-city shapes once at init; layers re-added after style swaps unchanged. | ✅ PASS |
| **VIII. Generative Music** | Unchanged — deterministic per-route tone assignment is keyed by routeId as today (per joined city). | ✅ PASS |
| **IX. Persistent Multi-Selection** | Unchanged — selection set now scoped to the joined city's routes. | ✅ PASS |
| **X / XI. Controls / Overlays** | Unchanged. | ✅ PASS |
| **XII. i18n** | City names are stable lowercase keys, not display strings; any UI label goes through `IStringLocalizer<RouteFilterResources>` (EN-only this pass, consistent with 015–017). | ✅ PASS |

**No violations.** Complexity Tracking section omitted (nothing to justify). The `ITransitCity`
interface has two implementations (`GtfsRtCity`, `MartaCity`) on day one, so it is not a
single-implementation abstraction — it is the constitution-aligned anti-drift mechanism.

## Project Structure

### Documentation (this feature)

```text
specs/031-multi-city-transit/
├── plan.md              # This file
├── research.md          # Phase 0 — Q1–Q8 decisions consolidated
├── data-model.md        # Phase 1 — entities & keying
├── quickstart.md        # Phase 1 — add-a-city walkthrough + verification
├── contracts/           # Phase 1 — interface/config/transport contracts
│   ├── itransitcity.md
│   ├── city-config.md
│   ├── signalr-transport.md
│   └── shapes-endpoint.md
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.MartaJazz.Shared/
│   ├── GtfsData/RouteShapeFeature.cs              # ADD: string? City to RouteShapeProperties
│   └── ITransitHubPublisher.cs                    # CHANGE: PublishBatchAsync(string city, …)
│
├── Server/
│   ├── ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/
│   │   ├── Cities/                                # NEW folder
│   │   │   ├── ITransitCity.cs                    # NEW: strategy interface (§3)
│   │   │   ├── GtfsRtCity.cs                      # NEW: generic config-driven impl
│   │   │   ├── MartaCity.cs                       # NEW: bespoke impl (bus + JSON rail)
│   │   │   └── CityConfig.cs                      # NEW: Cities: binding model
│   │   ├── Worker.cs                              # CHANGE: loop IEnumerable<ITransitCity>; per-city index/cache; per-city try/catch; telemetry gated by EmitsTelemetry; retire _gtfsRtUrl + global rail adapter
│   │   ├── SignalRHubPublisher.cs                 # CHANGE: forward city to InvokeAsync("PublishBatch", city, batch)
│   │   ├── RailRealtime/RailRealtimeAdapter.cs    # UNCHANGED class; DI registration retired, composed into MartaCity
│   │   └── Program.cs                             # CHANGE: bind Cities:; register named impls else GtfsRtCity
│   │
│   └── ChefKnifeStudios.MartaJazz.Server.WebAPI/
│       ├── SignalR/TransitHub.cs                  # CHANGE: add JoinCity(string); on join replay cache.Current(city)
│       ├── SignalR/WorkerTransitHub.cs            # CHANGE: PublishBatch(city, batch) → cache per city → Clients.Group(city)
│       ├── SignalR/ILastBatchCache.cs             # CHANGE: key cache by city — Set(city, batch)/Current(city)
│       ├── GtfsStatic/GtfsStaticLoader.cs         # CHANGE: loop city registry; multi-zip per city; seed {city}:{routeId}; set City on shapes
│       └── EndpointGroups/GtfsEndpoints.cs        # CHANGE: /gtfs/routes/shapes accepts ?city=; keys {city}:{routeId}
│
└── Client/
    ├── ChefKnifeStudios.MartaJazz.Client.Core/Services/SignalRNotificationService.cs  # CHANGE: read city from URL; JoinCity(city) after connect
    └── ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs    # CHANGE: pass ?city=; consume RouteShapeProperties.City

# Config
src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings*.json       # CHANGE: flat Marta: → Cities: array; WMATA key via CA secret/env var
```

**Structure Decision**: Web application (constitution Principle I). No new projects. The only new
*directory* is `Cities/` inside the existing TransitDataWorker. Everything else is edits to existing
files. This keeps the diff inside the established 11-project structure.

## Implementation Order (from design §8)

1. **Slice 1 — Pure refactor on MARTA, no behavior change** (covers spec US1 + US3): introduce
   `ITransitCity` + `MartaCity` (wrapping today's bus URL + JSON rail adapter), loop-over-one-city,
   per-city index/cache, `city` param threaded publisher → hub → client `JoinCity`. Ship and verify
   MARTA is byte-identical end-to-end (FR-017/SC-004). This proves the pattern with one city's
   variables and is the constitution-mandated MARTA-unchanged gate.
2. **Slice 2 — Add WMATA as config only** (covers spec US2): `Cities:` entry + `GtfsRtCity` +
   `RailRouteIdMap` + CA secret + multi-zip in `GtfsStaticLoader`. Proves zero-new-processing-code
   (SC-002).

The slices map cleanly onto the spec's prioritized user stories, so `/speckit-tasks` can order P1/P3
(Slice 1) before P2 (Slice 2).
