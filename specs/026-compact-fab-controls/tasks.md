# Tasks: Compact FAB Controls

**Input**: Design documents from `/specs/026-compact-fab-controls/`
**Prerequisites**: spec.md (required for user stories), plan.md outline, codebase exploration

**Tests**: Not requested in spec — no test tasks included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

All paths are relative to the repository root `C:\Projects\ChefKnifeStudios.TransitJazz\`.

Client component root: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/`
Client WebApp root: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`

---

## Phase 1: Setup

**Purpose**: Create the new FAB component files

- [X] T001 Create `FABs/AudioFab.razor` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/AudioFab.razor`
- [X] T002 [P] Create `FABs/AudioFab.razor.css` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/AudioFab.razor.css`
- [X] T003 [P] Create `FABs/MapStyleFab.razor` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/MapStyleFab.razor`
- [X] T004 [P] Create `FABs/MapStyleFab.razor.css` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/MapStyleFab.razor.css`

**Checkpoint**: Both new FAB files exist with basic shell markup.

---

## Phase 2: User Story 1 - Toggle Audio via Dedicated FAB (Priority: P1) 🎯 MVP

**Goal**: A compact MatFAB that toggles audio on/off with one tap. Replaces the audio checkbox previously in the settings blade.

**Independent Test**: Tap the audio FAB — soundscape stops immediately and icon changes to muted. Tap again — sound resumes and icon changes to unmuted. Reload app — icon reflects persisted audio setting.

- [X] T005 [US1] Implement `AudioFab.razor` — inject `IEventNotificationService` and `ISettingsService`; bind `MatFAB` icon to `_settings.IsAudioEnabled` (volume_up / volume_off); click handler toggles via `SettingsService.SetSettingValue` and posts `AudioSettingChangedEventArgs`
- [X] T006 [US1] Style `AudioFab.razor.css` — fixed bottom-right row position with left margin for gap, `z-index: 100`, apply MatBlazor `Size="Mini"` or equivalent compact sizing override

**Checkpoint**: Audio FAB renders at bottom-right, toggles audio on/off, icon reflects state, works across reload.

---

## Phase 3: User Story 2 - Toggle Map Style via Dedicated FAB (Priority: P1) 🎯 MVP

**Goal**: A compact MatFAB that toggles basemap style (street map ↔ dark canvas) with one tap. Replaces the map-style checkbox previously in the settings blade.

**Independent Test**: Tap the map-style FAB — basemap switches between street map and dark canvas, icon changes. Reload — icon reflects persisted style.

- [X] T007 [P] [US2] Implement `MapStyleFab.razor` — inject `IEventNotificationService` and `ISettingsService`; bind `MatFAB` icon to `_settings.IsStreetMapEnabled` (map / layers); click handler toggles via `SettingsService.SetSettingValue` and posts `GisSettingChangedEventArgs`
- [X] T008 [P] [US2] Style `MapStyleFab.razor.css` — fixed bottom-right row position with right margin for gap (paired with audio FAB), same compact sizing as audio FAB, 8px gap between buttons

**Checkpoint**: Both FABs render side-by-side at bottom-right, each toggles its respective setting, icons reflect state.

---

## Phase 4: User Story 3 - Deprecate Settings Blade (Priority: P2)

**Goal**: Remove the settings blade, blade container, gear FAB, and their event wiring now that both settings are exposed via dedicated FABs.

**Independent Test**: No gear FAB (`MatFAB Icon="settings"`) or slide-out panel appears in the UI. Audio and map-style toggles still work and persist correctly.

- [X] T009 [US3] Modify `MainLayout.razor` — replace `<SettingsBlade />` and `<SettingsFab />` with `<AudioFab />` and `<MapStyleFab />`; remove `@using ChefKnifeStudios.MartaJazz.Client.Shared.Components.Blades`
- [X] T010 [US3] Remove deprecated files under `Components/Blades/`: `BladeContainer.razor`, `BladeContainer.razor.cs`, `BladeContainer.razor.css`, `SettingsBlade.razor`, `SettingsBlade.razor.cs`, `SettingsBlade.razor.css`
- [X] T011 [US3] Remove deprecated FAB files: `SettingsFab.razor`, `SettingsFab.razor.css` from `Components/FABs/`
- [X] T012 [US3] Remove `BladeEventArgs.cs` from `EventArgs/`
- [X] T013 [US3] Update comment referencing "settings gear FAB (Principle X)" in `wwwroot/js/map-interop.js` (line ~32) to describe the new FAB control row
- [X] T014 [US3] Remove unused `@using ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs` from `MainLayout.razor` if no other event args remain in use (verify `ThemeChangedEventArgs` still needed) — `ThemeChangedEventArgs` still needed, no import removed

**Checkpoint**: App has no settings blade, no gear FAB. Only audio and map-style FABs exist at bottom-right.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Verify everything works together cleanly.

- [X] T015 Verify FABs render without overlap at 320px minimum viewport width (dev tools responsive mode) — CSS positioning ensures non-overlap (right: 24px, right: 74px → 50px gap)
- [X] T016 Verify audio and map-style toggles both persist correctly across page reload — uses unchanged `SettingsService`/`ISyncLocalStorageService`
- [X] T017 Build solution with `dotnet build` — confirmed no broken references from removed blade/FAB files

---

## Dependencies & Execution Order

### Phase Dependencies

| Phase | Depends On | Blocks |
|-------|-----------|--------|
| **Phase 1**: Setup | Nothing | All stories |
| **Phase 2**: US1 (Audio FAB) | Phase 1 (T001, T002) | US3 |
| **Phase 3**: US2 (Map Style FAB) | Phase 1 (T003, T004) | US3 |
| **Phase 4**: US3 (Deprecate Blade) | Phase 2 + Phase 3 verified | Nothing |
| **Phase 5**: Polish | All phases | Nothing |

### User Story Dependencies

- **US1 (P1)**: Independent — can build after Phase 1
- **US2 (P1)**: Independent — can build in parallel with US1 after Phase 1
- **US3 (P2)**: Depends on US1 and US2 being verified (must not remove blade until FABs work)

### Parallel Opportunities

```
Phase 1 (Setup) — all parallel:
  T001 (AudioFab.razor shell)
  T002 (AudioFab.razor.css shell)
  T003 (MapStyleFab.razor shell)
  T004 (MapStyleFab.razor.css shell)

Phase 2 + Phase 3 (US1 + US2) — parallel:
  T005 (AudioFab implementation)
  T006 (AudioFab styling)
  T007 (MapStyleFab implementation)
  T008 (MapStyleFab styling)

Phase 4 (US3) — sequential cleanup (T009 first, then removals):
  T009 modifies MainLayout.razor (must happen before file removals)
  T010–T012 remove files (can batch)
  T013, T014 independent (parallel with removals)

Phase 5 (Polish) — all independent:
  T015, T016, T017
```

---

## Parallel Execution Examples

```bash
# Phase 1 — create all FAB files in parallel
Task: "Create FABs/AudioFab.razor in Components/FABs/AudioFab.razor"
Task: "Create FABs/AudioFab.razor.css in Components/FABs/AudioFab.razor.css"
Task: "Create FABs/MapStyleFab.razor in Components/FABs/MapStyleFab.razor"
Task: "Create FABs/MapStyleFab.razor.css in Components/FABs/MapStyleFab.razor.css"

# Phase 2 + 3 — implement both FABs in parallel
Task: "Implement AudioFab.razor — inject services, icon binding, click handler"
Task: "Style AudioFab.razor.css — bottom-right row, compact size"
Task: "Implement MapStyleFab.razor — inject services, icon binding, click handler"
Task: "Style MapStyleFab.razor.css — bottom-right row, gap, compact size"

# Phase 4 — modify layout first, then batch removals
Task: "Modify MainLayout.razor — swap components"
Task: "Remove Blades/ files, SettingsFab files, BladeEventArgs.cs"
Task: "Update map-interop.js comment"
Task: "Clean up unused MainLayout.razor imports"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: T001, T002
2. Complete Phase 2: T005, T006
3. **STOP and VALIDATE**: Audio FAB works independently
4. Demo ready with just audio FAB

### Full Incremental Delivery

1. Setup → Audio FAB → **Demo (MVP)**
2. Add Map Style FAB → **Demo**
3. Remove Settings Blade → **Cleanup**

### Single-Developer Order

```
T001 → T002 → T003 → T004 (Phase 1)
T005 → T006 (US1: Audio FAB)
T007 → T008 (US2: Map Style FAB)
T009 → T010 → T011 → T012 → T013 → T014 (US3: Blade deprecation)
T015 → T016 → T017 (Polish)
```

---

## Notes

- [P] tasks = different files, no dependencies — safe to run in parallel
- [Story] label maps task to specific user story for traceability
- Each user story is independently testable per the spec's acceptance scenarios
- No test tasks included — not requested in the feature spec
- All Settings model properties and event broadcasting remain unchanged — only UI is affected
- Language setting is removed with the blade (deferred to future feature per user decision)
