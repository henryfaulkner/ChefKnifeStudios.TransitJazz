# Implementation Plan: Telemetry Denormalization

**Branch**: `038-telemetry-denormalization` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/038-telemetry-denormalization/spec.md`

## Summary

Replace the three separately-schemaed telemetry datasets (`snap`/`lerp`/`cycle`) with **one denormalized parquet table** (`telemetry/`) whose rows are discriminated by an `event_type` column, and retire per-vehicle snap/lerp granularity in favor of two aggregate event types: **PerCityCycle** (one row per telemetry-emitting city per tick) and **FullCycle** (one row per worker tick across all cities). The record becomes a single POCO (`TelemetryEvent`) serialized with `Parquet.Net`'s `ParquetSerializer` so adding a field is a one-property change; the `switch`-based three-buffer `ParquetLoggingService` collapses to one buffer / one schema / one blob path. In `Worker.cs`, the three scattered mid-processing post sites are replaced by exactly two end-of-processing posts: PerCityCycle wraps the **entire** per-city `try/catch` (so failed/skipped ticks emit a row — the key visibility fix), and FullCycle posts once after the `foreach`. New memory (`gc_heap_bytes`, `process_working_set_bytes`) and per-city cache-size diagnostics are added. The Go MCP validator's three per-dataset column maps collapse to one merged map for the single `telemetry` dataset. Reference docs and schema tests are updated to match. **Backend/tools-only**: no frontend, Shared, or WebAPI changes.

## Technical Context

**Language/Version**: C# / .NET 10.0 (TransitDataWorker + its Tests); Go 1.x (tools/telemetry-mcp)
**Primary Dependencies**: `Parquet.Net` 5.* (now used via `ParquetSerializer` POCO/attribute API instead of hand-built `ParquetSchema`), `Azure.Storage.Blobs` + `Azure.Identity` (`DefaultAzureCredential`), `System.Threading.Channels`; Go `mcp-go` + the DuckDB-backed `telemetry-query-tool` (unchanged)
**Storage**: Azure Blob — single container `telemetry`, single partitioned path `telemetry/dt={yyyy-MM-dd}/part-{ts}-{guid}.parquet` (drops the `{dataset}/` segment)
**Testing**: xUnit (`...TransitDataWorker.Tests`); Go `testing` (`validate_test.go`)
**Target Platform**: Linux container (Worker as Docker image); local dev via .NET Aspire AppHost
**Project Type**: Background worker service + a standalone Go developer tool
**Performance Goals**: Telemetry MUST NOT block the 10s reconciliation hot path — preserve existing `DropWrite` load-shedding and 5-min buffered flush; two posts per tick per city instead of N per-vehicle posts (strictly fewer events)
**Constraints**: Column names are a frozen snake_case contract shared C#↔Go — the two MUST NOT drift (enforced via `ParquetSerializer` column-name attributes, not hand-typed constants); memory sampled once per tick and reused (not summed); NEVER re-fetch feed data to populate telemetry
**Scale/Scope**: One telemetry-emitting city today (MARTA, `EmitsTelemetry=true`); design must stay correct as cities are added. ~17 columns total (3 common + 14 metric/detail).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The constitution (v3.3.1) is overwhelmingly frontend/UX-focused (Principles VII–XIII govern map, audio, filtering, settings, dark-mode). This feature is a backend telemetry/data change and touches none of those surfaces. Relevant gates:

| Principle | Applies? | Assessment |
|---|---|---|
| I. Decoupled Cloud Architecture | Partial | Change is confined to the TransitDataWorker unit + a local dev tool. No new deployable, no cross-unit contract change. ✅ |
| III. Two-Pass Pipeline | Indirect | The V2 reconciliation pass is unchanged in behavior; only where/what telemetry it *emits* changes. The `RouteNearestPointBatchEvent`/`RouteCrossingBatchEvent` SignalR emissions are untouched. ✅ |
| IV. OpenTelemetry Observability | ✅ Aligned | This *improves* observability: failed/skipped city ticks become visible (they're silently invisible today). Structured `logger.Log*` calls are retained. ✅ |
| V. GitHub Actions CI/CD | ✅ | No pipeline change; Worker still ships as the Docker artifact. ✅ |
| VIII. Generative Music (determinism) | ✅ | `tones_emitted` renames a counter (`crossingRecords.Count`); no change to tone generation. ✅ |
| II. No Frontend Secrets | ✅ | Blob auth stays `DefaultAzureCredential` (managed identity); no committed key introduced. ✅ |

**No violations.** No frontend, localization, dark-mode, or map/audio-interaction concerns are in scope, so Principles VII, IX–XIII are not engaged. Complexity Tracking table below is empty (nothing to justify).

**Post-Phase-1 re-check**: Design keeps the single-POCO/single-buffer/single-path approach (net *reduction* in code and moving parts vs. today's three-of-everything). Still no violations.

## Project Structure

### Documentation (this feature)

```text
specs/038-telemetry-denormalization/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output — TelemetryEvent record + column contract
├── quickstart.md        # Phase 1 output — build/run/query validation steps
├── contracts/           # Phase 1 output
│   ├── telemetry-event-schema.md   # C# POCO ⇄ parquet column contract (both event types)
│   ├── blob-layout.md              # single partitioned path
│   └── query-validator.md          # Go allow-list: single dataset + merged column map
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/
├── Worker.cs                         # MODIFY: replace 3 scattered posts with 2 end-of-cycle posts;
│                                     #   PerCityCycle wraps the per-city try/catch (lines 55-75);
│                                     #   FullCycle posts once after the foreach; sample memory once/tick;
│                                     #   read the 4 per-city cache sizes; aggregate across cities.
├── Logging/
│   ├── TelemetryEvent.cs             # ADD: single POCO record (was SnapEventArgs/LerpEventArgs/CycleEventArgs)
│   ├── SnapEventArgs.cs              # DELETE
│   ├── LerpEventArgs.cs              # DELETE
│   ├── CycleEventArgs.cs            # DELETE
│   ├── LogEventArgs.cs              # DELETE (CycleId base no longer needed)
│   ├── TelemetryColumns.cs          # DELETE (column names move onto the POCO as attributes)
│   ├── ParquetLoggingService.cs     # REWRITE: one ConcurrentBag<TelemetryEvent>, ParquetSerializer,
│   │                                 #   one FlushAsync, one blob path (drop {dataset}/ segment)
│   ├── ILoggingService.cs           # UNCHANGED (interface is already IEventArgs-based)
│   ├── LogEventWorker.cs            # UNCHANGED (already dataset-agnostic)
│   ├── IEventNotificationService.cs # UNCHANGED
│   └── LoggingOptions.cs            # UNCHANGED (Container already defaults to "telemetry")
└── Program.cs                        # UNCHANGED (DI registers ILoggingService/LogEventWorker by interface)

src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/
├── SnapParquetSchemaTests.cs        # DELETE
├── LerpParquetSchemaTests.cs        # DELETE
├── CycleParquetSchemaTests.cs       # DELETE
├── TelemetryEventSchemaTests.cs     # ADD: one schema/round-trip test for both event types incl. nulls
├── ChannelLoadSheddingTests.cs      # MODIFY: re-point at single-buffer service (post TelemetryEvent)
├── FailureIsolationTests.cs         # MODIFY: re-point at single-buffer service
└── PartitionPathTests.cs            # MODIFY: assert the new telemetry/dt=.../part-*.parquet path

tools/telemetry-mcp/internal/validate/
├── validate.go                      # MODIFY: validDatasets → {"telemetry"}; datasetColumns → one
│                                     #   merged map (event_type string + 16 others); error text updated
└── validate_test.go                 # MODIFY: accept/reject vectors for the single dataset + event_type

.claude/skills/mj-data-explorer/references/
├── telemetry-schema.md              # MODIFY: one TelemetryEvent table + event_type discriminator
└── telemetry-query-guide.md         # MODIFY: dataset name, examples, event_type filtering pattern
```

**Structure Decision**: This is the existing multi-project .NET solution + the co-located Go tool. No new projects. Work is localized to the `TransitDataWorker` project (`Logging/` + `Worker.cs`), its test project, the Go validator, and two skill reference docs. The DI graph in `Program.cs` is untouched because the sidecar is wired by interface (`ILoggingService`) and the marker (`IEventArgs`) — only the concrete `ParquetLoggingService` body and the event type change.

## Complexity Tracking

> No Constitution Check violations — table intentionally empty. The change is a net simplification (three buffers/schemas/paths/event-types → one).
