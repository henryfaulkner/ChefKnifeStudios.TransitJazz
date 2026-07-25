# Tasks: Map Style Toggle

**Input**: Design documents from `/specs/017-map-style-toggle/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: No automated tests — the project has no client UI test harness. Verification follows `quickstart.md`.

**Organization**: Tasks grouped by user story. US3 (persistence) requires no new code — the existing
`SettingsService` serializes all `Settings` bool fields automatically; adding the field (Phase 2) fully
delivers US3. US3 is therefore included in the Polish phase as a quickstart verification.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no pending dependencies)
- **[Story]**: User story this task belongs to

## Path Key

All paths relative to repo root. Client.Shared = `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`.
WebApp = `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`.

---

## Phase 1: Setup (Config & Strings)

**Purpose**: Add the config and localized string that every subsequent task reads from. No C# or JS logic.
These two tasks are safe to do before any code change.

- [x] T001 [P] Add `MapTiler:StyleUrls` block (`LightOn`, `LightOff`, `DarkOn`, `DarkOff`) to `WebApp/wwwroot/appsettings.json`, replacing the existing flat `StyleUrl` default with the `LightOff` URL (see `contracts/style-config.md` and `appsettings.Development.json` for the existing block to copy/adapt with the correct production style IDs)
- [x] T002 [P] Add resx entry `SettingStreetMap` → `"Street map"` to `Client.Shared/Resources/RouteFilterResources.resx` (the blade renders the label automatically via the `[Description]` attribute → `IStringLocalizer` lookup; no markup change needed)

**Checkpoint**: Config and strings are in place. Build: `dotnet build` — should be clean.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The two new C# types that every downstream task depends on. Must be complete before US1/US2 work.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 Add `IsStreetMapEnabled` property to `Client.Shared/Models/Settings.cs` — `[ObservableProperty]` `[property: Description("SettingStreetMap")]` `private bool _isStreetMapEnabled = false;` (see `data-model.md`; the blade reflection render, `SettingsService` persistence, and default seeding all work automatically with no further changes)
- [x] T004 [P] Create `Client.Shared/EventArgs/GisSettingChangedEventArgs.cs` — `public class GisSettingChangedEventArgs : IEventArgs { public required bool IsStreetMapEnabled { get; init; } }` (mirrors `AudioSettingChangedEventArgs.cs` in the same folder)

**Checkpoint**: `dotnet build` clean. The Settings Blade will now render a **Street map** checkbox (blade iterates all bool properties via reflection); toggling it will call `HandleSettingPressed` but the switch arm returns null for this property (no event yet), which is safe — no crash.

---

## Phase 3: User Story 1 — Default Map Display Uses LightOff (Priority: P1) 🎯 MVP

**Goal**: The map loads in the LightOff basemap from first render, sourcing the URL from `MapTiler:StyleUrls:LightOff` in config (rather than the hard-coded fallback from the old flat `StyleUrl`).

**Independent Test**: Clear local storage, reload — map renders in LightOff; DevTools confirms the style URL used is the `LightOff` entry from `appsettings.json` (quickstart Scenario 1).

- [x] T005 [US1] Inject `ISettingsService` into `Client.Shared/Components/Map.razor.cs` — add `[Inject] ISettingsService SettingsService { get; set; } = null!;` (follows same injection pattern as `IConfiguration` already present)
- [x] T006 [US1] Update `GetMapSettings` in `Client.Shared/Components/Map.razor.cs` to select the initial style URL from config using the persisted setting: `var key = SettingsService.GetSettings().IsStreetMapEnabled ? "MapTiler:StyleUrls:LightOn" : "MapTiler:StyleUrls:LightOff";` then `Configuration.GetValue<string>(key) ?? Configuration.GetValue<string>("MapTiler:StyleUrl") ?? string.Empty;` (see `contracts/map-style-interop.md` IO-6 + fallback chain in `contracts/style-config.md`)

**Checkpoint**: `dotnet build` clean. Reload app with cleared storage → map paints in LightOff. Quickstart Scenario 1 passes.

---

## Phase 4: User Story 2 — Hot-Switch Between LightOff and LightOn (Priority: P1)

**Goal**: Toggling the Street map checkbox in the Settings Blade hot-swaps the basemap between LightOff and LightOn with no reload; all plotted data layers (routes, vehicles, checkpoints) survive the swap with their visibility preserved.

**Independent Test**: Open blade, toggle Street map on → basemap switches to LightOn, all data intact, no re-fetch; toggle off → returns to LightOff (quickstart Scenarios 2 and 3).

- [x] T007 [US2] Implement `ChefMap.setMapStyle` in `Client.Shared/wwwroot/js/map-interop.js` — replace the current no-op stub body with: (1) guard `map = ChefMap.maps[containerDivId]; if (!map) return;`, (2) capture custom sources and layer defs from `map.getStyle()` for ids `vehicles`, `trigger-points`, and anything starting `route-` / `route-layer-` (save GeoJSON data + layer visibility), (3) call `map.setStyle(styleUrl)`, (4) inside `map.once('style.load', …)` re-add each saved source then each saved layer in order, preserving visibility and inserting route/trigger layers below `vehicles-layer` as the existing code does (see `contracts/map-style-interop.md` for full JS contract)
- [x] T008 [P] [US2] Add `SetBasemapStyleAsync` C# interop wrapper to `Client.Shared/Components/Map.razor.Helper.cs`: `public async Task SetBasemapStyleAsync(string styleUrl) { try { await JsRuntime.InvokeVoidAsync("ChefMap.setMapStyle", ElementId, styleUrl); } catch (Exception ex) { Console.WriteLine($"[Map] SetBasemapStyle failed: {ex}"); } }` (matches existing method shapes in the same file)
- [x] T009 [P] [US2] Add `GisSettingChangedEventArgs` switch arm to `Client.Shared/Components/Blades/SettingsBlade.razor.cs` in `HandleSettingPressed` — `nameof(Settings.IsStreetMapEnabled) => new GisSettingChangedEventArgs { IsStreetMapEnabled = value },` (see `contracts/map-style-events.md` Producer section; insert between the `AreCheckpointsVisible` arm and the `_ => null` fallback)
- [x] T010 [US2] Add `GisSettingChangedEventArgs` consumer arm to `WebApp/Pages/TransitMap.razor.cs` in `HandleSettingsEventReceived` — inject `[Inject] IConfiguration Configuration { get; set; } = null!;`, add arm: `if (e is GisSettingChangedEventArgs gis) { InvokeAsync(async () => { if (_map is null) return; var key = gis.IsStreetMapEnabled ? "MapTiler:StyleUrls:LightOn" : "MapTiler:StyleUrls:LightOff"; var url = Configuration.GetValue<string>(key) ?? Configuration.GetValue<string>("MapTiler:StyleUrl") ?? string.Empty; if (string.IsNullOrEmpty(url)) return; await _map.SetBasemapStyleAsync(url); }); return; }` (see `contracts/map-style-events.md` Consumer section; add before the existing audio arm)

**Checkpoint**: `dotnet build` clean. Open blade, toggle Street map on → basemap switches to LightOn; routes, vehicles, checkpoints remain; no Network requests for route-shapes or data; toggle off → back to LightOff. Quickstart Scenarios 2 and 3 pass.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Build verification, quickstart walk, confirm US3 (persistence) is covered by the delivered data layer.

- [x] T011 Run `dotnet build ChefKnifeStudios.TransitJazz.sln` and confirm zero errors and no new warnings
- [ ] T012 [P] Walk quickstart.md Scenario 4 (persistence): toggle on → reload → map paints LightOn from first render; toggle off → reload → paints LightOff — confirms US3 is fully delivered by the existing `SettingsService` serialization (no new code needed)
- [ ] T013 [P] Walk quickstart.md Scenario 5 (rapid toggling) and Scenario 6 (toggle before map ready) to confirm no torn map or errors
- [ ] T014 Walk quickstart.md Scenario 7 (missing config entry) to confirm FR-013: temporarily remove `MapTiler:StyleUrls:LightOn` from dev config, toggle on, confirm map stays on current valid style; restore config

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: No external dependencies; can start after or in parallel with Phase 1
- **Phase 3 (US1)**: Depends on T001 (config StyleUrls present), T003 (`ISettingsService` reads the new bool field). T004 not required.
- **Phase 4 (US2)**: Depends on T002 (label), T003 (new setting field), T004 (`GisSettingChangedEventArgs` type); T005 + T006 not strictly required but Phase 3 should be complete for a working end-to-end.
- **Phase 5 (Polish)**: Depends on all prior phases complete

### User Story Dependencies

- **US1 (default style)**: Depends on Phase 1 config (T001) + Phase 2 model field (T003). No US2 dependency.
- **US2 (hot-switch)**: Depends on Phase 1 config + resx (T001, T002) + Phase 2 both types (T003, T004) + US1 interop (T005, T006 for initial-style consistency). Can technically be implemented before US1 but shares the same C# file changes.
- **US3 (persistence)**: Zero new code — fully delivered by the `Settings` bool (T003) + existing `SettingsService`. Verified in Polish phase.

### Within Each Phase

- T001 and T002 are independent files → parallel
- T003 and T004 are independent files → parallel (T003 is the `Settings.cs` model; T004 is a new EventArgs file)
- T007, T008, T009 are independent files → parallel after Phase 2 complete
- T010 depends on T004 (the event-args type) and T008 (the interop wrapper method); sequence: T008 → T010 or do in the same pass

---

## Parallel Example: Phase 4 (US2)

```text
# After Phase 2 is complete, launch these three in parallel:
Task T007: Implement ChefMap.setMapStyle in map-interop.js
Task T008: Add SetBasemapStyleAsync to Map.razor.Helper.cs
Task T009: Add GisSettingChangedEventArgs arm to SettingsBlade.razor.cs

# Then (depends on T008 existing):
Task T010: Add consumer arm to TransitMap.razor.cs
```

---

## Implementation Strategy

### MVP First (US1 only — LightOff default)

1. Complete Phase 1 (T001, T002)
2. Complete Phase 2 (T003, T004)
3. Complete Phase 3 (T005, T006)
4. **STOP and VALIDATE**: quickstart Scenario 1 — map loads in LightOff
5. US1 is independently demonstrable (the blade already renders the checkbox; it just doesn't fire an effect yet)

### Incremental Delivery

1. Phases 1 + 2 → model + config ready; blade shows the checkbox (no-op on toggle)
2. Phase 3 → US1 complete: map loads in LightOff; reload respects persisted value
3. Phase 4 → US2 + US3 complete: full hot-switch + persistence works
4. Phase 5 → verified and clean

### Full change summary (7 files modified, 1 file created)

| File | Change |
|------|--------|
| `WebApp/wwwroot/appsettings.json` | Add `MapTiler:StyleUrls` block |
| `Client.Shared/Resources/RouteFilterResources.resx` | Add `SettingStreetMap` entry |
| `Client.Shared/Models/Settings.cs` | Add `IsStreetMapEnabled` bool |
| `Client.Shared/EventArgs/GisSettingChangedEventArgs.cs` | **NEW** file |
| `Client.Shared/Components/Map.razor.cs` | Inject `ISettingsService`; update `GetMapSettings` |
| `Client.Shared/Components/Map.razor.Helper.cs` | Add `SetBasemapStyleAsync` |
| `Client.Shared/Components/Blades/SettingsBlade.razor.cs` | Add `IsStreetMapEnabled` switch arm |
| `Client.Shared/wwwroot/js/map-interop.js` | Implement `setMapStyle` (replace no-op) |
| `WebApp/Pages/TransitMap.razor.cs` | Inject `IConfiguration`; add GIS consumer arm |

---

## Notes

- [P] tasks = touch different files, no blocking dependency on prior tasks in the same phase
- US3 (persistence) needs **zero new code** — adding the `IsStreetMapEnabled` field to `Settings.cs` (T003) is sufficient; `SettingsService` serializes/deserializes it automatically
- `appsettings.Development.json` already has the `StyleUrls` block — only `appsettings.json` (production) needs the addition (T001)
- The `ChefMap.setMapStyle` stub already exists in `map-interop.js` with the right signature; T007 replaces the one-liner no-op body
- The blade's pure-reflection render is unchanged — `Settings` remains boolean-only; the new field is picked up automatically
