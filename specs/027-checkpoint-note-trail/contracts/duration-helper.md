# Contract: `durationSecondsFor` (audio-independent note duration)

Two coordinated surfaces so the trail's growth duration always equals the audible note's duration, **without** depending on audio being unlocked (FR-001, FR-002).

## 1. JS: `transit-synth.js`

```js
// Default Tone Transport tempo is 120 BPM (unchanged in this app):
//   4n = 0.5s, 8n. = 0.375s, 8n = 0.25s
const DURATION_SECONDS = { '8n': 0.25, '8n.': 0.375, '4n': 0.5 };

// Deterministic, audio-independent. Same selection used by triggerNote.
export function durationSecondsFor(vehicleId, routeId) {
    // durations live on the per-route palette slot; all current slots share
    // ['8n','8n.','4n'], so selection is stable across routes. Resolve the
    // slot's durations array if available, else fall back to the shared set.
    const durations = ['8n', '8n.', '4n'];
    const tok = durations[djb2(String(vehicleId)) % durations.length];
    return DURATION_SECONDS[tok] ?? 0.25;
}

window.TransitSynth = { unlock, isUnlocked, preload, triggerNote, dispose, durationSecondsFor };
```

- **No `_unlocked` guard, no AudioContext, no Tone import** — callable while muted/locked.
- `triggerNote` is refactored to select its `duration` token via the **same** `djb2(vehicleId) % durations.length` expression (it already does this), guaranteeing the audible note and the trail agree (single source of selection logic).
- The seconds mapping is a fixed lookup because the palette durations (`8n`, `8n.`, `4n`) and the Transport tempo (120 BPM default) are constants in this app. If a future feature changes tempo or palette durations, update `DURATION_SECONDS` to match.

## 2. C#: `TransitSynthJsInterop` (or equivalent existing wrapper)

Expose a thin wrapper so `TransitMap.OnCrossingsAsync` can fetch the duration:

```csharp
public async Task<double> DurationSecondsForAsync(string vehicleId)
{
    try { return await _module.InvokeAsync<double>("durationSecondsFor", vehicleId); }
    catch (Exception ex) { Logger.LogWarning(ex, "DurationSecondsFor failed for {VehicleId}", vehicleId); return 0.25; }
}
```

- Returns a safe default (`0.25`) on any interop error so the trail still renders a short mark.
- If `transit-synth.js` is consumed via the existing lazy-module interop wrapper (`TransitSynthJsInterop`), add this method there; the contract is the return value, not the wrapper's exact name.

> The interop snippet in `trail-interop.md` shows `TransitSynth.DurationSecondsFor(...)` as shorthand; in practice it is the async interop call above, awaited before `StartCrossingTrailAsync`.

## Acceptance mapping

| Requirement | Enforced by |
|---|---|
| FR-001 trail when muted/locked | helper has no audio/unlock dependency |
| FR-002 grows over the *note's* duration | trail `durationSec` == the note's selected duration |
| AC#4 disappears when note ends | trail lifetime = `durationSec`; note `triggerAttackRelease` uses the same token |
| Determinism (Principle VIII) | identical `djb2(vehicleId) % n` selection for audio and visual |
