# Implementation Plan: Multi-Route Selection — Persistent Filter, Bus Count & Tone Scoping

**Branch**: `019-lerp-event-cache` *(documentation-only; no new branch per request)* | **Date**: 2026-06-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/020-multi-route-select/spec.md`

## Summary

Evolve the route filter from the **single-focus, transient hover/tap** model (features #14/#15) into a
**persistent multi-selection** model, and make three downstream behaviors respect that selection set:

1. **Map** — every *selected* route is emphasized and every *non-selected* route is blurred/greyed
   (the existing `focusRoute` single-route behavior generalizes to a *set* of routes).
2. **"# buses running" count** — counts only vehicles on selected routes when the selection is
   non-empty; counts all running vehicles when the selection is empty (unscoped).
3. **Audio tones** — only selected routes produce tones when the selection is non-empty; all routes
   produce tones when empty (unscoped). Subordinate to the existing audio-mute setting.

Plus two convenience controls: **"Select all"** and **"Clear selections"**, and a blurb-bar change so
the bar shows **only when exactly one route is selected** (it is a single-route detail view).

The single source of truth is the existing `IRouteFilterViewModel`, whose selection model changes from
"at most one `IsSelected`" to "a persistent set of `IsSelected` routes". The map page (`TransitMap`),
the bus-count label (`BusesRunningLabel` via the VM), the blurb bar (`RouteBlurbBar`), and the tone
trigger (`TransitMap.OnCrossingsAsync`) all already subscribe to this VM or run inside `TransitMap`, so
the wiring is a matter of *re-pointing existing consumers at the set* rather than introducing new
infrastructure. The map gets one new multi-route interop call (`focusRoutes(routeIds[])`) alongside the
existing `focusRoute`/`clearRouteFocus`.

**Frontend-only** slice in the Blazor WASM client. No server, worker, or shared-backend changes; route
and vehicle data continue to be fetched and rendered exactly as today.

> **Constitution note (read the Constitution Check):** this feature changes the route-filter interaction
> model from "single-focus, hover-to-filter, immediate-reversal" to **persistent multi-selection**, at the
> user's explicit direction. **Principle IX was amended (constitution v3.2.0) to define this new model**, so
> the plan now implements the constitution — there is no outstanding deviation.

## Technical Context

**Language/Version**: C# / .NET 10.0; JavaScript (ES, `window.ChefMap` global) for the MapLibre interop
**Primary Dependencies**: Blazor WebAssembly; CommunityToolkit.Mvvm (`[ObservableProperty]`,
`[NotifyPropertyChangedFor]` on `RouteFilterViewModel`); MapLibre GL JS over MapTiler (`setPaintProperty`
on `route-layer-*`); Tone.js via `ITransitSynthJsInterop` (tone trigger); `IStringLocalizer<RouteFilterResources>`
(control labels — already wired); MatBlazor (optional, for the two new buttons)
**Storage**: None new. The selection set is **in-memory client state** on the singleton
`RouteFilterViewModel` (not persisted to local storage — selection is a session-scoped exploration tool,
consistent with the prior transient model). No backend or schema changes.
**Testing**: Manual verification per quickstart (no automated client-UI harness exists in this repo);
`dotnet build` on the solution. Optional xUnit unit test of the selected-routes count rule on the VM if a
client test project is added — currently none exists, so manual.
**Target Platform**: Browser (Blazor WASM); web (click/hover) + mobile (tap)
**Project Type**: Web application — Blazor WASM frontend only; touches **no** server, worker, or shared code
**Performance Goals**: Selection→effect (map blur, bus count, tone scope, blurb) perceptibly immediate
(<100ms, consistent with the existing focus reactions); no re-fetch of routes or vehicles on selection change
**Constraints**: Frontend-only; reuse the existing `IRouteFilterViewModel` as the single source of truth and
the existing `PropertyChanged` subscriptions in `RouteFilters`, `BusesRunningLabel`, `RouteBlurbBar`, and
`TransitMap`; **empty selection = no filter** (unscoped count + all tones), scoping engages only when ≥1 route
selected; tone scoping MUST be subordinate to the existing audio-mute setting (FR-009); map highlight/blur must
survive a basemap style swap (the GeoJSON data layers persist — Principle VII); new control labels MUST come
from `RouteFilterResources.resx` (Principle XII — no inline copy)
**Scale/Scope**: ~24 MARTA routes in the grid; one VM selection-model change; one bus-count rule change; one
tone-gate addition; one new multi-route map interop call (`focusRoutes`); two new controls; one blurb-visibility
rule change; 5 user stories; 15 functional requirements; EN-only resx additions (`.es` deferred)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against **TransitJazz Constitution v3.2.0**. This is a frontend UX feature that **changes the
route-filter interaction model**; Principle IX was amended (v3.2.0) to define that new model, so this plan
now *implements* the constitution rather than deviating from it.

| Principle | Relevance | Status |
|-----------|-----------|--------|
| **IX. Persistent Multi-Selection Interaction Model** | Amended in **constitution v3.2.0** (this feature drove the amendment) to define persistent multi-selection, selection-scoped bus count + tones (unscoped when empty), and blurb-for-single-selection. This plan now **implements** the amended principle. | ✅ PASS (implements the amended IX) |
| VII. OpenStreetMap-Based Cartography | Map highlight/blur operates on the persistent `route-layer-*` GeoJSON data layers via `setPaintProperty`; **no re-fetch**, and the treatment must survive a basemap swap (re-applied after `style.load`, like #17). | ✅ PASS |
| VIII. Generative Transit Music | Tone *generation* is unchanged (deterministic per route). This feature only **gates which routes are allowed to sound** at the crossing-trigger boundary; it does not author or alter any tone. | ✅ PASS |
| XI. Snappy, Reversible Overlays | The blurb bar keeps its 100ms-in / instant-out behavior; it now appears only for exactly-one selection. No new overlay. Selection effects are immediate. | ✅ PASS (timing unchanged) |
| XII. Internationalized, Settings-Driven | New control labels ("Select all", "Clear selections") sourced from `RouteFilterResources.resx` (EN now; `.es` deferred per 015/016/017 precedent). No inline copy. | ✅ PASS |
| X. Zoom-Adaptive, Non-Occluding Controls | The two new buttons sit with the existing filter grid; placement must not occlude map data. No change to the zoom-adaptive anchoring (still deferred from #15). | ✅ PASS (no regression) |
| IV. OpenTelemetry / structured logging | Selection changes and interop failures logged via the existing `ILogger`/console pattern. | ✅ PASS |
| I, II, III, V, VI (engineering/backend) | No architecture, secrets, pipeline, CI/CD, or GTFS-mapping changes. | ✅ N/A |

**Gate result**: **PASS.** Principle IX was amended to the persistent-multi-selection model (constitution
v3.2.0, driven by this feature), so this plan implements the constitution with no outstanding deviation. The
empty-selection-as-unscoped rule (now codified in IX) was chosen specifically to avoid an accidental-mute
footgun. No other principle is violated.

**Post-Phase-1 re-check**: Design keeps the selection set on the existing VM (single source of truth), gates
tones at the existing `OnCrossingsAsync` boundary (VIII intact), and re-applies map blur after `style.load`
(VII intact). All consistent with amended IX. Gate still PASS.

## Project Structure

### Documentation (this feature)

```text
specs/020-multi-route-select/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── route-selection-viewmodel.md   # IRouteFilterViewModel multi-select contract (state + members)
│   ├── bus-count-rule.md              # Selected-routes count rule (empty = unscoped)
│   ├── tone-scoping.md                # OnCrossingsAsync selected-routes gate (subordinate to mute)
│   └── map-multi-focus-interop.md     # ChefMap.focusRoutes(routeIds[]) / Map.FocusRoutesAsync contract
└── checklists/
    └── requirements.md  # (already created by /speckit-specify)
```

### Source Code (repository root)

All changes are within the Blazor WASM client. No server/worker/shared changes. Namespace root is
`ChefKnifeStudios.MartaJazz`, under `src/Client/`.

```text
src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/
├── ViewModels/
│   └── RouteFilterViewModel.cs        # MODIFY: selection becomes a persistent SET; SelectRoute → toggle;
│                                       #   add SelectAll(); ClearSelection() empties set; add SelectedRouteIds;
│                                       #   IsSingleSelection; OnNotificationReceived counts only selected routes
│                                       #   (when non-empty) — also store last batch so count recomputes on
│                                       #   selection change; SelectedRouteId = single-selection convenience
├── Components/
│   ├── RouteFilters.razor             # MODIFY: hover/out → persistent toggle (tap on mobile);
│   │                                   #   add "Select all" + "Clear selections" buttons; de-emphasis class
│   │                                   #   keys off membership in the set
│   ├── RouteFilters.razor.cs          # MODIFY: HandleSelect toggles; HandleSelectAll; HandleClearSelections
│   ├── RouteBlurbBar.razor.cs         # MODIFY: show blurb only when IsSingleSelection (exactly one)
│   └── BusesRunningLabel.razor        # (no change — already binds ActiveBusCount; rule lives in the VM)
├── Resources/
│   └── RouteFilterResources.resx      # MODIFY: add "SelectAllRoutes", "ClearSelections" labels (EN)
└── Components/Map.razor.Helper.cs     # MODIFY: add FocusRoutesAsync(IEnumerable<string> routeIds) wrapper
    wwwroot/js/map-interop.js          # MODIFY: add ChefMap.focusRoutes (emphasize a SET, blur the rest)

src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/
└── Pages/
    └── TransitMap.razor.cs            # MODIFY: OnRouteFilterPropertyChanged → FocusRoutesAsync(set) /
                                        #   ClearRouteFocusAsync() when empty; OnCrossingsAsync → gate tone by
                                        #   selected-routes set (when non-empty); IsAllowedRoute stays unrelated
```

**Structure Decision**: Web application, frontend-only slice. The selection model, toggle/select-all/clear
logic, the count rule, the blurb-visibility rule, the two new buttons, and the new map interop wrapper + JS all
live in the shared RCL (`Client.Shared`) alongside the existing filter code. The per-page effect wiring (map
multi-focus + tone gating) lives in the `WebApp` host (`TransitMap.razor.cs`), matching the split established by
#15/#17. The existing `IRouteFilterViewModel` singleton and the existing `PropertyChanged` subscriptions are
**reused as the single source of truth**, not re-created — every consumer (grid, map, bus count, blurb, tones)
already reads from this VM or runs inside `TransitMap`.

## Complexity Tracking

> No outstanding constitution violations. The route-filter model change was reconciled by **amending
> Principle IX** to the persistent-multi-selection model (constitution **v3.2.0**, MINOR — redefines an
> interaction principle's mechanics without removing it). The record below is retained for context.

| Former deviation (now resolved) | Why Needed | Simpler Alternative Rejected Because |
|---------------------------------|------------|--------------------------------------|
| Changed Principle IX from single-focus/hover-only/immediate-reversal to **persistent multi-selection** with Select-all / Clear controls, and made bus count + tones selection-scoped. **Resolved by constitution v3.2.0 amendment.** | The user **explicitly requested** multi-route selection: "I want to be able to select multiple routes", a Select-all/Clear pair, selection-scoped bus count, and selection-scoped tones. Single-focus cannot express "listen to *these three* routes at once" — the core ask. Persistent selection is required so the choice survives moving the pointer away to read the map. | **Keeping single-focus** structurally cannot satisfy the primary requirement (multiple simultaneous routes). **Transient multi-select** (hold to multi-highlight, release to clear) is rejected because the user needs the selection to persist while they interact elsewhere (read the blurb, watch the count, pan the map). |

**Constitution action taken**: Principle IX (and the "Filtering & Focus" UX table + "Top Status Text"
subsection) were amended to the persistent-multi-selection model — selection toggles per route, persists until
changed, with Select-all / Clear affordances; bus count and tones scope to the selection when non-empty and are
unscoped when empty; the blurb bar (a single-route detail view) shows only for exactly-one selection.
Constitution version **3.1.1 → 3.2.0**. The constitution and the planned UX now agree (Governance §Compliance
Review).
