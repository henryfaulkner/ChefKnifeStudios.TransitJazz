# Tasks: 035 — Dark Mode Polish

**Input**: Design documents from `specs/035-dark-mode-polish/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

No setup or foundational phases — all tasks are targeted edits to existing components. No new files, no new services, no new event types.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no shared state)
- **[Story]**: Which user story this task belongs to

---

## Phase 1: P1 Regressions (US1 + US2) — Fix Before Testing Anything Else

### US1 — Audio FAB Repositioning

**Goal**: Audio FAB is visible and tappable with no overlap in the bottom FAB row.

**Independent Test**: Open the app and confirm all 5 FABs (City, DarkMode, Audio, Info, MapStyle) are visible and non-overlapping.

- [x] T001 [P] [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/InfoFab.razor.css`, change `right: 224px` → `right: 174px`
- [x] T002 [P] [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/MapStyleFab.razor.css`, change `right: 124px` → `right: 224px`

**Checkpoint**: Audio FAB at 124px, Info at 174px, MapStyle at 224px — no overlap. Build and visually verify.

---

### US2 — DarkMode FAB Icon Semantics

**Goal**: Sun icon in light mode, moon icon in dark mode.

**Independent Test**: Toggle dark mode; verify icon is moon when dark, sun when light.

- [x] T003 [US2] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/DarkModeFab.razor`, fix `GetIcon()`: change `_settings.IsDarkModeEnabled ? "light_mode" : "dark_mode"` → `_settings.IsDarkModeEnabled ? "dark_mode" : "light_mode"`

**Checkpoint**: Build and verify: tap DarkMode FAB → dark mode on → icon shows moon (`dark_mode`). Tap again → icon shows sun (`light_mode`).

---

## Phase 2: P2 Dark Mode Propagation (US3–US6)

All four tasks in this phase are independent (different files) and can be done in parallel.

---

### US3 — AudioUnlockOverlay Dark Mode

**Goal**: Overlay renders with dark background and light text when dark mode is active.

**Independent Test**: Enable dark mode, reload app (to see overlay) → overlay is dark; light mode → overlay is white.

- [x] T004 [US3] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/AudioUnlockOverlay.razor`:
  1. Add `@implements IDisposable`, `@inject IEventNotificationService EventNotificationService`, `@inject ISettingsService SettingsService` directives
  2. Add `bool _isDark;` field in `@code`
  3. Seed `_isDark = SettingsService.GetSettings().IsDarkModeEnabled;` in `OnInitialized`
  4. Add `HandleEvent` handler and `Dispose` per the plan.md subscription pattern
  5. Add `audio-unlock-overlay--dark` class conditionally on the root `<div>`
  6. Add dark CSS overrides inside the inline `<style>` block:
     ```css
     .audio-unlock-overlay--dark {
         background: #1A1C1E;
     }
     .audio-unlock-overlay--dark .audio-unlock-overlay__header,
     .audio-unlock-overlay--dark .audio-unlock-overlay__body p {
         color: rgba(226, 226, 230, 0.9);
     }
     .audio-unlock-overlay--dark .audio-unlock-overlay__button {
         color: rgba(226, 226, 230, 0.9);
         border-color: rgba(255, 255, 255, 0.4);
     }
     .audio-unlock-overlay--dark .audio-unlock-overlay__button:hover {
         border-color: rgba(255, 255, 255, 0.8);
         background: rgba(255, 255, 255, 0.05);
     }
     ```

**Checkpoint**: Dark mode on → overlay background `#1A1C1E`, text light. Light mode → unchanged white.

---

### US4 — InfoOverlay Dark Mode

**Goal**: Info overlay renders dark when dark mode is active.

**Independent Test**: Enable dark mode, tap Info FAB → overlay is dark; light mode → overlay is white.

- [x] T005 [US4] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/InfoFab.razor`:
  1. Add `@implements IDisposable`, `@inject IEventNotificationService EventNotificationService`, `@inject ISettingsService SettingsService` directives
  2. Add `bool _isDark;` field in `@code`
  3. Seed `_isDark = SettingsService.GetSettings().IsDarkModeEnabled;` in `OnInitialized` (already has `OnInitialized`)
  4. Add `HandleEvent` handler and `Dispose` per plan.md subscription pattern
  5. Add `info-overlay--dark` class conditionally on the `<div class="info-overlay">` element
- [x] T006 [US4] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/InfoFab.razor.css`, add dark overrides:
  ```css
  .info-overlay--dark {
      background: #1A1C1E;
  }
  .info-overlay--dark .info-overlay__body p,
  .info-overlay--dark .info-overlay__footer {
      color: rgba(226, 226, 230, 0.85);
  }
  .info-overlay--dark .info-overlay__button {
      color: rgba(226, 226, 230, 0.9);
      border-color: rgba(255, 255, 255, 0.4);
  }
  .info-overlay--dark .info-overlay__button:hover {
      border-color: rgba(255, 255, 255, 0.8);
      background: rgba(255, 255, 255, 0.05);
  }
  ```

**Checkpoint**: Dark mode on → Info overlay background `#1A1C1E`, text light. Also verify the overlay updates if dark mode is toggled while it is open.

---

### US5 — TransitRunningLabel Dark Mode

**Goal**: Label text is legible (light color) when dark mode is active.

**Independent Test**: Enable dark mode; observe TransitRunningLabel count text is light against the dark basemap.

- [x] T007 [US5] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/TransitRunningLabel.razor`:
  1. Add `@implements IDisposable`, `@inject IEventNotificationService EventNotificationService`, `@inject ISettingsService SettingsService` directives
  2. Add `bool _isDark;` field in `@code` block
  3. Seed `_isDark = SettingsService.GetSettings().IsDarkModeEnabled;` in `OnInitialized` (add after existing subscription)
  4. Add `HandleEvent` handler and `Dispose` per plan.md subscription pattern (alongside existing `PropertyChanged` handler)
  5. Add `transit-running-label--dark` class conditionally on root `<div>`
  6. Add dark CSS overrides inside the inline `<style>` block:
     ```css
     .transit-running-label--dark .transit-running-label__count,
     .transit-running-label--dark .transit-running-label__text {
         color: rgba(226, 226, 230, 0.9);
     }
     ```

**Checkpoint**: Dark mode on → count and label text light. Rail/bus color dots unchanged (still legible on dark).

---

### US6 — RouteFilters Dark Mode

**Goal**: Route filter panel renders with dark-appropriate neutral label colors when dark mode is active.

**Independent Test**: Enable dark mode, expand route filter panel → section labels and bus count text are light.

- [x] T008 [US6] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/RouteFilters.razor.cs`:
  1. Add `using` imports for `IEventNotificationService`, `ThemeChangedEventArgs`, `ISettingsService`
  2. Inject `IEventNotificationService EventNotificationService` and `ISettingsService SettingsService`
  3. Implement `IDisposable`
  4. Add `bool _isDark;` field
  5. Seed `_isDark = SettingsService.GetSettings().IsDarkModeEnabled;` in `OnInitialized`
  6. Add `HandleEvent` handler (cast to `ThemeChangedEventArgs`, set `_isDark`, call `InvokeAsync(StateHasChanged)`)
  7. Add `Dispose` to unsubscribe
- [x] T009 [US6] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/RouteFilters.razor`, add `route-filters--dark` CSS class conditionally on the root `<div class="route-filters">`:
  ```razor
  <div class="route-filters @(_isDark ? "route-filters--dark" : "")">
  ```
- [x] T010 [US6] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/RouteFilters.razor.css`, add dark overrides:
  ```css
  .route-filters--dark .route-filters__bus-count {
      color: rgba(193, 199, 206, 0.7);
  }
  .route-filters--dark ::deep .route-filters__section-label {
      color: rgba(193, 199, 206, 0.7) !important;
  }
  ```

**Checkpoint**: Dark mode on, route filter open → section labels and count text are light. Route color circles unchanged.

---

## Phase 3: Polish & QA

- [x] T011 Build the solution and confirm zero new errors: `dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ChefKnifeStudios.MartaJazz.Client.Shared.csproj --no-restore -v q`
- [x] T012 Build the WebApp project: `dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/ChefKnifeStudios.MartaJazz.Client.WebApp.csproj --no-restore -v q`
- [ ] T013 Manual QA per quickstart.md: verify all 6 user stories, no overlap in FAB row, correct icons, dark overlays from first paint, no GTFS re-fetch on toggle

---

## Dependencies & Execution Order

- T001 and T002 are [P] — touch different CSS files, run together
- T003 — independent, no dependencies
- T004, T005+T006, T007, T008+T009+T010 — all independent, all [P] opportunity
- T009 depends on T008 (needs `_isDark` field from the `.cs` code-behind)
- T010 depends on T009 (the CSS class must exist before overrides are meaningful, though they build fine independently)
- T011, T012 after all implementation tasks
- T013 after T011, T012

### Parallel Opportunities

```
Phase 1 (run together): T001, T002, T003
Phase 2 (run together): T004, T005, T006, T007, T008
Phase 2 (then):         T009, T010 (after T008)
Phase 3 (sequential):   T011 → T012 → T013
```

---

## Implementation Strategy

### MVP First (P1 regressions only — US1 + US2)

1. T001 + T002: Fix Audio FAB collision
2. T003: Fix DarkMode FAB icon
3. Build + verify no overlap, correct icon

### Full Feature (all 6 stories)

1. T001–T003 (P1 regressions)
2. T004–T010 in parallel (dark mode propagation)
3. T011–T013 (build + QA)
