# Implementation Plan: Last Lerp Event Cache

**Branch**: `019-lerp-event-cache` | **Date**: 2026-06-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/019-lerp-event-cache/spec.md`

## Summary

Eliminate the up-to-one-poll-interval (~10s) "blank of buses" window a client sees on fresh page load. The WebAPI already relays every published vehicle batch to clients through `WorkerTransitHub.PublishBatch` (relayed as `ReceiveBatch` via `IHubContext<TransitHub>`). We cache the most recent batch — the full `List<EventEnvelope>` payload — in an in-memory singleton at that relay point, and expose it through a new read-only GTFS-adjacent endpoint. The client calls that endpoint once during map load (in `TransitMap.OnInitializedAsync`, after the map is ready) and feeds the result straight into its existing `HandleVehicleBatchAsync`, so buses render immediately without waiting for the next SignalR push. The live SignalR channel is unchanged; the cache is purely additive.

## Technical Context

**Language/Version**: C# / .NET 10.0 (all projects)
**Primary Dependencies**: ASP.NET Core Minimal API, SignalR (`IHubContext<TransitHub>`), Blazor WebAssembly (client), Ardalis.Result (client service contract)
**Storage**: In-memory only — a single mutable reference to the latest `List<EventEnvelope>` snapshot held in a WebAPI singleton. No persistence; resets on restart (per spec).
**Testing**: xUnit (server). Existing test project: `ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests` — but it references only the Worker, not the WebAPI where this feature's code lives. This feature adds a **new** WebAPI test project (`ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests`, xUnit) carrying **pure unit tests** for the `LastBatchCache` invariants and the `WorkerTransitHub` write-path. No host is booted and no live MARTA feed is touched. Endpoint HTTP behavior and client load-path behavior are covered by the manual quickstart (integration tests are explicitly out of scope for this feature). See the "Testing Strategy" section below and `contracts/tests.md`.
**Target Platform**: WebAPI on Azure Container Apps (Linux); frontend WASM on Azure Static Web Apps
**Project Type**: Web (Blazor WASM frontend + ASP.NET Core WebAPI backend + Worker), per the constitution's three-deployable architecture
**Performance Goals**: Snapshot read served from memory with no upstream fetch (FR-007); endpoint adds negligible latency to map load. Buses visible within the load itself (SC-001).
**Constraints**: Read MUST yield one internally consistent batch during concurrent write (FR-008) — solved with a single atomic reference swap (`volatile` field / `Interlocked` or `Volatile.Write`). Snapshot shape MUST be byte-for-byte the same `EventEnvelope` contract the client already consumes (FR-009), serialized with the existing `JsonOptions.Get()` HTTP JSON config so polymorphic `ISignalREvent` payloads round-trip.
**Scale/Scope**: One cached object; one new endpoint; one new client method call. Batch size is bounded by the active MARTA vehicle fleet (low hundreds of records).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status |
|-----------|-----------|--------|
| I. Decoupled Cloud Architecture | Cache lives in the WebAPI (the SignalR-hosting deployable), at the relay it already owns. Client↔WebAPI over HTTPS, unchanged. No new deployable, no new cross-service call. | ✅ Pass |
| II. No Frontend Secrets | New read endpoint serves public map-motion data, same posture as existing GTFS read endpoints. No secret introduced. | ✅ Pass |
| III. Two-Pass Pipeline | Worker's V1/V2 passes unchanged. The cache observes the already-published V2 batch; it does not alter or re-run processing. | ✅ Pass |
| IV. OpenTelemetry Observability | New cache write and endpoint use the existing structured `ILogger`. | ✅ Pass |
| V. CI/CD Pipeline | No new artifact; changes ride the existing WASM + Worker/WebAPI image builds. | ✅ Pass |
| VI. GTFS ID Mapping | Cached records carry `RouteId` (route short name) exactly as published; no new correlation logic. | ✅ Pass |
| VII. OSM Cartography / no re-fetch | The client renders the snapshot through the same animator path as a live batch; data layers persist. Reading the snapshot triggers **no** upstream fetch (FR-007), honoring the no-refetch ethos. | ✅ Pass |
| VIII. Generative Music | No audio change. Crossing/held notes still fire from animation, now able to start a beat sooner. | ✅ Pass |
| IX–XI. Interaction model / overlays | No UI surface added; no filter, overlay, or control change. | ✅ Pass |
| XII. i18n / Settings | No user-facing copy added. No `.resx` change required. | ✅ Pass |

**Result**: PASS — no violations, no Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/019-lerp-event-cache/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── last-batch-endpoint.md
│   ├── batch-cache.md
│   └── tests.md
└── checklists/
    └── requirements.md  # from /speckit-specify
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.MartaJazz.Shared/
│   └── ApiEndpoints.cs                         # ADD: ApiEndpoints.Transit.GetLastBatch constant
│
├── Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/
│   ├── SignalR/
│   │   ├── ILastBatchCache.cs                  # NEW: interface + impl (atomic-swap singleton)
│   │   └── WorkerTransitHub.cs                 # EDIT: write cache before relaying
│   ├── EndpointGroups/
│   │   └── TransitEndpoints.cs                 # NEW: MapTransitEndpoints — GET last-batch
│   └── Program.cs                              # EDIT: register ILastBatchCache singleton; MapTransitEndpoints()
│
├── Client/
│   ├── ChefKnifeStudios.MartaJazz.Client.Core/
│   │   └── Services/EndpointsServices/
│   │       └── TransitEndpointsService.cs      # NEW: ITransitEndpointsService.GetLastBatch -> Result<IEnumerable<EventEnvelope>>
│   ├── ChefKnifeStudios.MartaJazz.Client.WebApp/
│   │   ├── Program.cs                          # EDIT: register ITransitEndpointsService
│   │   └── Pages/TransitMap.razor.cs           # EDIT: fetch snapshot on load, feed HandleVehicleBatchAsync
│
└── Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/   # NEW xUnit project (refs WebAPI + Shared)
    ├── ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests.csproj
    ├── LastBatchCacheTests.cs                  # NEW: cache invariants (unit)
    └── WorkerTransitHubTests.cs                # NEW: write-path caches then relays (unit)
```

The new test project must be added to `ChefKnifeStudios.TransitJazz.sln`.

**Structure Decision**: Web application (constitution's three-deployable layout). The cache and endpoint live in the existing `Server.WebAPI` project beside the SignalR hub it relays through; the client call mirrors the existing `GtfsEndpointsService` pattern in `Client.Core`. The endpoint route constant goes in the shared `ApiEndpoints` so server and client stay in lockstep.

## Testing Strategy

Pure unit tests only — **integration tests are out of scope for this feature**. A new xUnit project `ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests` (mirrors the existing `...TransitDataWorker.Tests` setup: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`) references `Server.WebAPI` and `Shared`. No host is booted and no live MARTA feed is touched — every test runs against plain objects, so the Worker/`GtfsStaticLoader` hosted services are never started. Full detail and assertions in `contracts/tests.md`.

### Unit tests

| Test | Target | Asserts | FR/INV |
|------|--------|---------|--------|
| `LastBatchCacheTests` | `LastBatchCache` | Cold start `Current` empty & non-null; `Set(b1)` ⇒ `Current==b1`; `Set(b1)`→`Set(b2)` ⇒ latest wins; `Set(null)` ⇒ empty non-null; concurrent `Set`/read returns one whole list (parallel loop, never null/torn) | FR-002, FR-004, FR-008 / INV-1..3 |
| `WorkerTransitHubTests` | `WorkerTransitHub.PublishBatch` | With a fake `ILastBatchCache` + mocked `IHubContext<TransitHub>`, `PublishBatch(b)` calls `cache.Set(b)` **and** relays `ReceiveBatch`; caching happens before/independently of relay | FR-001, FR-002, FR-010 |

### Covered by quickstart, not automated tests (deliberate scope boundary)

- GET `/transit/last-batch` real HTTP routing, status code, and polymorphic `EventEnvelope.Payload` JSON round-trip → `quickstart.md` Steps 1–2, 6 (FR-003, FR-004, FR-007).
- Client snapshot-fetch-on-load, immediate render, and smooth transition to the first live push → `quickstart.md` Steps 3–5 (FR-005, FR-006; SC-001–SC-004). No bUnit/WASM harness exists in the repo.

## Complexity Tracking

> No Constitution Check violations. The one structural addition — a new WebAPI test project — is standard test scaffolding (matches the existing Worker test project) and needs no justification entry.
