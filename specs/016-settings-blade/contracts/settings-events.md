# Contract: Settings Bus Events

The UI contract between the FAB, the blade, and the effect consumers. All events ride the **existing**
`IEventNotificationService` (singleton) and implement the **existing** `IEventArgs` marker
(`ChefKnifeStudios.MartaJazz.Client.Core.Services`). Handler delegate is **synchronous**:
`void EventReceivedEventHandler(object sender, IEventArgs e)`.

## `BladeEventArgs`

```csharp
namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class BladeEventArgs : IEventArgs
{
    public enum Types { Close, Settings }
    public required Types Type { get; init; }
    public object? Data { get; init; }
}
```

### Producer: `SettingsFab`
- On gear click, posts a **toggle**:
  - If the blade is currently closed → post `BladeEventArgs { Type = Settings }`.
  - If currently open → post `BladeEventArgs { Type = Close }`.
- The FAB does not hold the blade reference; it tracks open/closed by also listening to the bus (it sees its
  own `Settings`/`Close` posts and the `Close` posted by outside-click). Alternative acceptable
  implementation: the FAB always posts `Settings` and the blade treats a `Settings` event as "toggle" when
  already open. **Either** satisfies the "re-click closes" requirement (Principle XII); pick one in
  implementation and keep it single-source.

### Consumer: `SettingsBlade.HandleEventReceived(object, IEventArgs)`
```
if (e is not BladeEventArgs blade) return;        // guard — ignore effect events & theme events (no warning)
switch (blade.Type)
{
    case Types.Settings: _bladeContainer?.Open();  // (or toggle if already open, per chosen FAB scheme)
    default:             _bladeContainer?.Close();  // Close and any non-Settings
}
```

| Input event | Expected blade action |
|-------------|-----------------------|
| `BladeEventArgs { Type = Settings }` (blade closed) | Open |
| `BladeEventArgs { Type = Settings }` (blade open, toggle scheme) | Close |
| `BladeEventArgs { Type = Close }` | Close |
| any non-`BladeEventArgs` (e.g. `AudioSettingChangedEventArgs`, `ThemeChangedEventArgs`) | **Ignore** (no-op, no log) |

## Effect events

```csharp
public class AudioSettingChangedEventArgs : IEventArgs { public required bool IsAudioEnabled { get; init; } }
public class GisSettingChangedEventArgs : IEventArgs { public required bool IsStreetsBasemap { get; init; } }
public class CheckpointVisibilityChangedEventArgs : IEventArgs { public required bool AreCheckpointsVisible { get; init; } }
```

### Producer: `SettingsBlade.HandleSettingPressed(string propertyName, bool value)`
1. `SettingsService.SetSettingValue(propertyName, value)` (persist).
2. Post the matching effect event:

| `propertyName` (`nameof`) | Effect event posted |
|---------------------------|---------------------|
| `Settings.IsAudioEnabled` | `AudioSettingChangedEventArgs { IsAudioEnabled = value }` |
| `Settings.IsStreetsBasemap` | `GisSettingChangedEventArgs { IsStreetsBasemap = value }` |
| `Settings.AreCheckpointsVisible` | `CheckpointVisibilityChangedEventArgs { AreCheckpointsVisible = value }` |

### Consumer: `TransitMap` (subscribes in `OnInitialized`, unsubscribes in `Dispose`)
| Event | Action | Constitution tie |
|-------|--------|------------------|
| `AudioSettingChangedEventArgs` | Mute/unmute synth playback | XII (Audio control) |
| `GisSettingChangedEventArgs` | `ChefMap.setBasemapStyle(elementId, IsStreetsBasemap)`; re-apply data layers after style load | VII (layers persist) |
| `CheckpointVisibilityChangedEventArgs` | `ChefMap.setCheckpointVisibility(elementId, AreCheckpointsVisible)` | VII (no re-fetch) |
| State touched by handler | wrap in `InvokeAsync(StateHasChanged)` if rendering changes | — |

### Reject / no-op vectors
- A handler MUST NOT throw on an event type it does not recognize — it returns immediately.
- Effect handlers MUST be idempotent: posting `IsAudioEnabled=false` twice leaves audio muted (no toggle-on
  bug). Implementations carry the **absolute** new state, never a "flip" instruction.
