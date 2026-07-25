# Phase 1 Data Model: Settings Blade

Frontend-only feature; "data model" here means the client-side entities, their persistence shape, and the
in-app event payloads. No database, no backend schema.

## Entity: `Settings` (persisted model)

`ChefKnifeStudios.MartaJazz.Client.Shared.Models.Settings` — a `partial class : ObservableObject`
(CommunityToolkit.Mvvm). One `bool` property per setting; the blade reflects over these.

| Property (generated) | Backing field | Type | `[property: Description]` value (resource KEY) | Default |
|----------------------|---------------|------|------------------------------------------------|---------|
| `IsAudioEnabled` | `_isAudioEnabled` | `bool` | `SettingAudioEnabled` | `true` |
| `IsStreetsBasemap` | `_isStreetsBasemap` | `bool` | `SettingStreetsBasemap` | `true` |
| `AreCheckpointsVisible` | `_areCheckpointsVisible` | `bool` | `SettingCheckpointsVisible` | `true` |

Rules:
- **Boolean-only** invariant: every public property MUST be `bool`. Pure reflection + the `as bool? ?? false`
  cast depend on this; a non-bool property would render as a misleading unchecked box. Enforced by code review
  (and a future non-bool setting — e.g. Language — requires moving off pure reflection, see plan deferral).
- The `[property: Description("…")]` attribute carries a **resource key**, not display text. The blade resolves
  it via `IStringLocalizer<RouteFilterResources>` (fallback order: localized → `[Description]` raw → property
  name).
- The order properties are declared = top-to-bottom order in the rendered list.

### Persistence shape

- **Store**: browser local storage via `ISyncLocalStorageService` (Blazored).
- **Key**: `"Setting"` (singular) — `LocalStorageConstants.SettingsKey`.
- **Value**: the whole `Settings` object serialized as one JSON blob, e.g.
  `{"IsAudioEnabled":true,"IsStreetsBasemap":true,"AreCheckpointsVisible":true}`.
- **Seed-on-first-read**: `GetSettings()` returns the stored object if present; otherwise constructs `new
  Settings()` (defaults above), persists it, and returns it. This makes the first read idempotent and ensures
  subsequent reads are consistent (FR-008).

### State transitions

A setting has exactly two states (on/off). Transition occurs only via `SettingsService.SetSettingValue`, which
(1) mutates the in-memory object by reflection, (2) re-serializes the whole object to local storage. There is
no partial-update path and no migration.

## Entity: `Theme` state (existing, unchanged)

Not part of this feature's roster (Dark-Mode deferred), but noted because the blade lives in `MainLayout`
which already owns light/dark `MatTheme` via `ThemeChangedEventArgs`. This feature does not post that event.

## Event payloads (in-app bus `IEventArgs`)

All implement the existing `ChefKnifeStudios.MartaJazz.Client.Core.Services.IEventArgs` marker. Posted on the
existing singleton `IEventNotificationService`. Full accept/reject behavior in `contracts/settings-events.md`.

### `BladeEventArgs` — open/close the blade

| Field | Type | Meaning |
|-------|------|---------|
| `Type` | `enum { Close, Settings }` | `Settings` → open the settings blade; `Close` (or any non-`Settings`) → close it |
| `Data` | `object?` (optional) | Generic payload slot; unused by the settings blade |

Posted by: `SettingsFab` (toggles — see contract), `BladeContainer.HandleClosePressed` (on outside-click).
Consumed by: `SettingsBlade.HandleEventReceived`.

### `AudioSettingChangedEventArgs`

| Field | Type | Meaning |
|-------|------|---------|
| `IsAudioEnabled` | `bool` | New audio state; `false` = muted |

Posted by: `SettingsBlade` when `IsAudioEnabled` toggles. Consumed by: `TransitMap` (gates synth playback).

### `GisSettingChangedEventArgs`

| Field | Type | Meaning |
|-------|------|---------|
| `IsStreetsBasemap` | `bool` | `true` = streets basemap; `false` = blank dark canvas |

Posted by: `SettingsBlade` when `IsStreetsBasemap` toggles. Consumed by: `TransitMap` (swaps MapTiler style
URL; re-applies data layers — Principle VII).

### `CheckpointVisibilityChangedEventArgs`

| Field | Type | Meaning |
|-------|------|---------|
| `AreCheckpointsVisible` | `bool` | `true` = checkpoint markers shown; `false` = hidden |

Posted by: `SettingsBlade` when `AreCheckpointsVisible` toggles. Consumed by: `TransitMap` (toggles checkpoint
layer visibility; no re-fetch).

## Relationships

```
SettingsFab ──BladeEventArgs(Settings, toggle)──▶ IEventNotificationService ◀──subscribe── SettingsBlade
                                                          │                                     │
                                                          │                                     ├─ ISettingsService ─▶ Settings ─▶ ISyncLocalStorageService ("Setting")
                                                          │                                     ├─ BladeContainer ─▶ IOutsideClickJsInterop ─▶ outside-click.js
                                                          │                                     └─ posts ▼ effect events
                                                          │              AudioSettingChangedEventArgs / GisSettingChangedEventArgs / CheckpointVisibilityChangedEventArgs
                                                          ▼                                                          │
                                                  TransitMap (subscribes) ──────────────────────────────────────────┘
                                                     ├─ ITransitSynthJsInterop  (mute)
                                                     └─ window.ChefMap.setBasemapStyle / setCheckpointVisibility
```

## Localization keys (added to `RouteFilterResources.resx`, English)

| Key | English value (illustrative) |
|-----|------------------------------|
| `SettingsTitle` | `Settings` |
| `SettingAudioEnabled` | `Audio` |
| `SettingStreetsBasemap` | `Street map` |
| `SettingCheckpointsVisible` | `Checkpoints` |
