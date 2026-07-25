# Contract: C# Settings + Interop

## `Settings.cs` (Client.Shared/Models)

Add the enum and a persisted, hidden property; bump the version.

```csharp
public enum BackfillTexture { Noise, Percussion }   // no "off" — there is always a backfill

public partial class Settings : ObservableObject
{
    public const int CurrentVersion = 5;            // was 4 — schema change (added property)

    // ...existing bool [ObservableProperty] members unchanged...

    [ObservableProperty]
    [HiddenSetting]                                 // keep the enum out of the bool-only SettingsBlade
    BackfillTexture _backfillTexture = BackfillTexture.Noise;
}
```

**Requirements**:
- **SETT-1**: `CurrentVersion` MUST become `5`. (The `GetSettings` version guard then
  discards old blobs cleanly — no partial deserialization.)
- **SETT-2**: the property MUST default to `BackfillTexture.Noise` (FR-005).
- **SETT-3**: the property MUST carry `[HiddenSetting]` so `SettingsBlade` (bool-only,
  reflection-driven) does not attempt to render it. The blade's boolean-only invariant
  is preserved.
- **SETT-4**: **no change** to `SettingsService` — `SetSettingValue<T>` /
  `GetSettingValue<T>` are generic + reflection-based and already persist an enum.

## `ITransitSynthJsInterop.cs` / `TransitSynthJsInterop.cs`

Add one method mirroring `SetAudioEnabledAsync`.

```csharp
// interface
/// <summary>
/// Selects which continuous background "backfill" texture plays under the note triggers
/// ('noise' | 'percussion'). Mirrors the persisted BackfillTexture into the JS engine.
/// Safe to call before the master bus exists — the mode is recorded and honored on build.
/// </summary>
Task SetBackfillTextureAsync(string mode);

// implementation (copy of SetAudioEnabledAsync's shape)
public async Task SetBackfillTextureAsync(string mode)
{
    try
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("setBackfillTexture", mode);
    }
    catch (Exception ex) { LogError(ex, nameof(SetBackfillTextureAsync)); }
}
```

**Requirements**:
- **INT-1**: `mode` is the lowercased enum name (`"noise"` / `"percussion"`), produced
  caller-side via `mode.ToString().ToLowerInvariant()`.
- **INT-2**: MUST invoke the JS export `setBackfillTexture` on the **same** shared
  module instance the rest of the interop uses (the existing `_moduleTask`, whose URL
  has NO cache-buster so the crossing dispatcher shares one module — do not change that).
- **INT-3**: MUST swallow + log JS errors like the sibling methods (never throw into
  Blazor).
