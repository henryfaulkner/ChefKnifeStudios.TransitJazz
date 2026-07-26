# Implementation Plan: Stale Snapshot Filter

**Branch**: `023-stale-snapshot-filter` | **Date**: 2026-06-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/023-stale-snapshot-filter/spec.md`

## Summary

The cold-start snapshot served by `GET /transit/last-batch` can be entirely stale (duplicate GPS readings the client discards), leaving a blank map until the first live SignalR batch (~10s). The fix restructures the in-memory `LastBatchCache` from a single-slot "last raw batch" holder into a **per-vehicle accumulator**: on each `Set`, non-stale records upsert a `Dictionary<string, RouteNearestPointRecord>` keyed by `VehicleId`, stale records are ignored, and the cache rebuilds an immutable single-envelope snapshot under a lock. `Current` returns the prebuilt snapshot via `Volatile.Read`. The live SignalR relay in `WorkerTransitHub.PublishBatch` is untouched (still sends the full raw batch, including stale records). The `ILastBatchCache` interface, DI registration, endpoint, hub, and all record/contract shapes are unchanged. Server-side only.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: ASP.NET Core (Minimal API + SignalR), xUnit (tests). No new packages.  
**Storage**: In-process memory only (the `LastBatchCache` singleton's per-vehicle dictionary). No persistence, no Redis.  
**Testing**: xUnit in `ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` (`dotnet test`).  
**Target Platform**: Linux/Windows server (ASP.NET Core WebAPI host).  
**Project Type**: Web service (server-side component of the decoupled architecture).  
**Performance Goals**: Snapshot read (`Current`) remains a cheap lock-free `Volatile.Read` of a prebuilt reference; merge cost is O(records in batch) per ~10s publish, well within headroom.  
**Constraints**: Read-modify-write merge must be thread-safe (lock); reads must never observe a torn/partial state; live broadcast byte-for-byte unchanged; bounded memory (one entry per VehicleId ever seen — hundreds, not millions).  
**Scale/Scope**: One MARTA fleet (hundreds of buses); a single in-process singleton; ~186 records per batch observed.

**Note on namespaces**: The constitution lists `ChefKnifeStudios.TransitJazz.*`; the actual code uses `ChefKnifeStudios.TransitJazz.*`. This plan follows the **real codebase namespaces**.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status |
|-----------|-----------|--------|
| I. Decoupled Cloud Architecture | Change is confined to the WebAPI unit (SignalR hub relay + REST snapshot). No new deployable, no cross-unit contract change. | ✅ Pass |
| III. Two-Pass Real-Time Pipeline | `IsStale` is the V1/V2 stale-detection concept already defined here. We consume the existing `RouteNearestPointRecord.IsStale` flag; we do not alter the Worker's two-pass emission. | ✅ Pass |
| IV. OpenTelemetry / Structured Logging | Existing debug/info logs in the hub and endpoint are preserved; merge/rebuild adds no console noise. | ✅ Pass |
| VII. OSM Cartography / data layers persist | Snapshot delivers the same `RouteNearestPointBatchEvent` shape the client already consumes; data-layer handling on the client is unchanged. | ✅ Pass (no client change) |
| XII / Localization | No user-facing copy added (server-side, no `.razor`/UI strings). | ✅ N/A |
| Tech Stack Enforcement | .NET 10, ASP.NET Core, xUnit — no new technology, no new package. | ✅ Pass |

**Gate result**: PASS — no violations, Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/023-stale-snapshot-filter/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── last-batch-cache.md
└── checklists/
    └── requirements.md  # From /speckit-specify
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.TransitJazz.Shared/
│   └── Events/
│       ├── EventEnvelope.cs                  # UNCHANGED (read-only reference)
│       └── RouteNearestPointBatchEvent.cs    # UNCHANGED (IsStale flag lives here)
│
└── Server/
    ├── ChefKnifeStudios.TransitJazz.Server.WebAPI/
    │   ├── SignalR/
    │   │   ├── ILastBatchCache.cs            # MODIFIED — LastBatchCache impl rewritten; interface UNCHANGED
    │   │   └── WorkerTransitHub.cs           # UNCHANGED (still Set(batch) + relay full batch)
    │   ├── EndpointGroups/
    │   │   └── TransitEndpoints.cs           # UNCHANGED (still returns cache.Current)
    │   └── Program.cs                        # UNCHANGED (AddSingleton already correct)
    │
    └── ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/
        ├── LastBatchCacheTests.cs            # MODIFIED — rewrite 3 inverted tests, add 6 new, richer factory
        └── WorkerTransitHubTests.cs          # UNCHANGED (both hub tests stay green)
```

**Structure Decision**: This is the existing web-service layout. The entire production change lands in one file — `ILastBatchCache.cs` (the `LastBatchCache` class body) — plus its test file. The interface, hub, endpoint, DI registration, and all shared record shapes are deliberately untouched, so the blast radius is a single class implementation and its tests.

## Complexity Tracking

> No Constitution Check violations. Section intentionally empty.
