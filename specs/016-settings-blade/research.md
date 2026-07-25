# Phase 0 Research: Settings Blade

All open questions from the spec/clarifications are resolved here. The reference design document
(`docs/SETTINGS_BLADE_DESIGN_DOCUMENT.md`) supplies the implementation pattern; this file records where we
follow it verbatim, where the existing MartaJazz codebase already provides a piece, and where we deviate.

## D1 — Settings roster

- **Decision**: Ship exactly three **boolean** settings: **Audio** (mute/unmute), **GIS** (streets basemap ↔
  blank dark canvas), **Checkpoint visibility** (show/hide checkpoint markers). Defer **Language** and
  **Dark-Mode**.
- **Rationale**: Resolved by user clarification. The constitution (Principle XII) mandates Audio + GIS +
  Language; the user elected to substitute Checkpoint visibility for Language now and defer Language (and the
  doc's Dark-Mode). All three shipped settings are booleans, which preserves the doc's pure-reflection render
  model with zero adaptation.
- **Alternatives considered**: (a) Constitution-literal Audio+GIS+Language — rejected for now because the
  non-boolean Language control breaks pure reflection and pulls in a culture switcher. (b) Doc-literal
  Audio+AppTour+DarkMode — rejected: AppTour has no feature in this app, and it omits the mandated GIS control.

## D2 — Render model: pure reflection over a boolean `Settings` model

- **Decision**: Reproduce the doc's pattern exactly — a `Settings : ObservableObject` with
  `[ObservableProperty]` + `[property: Description("…")]` on each `bool`, and a `SettingsBlade` that does
  `typeof(Settings).GetProperties()` → one `MatCheckbox` per property, label from `DescriptionAttribute`.
- **Rationale**: All three settings are boolean (D1), so reflection is sufficient and matches the north-star
  doc. The `[property:]` forwarding is required so `[Description]` lands on the generated property (the doc
  calls this out explicitly).
- **Deviation from doc**: The **visible label** is NOT the raw `[Description]` string. Per Principle XII (no
  hardcoded inline copy), `[Description]` carries a **resource key**; the blade resolves it through
  `IStringLocalizer<RouteFilterResources>` for the displayed text. (If a key is missing, fall back to the
  `[Description]` text, then the property name.)
- **Alternatives considered**: Explicit per-control markup — unnecessary while every setting is boolean; would
  diverge from the doc for no benefit.

## D3 — Event bus: reuse the existing `IEventNotificationService`

- **Decision**: Use the **existing** `ChefKnifeStudios.MartaJazz.Client.Core.Services.IEventNotificationService`
  (already registered as a singleton in `Program.cs`) for both (a) the FAB→blade open/close signal and (b)
  broadcasting each setting's effect to the map/audio consumers.
- **Rationale**: The doc's design centers on this exact bus; MartaJazz already has it, already shares it as a
  singleton, and `MainLayout` already subscribes to it for `ThemeChangedEventArgs`. No new bus.
- **Note on handler signature**: The existing delegate is `void EventReceivedEventHandler(object, IEventArgs)`
  (synchronous), **not** the doc's `Task`-returning handler. The blade's handler must therefore be `void` (or
  `async void` for the UI event path), matching the existing `MainLayout.HandleThemeChange` style. Subscribers
  that touch render state call `InvokeAsync(StateHasChanged)`, as `MainLayout` already does.
- **Noise guard**: The doc warns that a blade handler which logs on the `default` switch arm will warn on every
  non-blade event (including effect events posted by the blade itself). We adopt the doc's recommended fix:
  `if (e is not BladeEventArgs) return;` at the top of the blade's handler.

## D4 — Persistence: `Blazored.LocalStorage` (already registered)

- **Decision**: `SettingsService` wraps the **synchronous** `ISyncLocalStorageService` (from
  `AddBlazoredLocalStorage()`, already in `Program.cs`). One JSON blob under key `"Setting"` (singular — the
  doc's storage-compat note; no prior data exists here, but we keep the convention). `GetSettings()` lazily
  seeds + persists defaults on first read. `SetSettingValue<T>(name, value)` sets by reflection then persists.
- **Rationale**: Verbatim from the doc; the dependency is already present and registered.
- **Defaults**: Audio = **on** (true), GIS = streets basemap = **on** (true), Checkpoints = **visible** (true).
  Defaults chosen so a first-run user sees the full, audible, street-mapped experience.
- **Lifetime**: `ISettingsService` registered **transient** (doc default). In WASM there is one DI scope per
  session, so transient vs. scoped is immaterial; the bus is the only piece that MUST be singleton (it is).

## D5 — JS interop: outside-click listener (new) + lazy-module pattern

- **Decision**: Add a new `outside-click.js` ES module + `IOutsideClickJsInterop`/`OutsideClickJsInterop`
  following the **existing** `TransitSynthJsInterop` idiom: `Lazy<Task<IJSObjectReference>>` with a dynamic
  `import("./_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/outside-click.js?g=<guid>")`, try/catch +
  `ILogger`, `IAsyncDisposable`. The module exposes `addOutsideClickListener(elementId, dotNetRef)` (returns
  the listener handle) and `removeOutsideClickListener(handle)`; .NET stores the handle + a callback in a
  dictionary and exposes a `[JSInvokable] HandleOutsideClick(elementId)`.
- **Rationale**: This is the one genuinely new interop the blade needs (the doc's `setTheme` is not needed since
  Dark-Mode is deferred; GIS + checkpoint effects go through the existing `window.ChefMap` global). The lazy
  RCL-module pattern is the house style (`TransitSynthJsInterop`, `CheckpointTrackerJsInterop`).
- **Deviation from doc**: The doc's `CommonJsInterop` bundles theme + outside-click. We keep outside-click in
  its own focused service and route GIS/checkpoint effects through the already-global `window.ChefMap` map
  interop (see D6), rather than the `_content/` module call. No `setTheme`/`IWebAssemblyHostEnvironment`.
- **Registration**: `IOutsideClickJsInterop` as **singleton** (holds the lazy module + the listener registry),
  matching the doc and the existing interop singletons.

## D6 — Applying setting effects (Audio / GIS / Checkpoints)

- **Decision**: On toggle, the blade persists the value (D4) and posts a typed effect event (D3). The
  `TransitMap` page (which already owns the `Map` component and is bus-aware) subscribes and applies:
  - **Audio** → gate synth playback (skip `TriggerNoteAsync`/`triggerNote` when muted; the existing
    `ITransitSynthJsInterop`/`transit-synth.js` is the seam — exact gating point determined in implementation).
  - **GIS** → `window.ChefMap.setBasemapStyle(elementId, isStreets)` swaps the MapTiler style URL for a blank
    dark MapLibre style; the route/bus/checkpoint **GeoJSON sources/layers are re-added after the style load**
    so they persist (MapLibre drops style-owned layers on `setStyle`; data layers must be re-applied on the
    `style.load` event — this is the Principle VII contract, detailed in `contracts/`).
  - **Checkpoints** → `window.ChefMap.setCheckpointVisibility(elementId, visible)` toggles the checkpoint
    layer's `visibility` paint/layout property (no re-fetch).
- **Rationale**: Map effects already flow through the `window.ChefMap.*` global (see `Map.razor.Helper.cs`); we
  extend that surface rather than inventing a parallel mechanism. Audio already flows through the synth interop.
- **Open implementation detail (non-blocking)**: the precise blank-dark MapLibre style (inline minimal style
  JSON vs. a hosted blank style URL) and the exact audio-mute gating call site are left to implementation;
  both have an obvious house-pattern home and neither changes the plan's shape. Recorded, not a blocker.

## D7 — Timing & dismissal (Principle XI)

- **Decision**: Drawer CSS transition is **100ms** in (Principle XI overrides the doc's 300ms slide). Dismissal
  is **immediate** (no exit animation). Dismissal affordances: close ✕, outside-click, **and re-click of the
  gear FAB** (constitution requirement; the FAB posts a toggle — open if closed, close if open).
- **Min-open guard**: Keep the doc's ~300ms guard ONLY to stop the opening click from immediately closing the
  drawer via the outside-click listener. This guard is about the input race, not the visual transition, so it
  is independent of the 100ms transition. (Re-evaluate during implementation whether the guard can be smaller
  now that the transition is 100ms; 300ms remains safe.)
- **Element id fix**: Adopt the doc's recommended fix — cache one real id:
  `readonly string _elementId = $"blade-{Guid.NewGuid()}";` — NOT the doc's `new Guid()` (empty-GUID) quirk.

## D8 — Localization seam

- **Decision**: Add blade strings (title + the three setting labels) to the existing single resource file
  `Client.Shared/Resources/RouteFilterResources.resx`, consumed via `IStringLocalizer<RouteFilterResources>`
  (the marker class already exists). English only now; ES deferred with Language (D1).
- **Rationale**: Constitution mandates one resource file and `IStringLocalizer<RouteFilterResources>`;
  `AddLocalization()` is already wired. Adding keys here is the only compliant path.

## Summary of where we follow vs. deviate from the reference doc

| Area | Follow doc | Deviate (and why) |
|------|-----------|-------------------|
| Generic `BladeContainer` + `SettingsBlade` split | ✅ | — |
| Reflect over boolean `Settings` model | ✅ | Labels resolved via `.resx` not raw `[Description]` (Principle XII) |
| `SettingsService` over sync local storage, key `"Setting"`, lazy-seed defaults | ✅ | — |
| Event bus decoupling FAB↔blade↔consumers | ✅ (reuse existing) | Handler is `void` (existing delegate is synchronous), `if (e is not BladeEventArgs) return;` guard |
| Outside-click JS interop | ✅ (pattern) | Own focused service via the existing lazy-RCL-module idiom; no theme/env in it |
| Settings shown | — | Audio + GIS + Checkpoint (not Audio+AppTour+DarkMode); Language & Dark-Mode deferred |
| Theme / `setTheme` JS | — | Omitted (Dark-Mode deferred; `MainLayout` already has the theme seam) |
| Timing | — | 100ms in / instant out (Principle XI), gear re-click closes; min-open guard kept for the input race |
| `_elementId` | — | Cached real `Guid.NewGuid()` (apply the doc's recommended fix, not the empty-GUID quirk) |
