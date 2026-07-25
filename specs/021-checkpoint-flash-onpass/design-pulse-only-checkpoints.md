# Design: Pulse-Only Checkpoints (no resting dots)

**Branch**: `021-checkpoint-flash-onpass` | **Date**: 2026-06-19
**Status**: Proposed — pending implementation

## Problem

The current implementation renders two things when checkpoints are visible:

1. **Resting dots** — all 200+ checkpoint positions drawn as yellow circles on `trigger-points-layer` at all times
2. **Pulse rings** — transient expanding rings on `checkpoint-pulse-layer` when a bus passes

Resting dots are visual noise. The map communicates motion; a static field of dots adds clutter without adding meaning. Only the moment of crossing is worth showing.

## Decision

**Remove the resting dot layer entirely. Pulses become the sole checkpoint visualization.**

`AreCheckpointsVisible` is re-purposed: it now gates whether pulse animations fire at all, not whether a static dot layer is visible. The setting label in the UI changes from `"Checkpoints"` to `"Checkpoint pulses"` to match the new meaning.

No new setting is needed. The existing `AreCheckpointsVisible` + `CheckpointVisibilityChangedEventArgs` pipeline is unchanged structurally — only its JS-side effect changes.

## What Changes

### JS: `map-interop.js`

**`addTriggerPointMarkers`** — Keep building `ChefMap._triggerPointFeatures[routeId]` (coordinate lookup for pulses still needs this). Remove the `addSource` / `addLayer` block that creates `trigger-points` and `trigger-points-layer`. The source and layer are never added to the map.

**`pulseCheckpoint`** — Remove the `trigger-points-layer` visibility gate. The gate is replaced by a C#-side check (see below), so JS receives no call when pulses are disabled. Also remove the `map.getLayer('trigger-points-layer')` check that was there for the same reason.

**`setCheckpointVisibility`** — Simplify: only manage `checkpoint-pulse-layer`. When `visible === false`: call `pulse.reset(map)` and hide `checkpoint-pulse-layer`. When `visible === true`: show `checkpoint-pulse-layer`. Remove all references to `trigger-points-layer`.

**`setMapStyle` (style.load callback)** — Remove the `checkpointVisible` snapshot line that reads `trigger-points-layer` visibility. The returned object no longer needs `checkpointVisible` — but to avoid breaking the C# caller, keep returning `{ checkpointVisible: 'none' }` as a constant (or the C# side can be updated to ignore it entirely — see below).

### C#: `TransitMap.razor.cs`

**`OnCrossingsAsync`** — Add a C#-side pulse-enabled guard mirroring `_audioEnabled`:

```csharp
// before the foreach:
bool _checkpointsVisible; // field, initialized and kept in sync like _audioEnabled
```

In the foreach, wrap the `PulseCheckpointAsync` call:

```csharp
if (_checkpointsVisible)
{
    if (_map is not null)
    {
        try { await _map.PulseCheckpointAsync(...); }
        catch ...
    }
}
```

**`HandleSettingsEventReceived`** — In the `CheckpointVisibilityChangedEventArgs` branch, also update `_checkpointsVisible`:

```csharp
if (e is CheckpointVisibilityChangedEventArgs checkpoint)
{
    _checkpointsVisible = checkpoint.AreCheckpointsVisible;
    InvokeAsync(async () =>
    {
        if (_map is not null)
            await _map.SetCheckpointVisibilityAsync(checkpoint.AreCheckpointsVisible);
    });
    return;
}
```

**`OnInitializedAsync`** — Seed `_checkpointsVisible` from `SettingsService.GetSettings().AreCheckpointsVisible` alongside the existing `_audioEnabled` initialization.

**`OnAfterRenderAsync`** — The `SetCheckpointVisibilityAsync(false)` / `SetCheckpointVisibilityAsync(settings.AreCheckpointsVisible)` calls can remain as-is — they now just manage the pulse overlay layer visibility instead of the dot layer.

**`HandleSettingsEventReceived` (GIS branch)** — The post-basemap-swap call to `SetCheckpointVisibilityAsync(settings.AreCheckpointsVisible)` remains and still correctly shows/hides the pulse layer after a style swap.

### Resx: `RouteFilterResources.resx`

```xml
<data name="SettingCheckpointsVisible" xml:space="preserve">
  <value>Checkpoint pulses</value>
</data>
```

### No changes needed

- `checkpoint-pulse.js` — unchanged; `ensureLayer`, `start`, `reset` are correct as-is
- `Map.razor.Helper.cs` — `PulseCheckpointAsync`, `SetCheckpointVisibilityAsync` signatures unchanged
- `SettingsBlade.razor.cs` — unchanged; `CheckpointVisibilityChangedEventArgs` arm unchanged
- `Settings.cs` — `AreCheckpointsVisible` property unchanged; default `false` remains correct
- `BusVisibilitySettingChangedEventArgs.cs` — unchanged

## What Does NOT Change

- `_triggerPointFeatures` is still populated — pulse coordinate lookup still works
- `_routeColorsByRouteId` is still used in `pulseCheckpoint` for ring color
- The `AreCheckpointsVisible` default (`false`) is already correct: no pulses on first load
- The `CheckpointVisibilityChangedEventArgs` event bus pipeline is structurally unchanged
- Basemap-swap resilience: `ensureLayer` + `reset` in `style.load` still re-adds the empty pulse overlay

## Dev Access to Checkpoint Positions

The coordinate data (`_triggerPointFeatures`) is still built at load time and lives in the JS object — it's inspectable from DevTools at any time via `ChefMap._triggerPointFeatures`. If a full dot overlay is ever needed again, it's a 10-line re-add to `addTriggerPointMarkers`. No design work required.

## Files Touched

| File | Change |
|------|--------|
| `wwwroot/js/map-interop.js` | Remove `trigger-points` source/layer; simplify `setCheckpointVisibility`; remove `trigger-points-layer` gate in `pulseCheckpoint`; remove `checkpointVisible` snapshot in `setMapStyle` |
| `Pages/TransitMap.razor.cs` | Add `_checkpointsVisible` field; gate `PulseCheckpointAsync` on it; seed + sync from settings event |
| `Resources/RouteFilterResources.resx` | Rename `SettingCheckpointsVisible` value to `"Checkpoint pulses"` |
