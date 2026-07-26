# Contract: Trail interop surface (`map-interop.js` + `Map.razor.Helper.cs`)

## 1. `ChefMap.startCrossingTrail` (JS, in `map-interop.js`)

```js
startCrossingTrail: async function (containerDivId, routeId, vehicleId, triggerIndex, durationSec) {
    let map = ChefMap.maps[containerDivId];
    if (!map) return;

    // Anchor: reuse the trigger feature (same lookup as pulseCheckpoint)
    let features = ChefMap._triggerPointFeatures[routeId];
    if (!features) return;                       // route geometry not loaded yet
    let feature = features.find(f => f.properties.triggerIndex === triggerIndex);
    if (!feature) return;

    let anchorCoord     = feature.geometry.coordinates;
    let anchorDistanceM = feature.properties.alongDistanceM;
    let color           = ChefMap._routeColorsByRouteId[routeId] || '#facc15';   // FR-005

    // Speed: read empirical speed from the animator (audio-independent — R5)
    let vstate   = ChefMapAnimator.vehicles[vehicleId];
    let speedMps = (vstate && (vstate.empiricalSpeed ?? vstate.speed)) || 0;

    let trail = await _getCheckpointTrail();
    trail.ensureLayer(map);
    trail.start(map, routeId, vehicleId, triggerIndex, anchorCoord, anchorDistanceM, color, speedMps, durationSec);
}
```

- Lazy import via a new `_getCheckpointTrail()` mirroring `_getCheckpointPulse()` (imports `/_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/checkpoint-trail.js`).
- `ensureLayer` is also called once on map load next to the pulse `ensureLayer` (so the layer exists before the first crossing).

## 2. `ChefMap.setCrossingTrailVisibility` (JS, in `map-interop.js`)

```js
setCrossingTrailVisibility: async function (containerDivId, visible) {
    let map = ChefMap.maps[containerDivId];
    if (!map) return;
    try {
        let trail = await _getCheckpointTrail();
        trail.setVisible(map, visible);          // false → reset() clears active trails (FR-006)
    } catch (e) {
        console.warn('[ChefMap] setCrossingTrailVisibility: trail layer error — ' + e);
    }
}
```

## 3. `setMapStyle` restore additions (Principle VII)

In **both** restore paths inside `setMapStyle` (the `style.load` handler and the timed fallback), after the existing vehicles/trigger-points/routes restoration, re-create the trail layer:

```js
try { _getCheckpointTrail().then(t => t.ensureLayer(map)); }
catch (e) { console.warn('[ChefMap] setMapStyle: could not restore crossing-trail layer: ' + e); }
```

Active trails are sub-second and need not be preserved across a swap — only the empty source/layer is re-added so the next crossing renders.

## 4. `Map.razor.Helper.cs` wrappers (C#)

```csharp
public async Task StartCrossingTrailAsync(string routeId, string vehicleId, int triggerIndex, double durationSeconds)
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.startCrossingTrail", ElementId, routeId, vehicleId, triggerIndex, durationSeconds); }
    catch (Exception ex) { Console.WriteLine($"[Map] StartCrossingTrail failed for routeId={routeId} triggerIndex={triggerIndex}: {ex}"); }
}

public async Task SetCrossingTrailVisibilityAsync(bool visible)
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.setCrossingTrailVisibility", ElementId, visible); }
    catch (Exception ex) { Console.WriteLine($"[Map] SetCrossingTrailVisibility failed: {ex}"); }
}
```

Style and signature mirror the existing `PulseCheckpointAsync` / `SetCheckpointVisibilityAsync` wrappers (fire-and-forget, try/catch, `ElementId` first).

## 5. `TransitMap.razor.cs` — `OnCrossingsAsync` integration

Inside the existing per-crossing loop, in the **same** `if (_checkpointsVisible && _map is not null)` block that already calls `PulseCheckpointAsync`:

```csharp
if (_checkpointsVisible && _map is not null)
{
    try { await _map.PulseCheckpointAsync(crossing.RouteId, crossing.TriggerIndex); }
    catch (Exception ex) { Logger.LogWarning(ex, "PulseCheckpointAsync failed for {RouteId}/{Idx}", crossing.RouteId, crossing.TriggerIndex); }

    // NEW — trail fires with the pulse, independent of audio state (FR-001)
    var durationSec = TransitSynth.DurationSecondsFor(crossing.VehicleId);   // see duration-helper.md
    try { await _map.StartCrossingTrailAsync(crossing.RouteId, crossing.VehicleId, crossing.TriggerIndex, durationSec); }
    catch (Exception ex) { Logger.LogWarning(ex, "StartCrossingTrailAsync failed for {RouteId}/{Idx}", crossing.RouteId, crossing.TriggerIndex); }
}
```

> The audio block (`if (_audioEnabled) … TriggerNoteAsync`) is unchanged and remains separate, so muting never suppresses the trail.

### Visibility toggle wiring

Wherever `SetCheckpointVisibilityAsync` is currently driven from the checkpoint-visibility setting handler, also call `SetCrossingTrailVisibilityAsync(visible)` so one toggle clears/suppresses both pulse and trail (FR-006). (Both are no-ops if the map isn't ready.)

## Contract test vectors (manual, see quickstart)

| Vector | Expected |
|---|---|
| Crossing, checkpoints visible, audio ON | Trail grows + note plays + pulse |
| Crossing, checkpoints visible, audio MUTED/locked | Trail grows + pulse; **no** note (FR-001) |
| Crossing, checkpoints HIDDEN | No trail, no pulse |
| Toggle checkpoints OFF mid-trail | Active trail cleared immediately (FR-006) |
| Two routes cross same instant | Two trails, correct distinct colors, no interference (SC-006) |
| Basemap GIS toggle after a crossing | Next crossing still renders a trail (Principle VII) |
