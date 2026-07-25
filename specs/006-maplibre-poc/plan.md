# Implementation Plan: MapLibre + MapTiler Side-by-Side POC

**Branch**: `006-maplibre-poc` | **Date**: 2026-05-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/006-maplibre-poc/spec.md`

## Summary

Add a parallel client page (`MapLibreTest.razor`) that renders the same live MARTA SignalR vehicle stream as the existing `TransitMap.razor`, but using MapLibre GL JS against MapTiler-hosted vector tiles instead of Azure Maps. The existing page and its supporting code remain untouched. Both pages are instrumented with the same named browser performance measurements (cold-load time, per-frame timing, long-task count, transferred bytes) so the migrate / don't-migrate decision is grounded in numeric comparison rather than impression. The POC is timeboxed to one working day with a noon checkpoint, and a hard-gate failure defaults to don't-migrate.

The animator (`vehicle-animator.js`) is ~95% provider-agnostic — only four touch points (datasource lookup, shape-by-id lookup, `shape.setCoordinates`, and `atlas.data.Feature` construction) are Azure-specific. The plan ports those four touch points to MapLibre's GeoJSON-source data-replacement pattern in a parallel animator file, leaving the original untouched for side-by-side comparison.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WebAssembly), JavaScript ES2017+ (browser interop)
**Primary Dependencies (new)**: MapLibre GL JS v4.x (CDN), MapTiler vector tiles (free tier, ≤100K loads/mo)
**Primary Dependencies (reused)**: `Microsoft.AspNetCore.SignalR.Client` (existing), `Microsoft.JSInterop` (existing), the existing `NotificationService` and `IGtfsEndpointsService`
**Storage**: In-browser only — `routeGeometry` map (RAF animator state), MapLibre GeoJSON source (`vehicles`). No server-side persistence added by this feature.
**Testing**: Manual measurement via Chrome DevTools Performance panel, Lighthouse, and `performance.mark()` / `PerformanceObserver` instrumentation. No automated test framework added.
**Target Platform**: Modern WebGL-capable browser (Chrome/Edge/Firefox latest). Mobile Safari not in scope for POC measurement.
**Project Type**: Web frontend feature (parallel page within existing Blazor WASM app)
**Performance Goals**: Cold-load tiles visible ≤1.5s on home internet, cold cache; sustained ≥45 FPS during a 10-second SignalR batch interval with ~200 markers; zero long tasks (>50ms) during that interval.
**Constraints**: POC must be timeboxed to 1 working day with a noon qualitative checkpoint; hard-gate failure defaults to don't-migrate; both pages measured in the same browser/machine/network/session.
**Scale/Scope**: ~200 simultaneous animated markers, ~100 routes available with ≥5 simultaneously visible, route geometries up to ~3,000 points each. Single-user POC traffic (developer measurement session), not production load testing.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | PASS | Change is internal to the Blazor WASM frontend; no architectural boundary changes. No new services, no SignalR contract changes. |
| II. No Frontend Secrets | **CONDITIONAL PASS — documented exception** | The constitution's wording binds the no-secrets principle specifically to Azure Maps and the Azure Maps Auth Function. MapTiler's web auth model uses a *public* URL-restricted API key embedded in the client, not a secret. The key is restricted by domain/origin at the MapTiler side and is safe to embed in the WASM bundle. **No secret is held by the frontend.** This is an interpretation of the principle's intent (don't ship secrets), not a violation. See Complexity Tracking for the explicit justification. If the POC results in a migrate decision, the constitution itself should be revised to reflect the new provider's auth model rather than treating this as a permanent exception. |
| III. Two-Pass Real-Time Data Processing Pipeline | PASS | Worker and SignalR event pipeline are not touched. POC page consumes the same `RouteNearestPointBatchEvent` and `VehiclePositionUpdatedEvent` payloads. |
| IV. OpenTelemetry Observability | PASS | No backend changes; client-side `console.log` instrumentation in animator is preserved. New browser performance marks are local diagnostic measurements, not subject to OTEL backend reporting. |
| V. Azure DevOps CI/CD Pipeline | PASS | No build/deploy changes. Same WASM artifact, same Docker image. MapLibre is loaded from a CDN at runtime, mirroring how Azure Maps is loaded today (also from a CDN). |
| VI. GTFS ID Mapping | PASS | POC consumes the same `RouteShapeFeature` / `route_short_name` join key as the existing page; no GTFS data path changes. |

**Post-Phase-1 Re-check**: All gates remain in the same status. The MapLibre interop, parallel animator, and POC page are entirely within `Client.Shared` and `Client.WebApp`; nothing crosses into Server, Worker, or Shared event contracts.

## Project Structure

### Documentation (this feature)

```text
specs/006-maplibre-poc/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output — research decisions
├── data-model.md        # Phase 1 output — animator state + measurement set
├── quickstart.md        # Phase 1 output — how to run the POC day
├── contracts/
│   └── maplibre-interop.md   # Phase 1 output — JS interop contract for the new component
├── checklists/
│   └── requirements.md       # Spec quality checklist (already exists)
└── tasks.md             # Phase 2 output (created by /speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── Client/
│   ├── ChefKnifeStudios.TransitJazz.Client.Shared/
│   │   └── Components/
│   │       ├── Map.razor                       # unchanged
│   │       ├── Map.razor.cs                    # unchanged
│   │       ├── Map.razor.Helper.cs             # unchanged
│   │       ├── MapLibre.razor                  # NEW — parallel component, MapLibre-backed
│   │       ├── MapLibre.razor.cs               # NEW — Blazor lifecycle + JS interop bindings
│   │       └── MapLibre.razor.Helper.cs        # NEW — mirrors Map.razor.Helper.cs methods
│   │
│   └── ChefKnifeStudios.TransitJazz.Client.WebApp/
│       ├── Pages/
│       │   ├── TransitMap.razor                # unchanged (baseline)
│       │   ├── TransitMap.razor.cs             # unchanged (baseline)
│       │   ├── AzureMapsTest.razor             # unchanged
│       │   ├── AzureMapsTest.razor.cs          # unchanged
│       │   ├── MapLibreTest.razor              # NEW — POC page, mirrors TransitMap.razor data wiring
│       │   └── MapLibreTest.razor.cs           # NEW — wires NotificationService → MapLibre component
│       │
│       ├── wwwroot/
│       │   ├── index.html                      # MODIFIED — add MapLibre GL JS + CSS script/link tags
│       │   ├── appsettings.json                # MODIFIED — add MapTiler:ApiKey + MapTiler:StyleUrl
│       │   └── js/
│       │       ├── azure-maps-interop.js       # unchanged
│       │       ├── vehicle-animator.js         # unchanged (Azure-coupled, kept for baseline)
│       │       ├── maplibre-interop.js         # NEW — parallel to azure-maps-interop.js
│       │       └── maplibre-vehicle-animator.js  # NEW — ported animator using MapLibre source updates
│       │
│       └── (no other changes)
│
└── (no other projects modified)
```

**Structure Decision**: The POC introduces a parallel `MapLibre.razor` component and `MapLibreTest.razor` page rather than abstracting `Map.razor` to support multiple providers. The abstraction would be premature (sample size of 2, one of which is being evaluated for deletion) and would force a leaky interface across providers with different style/auth/data models. If the POC yields a migrate decision, the follow-on feature deletes `Map.razor` + Azure interop + `vehicle-animator.js`, renames `MapLibre.razor` → `Map.razor`, and updates `TransitMap.razor` accordingly. If the POC yields a don't-migrate decision, the new files remain as a documented dead-end so the question doesn't reopen blindly.

## Phase 0 Research

See [research.md](research.md). Resolves the following research items:

1. MapLibre GL JS marker-update strategy under high-frequency (60 Hz) updates at ~200 markers — GeoJSON source `setData` vs `feature-state` vs custom layer
2. MapTiler free tier limits, API-key model (URL-restricted public key vs server-issued token), and applicable styles
3. Performance measurement protocol — what to record, how to record it, and how to compare both pages apples-to-apples
4. Mapping the four Azure-specific touch points in `vehicle-animator.js` to MapLibre equivalents

## Phase 1 Design

### Data Model

See [data-model.md](data-model.md). Documents:

- The animator state machine ports unchanged (`idle` → `interpolating` → `extrapolating`); only the storage representation changes from Azure Maps `DataSource` shapes to a MapLibre GeoJSON source.
- The route geometry cache (`routeGeometry[routeId] = { coords, cumDist }`) ports unchanged — it's pure JS, not provider-bound.
- The `Performance Measurement Set` entity from the spec is given concrete shape: a list of named `performance.mark()` / `performance.measure()` entries plus a `PerformanceObserver` long-task buffer, dumped to console as JSON at end-of-measurement-interval.

### Contracts

See [contracts/maplibre-interop.md](contracts/maplibre-interop.md). Documents:

- The JS-side `ChefMapLibre` namespace (parallel to `ChefMap`) with the same method surface: `createMap`, `setMapZoom`, `plotFeatures`, `showRouteShape`, `clearRouteShape`, `addRouteShapeFeature`, click event registration.
- The animator namespace `ChefMapLibreAnimator` with the same `loadRouteGeometry` and `processNearestPointBatch` entry points used by `TransitMap.razor.cs`-equivalent code, so `MapLibreTest.razor.cs` can be a near-direct copy with two type substitutions.
- The Blazor-side `MapLibre.razor.cs` exposes the same `EventCallback` parameters and `JSInvokable` methods as `Map.razor.cs` (`OnMapReady`, `OnMapBodyClicked`, `OnBusMarkerClicked`, `getMapSettings`) so a future migration is a delete-and-rename rather than a rewrite.

### Quickstart

See [quickstart.md](quickstart.md). Documents:

- The hour-by-hour POC day schedule (morning: setup + noon checkpoint; afternoon: instrumentation + measurement)
- How to obtain a MapTiler API key with a URL restriction
- How to disable browser cache and run a cold-load measurement on each page
- How to record a 10-second DevTools Performance trace during peak MARTA hours
- The exact list of measurements to capture and the decision-record template

### Agent Context

`CLAUDE.md`'s SPECKIT marker block has been updated to point to this plan (`specs/006-maplibre-poc/plan.md`) as the active feature plan.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Constitution Principle II names Azure Maps explicitly; the POC introduces a different tile provider with a different auth model (URL-restricted public key, not a server-issued token). | The whole purpose of the POC is to evaluate replacing Azure Maps for cost and load-time reasons. Re-implementing MapTiler behind a server-issued-token endpoint to literally satisfy the principle's wording would (a) add ≥1 day of unnecessary server work to a 1-day POC, (b) introduce a service that we'd delete on either decision outcome, and (c) miss the principle's *intent* — which is "no secrets in the frontend bundle," a condition the URL-restricted public key already satisfies. | Implementing a server-side `/maptiler/auth/token` endpoint (paralleling `MapsEndpoints.GetMapsAuthToken`) would satisfy the letter of the principle but waste a measurable fraction of the POC day on infrastructure that exists only to wrap a key that doesn't need wrapping. The cost-of-compliance exceeds the value-of-compliance. The compensating control is the URL restriction on the MapTiler key, which prevents the key from being used by any origin other than the project's own domain(s). If the POC succeeds and a migration follows, the constitution itself should be amended to recognize MapTiler's auth model alongside Azure Maps' auth model — the migration is the right moment to make that change, not the POC. |
