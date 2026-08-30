# Phase 1 Data Model: Remove the Parquet Telemetry Sidecar

**Feature**: 055-remove-parquet-sidecar
**Date**: 2026-08-30

This feature creates no entities. What follows is the inventory of entities being
**retired**, the entities that **survive** and must be provably unaffected, and the
dependency edges that dictate deletion order.

---

## 1. Retired entities

### 1.1 `TelemetryEvent` — the parquet row

`Logging/TelemetryEvent.cs`. A denormalized record whose C# property names *are* the
parquet column names (snake_case, deliberately, because `Parquet.Net 5.6.1` has no
column-rename attribute).

| Group | Columns |
|---|---|
| Common | `event_type`, `event_id`, `observation_utc` |
| PerCityCycle only | `city_name`, `feed_freshness_seconds` |
| FullCycle only | `cities_processed_count`, `cities_processed_csv` |
| Shared | `time_taken_seconds`, `health_ok`, `tones_emitted`, `vehicles_processed`, `gc_heap_bytes`, `process_working_set_bytes`, `vehicle_state_cache_size`, `crossing_baseline_cache_size`, `route_index_size`, `route_trigger_point_cache_size` |
| Crossing attribution (045) | `crossings_suppressed_first_seen`, `..._delta_leq0`, `..._teleport`, `..._transfer` |
| Egress (051) | `batch_wire_bytes` |

**Retirement note**: this schema was a *frozen wire contract* shared with
`tools/telemetry-mcp`'s validator allow-list. Both sides are removed in this feature, so
the contract is dissolved rather than broken. `batch_wire_bytes` carried the 051 Phase 3
baseline — see the FR-020 checkpoint in `quickstart.md`.

### 1.2 Sidecar machinery

| Entity | File | Role |
|---|---|---|
| `ILoggingService` | `Logging/ILoggingService.cs` | Sink contract: `Accumulate`, `FlushAsync`, `DroppedRecords`, `PersistFailures` |
| `ParquetLoggingService` | `Logging/ParquetLoggingService.cs` | Buffers rows, serializes, uploads via `DefaultAzureCredential` |
| `LogEventWorker` | `Logging/LogEventWorker.cs` | `BackgroundService`; bounded channel (`DropWrite`), timer flush + shutdown flush |
| `LoggingOptions` | `Logging/LoggingOptions.cs` | Binds `Logging:Telemetry:*` |
| `IEventArgs` / `IEventNotificationService` / `EventNotificationService` | `Logging/IEventNotificationService.cs` | In-process bus; parquet-only (research D2) |

### 1.3 Sidecar self-health signals

Three OpenTelemetry instruments whose subject is the sidecar itself:

| Metric | Instrument | Source field |
|---|---|---|
| `transitjazz.worker.log_buffer_occupancy` | `Gauge<int>` | `CycleMetrics.LogBufferOccupancy` |
| `transitjazz.worker.log_dropped_records` | `Counter<long>` | `CycleMetrics.LogDroppedRecords` |
| `transitjazz.worker.log_persist_failures` | `Counter<long>` | `CycleMetrics.LogPersistFailures` |

Plus their two Grafana panels ("Sidecar queue occupancy", "Sidecar failure rate").
**Zero alert rules reference them** — verified against
`observability/grafana/alerts/transitjazz-worker-alerts.json`.

### 1.4 In-app telemetry contracts

| Entity | Location |
|---|---|
| `TelemetryEventDto` | `Shared/TelemetryData/TelemetryEventDto.cs` |
| `TelemetryTodaySummaryDto`, `TelemetryTableSummaryDto`, `TelemetryPageDto`, `TelemetrySortColumn` | `Shared/TelemetryData/TelemetryPagingDtos.cs` |
| `ApiEndpoints.Telemetry` (`/telemetry/today`, `/telemetry/today/summary`) | `Shared/ApiEndpoints.cs:23-27` |
| `ITelemetryEndpointsService` / `TelemetryEndpointsService` | `Client.Core/Services/EndpointsServices/` |
| `Telemetry.razor`, `TelemetryTable.razor`, `TelemetryColumn` | `Client.WebApp/Pages/` |

### 1.5 Storage entities

| Entity | Definition |
|---|---|
| Storage account `mjtel{env}{uniqueString}` | `bicep/modules/telemetryStorage.bicep` |
| Blob container `parquet` | same |
| `Storage Blob Data Contributor` assignment (server MI) | same, role `ba92f5b4-…` |
| Blob layout `telemetry/{dataset}/dt=YYYY-MM-DD/part-<utcts>.parquet` | written by `ParquetLoggingService` |
| `enableLegacyTelemetry` toggle | `bicep/main.bicep:63` |

---

## 2. Surviving entities — must be provably unaffected

### 2.1 Structured logging (054)

`StructuredLogEvent`, `StructuredLogEventName`, `StructuredLogReasonCode`,
`StructuredLogOutcome`, `StructuredEventEmitter`, `StructuredEventPolicy`,
`StructuredLogRedactor`, `StructuredLoggingOptions`, `IWorkerStructuredEventLogger`.

**Independence proof**: `StructuredEventEmitter` writes through `ILogger` directly — its own
doc comment states "no second transport is used." No reference to `ILoggingService`,
`IEventArgs`, or `Parquet` anywhere in the 054 set.

### 2.2 Anomaly classification

`CityCycleOutcome`, `CityAnomalyClassifier`. Feed `StructuredLogEventName.CityCycleAnomaly`.
Live in `Logging/` but belong to the 054 path. **Keep.**

### 2.3 Wire measurement (051)

`WireSize.Measure` — pure, unit-tested, called at `Worker.cs:837`. **Keep.** Only its
*parquet column* (`batch_wire_bytes`) is retired.

### 2.4 Retained metrics

Everything in `Metrics/` except the three sidecar instruments: cycle duration, allocated
bytes, vehicles processed, tones emitted, cache sizes, feed freshness, and the per-city
dimensions. All Grafana panels other than the two named above.

---

## 3. Dependency edges (deletion order)

```
Worker.cs ──posts──▶ IEventNotificationService ──▶ LogEventWorker ──▶ ILoggingService
                              │                          │                  │
                        TelemetryEvent            CycleMetrics         ParquetLoggingService
                                                  (3 fields)                  │
                                                       │               Azure Blob (parquet)
                                            WorkerMetricsReporter               ▲
                                                       │                        │
                                             Grafana (2 panels)      WebAPI TelemetryEndpoints
                                                                                │
                                                              Shared DTOs ─▶ Client service ─▶ Telemetry.razor
                                                                                ▲
                                                          telemetry-query-tool ─┤
                                                          telemetry-mcp ────────┤
                                                          mj-data-explorer ─────┘
```

**Safe deletion order** (leaves inward):

1. Readers — Go tools, `.mcp.json` entry, skill telemetry files, client page + service
2. Contracts — Shared DTOs, `ApiEndpoints.Telemetry`
3. Server endpoint — `TelemetryEndpoints.cs`, WebAPI DI + mapping
4. Metrics — 3 instruments, 3 `CycleMetrics` fields, 2 Grafana panels
5. Worker wiring — ctor param, 2 `PostEvent` sites, self-health block
6. Sidecar core — the six `Logging/` files
7. Package — `Parquet.Net` reference
8. Config — `Logging:Telemetry` block
9. Infra — Bicep module, params, env vars, outputs (**separate deployment**)

---

## 4. State transitions

The feature has one meaningful state machine — the release gate:

```
PENDING ──(7 consecutive passing days recorded)──▶ AUTHORIZED
   │                                                    │
   │ (any gate row fails)                               │ (slices A+B deploy & verify)
   ▼                                                    ▼
BLOCKED ────────────────────────────────────────▶ CODE-REMOVED
                                                        │
                                                        │ (FR-020 export-or-discard decision recorded)
                                                        ▼
                                                   RETIRED (infra gone, checklist updated)
```

No transition may be skipped. `AUTHORIZED` requires a recorded approver and date;
`RETIRED` requires the FR-020 decision to have been made *before* storage deletion.

---

## 5. Validation rules

| Rule | Source | Check |
|---|---|---|
| No component reads or writes parquet | FR-001, FR-003 | No `Parquet.Net` reference; no `.parquet` write path in `src/` |
| No telemetry config remains | FR-004 | `Logging:Telemetry` absent from every `appsettings*.json` and from Bicep env vars |
| Structured events unchanged | FR-005 | 054 test suite passes untouched; event names/fields byte-identical |
| No sidecar self-health signals | FR-006 | 3 instruments and 2 panels absent; no zero-valued residue |
| Cycle behavior unchanged | FR-007 | Cycle cadence within historical variation |
| No telemetry route or link | FR-008 | No `/telemetry` route; `Index.razor` link gone |
| Build and tests green | FR-023 | Full solution build + test run, zero failures |
| Only historical matches remain | FR-024 | Repo search classified per research D10 |
