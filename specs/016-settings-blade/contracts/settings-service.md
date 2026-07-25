# Contract: `ISettingsService`

Persistence of the `Settings` model to browser local storage. Wraps the **already-registered** synchronous
`ISyncLocalStorageService` (Blazored.LocalStorage). Reproduces the reference design document's
`SettingsService` verbatim, adapted to the `ChefKnifeStudios.MartaJazz` namespace.

```csharp
namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services;

public interface ISettingsService
{
    Settings GetSettings();
    void SaveSettings(Settings settings);
    T? GetSettingValue<T>(string propertyName);
    void SetSettingValue<T>(string propertyName, T value);
}
```

## Behavior

| Method | Contract |
|--------|----------|
| `GetSettings()` | Return the object stored under `LocalStorageConstants.SettingsKey` (`"Setting"`). If absent, construct `new Settings()` (defaults: all `true`), **persist it**, and return it. Never returns `null`. |
| `SaveSettings(s)` | Serialize `s` as one JSON blob under `"Setting"`. Overwrites. |
| `GetSettingValue<T>(name)` | `GetSettings()`, reflect `typeof(Settings).GetProperty(name)`; return the value if it is a `T`, else `default`. Unknown name → `default`. |
| `SetSettingValue<T>(name, value)` | `GetSettings()`, reflect the property; if found, `SetValue` then `SaveSettings`. Unknown name → no-op (no throw). |

## Invariants
- Reads are seed-idempotent: first `GetSettings()` on a fresh browser writes defaults so the second read is
  identical (satisfies FR-008 / SC-003).
- The whole object is the unit of persistence — there is no per-key storage entry.
- Storage key is the singular string `"Setting"` (design-doc storage-compat convention). Define it once in
  `LocalStorageConstants.SettingsKey`; never inline the literal.

## Registration
```csharp
builder.Services.AddTransient<ISettingsService, SettingsService>();   // Program.cs
// AddBlazoredLocalStorage() is ALREADY present — do not add twice.
```

## Reject vectors
- `GetSettingValue<int>("IsAudioEnabled")` (wrong T) → `default(int)` (0), no throw.
- `SetSettingValue("Nonexistent", true)` → silent no-op.
- Corrupt/unparseable stored JSON → treated by the underlying Blazored deserializer; on failure `GetSettings`
  falls through to seeding defaults (defensive — verify Blazored returns null rather than throwing; if it
  throws, wrap the read in try/catch and seed on failure).
