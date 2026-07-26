---

description: "Task list for Route Filter UI — Focus, Map Blur & Blurb"
---

# Tasks: Route Filter UI — Focus, Map Blur & Blurb

**Input**: Design documents from `/specs/015-route-filter-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: No automated client UI test harness exists in this repo and the spec did not request tests;
verification is via `quickstart.md` (manual, in-browser). No test tasks are generated.

**Organization**: Tasks are grouped by user story. US1 (highlight) and US2 (blur) are both P1 and are
implemented by the *same* `ChefMap.focusRoute`/`clearRouteFocus` interop pair — US2 completes the map
behavior US1 starts. US3 (blurb) is P2 and fully independent of the map work.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (maps to spec.md user stories)
- All paths are repository-relative; all work is under `src/Client/`

## Path Conventions

- Shared RCL: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/`
- WASM host: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Localization scaffolding + DI registration that later phases depend on. Frontend-only; no
new packages beyond `Microsoft.Extensions.Localization` (verify it resolves under .NET 10 WASM).

- [X] T001 Add `builder.Services.AddLocalization();` in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs` (place near the other service registrations).
- [X] T002 [P] Create English resource file `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx` with keys `RouteBlurbPlaceholder` (value `"Route {0} — tone and Atlanta story coming soon."`) and `RouteBlurbBarAriaLabel` (value `"Route information"`). Add an empty marker class `RouteFilterResources` in `Resources/RouteFilterResources.cs` so `IStringLocalizer<RouteFilterResources>` resolves. (Spanish `.es.resx` intentionally deferred.)
- [X] T003 Verify the Shared RCL `.csproj` includes the `Resources/` `.resx` as `EmbeddedResource` (add `<EmbeddedResource Include="Resources\**\*.resx" />` only if not already covered by the default SDK glob) — confirm with a `dotnet build` of the Shared project.

**Checkpoint**: Localization seam exists; `IStringLocalizer<RouteFilterResources>` resolves an English placeholder.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared focus-state accessor that BOTH the map phases and the blurb phase consume. Must
complete before any user story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Add a read-only convenience member to the route-filter VM: `string? SelectedRouteId => RouteItems.FirstOrDefault(x => x.IsSelected)?.RouteId;` — declare it on `IRouteFilterViewModel` and implement on `RouteFilterViewModel` in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`. No behavior change; `using System.Linq;` already present.

**Checkpoint**: Consumers can read the single focused route id from the existing VM without re-deriving it.

---

## Phase 3: User Story 1 — Highlight the focused route on the map (Priority: P1) 🎯 MVP

**Goal**: When a route is focused in the grid, its line becomes the visually dominant route on the map;
emphasis moves with focus and is never on more than one route.

**Independent Test**: Hover a route input → that route's `route-layer-<id>` reads dominant within ~100ms;
move to another input → emphasis moves; at most one emphasized. (quickstart §1)

### Implementation for User Story 1

- [X] T005 [US1] Add `ChefMap.focusRoute(containerDivId, routeId)` to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`: lazily init `ChefMap._preFocusColors = {}`; enumerate `map.getStyle().layers` for ids starting `route-layer-`; stash original `line-color` per layer; set the focused `route-layer-<routeId>` to `line-opacity` 0.95 with its own (stashed) color; guard every `getLayer`/`setPaintProperty` so a missing focused layer does not throw. (Non-focused greying is added in US2 — keep the iteration loop ready for it.) Per `contracts/chefmap-focus-interop.md`.
- [X] T006 [US1] Add the C# interop wrapper `FocusRouteAsync(string routeId)` to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs`, following the existing try/catch + `Console.WriteLine` pattern (invokes `ChefMap.focusRoute`, args `ElementId, routeId`).
- [X] T007 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`: inject `IRouteFilterViewModel`; subscribe to its `PropertyChanged` in `OnInitializedAsync`; on `RouteItems`/`HasSelection` change, when `SelectedRouteId is { } id` call `await _map.FocusRouteAsync(id)`; unsubscribe in `DisposeAsync`. Guard on `_mapReady && _map is not null`.

**Checkpoint**: Focusing a route emphasizes its line on the map (other routes not yet de-emphasized — completed in US2).

---

## Phase 4: User Story 2 — Blur every non-selected route on the map (Priority: P1)

**Goal**: Focusing one route greys + lowers opacity of every other route; losing focus restores all
routes to normal appearance instantly. Completes the map-side single-focus behavior.

**Independent Test**: With a route focused, all other lines are greyed (`#9ca3af`) + low opacity (~0.15);
unfocus → all restore to opacity 0.85 + original color immediately; fresh load → none blurred.
(quickstart §2)

### Implementation for User Story 2

- [X] T008 [US2] Extend `ChefMap.focusRoute` in `src/Client/.../wwwroot/js/map-interop.js`: in the same layer loop from T005, for every NON-focused `route-layer-*` set `line-opacity` 0.15 and `line-color` `'#9ca3af'`. Keep idempotency — calling with a new `routeId` re-evaluates all layers to the new target (supports direct route→route switch with no intermediate "all normal" frame). Per `contracts/chefmap-focus-interop.md`.
- [X] T009 [US2] Add `ChefMap.clearRouteFocus(containerDivId)` to `src/Client/.../wwwroot/js/map-interop.js`: for each `route-layer-*` set `line-opacity` 0.85 (creation default) and restore `line-color` from `ChefMap._preFocusColors[id]`; then reset `ChefMap._preFocusColors = {}`. Do not set any paint transition (keeps teardown immediate per Principle XI).
- [X] T010 [US2] Add the C# wrapper `ClearRouteFocusAsync()` to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs` (invokes `ChefMap.clearRouteFocus`, arg `ElementId`; same try/catch pattern).
- [X] T011 [US2] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`, extend the `PropertyChanged` handler from T007: when `SelectedRouteId is null` call `await _map.ClearRouteFocusAsync()` (the focused branch from T007 now produces full highlight+blur via the extended `focusRoute`).

**Checkpoint**: Full map single-focus works — one route emphasized, all others greyed, instant restore on unfocus. Map-side feature complete and independently demoable.

---

## Phase 5: User Story 3 — Bottom blurb bar with placeholder (Priority: P2)

**Goal**: Focusing a route shows a full-width bottom bar (100ms in, instant out) with that route's
content, falling back to a route-naming placeholder when no authored copy exists. Independent of the map
work.

**Independent Test**: Focus any route → dark full-width bar fades up within ~100ms with placeholder text
naming the route; switch focus → text swaps with no close/reopen; unfocus → bar disappears instantly.
(quickstart §3)

### Implementation for User Story 3

- [X] T012 [P] [US3] Create the blurb record `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Data/RouteBlurb.cs`: `public sealed record RouteBlurb(string RouteId, string ToneDescription, string Significance, bool IsPlaceholder = false);` Per `data-model.md`.
- [X] T013 [US3] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Data/RouteBlurbStore.cs` with `IRouteBlurbStore { RouteBlurb GetForRoute(string routeId); }` and an implementation: ctor takes `IStringLocalizer<RouteFilterResources>`; an authored `Dictionary<string,RouteBlurb>` (ordinal, MAY be empty at ship); on miss return a placeholder built from `string.Format(localizer["RouteBlurbPlaceholder"], routeId)` with `IsPlaceholder:true`; never returns null. Per `contracts/route-blurb-store.md`. (Depends on T002, T012.)
- [X] T014 [US3] Register the store in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs`: `builder.Services.AddSingleton<IRouteBlurbStore, RouteBlurbStore>();`
- [X] T015 [P] [US3] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/RouteBlurbBar.razor.css`: full-width bottom overlay (`position:absolute; left:0; right:0; bottom:0;`), semi-transparent dark background (`rgba(0,0,0,0.65)`), light text, z-index above the map; **in** = fade/slide transition ≤100ms; **out** = no exit animation (visibility gated by render, not a timed transition). Per `contracts/route-blurb-store.md`.
- [X] T016 [US3] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/RouteBlurbBar.razor` + `RouteBlurbBar.razor.cs`: inject `IRouteFilterViewModel` and `IRouteBlurbStore` (and `IStringLocalizer<RouteFilterResources>` for the aria-label); subscribe to VM `PropertyChanged`; when `SelectedRouteId is null` render nothing/hidden, else bind `RouteBlurb = store.GetForRoute(id)` and render `ToneDescription` + `Significance`; on R→S keep the element mounted and swap content (FR-008); implement `IDisposable` to unsubscribe (mirror `RouteFilters.razor.cs`). Per `contracts/route-blurb-store.md`. (Depends on T013.)
- [X] T017 [US3] Render `<RouteBlurbBar />` inside the map container in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor` (within `.transit-map-container`, after `<Map ... />`, so it overlays the map).

**Checkpoint**: Blurb bar appears with placeholder on focus, swaps on focus change, vanishes instantly on unfocus.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify the integrated behavior and the constitution-bound edge cases.

- [X] T018 Run `quickstart.md` §4 (consistency): rapid focus sweep then leave the grid → map fully un-blurred AND blurb gone; nothing stuck (FR-010, SC-004).
- [X] T019 [P] Verify `quickstart.md` §5 (style-swap resilience) if the GIS toggle is reachable: focus a route, toggle basemap → highlight/blur on data layers preserved (Principle VII). If GIS toggle not yet wired, note as N/A.
- [X] T020 [P] Confirm no hardcoded placeholder string literal remains in `RouteBlurbStore`/`RouteBlurbBar` (FR-011 English-only seam) — placeholder comes only from `RouteFilterResources.resx`.
- [X] T021 Run `dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj` and confirm it succeeds with no new warnings introduced by this feature.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. T001/T003 are sequential-ish (both touch build), T002 is [P].
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS all user stories (everyone reads `SelectedRouteId`).
- **US1 (Phase 3)**: Depends on Foundational (T004).
- **US2 (Phase 4)**: Depends on US1 — T008 extends the `focusRoute` loop from T005, and T011 extends the handler from T007. (US2 *completes* US1's map behavior rather than being independent.)
- **US3 (Phase 5)**: Depends on Foundational (T004) + Setup (T002). Independent of US1/US2 — can run in parallel with the map phases.
- **Polish (Phase 6)**: Depends on all desired stories complete.

### Within Each User Story

- US1: T005 (JS) → T006 (C# wrapper) → T007 (wiring). Sequential (each layers on the prior).
- US2: T008/T009 (JS) → T010 (C# wrapper) → T011 (wiring).
- US3: T012 → T013 → {T014, T016}; T015 [P] anytime; T017 last (renders the component).

### Parallel Opportunities

- T002 [P] (resx) runs alongside T001.
- **US3 (Phase 5) can run fully in parallel with US1+US2 (Phases 3–4)** — different files (Data/, new Component vs. map-interop.js + TransitMap map-wiring). Note both US2's T011 and US3's T017/T007 touch `TransitMap` — coordinate those edits (different regions: handler logic vs. razor markup) or sequence them.
- T012 [P] and T015 [P] within US3.
- Polish T019/T020 [P].

---

## Parallel Example

```text
# After Foundational (T004), a two-track split:
Track A (map, P1):  T005 → T006 → T007 → T008 → T009 → T010 → T011
Track B (blurb,P2): T012 [P] → T013 → T014 / T016, T015 [P], then T017

# Coordinate the two TransitMap touches: Track A edits TransitMap.razor.cs (focus handler),
# Track B edits TransitMap.razor (adds <RouteBlurbBar/>) — different files, safe in parallel.
```

---

## Implementation Strategy

### MVP First (Map single-focus = US1 + US2)

1. Phase 1 Setup → Phase 2 Foundational.
2. Phase 3 (US1) + Phase 4 (US2) — together these deliver the complete map highlight+blur, the core
   payoff. **STOP and VALIDATE** via quickstart §1–§2.
3. Demo: focusing a route visibly drives the map.

### Incremental Delivery

1. Setup + Foundational → seam ready.
2. US1+US2 → map single-focus → demo (MVP).
3. US3 → blurb bar with placeholder → demo.
4. Polish → edge cases + build gate.

---

## Notes

- All changes are under `src/Client/`; no server, worker, or shared backend code is touched.
- US1 and US2 are intentionally coupled (same interop pair) — US2 is the second half of the map story,
  not an independent slice; this is called out so the coupling is deliberate, not accidental.
- Spanish localization is deferred by design (see plan.md Complexity Tracking) — do NOT add `.es.resx`
  in this feature.
- Commit after each task or logical group. Stop at any checkpoint to validate.
