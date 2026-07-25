# Implementation Plan: Worker Route-Snap Refactor

**Branch**: `005-worker-route-snap-refactor` | **Date**: 2026-05-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/005-worker-route-snap-refactor/spec.md`

## Summary

Replace the TransitDataWorker's cross-route geohash spatial index with a per-route index keyed by routeId. Each bus snaps to the nearest point on its own route (via `entity.Vehicle.Trip?.RouteId`) instead of the nearest point across all routes. Extract the nearest-point algorithm into a shared `RouteSnapper` class in the Shared project for reuse by the future validate-snap API endpoint. Add fallback counters for vehicles with missing or unknown routeIds.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: protobuf-net (GTFS-RT deserialization), Microsoft.AspNetCore.SignalR.Client (event publishing), Azure.Identity  
**Storage**: In-memory (`ConcurrentDictionary` for vehicle state, `IReadOnlyDictionary` for route index)  
**Testing**: Manual integration testing (no formal test framework in project currently)  
**Target Platform**: Linux container (Azure Container App)  
**Project Type**: Background worker service  
**Performance Goals**: Process ~200 vehicles per 10-second cycle; spatial reconciliation <500ms per cycle  
**Constraints**: Route index must be thread-safe for concurrent reads; individual route has <3,000 shape points  
**Scale/Scope**: ~100 MARTA bus routes, ~200 active vehicles at peak

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | PASS | Change is internal to the Worker service; no architectural boundary changes |
| II. No Frontend Secrets | PASS | No frontend changes |
| III. Real-time Data Processing Pipeline | PASS | Enhances the Worker's transform step — snapping accuracy improves without changing the pipeline structure |
| IV. OpenTelemetry Observability | PASS | Existing structured logging retained; new skip counters add observability |
| V. Azure DevOps CI/CD Pipeline | PASS | No build/deploy changes; same Docker image artifact |

**Post-Phase-1 Re-check**: All gates still pass. Moving `RoutePoint` and `HaversineCalculator` to Shared is an internal refactor within the same solution — no new deployment artifacts or architectural boundaries introduced.

## Project Structure

### Documentation (this feature)

```text
specs/005-worker-route-snap-refactor/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output — research decisions
├── data-model.md        # Phase 1 output — entity and data structure design
├── quickstart.md        # Phase 1 output — change summary and test guide
├── contracts/
│   └── route-snapper.md # Phase 1 output — RouteSnapper API contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (created by /speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.TransitJazz.Shared/
│   ├── Geospatial/                    # NEW directory
│   │   ├── RoutePoint.cs              # MOVED from Server.TransitDataWorker
│   │   ├── RouteSnapper.cs            # NEW — FindNearest(), FindNearestN(), Snap type
│   │   └── HaversineCalculator.cs     # MOVED from Server.TransitDataWorker
│   ├── Events/                        # unchanged
│   └── GtfsData/                      # unchanged
│
├── Server/
│   └── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
│       ├── Worker.cs                  # MODIFIED — new index, new reconciliation logic
│       ├── RoutePoint.cs              # DELETED (moved to Shared)
│       ├── HaversineCalculator.cs     # DELETED (moved to Shared)
│       ├── GeohashEncoder.cs          # DELETED (no longer used after refactor)
│       ├── VehicleState.cs            # unchanged
│       ├── EventMapper.cs             # unchanged
│       └── ...                        # other files unchanged
```

**Structure Decision**: Geospatial utilities (`RoutePoint`, `HaversineCalculator`, `RouteSnapper`) move to `Shared/Geospatial/` so both Worker and WebAPI can reference them without cross-project dependencies. This follows the existing pattern where shared types live in the Shared project (e.g., `Events/`, `GtfsData/`).

## Complexity Tracking

No constitution violations. No complexity justifications needed.
