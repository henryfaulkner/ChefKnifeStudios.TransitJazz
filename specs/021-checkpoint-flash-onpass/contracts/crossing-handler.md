# Contract: `TransitMap.OnCrossingsAsync` split (audio + pulse)

Today (`TransitMap.razor.cs`):
```csharp
[JSInvokable]
public async Task OnCrossingsAsync(CrossingEventDto[] crossings)
{
    if (!_audioEnabled) return;                       // ← early-returns block audio AND would block pulse
    var selected = RouteFilterViewModel.SelectedRouteIds;
    foreach (var crossing in crossings)
    {
        if (selected.Count > 0 && !selected.Contains(crossing.RouteId)) continue;
        await TransitSynth.TriggerNoteAsync(...);
    }
}
```

## Required behavior after change

For each crossing in the batch:
- **Selection gate (both channels)**: if a selection is active (`selected.Count > 0`) and the crossing's `RouteId` is not selected → skip entirely (no audio, no pulse). Mirrors Principle IX.
- **Pulse channel (always, audio-independent)**: call `Map.PulseCheckpointAsync(crossing.RouteId, crossing.TriggerIndex)` — regardless of `_audioEnabled`. (Pulse is further gated inside JS by checkpoint visibility — FR-008.)
- **Audio channel (guarded)**: only when `_audioEnabled` → `TransitSynth.TriggerNoteAsync(...)` as today.

## Reference shape

```csharp
[JSInvokable]
public async Task OnCrossingsAsync(CrossingEventDto[] crossings)
{
    var selected = RouteFilterViewModel.SelectedRouteIds;
    foreach (var crossing in crossings)
    {
        if (selected.Count > 0 && !selected.Contains(crossing.RouteId)) continue;

        // Pulse: always (subject to checkpoint visibility, enforced in JS)
        if (_map is not null)
        {
            try { await _map.PulseCheckpointAsync(crossing.RouteId, crossing.TriggerIndex); }
            catch (Exception ex) { Logger.LogWarning(ex, "PulseCheckpointAsync failed for {RouteId}/{Idx}", crossing.RouteId, crossing.TriggerIndex); }
        }

        // Audio: only when enabled
        if (_audioEnabled)
        {
            try { await TransitSynth.TriggerNoteAsync(crossing.RouteId, crossing.VehicleId, crossing.TriggerIndex, crossing.TotalTriggers); }
            catch (Exception ex) { Logger.LogWarning(ex, "TriggerNoteAsync failed ..."); }
        }
    }
}
```

## Notes
- Anti-flicker (FR-006) is upstream in `checkpoint-tracker.js` (2000ms cooldown), so this handler does not need its own throttle.
- `TriggerIndex` here equals the checkpoint feature's `properties.triggerIndex` (same generator output), so it directly addresses the pulse target.
