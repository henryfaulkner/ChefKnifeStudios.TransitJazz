# Phase 1 Data Model: Selectable Backfill Texture

The feature's "data" is deliberately tiny: one persisted enum value plus a runtime
audio-layer state machine. No server, worker, shared, GTFS, or SignalR data changes.

---

## Entity: `BackfillTexture` (C# enum)

The listener's choice of background texture. Mutually exclusive; always populated.

```csharp
public enum BackfillTexture
{
    Noise,       // default — today's continuous pink-noise bed
    Percussion   // new — sparse lo-fi kit on a slow Tone.Transport loop
}
```

- **Members**: `Noise` (default, ordinal 0), `Percussion` (ordinal 1).
- **No "off" / "none" member** — silence is the audio mute's job (FR-007).
- **Extensibility**: a future texture (e.g. `VinylCrackle`) is one added member + one
  menu item + one JS branch in `_applyBackfillLayer` (FR-015). Not shipped now.
- **Wire form to JS**: lowercased member name (`"noise"` / `"percussion"`) via
  `mode.ToString().ToLowerInvariant()`, matching the JS `_backfillMode` string domain.

---

## Entity: `Settings.BackfillTexture` (persisted property)

Added to the existing `Settings` (`Client.Shared/Models/Settings.cs`), the single
JSON blob persisted by `SettingsService` in local storage.

| Field | Value | Notes |
|---|---|---|
| Property | `BackfillTexture BackfillTexture` | `[ObservableProperty]` backing field `_backfillTexture` |
| Default | `BackfillTexture.Noise` | reproduces today's sound until changed (FR-005) |
| Attribute | `[HiddenSetting]` | keeps it out of the reflection-driven bool-only `SettingsBlade` (R2) |
| Persistence | existing `SettingsService` blob | no service change — `SetSettingValue<T>` is generic (R1) |

**Schema version**: `Settings.CurrentVersion` **4 → 5**. Adding a property is a schema
change; the version guard in `SettingsService.GetSettings` discards a stored blob whose
`Version != CurrentVersion`, so old blobs fall back cleanly to defaults (FR-006 edge:
old-version fallback; spec US2 scenario 3).

**Validation / lifecycle**:
- Reading: `SettingsService.GetSettings().BackfillTexture` always returns a valid
  member (defaults seed `Noise`).
- Writing: `SetSettingValue(nameof(Settings.BackfillTexture), mode)` persists atomically
  (whole blob re-serialized).
- Only the **enum value** is persisted — never live Tone.js nodes (FR-013).

---

## Runtime state: JS backfill layer (`transit-synth.js`)

Not persisted — reconstructed each load. Two module-level variables + one derived
running state.

```js
let _backfillMode = 'noise';   // 'noise' | 'percussion' — LIVE selection; never 'off'
let _percussion = null;        // lazily-built { loop, kick, rim, volume } | null
```

### Derived running state (the invariant `_applyBackfillLayer` enforces)

The single choke point reconciles two inputs into exactly one running layer:

| `_audioEnabled` | `_backfillMode` | Noise node | Percussion loop |
|---|---|---|---|
| `false` (muted) | any | **stopped** | **stopped** |
| `true` | `'noise'` | **started** | stopped |
| `true` | `'percussion'` | stopped | **started** (built lazily on first need) |

**Invariants** (map to FR-001/008/010, SC-004/005):
- While unmuted: **exactly one** of {noise, percussion} runs — never zero, never both.
- While muted: **both** stopped (total silence).
- Switching modes stops the outgoing layer before/as it starts the incoming one — the
  two never overlap and never both stop while unmuted.
- Safe before `_masterBus` exists: `_backfillMode` is recorded and honored when the
  bus later builds (`getMasterBus` calls `_applyBackfillLayer` after wiring noise).

---

## Entity: Percussion voice parameters (`PERCUSSION_*` constants)

Pinned JS constants grouped with the existing `NOISE_*` constants
(`transit-synth.js:~204`). **Values are the §9 audition output** — the ones below are
the design-doc starting point, not final.

| Constant | Starting value | Meaning |
|---|---|---|
| `PERCUSSION_VOLUME_DB` | ~ −? dB (TBD by audition) | overall backfill-kit level into `bus.input` |
| loop interval | `'2n'` | how often the kick fires (slow/sparse) |
| kick tuning / decay | `C1`, short (TBD) | `MembraneSynth` pitch + envelope |
| rim volume | TBD | `MetalSynth` level |
| rim probability | `~0.4` | chance a rim/brush accompanies a kick |

These are a **voice recipe**, not authored per-route content — they do not participate
in the deterministic transit→note mapping (Principle VIII; the bed is out of that
mapping's scope, like today's noise bed).

---

## Audition-tool session state (`tools/instrument-compat/` localStorage)

Additive to the tool's existing session object (alongside `instruments` /
`activityLevel` / `muted`) — tool-local, never shipped to the app:

```js
{
  // ...existing keys...
  backfill: 'noise' | 'percussion',
  percussionParams: { loopInterval, kickPitch, kickDecay, kickVolume,
                      rimVolume, rimProbability, volumeDb }
}
```

Lets a dialed-in kit survive reload while tuning. The tuned `percussionParams` are the
values transcribed by hand into the app's `PERCUSSION_*` constants (no auto-export).
