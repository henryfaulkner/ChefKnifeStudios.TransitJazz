# Implementation Plan: Logging Sidecar Service

**Branch**: `013-logging-sidecar-service` | **Date**: 2026-06-04 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/013-logging-sidecar-service/spec.md`

## Summary

Add an in-process **logging sidecar** to the `TransitDataWorker` that captures structured Snap, Lerp, and Cycle decision telemetry without touching the real-time processing hot path, then durably persists it as **parquet in Azure Blob Storage** for the existing `telemetry-query-tool` (DuckDB) to query.

Technical approach: data-processing code posts marker event-args onto an in-process `IEventNotificationService` (mirroring the existing `Client.Core` notification pattern). A hosted `LogEventWorker` subscribes, drops each logging event into a bounded `Channel` (load-shedding on overflow), and a background consumer accumulates rows per event type. Every 5 minutes (and best-effort on shutdown) a `StructuredLoggingService` serializes the accumulated rows to parquet **in-process with Parquet.Net** and uploads one part-file per dataset into `…/{snap|lerp|cycle}/dt=YYYY-MM-DD/part-<utcts>.parquet` via the Azure Blob SDK using managed identity. All logging code lives under a single `Logging/` folder in the worker project. Persistence failures are caught, counted, and surfaced as health columns on the Cycle record — they never propagate to the processing loop.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Worker SDK, `Microsoft.NET.Sdk.Worker`)
**Primary Dependencies**: `Parquet.Net` (in-process parquet writer), `Azure.Storage.Blobs` + `Azure.Identity` (already referenced) for upload via `DefaultAzureCredential`, `System.Threading.Channels` (BCL). Existing: `Microsoft.Extensions.Hosting`, OpenTelemetry via `ServiceDefaults`.
**Storage**: Azure Blob Storage (parquet). Layout: container `telemetry`, three datasets `snap/`, `lerp/`, `cycle/`, each daily-partitioned `dt=YYYY-MM-DD/`, part-files `part-<yyyyMMddTHHmmssfffZ>.parquet`. Read downstream by `telemetry-query-tool` via the DuckDB Azure extension.
**Testing**: `xunit` (new `*.Tests` project for the worker, or unit tests colocated) covering: grammar of nothing (no parsing here), parquet schema round-trip, partition-path derivation, channel load-shedding behavior, flush-timer batching, and failure isolation. No test project exists for the worker today — add one.
**Target Platform**: Linux container (worker runs as a Docker image per Constitution V), also runnable locally under .NET Aspire AppHost.
**Project Type**: Backend background-worker feature inside an existing multi-project .NET solution (no new deployable unit — sidecar is in-process per spec).
**Performance Goals**: Zero added hot-path latency (SC-001). Telemetry volume bounded by feed size: ~hundreds–low-thousands of vehicles per 10s cycle → Snap/Lerp rows dominate; a 5-minute part-file holds ≈30 cycles. Bounded channel capacity 10,000 (from source spec) with `DropWrite`.
**Constraints**: Hot-path isolation is absolute (FR-002): posting an event is a non-blocking `TryWrite`. Parquet build + blob upload happen only on the consumer/flush path. At most one 5-minute interval of telemetry may be lost on ungraceful crash (SC-007). No append/mutate of written blobs (FR-004b). No DuckDB or external process on the worker host (FR-004a).
**Scale/Scope**: Three event schemas + one notification service + one hosted worker + one sink service + config/DI. ~8–12 new files, all under `TransitDataWorker/Logging/`. Worker.cs edited to (a) assign a `CycleId` per cycle, (b) post Snap/Lerp/Cycle events at the existing capture points (the data is already computed — see `BatchDebugRecord`, `VehicleState`, and the per-cycle counters in `ProcessSpatialReconciliationAsync`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> Note: The constitution (v3.0.0) uses the legacy `ChefKnifeStudios.TransitJazz.*` namespace; the live solution uses `ChefKnifeStudios.TransitJazz.*`. This plan targets the actual `MartaJazz` projects. Flagged as a pre-existing doc drift, not introduced here.

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | ✅ PASS | Sidecar is in-process in the existing TransitDataWorker unit; adds no new deployable, no new inter-service coupling. Blob is an output sink, not a service dependency the worker blocks on. |
| II. No Frontend Secrets | ✅ N/A | No frontend involvement. Blob credential lives server-side only. |
| III. Two-Pass Pipeline | ✅ PASS | Sidecar observes the existing V1/V2 passes; it does not alter pass logic or ordering. Snap/Lerp/Cycle telemetry is captured at existing decision points. |
| IV. OpenTelemetry Observability | ⚠️ WATCH | This feature adds parquet-based *domain* telemetry, distinct from OTEL operational telemetry. It does not replace OTEL. Worker must keep emitting existing structured logs; sidecar self-health (dropped/failed counts) also goes to the Cycle parquet (FR-012) and SHOULD additionally log via `ILogger` so OTEL/Log Analytics still sees failures. Verified compatible. |
| V. Azure DevOps CI/CD | ✅ PASS | No artifact-shape change; sidecar ships inside the existing worker Docker image. |
| VI. GTFS ID Mapping | ✅ PASS | Snap/Lerp records carry the route key already used by the worker (route short name); no new join logic. |

**Security gate (from feature 012 / FR-020 cross-reference)**: The existing `telemetry-query-tool` and design docs carried a hardcoded live Azure `AccountKey`. This plan MUST NOT introduce a new committed credential: the sidecar authenticates to Blob with `DefaultAzureCredential` (managed identity in Azure, `az login`/env locally). Connection details come from configuration/env, never source. This is a hard gate — see research R2.

**Initial Constitution Check: PASS** (one WATCH item with a concrete mitigation; no violations requiring Complexity Tracking).

**Post-Design Constitution Check (after Phase 1): PASS** — re-evaluated after research + data-model + contracts. New dependencies (`Parquet.Net`, `Azure.Storage.Blobs`) are in-process managed code shipping inside the existing worker image (no new deployable, no artifact-shape change → I, V hold). Auth resolved to `DefaultAzureCredential`/managed identity with config-only endpoint (II + security gate hold; no committed key). The IV WATCH item is mitigated: sidecar self-health is both written to the Cycle parquet **and** logged via `ILogger` so OTEL/Log Analytics still observes failures. Design observes the V1/V2 passes at existing capture points without altering them (III) and reuses the worker's existing route key (VI). No violations; Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/013-logging-sidecar-service/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (parquet dataset schemas + blob layout)
│   ├── parquet-schemas.md
│   └── blob-layout.md
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── Worker.cs                         # EDIT: assign CycleId, post Snap/Lerp/Cycle events at existing points
├── Program.cs                        # EDIT: register Logging DI (notification service, LogEventWorker, sink, options)
├── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj  # EDIT: add Parquet.Net + Azure.Storage.Blobs
└── Logging/                          # NEW — all logging-sidecar files (FR-013)
    ├── IEventNotificationService.cs  # IEventArgs, EventReceivedEventHandler, IEventNotificationService, EventNotificationService
    ├── LogEventArgs.cs               # abstract base : IEventArgs
    ├── SnapEventArgs.cs              # : LogEventArgs (+ SnapDecision enum)
    ├── LerpEventArgs.cs              # : LogEventArgs
    ├── CycleEventArgs.cs             # : LogEventArgs (includes sidecar self-health fields)
    ├── ILoggingService.cs            # sink abstraction: accumulate row + flush to parquet/blob
    ├── ParquetLoggingService.cs      # ILoggingService impl: Parquet.Net build + Azure Blob upload
    ├── LogEventWorker.cs             # IHostedService: subscribe, bounded Channel, 5-min flush loop, drain-on-stop
    ├── LoggingOptions.cs             # bound config: container, flush interval, channel capacity, blob URI
    └── TelemetryColumns.cs           # canonical column-name constants (downstream query-tool contract)

src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/   # NEW test project
├── ParquetSchemaTests.cs
├── PartitionPathTests.cs
├── ChannelLoadSheddingTests.cs
└── FailureIsolationTests.cs
```

**Structure Decision**: Single existing project edited in place plus a new sibling test project. Per FR-013 every new production file is consolidated under `TransitDataWorker/Logging/`. No new solution-deployable is created (Constitution I): the sidecar is hosted in the worker process as an `IHostedService` alongside the existing `Worker`.

## Complexity Tracking

> No constitution violations require justification. Section intentionally empty.
