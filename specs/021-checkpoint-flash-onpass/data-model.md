# Data Model: Checkpoint Flash on Bus Pass & Bus-Visibility Toggle

This feature is frontend-only and introduces **no persisted server/shared data** and no new wire (SignalR) events. The "data" is (a) one new client setting, (b) one new client event-args type, and (c) transient in-browser pulse state.

## 1. Settings (extended)

`ChefKnifeStudios.MartaJazz.Client.Shared/Models/Settings.cs` — `ObservableObject`, persisted by `ISettingsService` as one JSON blob in local storage (key `"Setting"`).

| Field | Type | Default | `[Description]` resx key | Notes |
|-------|------|---------|--------------------------|-------|
| IsAudioEnabled | bool | true | SettingAudioEnabled | existing |
| AreCheckpointsVisible | bool | false | SettingCheckpointsVisible | existing |
| IsStreetMapEnabled | bool | true | SettingStreetMap | existing |
| **IsBusesVisible** | **bool** | **false** | **SettingBusesVisible** | **NEW** — buses hidden by default (FR-009a) |

**Validation / rules**:
- Boolean only — preserves the `SettingsBlade` pure-reflection render contract (it renders one `MatCheckbox` per public bool).
- Default `false` ⇒ first load with no saved preference hides buses (FR-009a, SC-004).
- Persisted and seeded by existing `ISettingsService` lazy-default logic; survives reload (FR-009c, SC-004a).

## 2. BusVisibilitySettingChangedEventArgs (new client event)

`ChefKnifeStudios.MartaJazz.Client.Shared/EventArgs/BusVisibilitySettingChangedEventArgs.cs` — implements `IEventArgs`; carried on the existing in-process `IEventNotificationService` bus. Shape mirrors `GisSettingChangedEventArgs`.

| Field | Type | Notes |
|-------|------|-------|
| IsBusesVisible | bool (required init) | the new desired visibility |

**Producer**: `SettingsBlade.HandleSettingPressed` (new switch arm for `nameof(Settings.IsBusesVisible)`).
**Consumer**: `TransitMap.HandleSettingsEventReceived` → `Map.SetVehiclesVisibleAsync(args.IsBusesVisible)`.

## 3. CrossingEventDto (existing — reused, unchanged)

`TransitMap.CrossingEventDto(string VehicleId, string RouteId, int TriggerIndex, int TotalTriggers)`.
Already delivered to `OnCrossingsAsync`. The pulse path reads `RouteId` (for color + selection scoping) and `TriggerIndex` (to locate the checkpoint feature). No change to the DTO.

## 4. Checkpoint feature (existing — read-only reference)

Each resting checkpoint is a GeoJSON Point in the shared `trigger-points` source:
```
properties: { routeId, triggerIndex, alongDistanceM }
geometry:   { type: 'Point', coordinates: [lon, lat] }
```
Held in `ChefMap._triggerPointFeatures[routeId]` (array). The pulse module looks up the coordinate by `(routeId, triggerIndex)`. Route color: `ChefMap._routeColorsByRouteId[routeId]` (fallback `#facc15`).

## 5. Active pulse (transient, in-memory JS only)

Lives in `checkpoint-pulse.js`; never persisted. One entry per in-flight pulse.

| Field | Type | Notes |
|-------|------|-------|
| key | string | `"{routeId}::{triggerIndex}"` — dedupe/refresh target for re-pass while animating |
| coordinates | [lon, lat] | from the checkpoint feature |
| color | string (hex) | route color (or default) |
| startTimeMs | number | `performance.now()` at pulse start; re-pass refreshes this |

**Lifecycle / state transitions**:
1. **Start**: crossing arrives → upsert entry (new or refresh `startTimeMs`).
2. **Animate**: RAF loop computes `t = (now - startTimeMs) / DURATION_MS`; for `t < 1`, radius = lerp(rStart, rEnd, easeOut(t)), opacity = lerp(oStart, 0, t).
3. **End**: `t >= 1` → remove entry. When no entries remain, the loop idles (no RAF scheduled).
4. **Reset**: on basemap `style.load` or checkpoints-hidden → clear all entries; pulse source set to empty FeatureCollection.

**Tuning constants** (in module, not data): `DURATION_MS ≈ 600`, `R_START ≈ 4`, `R_END ≈ 24`, `O_START ≈ 0.6`, easing = ease-out cubic. Subject to visual tuning during implementation.
