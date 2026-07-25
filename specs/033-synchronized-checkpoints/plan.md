# Implementation Plan: Synchronized Checkpoints

**Branch**: `033-synchronized-checkpoints` | **Date**: 2026-06-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/033-synchronized-checkpoints/spec.md`
**Design source**: [`docs/SYNCHRONIZED_CHECKPOINTS_DESIGN_DOCUMENT.md`](../../docs/SYNCHRONIZED_CHECKPOINTS_DESIGN_DOCUMENT.md) (authoritative decision record; resolves OQ-1..OQ-5)

## Summary

Two browser instances currently play different checkpoint music because **each client detects
crossings from its own locally-extrapolated, RAF-paced animation** (`checkpoint-tracker.js` `onTick`),
so the clients pass different trigger points at different moments. The note is *already* a pure
function of `(routeId, vehicleId, triggerIndex, totalTriggers)` — so fixing the **crossing set** fixes
the audio by construction (the user's explicit bar: the *set* of firings must match, timing need not).

The fix moves crossing detection to the **server (TransitDataWorker)**, the single source of truth for
snapped positions. Each V2 reconciliation cycle, the worker converts each vehicle's prior/current snap
index to along-route distance over a per-route cumulative-distance array, runs the **same** crossing
logic the client used, and emits a new `RouteCrossingBatchEvent` as another `EventEnvelope` in the
existing per-city batch publish. Clients **delete** local detection (`checkpoint-tracker.js` +
`CheckpointTrackerJsInterop`) and add one branch mapping the new event to the **unchanged**
`TransitMap.OnCrossingsAsync(CrossingEventDto[])` path (pulse / crossing-trail / note, still gated on
visibility / mute / route filter).

The determinism contract is guaranteed by **moving `TriggerPointGenerator` + `TriggerPoint` into the
`Shared` project** (OQ-1) so server and client compile one generator and cannot drift. Transport, hub,
publisher, and SignalR client wiring are **unchanged** — `PublishBatchAsync(city, batch)` is already
generic over payload. Reconnect safety (OQ-3) is **already satisfied**: `LastBatchCache` filters its
warm snapshot to `OfType<RouteNearestPointBatchEvent>()`, so crossings are never replayed — a
no-op confirmed by inspection.

## Technical Context

**Language/Version**: C# / .NET 10.0 (all projects); JavaScript (client interop / synth)
**Primary Dependencies**: ASP.NET Core (Minimal API + SignalR), Blazor WebAssembly, MatBlazor,
SignalR client, protobuf-net (GTFS-RT), MapLibre GL JS / MapTiler. No new dependencies.
**Storage**: In-memory only — Worker per-city `_routeIndex` (`RoutePoint[]`), per-city
`vehicleStateCache`; new per-(city,vehicle) crossing-baseline map; WebAPI in-memory `LastBatchCache`.
No persistence, no DB.
**Testing**: xUnit — `Server.TransitDataWorker.Tests` (crossing-detection unit tests + server/client
trigger-point equality test). Manual two-instance parity per quickstart.
**Target Platform**: Blazor WASM (Azure Static Web App) + ASP.NET Core WebAPI + .NET Worker (Azure
Container App), per constitution Principle I.
**Project Type**: Web application — decoupled frontend (WASM) + backend (WebAPI) + background worker.
**Performance Goals**: Crossing detection is O(triggers) per moved vehicle per ~10s cycle, over arrays
already in memory; negligible added work on a pass that is otherwise I/O-bound. Adds at most one extra
`EventEnvelope` to an existing publish.
**Constraints**: Note/instrument/pulse MUST stay byte-identical to today (Principle VIII) — achieved by
emitting the same `(triggerIndex, totalTriggers)` the client's generator produced. No client re-fetch
of geometry (Principle VII). No clock sync / fire-time prediction / scheduler (spec FR-002 — set, not
timing). No hub/publisher/transport change (design §4.4).
**Scale/Scope**: Touches Shared (1 new event + 2 moved files), Worker (detection helper + cycle wiring
+ per-route cumDist build + baseline-map prune), Client (delete detection interop + JS file, add one
SignalR branch, keep marker generation). Hub / publisher / `ITransitHubPublisher` / SignalR client:
**no change**.

**Note on namespaces**: the design doc uses the product name `TransitJazz`; the actual source root
namespace is `ChefKnifeStudios.MartaJazz` under `src/`. All file references below use the real paths.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Impact | Status |
|---|---|---|
| **I. Decoupled Cloud Architecture** | Worker computes crossings, WebAPI relays, client consumes; three deployable units unchanged. | ✅ PASS |
| **II. No Frontend Secrets** | No credentials touched. | ✅ PASS |
| **III. Two-Pass Real-Time Pipeline** | Crossing detection is an *addition* inside the V2 pass, derived from the snap the V2 pass already computes (`snapValue.Index`, `prior.SnapIndex`). V1/V2 record emission is unchanged. | ✅ PASS |
| **IV. OpenTelemetry Observability** | Per-cycle structured log gains a crossings-emitted count; existing logging retained. Telemetry sidecar (snap/lerp/cycle) unchanged. | ✅ PASS |
| **V. Azure DevOps CI/CD** | Same two artifacts (WASM + Docker image); no pipeline change. | ✅ PASS |
| **VI. GTFS ID Mapping** | Crossings carry the same `RouteId` the V2 records use (the route key = `route_short_name`, scoped per city group); join semantics unchanged. | ✅ PASS |
| **VII. OSM Cartography / no re-fetch** | Client deletes *detection* only; it keeps generating checkpoint **markers** from the already-cached route shapes (no geometry re-fetch). Basemap data-layer persistence untouched. | ✅ PASS |
| **VIII. Generative Music (deterministic)** | **Strengthened.** Note/instrument/duration stay pure functions of `(routeId, vehicleId, triggerIndex, totalTriggers)`. Moving `TriggerPointGenerator`/`TriggerPoint` to `Shared` makes server+client compile **one** generator, removing the only drift risk (two copies). The server emits exactly the `(triggerIndex, totalTriggers)` the client would have. | ✅ PASS |
| **IX. Persistent Multi-Selection** | `OnCrossingsAsync` route-filter gating (`effectiveIds` over selected ∪ hovered) is preserved verbatim. | ✅ PASS |
| **X / XI. Controls / Overlays** | Untouched. | ✅ PASS |
| **XII. i18n / Settings-Driven** | Audio-mute, checkpoint-visibility, crossing-trail-visibility gating in `OnCrossingsAsync` preserved verbatim. No new user-facing copy. | ✅ PASS |

**No violations.** Complexity Tracking omitted. Moving shared pure math into `Shared` *reduces*
complexity (one generator instead of two); the new `RouteCrossingBatchEvent` follows the established
`ISignalREvent` / `EventEnvelope` pattern (sits beside `RouteNearestPointBatchEvent`).

## Project Structure

### Documentation (this feature)

```text
specs/033-synchronized-checkpoints/
├── plan.md              # This file
├── research.md          # Phase 0 — OQ-1..OQ-5 resolved against the codebase
├── data-model.md        # Phase 1 — entities (event payload, baseline state, trigger point)
├── quickstart.md        # Phase 1 — two-instance parity walkthrough + verification
├── contracts/           # Phase 1 — event + detection + interop contracts
│   ├── route-crossing-event.md      # RouteCrossingBatchEvent payload + determinism contract
│   ├── server-crossing-detection.md # detection algorithm (cumDist + in-window trigger collection)
│   └── client-crossing-consumer.md  # event → CrossingEventDto[] → OnCrossingsAsync; deletions
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.MartaJazz.Shared/
│   ├── Events/RouteCrossingBatchEvent.cs           # NEW: ISignalREvent payload — list of
│   │                                               #      (VehicleId, RouteId, TriggerIndex, TotalTriggers)
│   ├── Services/TriggerPointGenerator.cs           # MOVE from Client.Shared/Services (one shared impl)
│   ├── Services/ITriggerPointGenerator.cs          # MOVE from Client.Shared/Services
│   └── Models/TriggerPoint.cs                       # MOVE from Client.Shared/Models
│
├── Server/
│   └── ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/
│       ├── Checkpoints/CrossingDetector.cs         # NEW: per-route cumDist build + per-vehicle
│       │                                           #      crossing detection mirroring checkpoint-tracker.js onTick
│       └── Worker.cs                               # CHANGE: build/cache per-route triggerPoints+cumDist
│                                                   #         alongside _routeIndex; detect crossings per
│                                                   #         moved vehicle in ProcessSpatialReconciliationAsync;
│                                                   #         add RouteCrossingBatchEvent to the publish batch;
│                                                   #         prune crossing baselines alongside vehicle states
│
└── Client/
    ├── ChefKnifeStudios.MartaJazz.Client.Shared/
    │   ├── Services/TriggerPointGenerator.cs       # DELETE (moved to Shared)
    │   ├── Services/ITriggerPointGenerator.cs      # DELETE (moved to Shared)
    │   ├── Models/TriggerPoint.cs                  # DELETE (moved to Shared)
    │   ├── Services/JsInterop/CheckpointTrackerJsInterop.cs   # DELETE (detection retired)
    │   ├── Services/JsInterop/ICheckpointTrackerJsInterop.cs  # DELETE
    │   └── wwwroot/js/checkpoint-tracker.js        # DELETE (local detection retired)
    ├── ChefKnifeStudios.MartaJazz.Client.WebApp/
    │   ├── Pages/TransitMap.razor.cs               # CHANGE: in HandleVehicleBatchAsync, add a branch
    │   │                                           #         mapping RouteCrossingBatchEvent → CrossingEventDto[]
    │   │                                           #         → OnCrossingsAsync; drop CheckpointTracker inject,
    │   │                                           #         the ConfigureRouteAsync call, and ClearAsync dispose;
    │   │                                           #         KEEP TriggerPointGenerator + AddTriggerPointMarkersAsync
    │   │                                           #         (markers), now from Shared namespace
    │   └── Program.cs                              # CHANGE: drop ICheckpointTrackerJsInterop registration;
    │                                               #         keep ITriggerPointGenerator registration (Shared type)
    └── (SignalRNotificationService.cs)             # NO CHANGE — already relays whole EventEnvelope batches

# No change: WorkerTransitHub, TransitHub, LastBatchCache, ITransitHubPublisher, SignalRHubPublisher, transit-synth.js
```

**Structure Decision**: Web application (Principle I). No new projects. Two new files (`Shared`
event, Worker `Checkpoints/CrossingDetector.cs`), three moves into `Shared`, six client deletions,
and edits to `Worker.cs`, `TransitMap.razor.cs`, and client `Program.cs`. The diff stays inside the
established 11-project structure.

## Key Code Seams (verified in source)

- **Client consume seam (kept):** `TransitMap.OnCrossingsAsync(CrossingEventDto[])`
  (`TransitMap.razor.cs:155`) already fans each crossing to pulse / crossing-trail / note, gated on
  `_checkpointsVisible`, `_crossingTrailVisible`, `_audioEnabled`, and `effectiveIds` (route filter).
  This entire body is **unchanged**; only the *source* of the array changes from JS → SignalR.
  `CrossingEventDto(VehicleId, RouteId, TriggerIndex, TotalTriggers)` is defined at
  `TransitMap.razor.cs:562`.
- **Server snap seam (reused):** `ProcessSpatialReconciliationAsync` already holds `prior.SnapIndex`
  and current `snapValue.Index` per vehicle per cycle (`Worker.cs:213`, `:406`), plus the per-route
  `RoutePoint[]` index and `HaversineCalculator`. Crossing detection needs only a per-route `cumDist[]`
  (built once from `RoutePoint` lat/lon) and the shared `TriggerPointGenerator`.
- **Determinism contract:** `transit-synth.js noteForPosition(scale, triggerIndex, totalTriggers)`
  (`transit-synth.js:138`) is pure; the server must emit the same `triggerIndex = TriggerPoint.Index`
  and `totalTriggers = route trigger count` the client's generator produced — guaranteed by the shared
  generator over the shared route key (`route_short_name`, identical server/client).
- **Transport (unchanged):** `PublishBatchAsync(city, List<EventEnvelope>)` → `WorkerTransitHub.PublishBatch`
  → `Clients.Group(city).SendAsync(ReceiveBatch, batch)`. Generic over payload; the crossing event rides
  as an extra envelope in the same publish.
- **Reconnect safety (already satisfied):** `LastBatchCache.CityCache.Set` filters
  `OfType<RouteNearestPointBatchEvent>()` (`ILastBatchCache.cs:59`); the new event is structurally
  excluded from the warm `JoinCity` replay — no code change for OQ-3 (FR-005).

## Implementation Order

The spec's user stories map onto two slices:

1. **Slice A — Server-authoritative crossings + client re-wire (P1: US1 + US2).** Move the generator
   to `Shared`; add `RouteCrossingBatchEvent`; build per-route cumDist + crossing detection in the
   worker; emit the new envelope; add the client SignalR branch into `OnCrossingsAsync`; delete the
   client detection (`checkpoint-tracker.js`, `CheckpointTrackerJsInterop`) and its wiring while
   keeping marker generation. This single slice delivers two-client parity **and** preserves the
   single-user experience + all gating toggles + exactly-once (no residual local path) — the whole P1.
2. **Slice B — Reconnect/burst hardening (P2: US3).** Verify (don't re-add) reconnect safety via the
   `LastBatchCache` type filter; prune crossing baselines alongside `PruneStaleVehicleStatesAsync`;
   evaluate per-cycle burst musicality in-app and add light spreading only if needed (OQ-2, default
   off). Mostly verification + the small baseline-prune addition.

Slice A is the MVP and resolves the reported bug; Slice B is hardening. `/speckit-tasks` can order
P1 (Slice A) before P2 (Slice B).
