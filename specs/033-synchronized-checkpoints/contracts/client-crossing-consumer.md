# Contract: Client Crossing Consumer (+ deletions)

The client stops detecting crossings and starts consuming them from SignalR, feeding the **unchanged**
`OnCrossingsAsync` effect path.

## Add: one branch in `HandleVehicleBatchAsync` (`TransitMap.razor.cs`)

The component already subscribes to `NotificationService.NotificationReceived` and handles each
`EventEnvelope` batch. Add a branch alongside the existing `RouteNearestPointBatchEvent` handling:

```csharp
// after / alongside the RouteNearestPointBatchEvent pass over `batch`
var crossings = batch
    .Select(e => e.Payload)
    .OfType<RouteCrossingBatchEvent>()
    .SelectMany(e => e.BatchRecords)
    .Select(r => new CrossingEventDto(r.VehicleId, r.RouteId, r.TriggerIndex, r.TotalTriggers))
    .ToArray();

if (crossings.Length > 0)
    await OnCrossingsAsync(crossings);
```

- `OnCrossingsAsync(CrossingEventDto[])` (`TransitMap.razor.cs:155`) is **unchanged** — same pulse /
  crossing-trail / note fan-out, same gating on `_checkpointsVisible`, `_crossingTrailVisible`,
  `_audioEnabled`, and `effectiveIds` (route filter). It is invoked directly from C# now (it remains
  `[JSInvokable]` only incidentally; nothing else calls it from JS once the tracker is deleted — the
  attribute may be removed).
- `CrossingEventDto` stays defined where it is (`TransitMap.razor.cs:562`); only its source changes.

## Keep: checkpoint marker generation

`ConfigureAllTrackersAsync` / `ConfigureTrackerForRouteAsync` still build `cumDist` and call
`TriggerPointGenerator.Generate` to render **markers** via `AddTriggerPointMarkersAsync` +
`FlushTriggerPointsAsync`. Remove only the detection wiring:

- Delete the `CheckpointTracker.ConfigureRouteAsync(routeId, triggerPoints.ToArray(), _dotNetRef)` call.
- Update `using` of `TriggerPointGenerator` / `TriggerPoint` to the `Shared` namespace.

## Delete (detection retired)

| File | Reason |
|---|---|
| `Client.Shared/wwwroot/js/checkpoint-tracker.js` | local crossing detection — superseded by server |
| `Client.Shared/Services/JsInterop/CheckpointTrackerJsInterop.cs` | detection interop wrapper |
| `Client.Shared/Services/JsInterop/ICheckpointTrackerJsInterop.cs` | its interface |
| `Client.Shared/Services/TriggerPointGenerator.cs` | **moved** to `Shared` |
| `Client.Shared/Services/ITriggerPointGenerator.cs` | **moved** to `Shared` |
| `Client.Shared/Models/TriggerPoint.cs` | **moved** to `Shared` |

## Edit: `TransitMap.razor.cs` member removals

- Remove `[Inject] ICheckpointTrackerJsInterop CheckpointTracker`.
- Remove `await CheckpointTracker.ClearAsync();` from `DisposeAsync`.
- `_dotNetRef` is still used by other interop? If not, it may be removed too (verify at implementation
  time — it was created for the tracker callback). Keep if any remaining JSInvokable callback needs it.

## Edit: `Client.WebApp/Program.cs`

- Remove the `ICheckpointTrackerJsInterop` → `CheckpointTrackerJsInterop` DI registration.
- Keep the `ITriggerPointGenerator` → `TriggerPointGenerator` registration (now the `Shared` type;
  still needed for markers).

## Invariants (MUST)

- **Exactly-once (FR-014):** after deletion, the server SignalR branch is the *only* path into
  `OnCrossingsAsync`. No tick-hook, no JS detection remains.
- **Gating preserved (FR-012/FR-013):** `OnCrossingsAsync` body and all four gates are untouched.
- **No geometry re-fetch (Principle VII):** markers come from the existing `_routeShapeCache`.

## Accept / reject vectors

| Scenario | Expectation |
|---|---|
| Batch with a `RouteCrossingBatchEvent` of 3 records, audio on, no filter | 3 notes + 3 pulses (if visible) + 3 trails (if visible) |
| Same batch, audio muted | 0 notes; pulses/trails per their own toggles |
| Same batch, a route filter excluding 2 of the 3 routes | only the 1 in-filter crossing produces effects |
| Batch with only position events (no crossing event) | no crossings fired (no error) |
| Reconnect replay (position-only snapshot) | no crossings fired on join |
