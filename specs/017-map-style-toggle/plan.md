# Implementation Plan: Map Style Toggle

**Branch**: `017-map-style-toggle` | **Date**: 2026-06-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/017-map-style-toggle/spec.md`

## Summary

Add one boolean setting to the already-shipped Settings Blade (feature 016) that hot-switches the map's
basemap between two named MapTiler styles — **LightOff** (the new app default) and **LightOn** — with no page
reload. The setting persists with the rest of the settings (single JSON blob in local storage), is labeled
from the existing `.resx`, and is decoupled from the map through the existing `IEventNotificationService` bus,
exactly like the Audio and Checkpoint settings.

The basemap swap is performed by MapTiler/MapLibre's `map.setStyle(url)`. Because `setStyle` **replaces the
entire style object** (wiping all custom sources and layers), the JS layer captures the existing domain
GeoJSON sources/layers (routes, vehicles, trigger-points) before the swap and **re-adds them on the
`style.load` event** — never re-fetching from the API or the SignalR feed (Principle VII). Re-added layers
preserve their current visibility (e.g. a hidden checkpoint layer stays hidden), satisfying FR-007.

The two style URLs come from the project's existing `MapTiler:StyleUrls` config object (already present in
`appsettings.Development.json`; it must be added to the production `appsettings.json`). The map's **initial**
load also reads the persisted preference so a returning user paints in their chosen style from first render
(FR-009), defaulting to LightOff when unset (FR-001).

This feature effectively completes the GIS/basemap-toggle that was scoped in feature 016's plan but cut before
merge (commit `9726df0 remove "Street map" setting`); the no-op `ChefMap.setMapStyle` stub becomes real.

Frontend-only slice in the Blazor WASM client. No server, worker, or shared-backend changes.

## Technical Context

**Language/Version**: C# / .NET 10.0; JavaScript (ES, `window.ChefMap` global object) for the MapLibre interop
**Primary Dependencies**: Blazor WebAssembly, MatBlazor (`MatCheckbox` — already used by the blade),
`Blazored.LocalStorage` (`ISyncLocalStorageService` — already registered), CommunityToolkit.Mvvm
(`[ObservableProperty]` on the `Settings` model), `Microsoft.Extensions.Localization`
(`IStringLocalizer<RouteFilterResources>` — already wired), MapLibre GL JS over MapTiler (the `map.setStyle`
call)
**Storage**: Browser local storage — the existing single `Settings` JSON blob under key `"Setting"`; one new
bool field. No backend or schema changes.
**Testing**: Manual verification per quickstart (no automated client UI test harness exists); `dotnet build`
on the solution.
**Target Platform**: Browser (Blazor WASM); web (click) + mobile (tap)
**Project Type**: Web application — Blazor WASM frontend only; touches **no** server, worker, or shared code
**Performance Goals**: Toggle effect immediate (hot-switch, no reload); basemap swap completes within
MapLibre's normal `style.load` cycle; no perceptible loss of plotted data
**Constraints**: Frontend-only; reuse the existing `IEventNotificationService` bus, `SettingsService`,
`SettingsBlade` reflection render, and `Map`/`TransitMap` bus subscription; the basemap swap MUST re-add the
existing GeoJSON data layers **without re-fetching** (Principle VII) and MUST preserve current layer
visibility (FR-007); the new setting's label MUST come from `RouteFilterResources.resx` (Principle XII — no
inline copy); two named states only (LightOff/LightOn), the config's Dark variants are out of scope
**Scale/Scope**: One new boolean setting; one new event-args type; one real `setMapStyle` JS implementation +
one C# interop wrapper; config + resx additions; 3 user stories; 13 functional requirements

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against TransitJazz Constitution v3.1.1. This is a frontend UX feature extending the
constitution-mandated settings panel with the GIS/basemap control the panel is required to expose.

| Principle | Relevance | Status |
|-----------|-----------|--------|
| II. No Frontend Secrets | The style URLs are public, origin-restricted MapTiler URLs (same key already in the bundle); no secret introduced | ✅ PASS |
| VII. OpenStreetMap-Based Cartography | The swap changes only the **basemap style URL**; route/vehicle/trigger-point **GeoJSON data layers are captured and re-added on `style.load`, never re-fetched**; the basemap is the disposable element, the data layers are not | ✅ PASS |
| XI. Snappy, Reversible Overlays | No new overlay; the toggle lives in the existing blade. The basemap swap is immediate (no exit animation), consistent with the snappy model | ✅ PASS |
| XII. Internationalized, Settings-Driven | **Directly advances XII**: delivers the mandated **GIS basemap toggle** in the gear-FAB settings drawer; label sourced from `RouteFilterResources.resx` (EN; `.es` deferred with the rest, per 015/016 precedent) | ✅ PASS (advances a previously-partial principle) |
| IV. OpenTelemetry / structured logging | Interop failures logged via the existing `ILogger`/console pattern used by `Map`/interop services | ✅ PASS |
| I, III, V, VI, VIII, IX, X (engineering/other UX) | No architecture, pipeline, GTFS-mapping, audio, filtering, or zoom changes | ✅ N/A |

**Gate result**: PASS. No violations. This feature *reduces* the constitution debt tracked in feature 016
(the GIS toggle XII mandates but 016 deferred). Spanish `.resx` remains deferred — pre-existing, not
introduced here.

**Post-Phase-1 re-check**: Design adds one bool to the boolean-only `Settings` model (the blade's
pure-reflection render still holds), one event-args type matching the existing pattern, and a `setStyle`-based
swap specified as capture-and-restore of existing layers (VII upheld). No new violations. Gate still PASS.

## Project Structure

### Documentation (this feature)

```text
specs/017-map-style-toggle/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── contracts/           # Phase 1 output
    ├── map-style-events.md       # GisSettingChanged bus event payload + flow
    ├── map-style-interop.md      # ChefMap.setMapStyle / Map.SetBasemapStyleAsync contract
    └── style-config.md           # MapTiler:StyleUrls config contract + default selection
```

### Source Code (repository root)

All changes are within the Blazor WASM client. No server/worker/shared changes. Namespace root is
`ChefKnifeStudios.MartaJazz`, under `src/Client/`.

```text
src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/
├── Models/
│   └── Settings.cs                          # MODIFY: add [Description("SettingStreetMap")] bool _isStreetMapEnabled = false
├── EventArgs/
│   └── GisSettingChangedEventArgs.cs        # NEW: { IsStreetMapEnabled } (matches AudioSettingChangedEventArgs shape)
├── Components/
│   ├── Blades/
│   │   └── SettingsBlade.razor.cs           # MODIFY: add switch arm → post GisSettingChangedEventArgs
│   └── Map.razor.Helper.cs                  # MODIFY: add SetBasemapStyleAsync(string styleUrl) interop wrapper
│       Map.razor.cs                         # MODIFY: getMapSettings picks initial style from persisted setting
├── Resources/
│   └── RouteFilterResources.resx            # MODIFY: add "SettingStreetMap" label (EN)
└── wwwroot/js/
    └── map-interop.js                       # MODIFY: implement ChefMap.setMapStyle (capture layers → setStyle → re-add on style.load)

src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/
├── Pages/
│   └── TransitMap.razor.cs                  # MODIFY: handle GisSettingChangedEventArgs → resolve style URL → Map.SetBasemapStyleAsync
└── wwwroot/
    ├── appsettings.json                     # MODIFY: add MapTiler:StyleUrls { LightOff, LightOn, ... }; default style → LightOff
    └── appsettings.Development.json          # MODIFY (if needed): default selection already has StyleUrls; ensure LightOff default
```

**Structure Decision**: Web application, frontend-only slice. The setting, event-args, blade wiring, interop
wrapper, and JS live in the shared RCL (`Client.Shared`) beside the rest of the 016 settings code; the
per-setting effect wiring lives in the `WebApp` host (`TransitMap.razor.cs`) and config lives in the WebApp
`wwwroot` — matching the split established by feature 016. The existing bus, `SettingsService`, and reflection
render are reused, not re-created.

## Complexity Tracking

> No constitution violations. This feature advances Principle XII (delivers the mandated GIS toggle) and
> introduces no deviations requiring justification. Spanish `.resx` remains deferred as a pre-existing,
> separately-tracked item (features 015/016), not a deviation introduced by this plan.
