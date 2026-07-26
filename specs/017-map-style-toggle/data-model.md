# Phase 1 Data Model: Map Style Toggle

The feature adds one boolean to the existing client `Settings` model and one bus event-args type. There are no
server, shared, or persisted-schema changes beyond the new bool inside the already-persisted settings blob.

## Entity: Settings (MODIFY)

`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Models/Settings.cs` — `ObservableObject`, boolean-only,
persisted as one JSON blob under local-storage key `"Setting"`. The blade renders one `MatCheckbox` per public
bool via reflection, labeling each from its `[Description]` resx key.

| Field (new) | Property | Type | Default | `[Description]` resx key |
|-------------|----------|------|---------|--------------------------|
| `_isStreetMapEnabled` | `IsStreetMapEnabled` | `bool` | `false` | `SettingStreetMap` |

Existing fields (unchanged): `IsAudioEnabled` (default `true`, `SettingAudioEnabled`), `AreCheckpointsVisible`
(default `true`, `SettingCheckpointsVisible`).

**Semantics**: `false` → basemap = **LightOff** (default app presentation). `true` → basemap = **LightOn**.

**Validation / rules**:
- Boolean only — preserves the blade's pure-reflection render (no non-bool control introduced).
- Default `false` satisfies FR-001 (LightOff default) and FR-010 (seed-and-persist on first read is handled by
  the existing `SettingsService.GetSettings`).
- Backward compatibility: a settings blob persisted before this feature lacks the field; JSON deserialization
  leaves it at the C# default (`false` = LightOff), which is the intended default — no migration needed.

**State transitions**: toggled in the blade → `SettingsService.SetSettingValue(nameof(IsStreetMapEnabled),
value)` persists synchronously → `GisSettingChangedEventArgs` posted → consumer swaps basemap.

## Entity: GisSettingChangedEventArgs (NEW)

`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/EventArgs/GisSettingChangedEventArgs.cs` — implements
`IEventArgs`, same shape as `AudioSettingChangedEventArgs`.

| Field | Type | Notes |
|-------|------|-------|
| `IsStreetMapEnabled` | `bool` (`required`, `init`) | The new value of the setting; consumer maps true→LightOn, false→LightOff |

**Lifecycle**: created in `SettingsBlade.HandleSettingPressed`, posted on `IEventNotificationService`,
consumed (and discarded) in `TransitMap.HandleSettingsEventReceived`. Not persisted.

## Configuration: MapTiler:StyleUrls (config contract, not a C# entity)

Read via `IConfiguration` in `Map.GetMapSettings`. See `contracts/style-config.md`.

| Key | Role |
|-----|------|
| `MapTiler:StyleUrls:LightOff` | Basemap when `IsStreetMapEnabled == false` (default) |
| `MapTiler:StyleUrls:LightOn` | Basemap when `IsStreetMapEnabled == true` |
| `MapTiler:StyleUrls:DarkOn`, `:DarkOff` | Present in config; **not used** by this feature |
| `MapTiler:StyleUrl` (flat) | Legacy fallback if `StyleUrls` block is absent |

## Map-layer state (runtime, JS-side — no schema, documented for VII compliance)

These are the custom MapLibre sources/layers captured-and-restored across a `setStyle` swap (never re-fetched):

| Source id | Layer id(s) | Origin | Visibility to preserve |
|-----------|-------------|--------|------------------------|
| `vehicles` | `vehicles-layer` | added on initial `map.on('load')` | per current `layout.visibility` |
| `trigger-points` | `trigger-points-layer` | `addTriggerPointMarkers` | per current visibility (checkpoint setting) |
| `route-<routeId>` (many) | `route-layer-<routeId>` | `addRouteShapeFeature` | full opacity / current paint |

Capture reads `map.getStyle().sources` (data) + `map.getStyle().layers` filtered to the ids above; restore
re-adds them in the `style.load` handler after the swap.
