# Contract: Bus-Visibility Setting (toggle → effect)

Rides entirely on the existing settings machinery. The only new C# type is one `IEventArgs`.

## 1. Settings model
`Settings.cs` gains:
```csharp
[ObservableProperty]
[property: Description("SettingBusesVisible")]
private bool _isBusesVisible = false;   // default OFF (buses hidden) — FR-009a
```
`SettingsBlade` renders it automatically (pure reflection over public bools). No `.razor` change.

## 2. Resx
`RouteFilterResources.resx` gains:
```xml
<data name="SettingBusesVisible" xml:space="preserve">
  <value>Buses</value>
</data>
```
(EN only; `.es` deferred — consistent with 015/016/017.)

## 3. Event-args
`BusVisibilitySettingChangedEventArgs : IEventArgs`:
```csharp
public class BusVisibilitySettingChangedEventArgs : IEventArgs
{
    public required bool IsBusesVisible { get; init; }
}
```

## 4. Producer — `SettingsBlade.HandleSettingPressed`
Add a switch arm:
```csharp
nameof(Settings.IsBusesVisible) => new BusVisibilitySettingChangedEventArgs { IsBusesVisible = value },
```

## 5. Consumer — `TransitMap.HandleSettingsEventReceived`
Add a branch (synchronous void handler; marshal with `InvokeAsync`):
```csharp
if (e is BusVisibilitySettingChangedEventArgs buses)
{
    InvokeAsync(async () =>
    {
        if (_map is not null)
            await _map.SetVehiclesVisibleAsync(buses.IsBusesVisible);
    });
    return;
}
```

## 6. Initial render + post-basemap-swap honoring
Replace the two existing hardcoded `await _map.SetVehiclesVisibleAsync(true);` calls
(in `OnAfterRenderAsync` and in the `GisSettingChangedEventArgs` handler) with:
```csharp
var settings = SettingsService.GetSettings();
await _map.SetVehiclesVisibleAsync(settings.IsBusesVisible);
```
→ first paint and post-style-swap both honor the persisted setting (FR-009c, FR-011, SC-004a).

## Acceptance vectors

| # | Action | Expected |
|---|--------|----------|
| 1 | Fresh load, no saved preference | Toggle shows OFF; zero bus markers; routes + checkpoints visible; pulses still fire (SC-004). |
| 2 | Toggle ON | Bus markers appear immediately, no reload (FR-009b). |
| 3 | Toggle OFF | Bus markers hidden immediately, no reload. |
| 4 | Set ON, reload app | Buses visible from first render (FR-009c, SC-004a). |
| 5 | Toggle the GIS/street-map setting | Bus visibility unchanged from current setting after the basemap swap (FR-011). |
| 6 | Buses OFF, bus passes checkpoint | Checkpoint pulses (FR-010). |
