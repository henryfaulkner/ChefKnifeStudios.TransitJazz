---
description: "Task list for Settings Blade implementation"
---

# Tasks: Settings Blade

**Input**: Design documents from `/specs/016-settings-blade/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**North star for implementation patterns**: `docs/SETTINGS_BLADE_DESIGN_DOCUMENT.md` (follow its
BladeContainer/SettingsBlade/SettingsService/reflection patterns verbatim, with the deviations recorded in
`research.md`: labels via `.resx`, synchronous bus handler, 100ms-in/instant-out timing, cached `_elementId`,
Audio/GIS/Checkpoint roster instead of Audio/AppTour/DarkMode).

**Tests**: NOT requested (no automated client UI harness exists in this repo). Verification is manual via
`quickstart.md`. No test tasks generated.

**Organization**: Tasks grouped by user story. Story mapping (reconciled with the plan's deferrals):
- **US1** = Open & adjust application settings (spec P1) — the blade surface + reflection-rendered toggles.
- **US2** = Settings persist across sessions (spec P1) — `Settings` model + `SettingsService` + local storage.
- **US3** = Setting effects apply immediately and app-wide (spec P2; the constitution-aligned replacement for
  the deferred Dark-Mode story) — Audio mute, GIS basemap swap (layers persist), Checkpoint visibility.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (Setup, Foundational, Polish phases carry no story label)

## Path Conventions

Blazor WASM client. Namespace root **`ChefKnifeStudios.MartaJazz`**. Two client projects touched:
- Shared RCL: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`
- WASM host: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`
- Existing bus + marker: `src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/EventNotificationService.cs`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify the already-present dependencies and create the new folders this feature uses.

- [x] T001 Verify prerequisite registrations already exist in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Program.cs`: `AddBlazoredLocalStorage()`, `AddLocalization()`, `AddMatBlazor()`, and the singleton `IEventNotificationService` (do NOT re-add; this task only confirms and notes line numbers for later edits)
- [x] T002 [P] Create folder `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Blades/`
- [x] T003 [P] Create folder `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/`
- [x] T004 [P] Confirm `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Constants/` and `.../Models/` exist (ColorConstants.cs already lives in Constants/); no action if present

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `Settings` model, persistence, storage key, and the open/close bus event — every user story
depends on these. Effect-event payloads are also created here so US3 can wire them without re-touching shared
files.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T005 [P] Create `Settings` model in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Models/Settings.cs` — `partial class Settings : ObservableObject` with three `[ObservableProperty]` + `[property: Description("<resourceKey>")]` bool fields per data-model.md: `_isAudioEnabled = true` (key `SettingAudioEnabled`), `_isStreetsBasemap = true` (key `SettingStreetsBasemap`), `_areCheckpointsVisible = true` (key `SettingCheckpointsVisible`). Boolean-only invariant.
- [x] T006 [P] Create `LocalStorageConstants` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Constants/LocalStorageConstants.cs` with `public const string SettingsKey = "Setting";` (singular, per contract)
- [x] T007 Create `ISettingsService`/`SettingsService` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/SettingsService.cs` per `contracts/settings-service.md`: wraps `ISyncLocalStorageService`; `GetSettings()` lazy-seeds + persists `new Settings()` on first read; `SaveSettings`; `GetSettingValue<T>`/`SetSettingValue<T>` by reflection; guard the read in try/catch and seed defaults on deserialize failure (depends on T005, T006)
- [x] T008 [P] Create `BladeEventArgs` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/EventArgs/BladeEventArgs.cs` — `: IEventArgs`, `enum Types { Close, Settings }`, `required Types Type { get; init; }`, `object? Data { get; init; }` (uses existing `ChefKnifeStudios.MartaJazz.Client.Core.Services.IEventArgs`)
- [x] T009 [P] Create effect event payloads in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/EventArgs/`: `AudioSettingChangedEventArgs.cs` (`required bool IsAudioEnabled`), `GisSettingChangedEventArgs.cs` (`required bool IsStreetsBasemap`), `CheckpointVisibilityChangedEventArgs.cs` (`required bool AreCheckpointsVisible`) — each `: IEventArgs`, absolute-state (not flip)
- [x] T010 Register `ISettingsService` as transient in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared.WebApp/Program.cs` → `builder.Services.AddTransient<ISettingsService, SettingsService>();` (file: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Program.cs`; depends on T007)

**Checkpoint**: Settings persist programmatically and the open/close + effect event types exist. UI can begin.

---

## Phase 3: User Story 1 - Open & adjust application settings (Priority: P1) 🎯 MVP

**Goal**: A gear FAB opens a right-side drawer that renders one localized toggle per boolean setting; the
drawer dismisses on ✕, outside-click, or gear re-click, with the opening click guarded against self-close.

**Independent Test**: Click the gear → drawer slides in ≤100ms showing Audio / Street map / Checkpoints
toggles; dismiss via ✕, outside-click, and gear re-click; the opening click does not bounce it shut.

### Implementation for User Story 1

- [x] T011 [P] [US1] Add the four blade strings to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Resources/RouteFilterResources.resx`: `SettingsTitle` ("Settings"), `SettingAudioEnabled` ("Audio"), `SettingStreetsBasemap` ("Street map"), `SettingCheckpointsVisible` ("Checkpoints")
- [x] T012 [P] [US1] Create outside-click JS module `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/outside-click.js` exporting `addOutsideClickListener(elementId, dotNetHelper)` (returns the listener handle) and `removeOutsideClickListener(listener)` per `contracts/outside-click-interop.md`
- [x] T013 [P] [US1] Create `IOutsideClickJsInterop` + `OutsideClickJsInterop` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/JsInterop/IOutsideClickJsInterop.cs` and `.../OutsideClickJsInterop.cs` — lazy-module idiom matching `TransitSynthJsInterop` (import `./_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/outside-click.js?g=<guid>`), dictionary of `(callback, listener)` keyed by elementId, `[JSInvokable] HandleOutsideClick(string elementId)`, `IAsyncDisposable`, try/catch + `ILogger`
- [x] T014 [US1] Register `IOutsideClickJsInterop` as singleton in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Program.cs` → `builder.Services.AddSingleton<IOutsideClickJsInterop, OutsideClickJsInterop>();` (depends on T013)
- [x] T015 [US1] Create `BladeContainer.razor` markup in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Blades/BladeContainer.razor` — `<div id="@_elementId" class="blade-container @(_isOpen ? "open" : "")">` with a `MatIconButton Icon="close"` (Class `cross`) wired to `HandleClosePressed`, and a `content-container` rendering `@ContentFragment`
- [x] T016 [US1] Create `BladeContainer.razor.cs` in `.../Components/Blades/BladeContainer.razor.cs` per `contracts/outside-click-interop.md`: `[Parameter] RenderFragment ContentFragment`; inject `IEventNotificationService` + `IOutsideClickJsInterop`; `readonly string _elementId = $"blade-{Guid.NewGuid()}";`; public `Open()` (set `_lastOpenedUtc`, `_isOpen=true`, add outside-click listener, `StateHasChanged`); public `Close()` (no-op within `MinOpenDurationMs=300` of open, else `_isOpen=false`, remove listener, `StateHasChanged`); `HandleClosePressed()` posts `BladeEventArgs{ Type=Close }`; `Dispose()` removes the listener (depends on T013, T008)
- [x] T017 [P] [US1] Create `BladeContainer.razor.css` in `.../Components/Blades/BladeContainer.razor.css` — right-anchored drawer, `transform: translateX(100%)`, `.open { transform: translateX(0) }`, transition **100ms** (Principle XI), full `100dvh`, hidden scrollbars, `::deep .cross` positioned top-right
- [x] T018 [US1] Create `SettingsBlade.razor` markup in `.../Components/Blades/SettingsBlade.razor` — wraps `<BladeContainer @ref="_bladeContainer">`; title from `@Localizer["SettingsTitle"]`; reflect `typeof(Settings).GetProperties()`, render one `MatCheckbox TValue="bool"` per property bound to `property.GetValue(settings) as bool? ?? false`, `ValueChanged` → `HandleSettingPressed(property.Name, e)`; label resolved via `IStringLocalizer<RouteFilterResources>` using the `[Description]` value as the key, falling back to `[Description]` raw then property name (depends on T011, T015)
- [x] T019 [US1] Create `SettingsBlade.razor.cs` in `.../Components/Blades/SettingsBlade.razor.cs` per `contracts/settings-events.md`: inject `IEventNotificationService`, `ISettingsService`, `IStringLocalizer<RouteFilterResources>`, `ILogger`; subscribe to the bus in `OnInitialized`, unsubscribe in `Dispose`; handler `if (e is not BladeEventArgs blade) return;` then `Settings`→`_bladeContainer?.Open()`, else `Close()`; `HandleSettingPressed(name, val)` calls `SettingsService.SetSettingValue(name, val)` (effect-posting added in US3); `[Inject]` blade container ref via `@ref` (depends on T007, T016, T008)
- [x] T020 [P] [US1] Create `SettingsBlade.razor.css` in `.../Components/Blades/SettingsBlade.razor.css` — settings-list flex column, label styles
- [x] T021 [P] [US1] Create `SettingsFab.razor` in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/SettingsFab.razor` — `MatFAB Icon="settings"`; inject `IEventNotificationService`; on click post a **toggle** `BladeEventArgs` (Settings when closed, Close when open) per the chosen scheme in `contracts/settings-events.md`; track open/closed by also listening to the bus, unsubscribe on dispose (depends on T008)
- [x] T022 [P] [US1] Create `SettingsFab.razor.css` in `.../Components/FABs/SettingsFab.razor.css` — fixed bottom-right anchor (constitution: gear FAB bottom-right)
- [x] T023 [US1] Host the blade + FAB once in `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Layout/MainLayout.razor`: add `<SettingsFab />` and `<SettingsBlade />` inside the `MatThemeProvider` (alongside `@Body`); add the needed `@using` for `...Client.Shared.Components.Blades` and `...Components.FABs` (do NOT disturb the existing `ThemeChangedEventArgs` handler) (depends on T019, T021)

**Checkpoint**: Drawer opens/closes correctly and shows three localized toggles. Toggles persist (US2 work
proves persistence; the wiring through `SettingsService` is already in T019). No effects applied yet.

---

## Phase 4: User Story 2 - Settings persist across sessions (Priority: P1)

**Goal**: Toggled settings survive an app reload; a fresh browser seeds defaults that read back identically.

**Independent Test**: Toggle Checkpoints off, reload, reopen → still off; local-storage key `Setting` holds the
blob; clearing storage and reopening seeds all-true defaults.

> Most of US2's mechanism is the Foundational `SettingsService` (T007) plus the blade's persist call (T019).
> These tasks verify the round-trip and harden the seed/initial-render path.

- [x] T024 [US2] In `SettingsBlade.razor` (`.../Components/Blades/SettingsBlade.razor`), ensure the checkbox initial `Value` reads from `SettingsService.GetSettings()` so a reopened drawer reflects persisted state (confirm the reflection read happens on each open/render, not cached stale) (depends on T018)
- [x] T025 [US2] Verify seed-on-first-read: confirm `SettingsService.GetSettings()` writes defaults under key `Setting` when absent and returns the same object on the next read (manual check per quickstart Scenario 3); add a defensive try/catch around the local-storage read in `SettingsService.cs` if not already added in T007 (depends on T007)

**Checkpoint**: Persistence round-trips across reloads; defaults seed correctly.

---

## Phase 5: User Story 3 - Setting effects apply immediately and app-wide (Priority: P2)

**Goal**: Toggling a setting takes effect instantly across the app: Audio mutes/unmutes the synth, GIS swaps
the basemap (route/bus/checkpoint GeoJSON layers persist — Principle VII), Checkpoints show/hide. This is the
constitution-aligned replacement for the deferred Dark-Mode story.

**Independent Test**: Toggle Audio off → notes stop; toggle Street map off → blank dark canvas but
routes/buses/checkpoints stay put; toggle Checkpoints off → markers hide. Each persists across reload.

### Effect plumbing (blade → bus)

- [x] T026 [US3] In `SettingsBlade.razor.cs` (`.../Components/Blades/SettingsBlade.razor.cs`), extend `HandleSettingPressed` to post the matching absolute-state effect event after persisting, per `contracts/settings-events.md`: `nameof(Settings.IsAudioEnabled)`→`AudioSettingChangedEventArgs`, `nameof(Settings.IsStreetsBasemap)`→`GisSettingChangedEventArgs`, `nameof(Settings.AreCheckpointsVisible)`→`CheckpointVisibilityChangedEventArgs` (depends on T019, T009)

### Map interop additions (JS + component wrappers)

- [x] T027 [P] [US3] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`, add `ChefMap.setBasemapStyle(elementId, isStreets)` — `map.setStyle(streetsUrl | blankDarkStyle)`; on `style.load`/`styledata` after the swap, **re-add** the route/bus/checkpoint GeoJSON sources+layers from cached state (check-before-add to avoid duplicates), and re-apply any active focused-route highlight (feature 015); guard if map not yet initialized (Principle VII — no re-fetch)
- [x] T028 [P] [US3] In the same `map-interop.js`, add `ChefMap.setCheckpointVisibility(elementId, visible)` — set the checkpoint layer(s) `visibility` layout property to `'visible'`/`'none'`; idempotent, no source mutation
- [x] T029 [US3] Add component wrappers in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Map.razor.Helper.cs`: `SetBasemapStyleAsync(bool isStreets)` → `ChefMap.setBasemapStyle(ElementId, isStreets)` and `SetCheckpointVisibilityAsync(bool visible)` → `ChefMap.setCheckpointVisibility(ElementId, visible)`, matching the existing try/catch interop style (depends on T027, T028)

### Effect consumers (TransitMap subscribes)

- [x] T030 [US3] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`, subscribe to `IEventNotificationService.EventReceived` in `OnInitialized` and unsubscribe in `Dispose`; in the handler route the three effect events: `AudioSettingChangedEventArgs`→mute/unmute synth, `GisSettingChangedEventArgs`→`Map.SetBasemapStyleAsync(...)`, `CheckpointVisibilityChangedEventArgs`→`Map.SetCheckpointVisibilityAsync(...)`; wrap render-affecting changes in `InvokeAsync(StateHasChanged)`; ignore unrelated events (depends on T026, T029)
- [x] T031 [US3] Implement the audio-mute gate in the synth path: in `TransitMap.razor.cs` (and/or `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Services/JsInterop/TransitSynthJsInterop.cs`) skip `TriggerNoteAsync`/`triggerNote` when audio is muted; the muted flag is set from `AudioSettingChangedEventArgs` and initialized from `SettingsService.GetSettings().IsAudioEnabled` on load (depends on T030)
- [x] T032 [US3] Apply persisted effects on initial load: on `TransitMap` first render (or map ready), read `SettingsService.GetSettings()` and apply the stored GIS basemap, checkpoint visibility, and audio-mute state so the app opens in the previously chosen configuration (FR-011 analog for effects) (depends on T030, T031)

**Checkpoint**: All three settings produce immediate, persisted, app-wide effects; data layers survive the
GIS swap.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Leak-safety, localization compliance, and full quickstart validation.

- [x] T033 [P] Audit disposal/leak-safety (FR-012, SC-006): `BladeContainer.Dispose` removes the outside-click listener; `SettingsBlade`, `SettingsFab`, and `TransitMap` each unsubscribe from the bus in `Dispose`; no duplicate `document` click listeners accumulate across open/close/navigation
- [x] T034 [P] Localization compliance sweep (Principle XII): confirm NO hardcoded user-visible copy in `SettingsBlade.razor`/`.cs`, `SettingsFab.razor`, or `BladeContainer.razor` — every label/title flows through `IStringLocalizer<RouteFilterResources>`; the four keys exist in `RouteFilterResources.resx`
- [x] T035 Build the solution: `dotnet build src/ChefKnifeStudios.TransitJazz.sln` — resolve any compile errors
- [ ] T036 Run full manual validation per `specs/016-settings-blade/quickstart.md` (Scenarios 1–7 + localization check); confirm 100ms-in/instant-out timing, gear re-click closes, GIS layers persist, and reload restores all effects

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**.
- **US1 (Phase 3)**: Depends on Foundational. The MVP surface.
- **US2 (Phase 4)**: Depends on Foundational; in practice rides on US1's blade (its persist wiring lives in
  T019) — verifiable once US1 renders toggles.
- **US3 (Phase 5)**: Depends on Foundational + US1 (needs `HandleSettingPressed` from T019 and the hosted
  blade); effect map-interop tasks (T027/T028) are independent of US1 and can start in parallel with US1.
- **Polish (Phase 6)**: Depends on all targeted stories.

### User Story Dependencies

- **US1 (P1)**: Independent after Foundational — the MVP.
- **US2 (P1)**: Mechanism is in Foundational (`SettingsService`) + US1 (persist call); independently testable.
- **US3 (P2)**: Builds on US1's blade for the post-effect events; map-interop JS (T027/T028) is independent.

### Within Each User Story

- Models/constants before services (T005/T006 → T007).
- JS module before its interop wrapper (T012 → T013); interop before registration (T013 → T014).
- Container before the blade that wraps it (T015/T016 → T018/T019); blade + FAB before hosting (→ T023).
- Blade persist call before effect-posting (T019 → T026); map JS before component wrappers before consumers
  (T027/T028 → T029 → T030).

---

## Parallel Opportunities

- **Setup**: T002, T003, T004 in parallel.
- **Foundational**: T005, T006, T008, T009 in parallel (T007 after T005/T006; T010 after T007).
- **US1**: T011, T012, T017, T020, T021, T022 in parallel (different files); T013→T014; T015/T016→T018/T019→T023.
- **US3**: T027 and T028 in parallel (then T029); T026 can proceed once T019 exists, in parallel with the JS.
- **Polish**: T033 and T034 in parallel; T035 then T036.

### Parallel Example: User Story 1

```text
# Independent files, launch together:
Task: "T011 Add blade strings to RouteFilterResources.resx"
Task: "T012 Create wwwroot/js/outside-click.js"
Task: "T017 Create BladeContainer.razor.css"
Task: "T020 Create SettingsBlade.razor.css"
Task: "T021 Create SettingsFab.razor"
Task: "T022 Create SettingsFab.razor.css"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational (CRITICAL — blocks everything).
2. Phase 3 US1 → **STOP and validate**: drawer opens/closes, three localized toggles render, dismissal works.
   This is a demoable MVP (the constitution's mandated settings surface).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → drawer + toggles (MVP) → validate.
3. US2 → confirm persistence across reload → validate.
4. US3 → wire Audio/GIS/Checkpoint effects → validate (GIS layer-persistence is the key Principle VII check).
5. Polish → leak audit, localization sweep, build, full quickstart.

---

## Notes

- `[P]` = different files, no incomplete-task dependency.
- Reuse the **existing** `IEventNotificationService` (synchronous `void` handler) — do NOT introduce a new bus.
- `Blazored.LocalStorage`, `AddLocalization()`, `MatBlazor`, and the singleton bus are **already registered** —
  only `ISettingsService` (T010) and `IOutsideClickJsInterop` (T014) are new registrations.
- Follow the reference design document for BladeContainer/SettingsBlade/SettingsService shapes; apply the
  `research.md` deviations (resx labels, synchronous handler with `if (e is not BladeEventArgs) return;`, 100ms
  timing, cached `_elementId`, Audio/GIS/Checkpoint roster).
- Deferred (NOT in scope): Language selector, Dark-Mode toggle. Do not add them.
- Commit after each task or logical group; stop at any checkpoint to validate independently.
