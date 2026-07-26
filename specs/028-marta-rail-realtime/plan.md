# Implementation Plan: MARTA Rail Realtime

**Branch**: `028-marta-rail-realtime` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/028-marta-rail-realtime/spec.md`
**Design source**: `docs/MARTA_RAIL_REALTIME_DESIGN_DOCUMENT.md` (all open questions OQ-1..OQ-4 resolved)

## Summary

Add MARTA heavy-rail trains (RED / GOLD / BLUE / GREEN) to the soundscape by ingesting the
MARTA Rail Realtime JSON API in the `TransitDataWorker` and normalizing it into the
**existing** `FeedMessage` shape the spatial-reconciliation loop already consumes. The rail
geometry is already loaded server-side (rail routes are indexed under keys `BLUE/GOLD/GREEN/
RED` because `GtfsStaticLoader` ingests all routes and `Worker.BuildRouteIndex` keys by
`RouteShortName ?? RouteId`), so the feed's `LINE` value maps to the route index with zero
translation. The work is a **single best-effort adapter** (`RailRealtimeAdapter`) plus a
**merge line** in `Worker.ExecuteAsync`. Trains then ride the unchanged snap → lerp →
telemetry → SignalR path to the client, where the existing route-aware animator
(`vehicle-animator.js`) and hash-assigned Tone.js voices handle them with no client change.

**Primary technical approach**: adapter normalization, not new architecture. Filter
`IS_REALTIME != "true"` → de-dup per `TRAIN_ID` → build one `FeedEntity` per train → concat
into the bus `FeedMessage` before `ProcessSpatialReconciliationAsync`. Best-effort: any
rail-fetch failure returns an empty entity list so the bus path is never affected.

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: `IHttpClientFactory`, `System.Text.Json` (JSON feed parse),
ProtoBuf-net (existing `FeedMessage` model — reused, not re-serialized), existing
`RouteSnapper` / `HaversineCalculator` (Shared.Geospatial)
**Storage**: N/A (in-memory; no persistence change). Existing `_vehicleStateCache`
(`ConcurrentDictionary`) absorbs trains as additional entities.
**Testing**: Manual in-app verification + telemetry inspection via `mj-data-explorer`
(snap/cycle datasets); runtime contract assertion (single coord per `TRAIN_ID`). No unit-test
project exists for the worker today; follow the repo's existing manual/telemetry verification
pattern (see prior features 013/019/023 quickstarts).
**Target Platform**: Server — `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker` (Docker
background service). No WASM/client deployable touched.
**Project Type**: Decoupled cloud app (Constitution Principle I). This feature is **worker-only**.
**Performance Goals**: Negligible — only ~16 trains system-wide at peak vs. hundreds of buses;
one extra HTTP GET per existing 10 s tick. No new timer.
**Constraints**: Best-effort rail fetch MUST NOT delay/break the bus path (FR-008);
additive-only — bus set identical with rail on/off (FR-009). API key never committed (FR-012,
Constitution Principle II).
**Scale/Scope**: 4 rail lines, ≤ ~16 concurrent trains. One new adapter class + one new DTO +
~3 added lines in `Worker.cs` + DI/config registration in `Program.cs` + appsettings.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | ✅ Pass | Change is confined to the `TransitDataWorker` deployable; SignalR/WebAPI/WASM untouched. |
| II. No Frontend Secrets | ✅ Pass | Rail API key is a **server** secret loaded from config/env/user-secrets; never enters the WASM bundle. Mirrors feature-012 FR-020 remediation. |
| III. Two-Pass Real-Time Pipeline | ✅ Pass | Trains flow into the **existing** V2 reconciliation (`ProcessSpatialReconciliationAsync`) as ordinary entities; `_vehicleStateCache` delta/prune logic reused unchanged. No new pass. |
| IV. OpenTelemetry Observability | ✅ Pass | Adapter logs structured warnings on fetch failure; trains emit the existing snap/lerp/cycle telemetry through the unchanged loop. |
| V. Azure DevOps CI/CD | ✅ Pass | No new deployable, no artifact change. |
| VI. GTFS ID Mapping | ✅ Pass | Rail joins on `route_short_name` (`LINE` = `RED/GOLD/BLUE/GREEN`), exactly the constitutional join key; matches `_routeIndex` keys with zero translation. |
| VII. OpenStreetMap Cartography | ✅ Pass | No basemap/layer change; trains render via existing bus-marker GeoJSON layer. |
| VIII. Generative Transit Music | ✅ Pass | Voices assigned by the **deterministic** `instrumentFor(routeId)` djb2 hash — no per-route authoring; rail keys hash like any route (OQ-4). |
| IX. Multi-Selection Interaction | ✅ Pass | Rail routes surface in the filter automatically as routes; no interaction-model change. |
| X. Zoom-Adaptive Controls | ✅ Pass | No control change. |
| XI. Snappy Reversible Overlays | ✅ Pass | No overlay change. |
| XII. Internationalized, Settings-Driven | ✅ Pass | No new user-facing copy; no settings change. |

**Gate result: PASS** — no violations; Complexity Tracking section omitted.

## Project Structure

### Documentation (this feature)

```text
specs/028-marta-rail-realtime/
├── plan.md              # This file
├── research.md          # Phase 0 — feed cadence, dedup, motion, voice, security findings
├── data-model.md        # Phase 1 — RailArrivalDto + RailTrain → FeedEntity mapping
├── quickstart.md        # Phase 1 — build-time spike + verification steps
├── contracts/
│   ├── rail-realtime-feed.md   # Inbound: MARTA JSON element schema + accept/reject vectors
│   └── feed-adapter.md         # Outbound: FeedMessage shape the adapter MUST emit
└── checklists/
    └── requirements.md         # Spec quality checklist (from /speckit.specify)
```

### Source Code (repository root)

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── RailRealtime/                          # NEW folder
│   ├── RailArrivalDto.cs                  # NEW — JSON DTO mirroring one feed element (all strings)
│   ├── RailRealtimeAdapter.cs             # NEW — IRailRealtimeAdapter: fetch → filter → dedup → emit FeedMessage
│   └── RailRealtimeOptions.cs             # NEW — bound config (BaseUrl, ApiKey, Enabled)
├── Worker.cs                              # EDIT — fetch rail feed + merge entities into the tick (≈3 lines)
├── Program.cs                             # EDIT — register named HttpClient + IRailRealtimeAdapter + bind options
└── appsettings*.json                      # EDIT — add Marta:RailRealtime BaseUrl (key via secrets/env)
```

**Structure Decision**: Worker-only. A new `RailRealtime/` folder isolates the adapter,
DTO, and options (mirrors the existing `Logging/` sidecar folder convention). `Worker.cs`
and `Program.cs` get minimal additive edits. No Shared/, Server.WebAPI, or Client changes.

## Key Design Decisions (grounded in current code)

1. **Adapter emits `FeedMessage`** (`GtfsRtModels.cs`): one `FeedEntity` per de-duped train —
   `Id = TRAIN_ID`; `Vehicle.Vehicle.Id = TRAIN_ID`; `Vehicle.Trip.RouteId = LINE`;
   `Vehicle.Position = { Latitude, Longitude }` (parsed `double` → `float`, `InvariantCulture`);
   `Vehicle.Position.Speed = null`, `Bearing = null`; `Vehicle.Timestamp = ` parsed `EVENT_TIME`
   as Unix seconds (drives the staleness check at `Worker.cs:197`).
2. **Merge in `ExecuteAsync`** (`Worker.cs:41-48`): after `FetchGtfsRtFeedAsync`, call
   `railAdapter.FetchAsync(ct)` and concat its entities into `feed.Entities`. Guard so a null
   bus feed still ships rail and vice-versa; reconciliation runs if `_routeIndex != null` and
   the merged feed has any entities.
3. **Best-effort isolation** (FR-008): `RailRealtimeAdapter.FetchAsync` catches all and returns
   an **empty** `IReadOnlyList<FeedEntity>` (never throws into the loop), mirroring
   `FetchGtfsRtFeedAsync`'s null-on-failure behavior (`Worker.cs:550`).
4. **De-dup + contract guard** (FR-003, FR-013): group rows by `TRAIN_ID`; assert all rows for a
   train share one `(LATITUDE, LONGITUDE)`; log a loud warning if violated (signals the feed's
   live-position contract changed — OQ-1). Pick any one row per train.
5. **Realtime filter** (FR-004): drop rows where `IS_REALTIME != "true"` **before** dedup (OQ-2).
6. **No collision risk**: train IDs (`"401"`) and route keys (`RED`) are naturally namespaced
   away from bus vehicle IDs and numeric bus route keys, so `_vehicleStateCache` is safe.
7. **Motion/voice are free** (OQ-3, OQ-4): client `vehicle-animator.js` route-aware
   extrapolation + `MAX_EXTRAPOLATION_MS` cap absorb the coarse rail cadence; `instrumentFor`
   hash assigns rail voices. **No client change in v1.** ETA-pacing is an explicit non-goal.

## Phase 0 — Research

See [research.md](./research.md). All four design open questions are pre-resolved by the design
doc's 2026-06-23 live probe; Phase 0 records those decisions plus the build-time confirmation
spike (re-probe cadence/dedup against the live endpoint before wiring).

## Phase 1 — Design & Contracts

- [data-model.md](./data-model.md): `RailArrivalDto` (raw JSON), `RailTrain` (de-duped
  intermediate), and the field-by-field `RailTrain → FeedEntity` mapping with parse/validation
  rules.
- [contracts/rail-realtime-feed.md](./contracts/rail-realtime-feed.md): inbound MARTA element
  schema + accept/reject vectors (non-realtime dropped, unparseable lat/lon skipped, multi-coord
  train → loud assert).
- [contracts/feed-adapter.md](./contracts/feed-adapter.md): the `FeedMessage`/`FeedEntity`
  contract the adapter MUST emit so reconciliation cannot tell trains from buses.
- [quickstart.md](./quickstart.md): build-time spike + the 7-step verification plan (dedup
  guard, snap correctness, in-app motion, voice, no bus regression, realtime filter, key safety).
- **Agent context update**: the `CLAUDE.md` SPECKIT block is updated to reference this plan.

## Phase 2 — (handled by `/speckit.tasks`)

This command stops here. `/speckit.tasks` will turn the above into an ordered tasks.md.
