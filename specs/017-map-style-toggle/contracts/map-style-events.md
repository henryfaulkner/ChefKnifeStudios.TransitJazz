# Contract: Map-Style Bus Event

Decouples the Settings Blade toggle from the map, via the existing `IEventNotificationService` singleton bus
(`ChefKnifeStudios.MartaJazz.Client.Core.Services`). Handlers are synchronous `void`; consumers MUST guard the
event type and ignore others.

## Event: GisSettingChangedEventArgs

```csharp
namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class GisSettingChangedEventArgs : IEventArgs
{
    public required bool IsStreetMapEnabled { get; init; }
}
```

## Producer — SettingsBlade.HandleSettingPressed (MODIFY)

Add a switch arm so the existing handler also emits this event when the new property toggles:

```csharp
IEventArgs? effectEvent = propertyName switch
{
    nameof(Settings.IsAudioEnabled)        => new AudioSettingChangedEventArgs { IsAudioEnabled = value },
    nameof(Settings.AreCheckpointsVisible) => new CheckpointVisibilityChangedEventArgs { AreCheckpointsVisible = value },
    nameof(Settings.IsStreetMapEnabled)    => new GisSettingChangedEventArgs { IsStreetMapEnabled = value },   // NEW
    _ => null
};
```

Persistence (`SettingsService.SetSettingValue`) already runs **before** the event is posted, so storage is
consistent with the visible toggle even if the swap is in flight.

## Consumer — TransitMap.HandleSettingsEventReceived (MODIFY)

Add an arm mirroring the existing checkpoint arm. Resolve the URL from config, then call the interop wrapper on
the renderer thread:

```csharp
if (e is GisSettingChangedEventArgs gis)
{
    InvokeAsync(async () =>
    {
        if (_map is null) return;                    // map not ready → no-op (edge case)
        var url = ResolveStyleUrl(gis.IsStreetMapEnabled);  // StyleUrls:LightOn / :LightOff (+ fallback)
        if (string.IsNullOrEmpty(url)) return;       // FR-013: missing entry → stay on current style
        await _map.SetBasemapStyleAsync(url);
    });
    return;
}
```

`ResolveStyleUrl(bool on)` reads `IConfiguration`:
`on ? "MapTiler:StyleUrls:LightOn" : "MapTiler:StyleUrls:LightOff"`, falling back to `"MapTiler:StyleUrl"` then
empty. `TransitMap` already injects nothing for config today — add `[Inject] IConfiguration Configuration` (or
read through the `Map` component; injecting in `TransitMap` keeps the resolver next to the handler).

## Behavioral requirements

| ID | Requirement |
|----|-------------|
| EV-1 | Toggling the Street-map checkbox posts exactly one `GisSettingChangedEventArgs` with the new value (FR-004, FR-011). |
| EV-2 | The consumer no-ops when the map is not ready (no throw, no blank map) (edge case). |
| EV-3 | When the resolved URL is empty/missing, the consumer leaves the current basemap untouched (FR-013). |
| EV-4 | Non-`GisSettingChangedEventArgs` events are ignored by the new arm (existing arms unaffected). |
| EV-5 | Rapid toggles converge: the last event's URL is the final basemap; persisted value matches the last toggle (SC-006). |
