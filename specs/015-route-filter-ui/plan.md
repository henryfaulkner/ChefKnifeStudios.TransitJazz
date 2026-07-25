# Implementation Plan: Route Filter UI — Focus, Map Blur & Blurb

**Branch**: `015-route-filter-ui` | **Date**: 2026-06-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/015-route-filter-ui/spec.md`

## Summary

The route filter grid and its single-focus selection state already exist (`RouteFilters` +
`RouteFilterViewModel`, shipped in #14). This feature wires that focus state into the rest of the
experience to complete Principle IX's single-focus model:

1. **Highlight + blur on the map** — when a route is focused, emphasize its `route-layer-<id>` line and
   grey/blur every other route layer; restore all layers instantly on unfocus. Implemented with new
   `ChefMap` interop methods that mutate per-layer MapLibre paint properties (`line-opacity`,
   `line-color`) — no geometry re-fetch, layers persist across basemap swaps.
2. **Bottom blurb bar** — a new full-width overlay component, driven by the same focus state, fading in
   over 100ms and disappearing instantly. Content comes from a static client blurb store keyed by route;
   absent entries fall back to a placeholder that names the route.

The connective tissue is the existing scoped `RouteFilterViewModel`: `TransitMap` and the new blurb
component both observe its `RouteItems`/`HasSelection` `PropertyChanged` notifications (the same
subscription pattern `RouteFilters` already uses) and react. No new state machine is introduced — the
VM remains the single source of truth for "which route is focused."

Localization (FR-011): minimal `IStringLocalizer`-based `.resx` infrastructure is stood up now with
**English resources only**; Spanish is deferred to a follow-up (see Complexity Tracking). The
placeholder string is sourced from the resource rather than hardcoded inline, satisfying the
"no hardcoded copy where a resource is feasible" intent and leaving the es-swap to a later i18n pass.

## Technical Context

**Language/Version**: C# / .NET 10.0; JavaScript (ES modules) for MapLibre interop
**Primary Dependencies**: Blazor WebAssembly, MapLibre GL JS (over MapTiler), MatBlazor,
CommunityToolkit.Mvvm (`[ObservableProperty]`), `Microsoft.Extensions.Localization` (`IStringLocalizer`)
**Storage**: Static client data file for route blurbs (in-memory dictionary built from a C# data class);
no persistence, no backend changes
**Testing**: Manual verification per quickstart (no automated client UI test harness exists in this repo);
build via `dotnet build` on the WebApp project
**Target Platform**: Browser (Blazor WASM); web (hover) + mobile (tap)
**Project Type**: Web application — Blazor WASM frontend only; this feature touches **no** server,
worker, or shared backend code
**Performance Goals**: Focus reaction (highlight + blur + blurb in) completes within 100ms; teardown is
immediate (no exit animation) per Principle XI
**Constraints**: Frontend-only; reuse existing route layers (`route-layer-<routeId>`) and the existing
`RouteFilterViewModel`; no new secrets; data layers must survive a GIS basemap swap
**Scale/Scope**: ~40 MARTA routes rendered as individual map line layers; one focused route at a time;
3 user stories, 11 functional requirements

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against TransitJazz Constitution v3.1.0. This is a frontend UX feature; the relevant gates are
Principles VII, IX, X, XI, XII and the engineering principles.

| Principle | Relevance | Status |
|-----------|-----------|--------|
| VII. OpenStreetMap-Based Cartography | Highlight/blur mutate the **persistent GeoJSON data layers** on top of the basemap, never the basemap; layers survive a style swap | ✅ PASS |
| IX. Hover-to-Filter, Single-Focus | This feature *implements* the map-blur + blurb-with-placeholder half of IX; reuses the existing single-focus grid; ≤1 focused route | ✅ PASS |
| X. Zoom-Adaptive Controls | Zoom-adaptive grid anchoring is **out of scope** (separate feature); this feature does not move the grid | ✅ N/A (not regressed) |
| XI. Snappy, Reversible Overlays | Blurb bar fades in 100ms, disappears immediately; map blur tears down with no exit animation | ✅ PASS |
| XII. Internationalized, Settings-Driven | Placeholder sourced from `IStringLocalizer` `.resx`; **English only now, Spanish deferred** — partial, tracked below | ⚠️ PARTIAL (justified) |
| I–VI (engineering) | No architecture, secrets, pipeline, or GTFS-mapping changes | ✅ N/A |

**Gate result**: PASS with one tracked partial (XII — Spanish deferred). No unjustified violations.

## Project Structure

### Documentation (this feature)

```text
specs/015-route-filter-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (JS interop + VM contracts)
│   ├── chefmap-focus-interop.md
│   └── route-blurb-store.md
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

All changes are within the Blazor WASM client. No server/worker/shared changes.

```text
src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/
├── wwwroot/js/
│   └── map-interop.js                      # MODIFY: add focusRoute / clearRouteFocus to window.ChefMap
├── Components/
│   ├── Map.razor.Helper.cs                 # MODIFY: add FocusRouteAsync / ClearRouteFocusAsync interop wrappers
│   ├── RouteBlurbBar.razor                 # NEW: full-width bottom overlay
│   ├── RouteBlurbBar.razor.cs              # NEW: observes RouteFilterViewModel focus state
│   └── RouteBlurbBar.razor.css             # NEW: 100ms fade-in, instant hide, dark translucent bar
├── ViewModels/
│   └── RouteFilterViewModel.cs             # MODIFY (minimal): expose SelectedRouteId convenience for consumers
├── Data/
│   ├── RouteBlurb.cs                        # NEW: blurb record (RouteId, Instrument/Key text, Significance)
│   └── RouteBlurbStore.cs                   # NEW: static keyed store + IRouteBlurbStore lookup w/ placeholder fallback
└── Resources/
    └── RouteFilterResources.resx            # NEW: English placeholder + blurb-bar strings (IStringLocalizer source)

src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/
├── Pages/
│   ├── TransitMap.razor                    # MODIFY: render <RouteBlurbBar /> over the map
│   └── TransitMap.razor.cs                 # MODIFY: inject IRouteFilterViewModel, subscribe, drive map focus interop
└── Program.cs                              # MODIFY: register IRouteBlurbStore; AddLocalization()
```

**Structure Decision**: Web application, frontend-only slice. New presentational pieces
(`RouteBlurbBar`, blurb data store, resources) live in the shared RCL
(`Client.Shared`) so they sit beside the existing `RouteFilters`/`Map` components; wiring and DI
registration live in the `WebApp` host (`TransitMap`, `Program.cs`), matching the established split.

## Complexity Tracking

> Only the justified partial on Principle XII is tracked; no other deviations.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Principle XII satisfied for **English only**; Spanish `.resx` deferred | The client has **no** existing localization infrastructure; standing up `IStringLocalizer` + en `.resx` and routing the placeholder through it is the minimum that honors "no hardcoded copy." Authoring/maintaining a full es translation set (and a culture switcher) is a cross-cutting concern beyond this focus/blur/blurb slice. | Hardcoding the string outright was rejected (violates XII outright and leaves no seam). Doing full app-wide EN/ES now was rejected as scope creep that would block the core map behavior. The chosen path leaves a clean seam: add `RouteFilterResources.es.resx` later with no code change. |
