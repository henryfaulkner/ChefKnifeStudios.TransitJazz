# Implementation Plan: Mobile Map Controls & Wider Default Zoom

**Branch**: `025-mobile-map-controls` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/025-mobile-map-controls/spec.md`

## Summary

Three small, front-end-only MapLibre interaction changes, all inside the existing `Map` component and its `map-interop.js`:

1. **Wider default zoom** — lower the initial zoom from `9.5` to a wider value (research recommends `8.5`) in `TransitMap.razor.cs` `DefaultCameraOptions`, so first load shows more of the MARTA network on phone and desktop.
2. **Enable touch zoom** — the current `createMap` call passes `touchZoomRotate: false`, which disables BOTH pinch-zoom and touch-rotate in one flag. Replace it so pinch-to-zoom works while rotation stays disabled (`map.touchZoomRotate.enable()` then `map.touchZoomRotate.disableRotation()`), keeping the map north-up (Principle VII / FR-007).
3. **Drag controls + on-screen zoom** — add a MapLibre `NavigationControl` (zoom in/out buttons, no compass/pitch) for tap/click zoom, and confirm standard drag-pan works on touch and desktop. The bespoke ctrl+drag handler is retained as-is (it does not conflict with standard drag-pan).

No new sources/layers, no style swap, no server/worker/shared changes, no new resx strings (NavigationControl buttons use built-in aria-labels; if any visible app-authored copy is added it goes through `RouteFilterResources`).

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WASM) + JavaScript (ES, MapLibre GL JS)
**Primary Dependencies**: MapLibre GL JS (via MapTiler vector tiles), MatBlazor; no new packages
**Storage**: N/A (initial view is not persisted; default applied every load — see spec Assumptions)
**Testing**: Manual quickstart verification on a phone-sized and desktop viewport (no automated UI test harness in repo for map interop)
**Target Platform**: Blazor WebAssembly in modern mobile + desktop browsers
**Project Type**: Web frontend (single client app); changes confined to `Client.Shared` (RCL) and `Client.WebApp`
**Performance Goals**: No regression to the existing `requestAnimationFrame` vehicle animation loop (60fps target); interaction changes are configuration-only
**Constraints**: Map MUST remain north-up and flat (no rotate/tilt); zoom bounded by existing `minZoom: 7` / `maxZoom: 18`; manual gestures must not be overridden by automatic camera moves (FR-009)
**Scale/Scope**: ~3 edit sites (1 C# default value, `createMap` interop options + control registration); no data model

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status |
|-----------|-----------|--------|
| VII. OpenStreetMap-Based Cartography | Changes are MapLibre interaction config only; basemap stays MapLibre/MapTiler; route/checkpoint/vehicle GeoJSON layers untouched and not re-fetched. Map stays north-up/flat (no rotate/tilt enabled). | ✅ PASS |
| X. Zoom-Adaptive, Non-Occluding Controls | The on-screen NavigationControl is anchored bottom-right (or a non-occluding corner) and does not overlap the zoom-adaptive route filter grid (top-left/top-right). Wider default zoom keeps the grid's existing zoom-adaptive anchoring intact. | ✅ PASS |
| XI. Snappy, Reversible Overlays | No new overlays introduced; native zoom/pan animations are MapLibre defaults and not transient app overlays. | ✅ PASS (N/A) |
| XII. Internationalized, Settings-Driven Presentation | No new user-facing app copy. NavigationControl uses MapLibre's built-in localized button titles. If any app-authored visible string is added, it MUST come from `RouteFilterResources.resx`. | ✅ PASS |
| II. No Frontend Secrets | No credentials touched. | ✅ PASS |
| I / III / VI / VIII | Server/worker/audio/GTFS untouched. | ✅ PASS (N/A) |

No violations. No entries required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/025-mobile-map-controls/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (interaction contract)
│   └── map-interaction.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (already created)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Client/
├── ChefKnifeStudios.TransitJazz.Client.WebApp/
│   └── Pages/
│       └── TransitMap.razor.cs                 # EDIT: DefaultCameraOptions Zoom 9.5 → 8.5
└── ChefKnifeStudios.TransitJazz.Client.Shared/
    ├── Components/
    │   ├── Map.razor.cs                          # (no change expected; getMapSettings already passes zoom)
    │   └── Map.razor.Helper.cs                   # (review only — centerMap/fitBounds behavior re FR-009)
    └── wwwroot/js/
        └── map-interop.js                        # EDIT: createMap — enable pinch zoom (no rotate),
                                                   #        add NavigationControl, confirm dragPan
```

**Structure Decision**: Existing solution structure (constitution §Solution Structure) is unchanged. All edits land in the two client projects already responsible for the map: `Client.WebApp` (the page that supplies the default camera) and `Client.Shared` (the `Map` RCL component + its interop). No new projects, files, or layers.

## Complexity Tracking

> No constitution violations — section intentionally empty.
