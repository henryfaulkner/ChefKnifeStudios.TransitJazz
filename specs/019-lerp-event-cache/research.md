# Phase 0 Research: Last Lerp Event Cache

No `NEEDS CLARIFICATION` markers remained in Technical Context. The two scope-defining ambiguities were resolved with the stakeholder during `/speckit-specify` (see spec Assumptions). The decisions below record the technical choices made against the existing codebase.

## Decision 1 — Where the cache hooks in

**Decision**: Cache the batch inside `WorkerTransitHub.PublishBatch` (the WebAPI relay), writing the snapshot *before* (or alongside) the `IHubContext<TransitHub>.Clients.All.SendAsync("ReceiveBatch", batch)` relay.

**Rationale**: `WorkerTransitHub.PublishBatch(List<EventEnvelope> batch)` is the single chokepoint through which every published batch passes on its way from the Worker (`SignalRHubPublisher.PublishBatchAsync` → hub method `"PublishBatch"`) to all clients. Caching here guarantees the snapshot always equals what clients receive over the wire (FR-002, FR-009) with **zero** new cross-service calls and no upstream fetch on read (FR-007). The Worker need not change.

**Alternatives considered**:
- *Worker holds the cache, WebAPI pulls it* — rejected: adds an HTTP hop across the service boundary and a second copy of the batch; `WorkerTransitHub` already sees the data for free. (Stakeholder also rejected this option.)
- *Cache the telemetry `LerpEventArgs`* — rejected: that type feeds the parquet logging sidecar and is never consumed by the client; caching it would not fix bus-appearance lag. (Stakeholder confirmed the V2 batch is the intended target.)

## Decision 2 — Cache type and thread safety

**Decision**: A dedicated singleton `ILastBatchCache` with a single mutable reference field updated by atomic reference swap (`Volatile.Write` / read with `Volatile.Read`), seeded to an empty `List<EventEnvelope>`.

**Rationale**: Only ever one snapshot is kept (no history). A reference swap of an immutable-once-published list is atomic, so a concurrent reader always observes one whole batch — either the prior or the new one — never a torn/partial object (FR-008). No lock needed on the read path, which keeps the endpoint cheap. SignalR hub method invocations can overlap, so the field must be `volatile`-accessed rather than a plain field.

**Alternatives considered**:
- *`ConcurrentDictionary` / locks* — unnecessary for a single-slot snapshot; a reference swap is simpler and lock-free.
- *Store in `IKeyValueRepository<string>`* — that repo is string-keyed JSON for GTFS shapes; serializing the batch to a string on every push just to deserialize on read adds cost with no benefit. Keep the live object.

## Decision 3 — Endpoint shape, route, and serialization

**Decision**: New endpoint group `TransitEndpoints` exposing `GET` at a new shared constant `ApiEndpoints.Transit.GetLastBatch` (`/transit/last-batch`), returning `200 OK` with `IEnumerable<EventEnvelope>` (the cached list, or an empty list before the first push). Serialization uses the WebAPI's already-configured HTTP JSON options (`Program.cs` copies `JsonOptions.Get()` into `ConfigureHttpJsonOptions`).

**Rationale**: Mirrors the existing `GtfsEndpoints` minimal-API group pattern (`MapGroup`, `.WithName`, `.Produces<>`). Returning the same `List<EventEnvelope>` the client already handles means the client pipes the response straight into `HandleVehicleBatchAsync` with no new mapping (FR-005, FR-009). The shared `JsonOptions.Get()` config (with its `ISignalREvent` polymorphic converters) is the same configuration SignalR uses via `JsonSettings`, so `EventEnvelope.Payload` round-trips identically over HTTP and WSS. Cold start returns an empty list with `200` (FR-004), per the stakeholder's choice over `204`.

**Alternatives considered**:
- *Add to `GtfsEndpoints`* — rejected: this is transit/vehicle data, not GTFS static shape data; a separate `Transit` group keeps concerns clean and matches how `ApiEndpoints` is already partitioned (`Test`, `Gtfs`).
- *Return `204 No Content` on cold start* — rejected by stakeholder; empty-`200` minimizes client branching.

## Decision 4 — Client integration point

**Decision**: Add `ITransitEndpointsService.GetLastBatch(...)` in `Client.Core/Services/EndpointsServices` (mirroring `GtfsEndpointsService`, returning `Result<IEnumerable<EventEnvelope>>` via `IHttpServiceFactory.Create(nameof(APIs.TransitJazzAPI))`). In `TransitMap`, fetch the snapshot once on load and feed it through the existing `HandleVehicleBatchAsync`.

**Rationale**: `HandleVehicleBatchAsync(IEnumerable<EventEnvelope>)` already contains all the rendering/animation logic and the `_pendingBatch` guard for "map not ready yet." Routing the snapshot through it reuses every existing behavior — allowed-route filtering, animator forwarding, V1 fallback — and the smooth-transition requirement (FR-006) falls out naturally because the next live push runs the identical code path against the animator's existing per-vehicle state. The fetch is fire-once and best-effort: a failure logs and is superseded by the first live push (graceful, per US2).

**Sequencing**: Call after `NotificationService.InitAsync()` is wired and after routes load, so that if a snapshot arrives before the map is ready, the existing `_pendingBatch` mechanism in `HandleVehicleBatchAsync`/`OnMapReadyAsync` replays it. The snapshot fetch must not block SignalR init.

**Alternatives considered**:
- *New dedicated render path for the snapshot* — rejected: duplicates `HandleVehicleBatchAsync` and risks divergence from live handling (violates FR-009's "reuse existing logic").
- *Fetch in `OnAfterRenderAsync`* — rejected: less deterministic ordering; `OnInitializedAsync` already owns the load sequence.

## Open risks / notes

- **Empty-payload events on cold start**: returning an empty `List<EventEnvelope>` means `HandleVehicleBatchAsync` sees zero nearest-point records and zero position events; it currently falls into the V1 branch and calls `PlotVehiclesAsync` with an empty feature collection. Confirm this is a no-op clear (it is — empty features), so cold start renders nothing without error (US2). The plan's quickstart verifies this explicitly.
- **Duplicate render on near-simultaneous load + push**: if the snapshot and the first live push arrive back-to-back, the animator processes the same vehicle transitions twice. Because the animator keys on `vehicleId` and interpolates from prior→current, a repeat of the same prior→current pair is idempotent (no teleport). Verified against `ProcessNearestPointBatchAsync` semantics; called out in quickstart step for visual confirmation (FR-006 / SC-004).
