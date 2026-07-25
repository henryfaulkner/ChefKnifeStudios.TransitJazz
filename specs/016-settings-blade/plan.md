# Implementation Plan: Settings Blade

**Branch**: `016-settings-blade` | **Date**: 2026-06-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/016-settings-blade/spec.md` + reference design document
`docs/SETTINGS_BLADE_DESIGN_DOCUMENT.md` (north star for implementation patterns)

## Summary

Implement the constitution-mandated settings panel (Principle XII / "Settings Panel"): a gear FAB in the
bottom-right opens a right-side slide-out drawer ("blade") that lets the user toggle application settings.
The drawer slides in over 100ms and dismisses **immediately** on the close button, an outside click, or a
re-click of the gear (Principle XI).

The structure follows the reference design document **verbatim in pattern**: a generic `BladeContainer`
slide-out shell + a `SettingsBlade` that **reflects over a boolean `Settings` model** and renders one
`MatCheckbox` per `bool` property, persisting to local storage via a `SettingsService`. The trigger and the
blade are decoupled through the **existing** `IEventNotificationService` bus (already in `Client.Core`),
exactly as the doc describes — no new bus is introduced.

Per the resolved clarifications, the shipped settings are **three booleans** — **Audio** (mute/unmute),
**GIS** (streets basemap ↔ blank dark canvas), and **Checkpoint visibility** (show/hide the procedurally
generated checkpoint markers). Because all three are booleans, the doc's **pure-reflection** render model is
used as-is. The constitution's **Language** selector and the doc's **Dark-Mode** toggle are **deferred**
(tracked in Complexity Tracking) — deferring Language mirrors the precedent already set in feature 015 (Spanish
`.resx` deferred). Each setting's effect is applied by posting a typed `IEventArgs` onto the existing bus; the
`Map`/`TransitMap` components (already bus-aware for theming) react — audio mute gates synth playback, GIS
swaps the MapTiler style URL while GeoJSON data layers persist (Principle VII), and checkpoint visibility
toggles the checkpoint layer.

No server, worker, or shared-backend changes. Frontend-only slice in the Blazor WASM client.

## Technical Context

**Language/Version**: C# / .NET 10.0; JavaScript (ES modules) for the outside-click interop
**Primary Dependencies**: Blazor WebAssembly, MatBlazor (`MatCheckbox`, `MatFAB`, `MatIconButton`),
`Blazored.LocalStorage` (already registered — `ISyncLocalStorageService`), CommunityToolkit.Mvvm
(`[ObservableProperty]` for the `Settings` model), `Microsoft.Extensions.Localization` (already registered
via `AddLocalization()`; used for the blade's visible strings), MapLibre GL JS over MapTiler (GIS toggle)
**Storage**: Browser local storage (single JSON blob under one key) for the `Settings` model; no backend, no
persistence schema changes
**Testing**: Manual verification per quickstart (no automated client UI test harness exists in this repo);
`dotnet build` on the solution
**Target Platform**: Browser (Blazor WASM); web (hover/click) + mobile (tap)
**Project Type**: Web application — Blazor WASM frontend only; touches **no** server, worker, or shared
backend code
**Performance Goals**: Drawer slide-in ≤100ms; dismissal and every toggle effect are immediate (no exit
animation), per Principle XI
**Constraints**: Frontend-only; reuse the existing `IEventNotificationService` singleton bus and the existing
`MainLayout`/bus subscription pattern; GIS toggle MUST swap only the basemap style and leave route/bus/
checkpoint GeoJSON layers intact (Principle VII); all blade copy sourced from `RouteFilterResources.resx`
(Principle XII — no hardcoded inline copy); single blade instance hosted once in `MainLayout`
**Scale/Scope**: One blade, one FAB, 3 boolean settings; 3 user stories; 14 functional requirements

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against TransitJazz Constitution v3.1.1. This is a frontend UX feature implementing the
constitution's own Settings-Panel requirement; the relevant gates are Principles VII, XI, XII and the
engineering principles.

| Principle | Relevance | Status |
|-----------|-----------|--------|
| II. No Frontend Secrets | GIS toggle swaps a public MapTiler style URL only; no secret introduced | ✅ PASS |
| VII. OpenStreetMap-Based Cartography | GIS toggle swaps the **basemap style URL** only; route/bus/checkpoint **GeoJSON data layers persist** unchanged across the swap (no re-fetch) | ✅ PASS |
| XI. Snappy, Reversible Overlays | Drawer slides in 100ms, dismisses **immediately** (no exit animation) on close ✕, outside-click, or gear re-click | ✅ PASS |
| XII. Internationalized, Settings-Driven | Implements the gear-FAB→right-drawer; ships **Audio + GIS** (two of the three mandated controls) plus a Checkpoint toggle; **Language deferred** (tracked). All blade copy via `IStringLocalizer<RouteFilterResources>` `.resx` | ⚠️ PARTIAL (justified) |
| IV. OpenTelemetry / structured logging | Blade/JS-interop errors logged via `ILogger`, matching existing interop services | ✅ PASS |
| I, III, V, VI (engineering) | No architecture, pipeline, data-pipeline, or GTFS-mapping changes | ✅ N/A |

**Gate result**: PASS with one tracked partial (XII — Language selector deferred; the panel surface, Audio,
and GIS controls it mandates ARE delivered). No unjustified violations.

**Post-Phase-1 re-check**: Design introduces no new violations. The `Settings` model is boolean-only (pure
reflection holds); the GIS toggle is specified as a style-URL swap with persistent data layers (VII upheld);
all visible strings are routed through the existing `.resx`. Gate still PASS.

## Project Structure

### Documentation (this feature)

```text
specs/016-settings-blade/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── settings-events.md        # bus event payloads (open + per-setting effect args)
│   ├── settings-service.md       # ISettingsService persistence contract
│   └── outside-click-interop.md  # JS interop contract for add/remove outside-click listener
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

All changes are within the Blazor WASM client. No server/worker/shared changes. Namespace root is
`ChefKnifeStudios.MartaJazz` (the solution's actual root), under `src/Client/`.

```text
src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/
├── Models/
│   └── Settings.cs                          # NEW: ObservableObject, 3 [Description]-labeled bool props
├── Constants/
│   └── LocalStorageConstants.cs             # NEW: SettingsKey = "Setting"
├── Services/
│   ├── SettingsService.cs                   # NEW: ISettingsService — get/seed/set over ISyncLocalStorageService
│   └── JsInterop/
│       ├── IOutsideClickJsInterop.cs        # NEW: add/remove outside-click listener contract
│       └── OutsideClickJsInterop.cs         # NEW: lazy-module interop (matches TransitSynthJsInterop pattern)
├── EventArgs/
│   ├── BladeEventArgs.cs                    # NEW: { Type: Settings | Close } — opens/closes the blade
│   ├── AudioSettingChangedEventArgs.cs      # NEW: { IsAudioEnabled }
│   ├── GisSettingChangedEventArgs.cs        # NEW: { IsStreetsBasemap }
│   └── CheckpointVisibilityChangedEventArgs.cs # NEW: { AreCheckpointsVisible }
├── Components/
│   ├── Blades/
│   │   ├── BladeContainer.razor             # NEW: generic slide-out shell (markup)
│   │   ├── BladeContainer.razor.cs          # NEW: Open/Close, 100ms-in, instant-out, min-open guard, outside-click
│   │   ├── BladeContainer.razor.css         # NEW: right-anchored drawer, translateX, 100ms transition
│   │   ├── SettingsBlade.razor              # NEW: reflects Settings bools → MatCheckbox list (localized labels)
│   │   ├── SettingsBlade.razor.cs           # NEW: bus subscribe, persist + post effect event per toggle
│   │   └── SettingsBlade.razor.css          # NEW: settings list layout
│   └── FABs/
│       ├── SettingsFab.razor                # NEW: gear MatFAB → posts BladeEventArgs(Settings) (toggle open/close)
│       └── SettingsFab.razor.css            # NEW: bottom-right anchor
├── Resources/
│   └── RouteFilterResources.resx            # MODIFY: add blade title + 3 setting labels (EN)
└── wwwroot/js/
    └── outside-click.js                     # NEW: addOutsideClickListener / removeOutsideClickListener

src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/
├── Layout/
│   └── MainLayout.razor                     # MODIFY: host <SettingsBlade/> + <SettingsFab/> once; (theme handler already present)
├── Pages/
│   ├── TransitMap.razor.cs                  # MODIFY: subscribe to Audio/GIS/Checkpoint event args; drive synth mute + map style swap + checkpoint layer toggle
│   └── TransitMap.razor                     # (no change expected; blade/FAB live in layout)
└── Program.cs                               # MODIFY: register ISettingsService (transient), IOutsideClickJsInterop (singleton)

src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/
└── map-interop.js                           # MODIFY: add setBasemapStyle (streets↔blank-dark) + setCheckpointVisibility to window.ChefMap
```

**Structure Decision**: Web application, frontend-only slice. The reusable blade primitives
(`BladeContainer`, `SettingsBlade`, `SettingsFab`, `Settings`/`SettingsService`, event args, interop) live in
the shared RCL (`Client.Shared`) beside the existing `RouteFilters`/`Map` components; the single hosting site
(`MainLayout`), DI registration (`Program.cs`), and the per-setting effect wiring (`TransitMap.razor.cs`) live
in the `WebApp` host — matching the split already used by features 014/015. The existing
`IEventNotificationService` bus and the `MainLayout` theming subscription are reused, not re-created.

## Complexity Tracking

> Only justified deviations are tracked.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Principle XII satisfied for **Audio + GIS** controls; **Language selector deferred** | The shipped roster (Audio, GIS, Checkpoint) is all-boolean, which lets the panel use the reference doc's pure-reflection render model unchanged and keeps this slice focused on standing up the blade surface itself. A runtime culture switcher + full ES translation set is a cross-cutting concern; deferring it mirrors the precedent set in feature 015 (Spanish `.resx` deferred) and leaves a clean seam (add a non-bool Language control + `.es.resx` later). | Shipping the Language selector now was rejected: it forces an explicit (non-reflection) control and a culture-switch mechanism mid-slice, contradicting the chosen pure-reflection approach and expanding scope. Hardcoding labels was rejected outright (violates XII); blade copy is routed through the existing `.resx`. |
| Dark-Mode toggle (in the reference doc) **omitted** | The constitution does not list dark mode among the settings; `MainLayout` already supports `ThemeChangedEventArgs` so the seam remains if it is wanted later. | Including it would add a fourth setting the binding spec never asked for. |
