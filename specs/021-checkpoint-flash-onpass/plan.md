# Implementation Plan: Checkpoint Flash on Bus Pass & Bus-Visibility Toggle

**Branch**: `021-checkpoint-flash-onpass` | **Date**: 2026-06-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/021-checkpoint-flash-onpass/spec.md`

## Summary

Two complementary, **frontend-only** changes that reuse existing infrastructure:

1. **Checkpoint pulse on bus pass (P1)** — When a bus passes a checkpoint, render an expanding, route-colored "ping" ring at that checkpoint that grows and fades. This reuses the *existing* crossing-detection signal already delivered to `TransitMap.OnCrossingsAsync` (currently used only for audio). The pulse fires **independently of the audio mute setting** (subject only to checkpoints being visible). It is drawn on a **new, dedicated `checkpoint-pulse` overlay layer** above the shared `trigger-points-layer`, driven by a `requestAnimationFrame` loop in a new `checkpoint-pulse.js` RCL module — leaving the resting checkpoint dots untouched and supporting many concurrent, independent pulses.

2. **Bus-visibility settings toggle (P2)** — Add one boolean to the existing reflection-driven `Settings` model (`IsBusesVisible`, default **false** = hidden), which auto-renders a checkbox in `SettingsBlade`. Wire a new `BusVisibilitySettingChangedEventArgs` through the existing `IEventNotificationService` bus to `TransitMap`, which calls the **existing** `Map.SetVehiclesVisibleAsync(visible)`. Honor the persisted setting on initial render and after a basemap style swap.

Route colors are already available client-side in JS (`ChefMap._routeColorsByRouteId[routeId]`). No server, worker, or shared-project changes.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WebAssembly), JavaScript (ES modules, MapLibre GL JS)
**Primary Dependencies**: MapLibre GL JS (over MapTiler), MatBlazor, CommunityToolkit.Mvvm (`ObservableObject`), Blazored.LocalStorage (via `ISettingsService`), `IStringLocalizer<RouteFilterResources>`
**Storage**: Browser local storage (single JSON blob under key `"Setting"`, via existing `ISettingsService`)
**Testing**: Manual/quickstart verification in the running app (consistent with prior client features 016/017/020 — no automated UI test harness in this project)
**Target Platform**: WASM in browser (desktop + mobile web)
**Project Type**: Web application (decoupled) — this feature touches only the Blazor WASM frontend
**Performance Goals**: Pulse animation at display refresh (~60fps) via a single shared RAF loop; one `setData` per frame on the pulse source; pulse begins within a fraction of a second of the pass (SC-001)
**Constraints**: Frontend-only; no re-fetch of route/checkpoint data on basemap swap (Principle VII); all user-facing copy via `.resx` (Principle XII / Localization); snappy/reversible (Principle XI)
**Scale/Scope**: ~tens of routes, hundreds of checkpoints; concurrent pulses bounded by buses currently crossing (small)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance |
|-----------|-----------|
| **VII. OpenStreetMap-Based Cartography** | ✅ Pulse is a new GeoJSON overlay layer added **on top of** the basemap; on basemap swap it is re-added empty and route/checkpoint data is **not** re-fetched. Resting checkpoint dots (`trigger-points`) unchanged. |
| **VIII. Generative Transit Music** | ✅ Untouched. Pulse is purely visual; the crossing signal it consumes is the same one that drives the deterministic crossing note. Audio behavior is unchanged. |
| **IX. Persistent Multi-Selection** | ✅ Pulses respect the selection-scoping path already present in `OnCrossingsAsync` is **reconsidered**: see note below. Bus-visibility toggle does not affect selection. |
| **XI. Snappy, Reversible Overlays** | ✅ Pulse is a short self-resolving animation (no trapping). Bus-visibility toggle applies immediately (no reload), reversible. |
| **XII. Internationalized, Settings-Driven** | ✅ New setting is a reflection-rendered checkbox in the existing settings drawer; label via `.resx` key `SettingBusesVisible`. Mirrors audio/checkpoint/street-map settings. (EN only this iteration, consistent with 015/016/017 deferred `.es`.) |
| **II. No Frontend Secrets** | ✅ N/A — no credentials touched. |

**Selection-scoping note (Principle IX):** Today `OnCrossingsAsync` skips crossings whose route is not in the active selection set, and also early-returns when audio is disabled. The pulse must (a) fire even when audio is muted, and (b) follow the **same selection scoping** the audio uses, so that when a route filter is active, only selected routes' checkpoints pulse — consistent with Principle IX ("emphasize selected, blur non-selected"). Design: split `OnCrossingsAsync` so the **audio** branch keeps its `_audioEnabled` guard, while the **pulse** branch runs regardless of audio but honors the selection filter. No constitution violation; this keeps pulses aligned with the selection-scoped experience.

**Result: PASS** (no violations; Complexity Tracking not required).

## Project Structure

### Documentation (this feature)

```text
specs/021-checkpoint-flash-onpass/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (interop + events + settings)
└── tasks.md             # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

All changes are under `src/Client/`. Namespace root is `ChefKnifeStudios.TransitJazz`.

```text
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
├── Models/
│   └── Settings.cs                        # ADD bool IsBusesVisible (default false), [Description("SettingBusesVisible")]
├── EventArgs/
│   └── BusVisibilitySettingChangedEventArgs.cs   # NEW (mirrors GisSettingChangedEventArgs)
├── Components/Blades/
│   └── SettingsBlade.razor.cs             # ADD one switch arm in HandleSettingPressed
├── Components/
│   └── Map.razor.Helper.cs                # ADD PulseCheckpointAsync(routeId, triggerIndex) interop wrapper
├── Resources/
│   └── RouteFilterResources.resx          # ADD <data name="SettingBusesVisible"> = "Buses"
└── wwwroot/js/
    ├── map-interop.js                     # ADD checkpoint-pulse source+layer setup + delegate to pulse module; re-add on style.load
    └── checkpoint-pulse.js                # NEW ES module: pulse(routeId, triggerIndex, color), RAF loop, reset()

src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/
└── Pages/TransitMap.razor.cs              # SPLIT OnCrossingsAsync (audio vs pulse); handle BusVisibilitySettingChangedEventArgs;
                                           #   honor IsBusesVisible on initial render + after basemap swap
```

**Structure Decision**: Single-frontend change. New visual behavior lives in a dedicated JS module (`checkpoint-pulse.js`) following the existing per-concern JS module pattern (`checkpoint-tracker.js`, `vehicle-animator.js`). The settings change rides entirely on the existing reflection-driven `Settings`/`SettingsBlade`/`ISettingsService`/`IEventNotificationService` machinery — the only new C# type is one `IEventArgs` class.

## Key Design Decisions

1. **Reuse the existing crossing signal (FR-005).** `checkpoint-tracker.js` already detects passes (with its 2000ms cooldown) and calls `OnCrossingsAsync(CrossingEventDto[])` with `{VehicleId, RouteId, TriggerIndex, TotalTriggers}`. The pulse consumes the same batch — no new proximity logic, and FR-006 (anti-flicker cooldown) is inherited for free.

2. **Pulse target lookup.** Each resting checkpoint feature already carries `properties.{routeId, triggerIndex}` and its coordinate. The pulse module resolves the checkpoint coordinate from `ChefMap._triggerPointFeatures[routeId]` by `triggerIndex`, and the color from `ChefMap._routeColorsByRouteId[routeId]` (fallback `#facc15`, matching the resting dot — FR-004).

3. **Dedicated overlay layer (chosen).** A new `checkpoint-pulse` GeoJSON source + `checkpoint-pulse-layer` (circle) sits **above** `trigger-points-layer`. Each active pulse is one Point feature with `feature-state`/property-driven `circle-radius` (grows) and `circle-opacity` (fades), colored per route. A single RAF loop advances all active pulses and removes finished ones. Concurrency and per-route color are natural (FR-013, FR-003). Resting dots are never mutated (FR-007 — nothing to "settle back"; the pulse simply disappears).

4. **Audio-independent, selection-scoped (clarified).** Pulse fires regardless of `_audioEnabled` (always-pulse decision) but honors the route-selection filter, matching the audio path's selection scoping and Principle IX.

5. **Checkpoint-visibility gating (FR-008).** When checkpoints are hidden, pulses are suppressed: the pulse layer's visibility tracks the same setting as `trigger-points-layer`. Toggling visibility off mid-pulse clears active pulses (no orphans).

6. **Basemap-swap resilience (FR-012).** On `style.load` in `setMapStyle`, re-add the empty `checkpoint-pulse` source+layer (like the vehicles layer is re-added today) and reset the pulse module's active-pulse state. Route colors reapply via the existing `addRouteShapeFeature` path.

7. **Bus visibility = pure reflection + existing interop (FR-009..011).** Adding `IsBusesVisible` to `Settings` auto-renders the checkbox. `SettingsBlade.HandleSettingPressed` posts `BusVisibilitySettingChangedEventArgs`; `TransitMap` calls the existing `SetVehiclesVisibleAsync`. Initial render and post-basemap-swap currently hardcode `SetVehiclesVisibleAsync(true)` — change both to read `SettingsService.GetSettings().IsBusesVisible` (default false ⇒ hidden first paint, FR-009a/c).

## Complexity Tracking

No constitution violations — section intentionally empty.
