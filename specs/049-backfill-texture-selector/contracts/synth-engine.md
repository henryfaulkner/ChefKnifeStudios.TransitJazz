# Contract: `transit-synth.js` Backfill Layer

Generalizes the single, fixed pink-noise bed into a swappable backfill layer with two
states, without changing the note-trigger path or the noise bed's default behavior.

## New module state

```js
let _backfillMode = 'noise';   // 'noise' | 'percussion'; NEVER 'off'. Default reproduces today.
let _percussion = null;        // lazy { loop, kick, rim, volume } | null
```

## New export: `setBackfillTexture(mode)`

Shape mirrors `setAudioEnabled` (the file's established live-toggle pattern).

```js
export function setBackfillTexture(mode) {
    _backfillMode = (mode === 'percussion') ? 'percussion' : 'noise'; // normalize; no "off"
    if (!_masterBus) return;            // flag recorded; honored when the bus builds
    _resumeContextIfNeeded();           // same defensive resume setAudioEnabled uses
    _applyBackfillLayer();              // reconcile _audioEnabled × _backfillMode
}
```

**Behavior**:
1. Normalize + record `_backfillMode` (anything ≠ `'percussion'` → `'noise'`).
2. If no master bus yet: return (recorded, honored on build). Matches
   `setAudioEnabled`'s early-return contract.
3. Otherwise resume the AudioContext if `suspended`/`interrupted` (switching *to* a
   texture after a long idle/mute must be able to sound).
4. Call `_applyBackfillLayer()`.

## New internal: `_applyBackfillLayer()` — the single choke point

Reconciles `_audioEnabled × _backfillMode` → exactly which of {noise, percussion} runs.
**Both** `setAudioEnabled` and `setBackfillTexture` call it so the two gates never drift.

| `_audioEnabled` | `_backfillMode` | Action |
|---|---|---|
| `false` | any | stop noise (if started), stop percussion loop (if started) |
| `true` | `'noise'` | ensure noise started; stop percussion loop; build percussion NOT required |
| `true` | `'percussion'` | build `_percussion` lazily if null; start its loop; stop noise |

Post-condition (unmuted): **exactly one** layer running. Post-condition (muted): **zero**.

## `buildPercussion(T)` (lazy, once)

Feeds `getMasterBus(T).input` (the compressor) — NOT `Destination` — so it inherits the
master glue + 4000 Hz softening and sits under the mix like the noise bed.

```js
function buildPercussion(T) {
    const bus = getMasterBus(T);
    const volume = new T.Volume(PERCUSSION_VOLUME_DB);
    volume.connect(bus.input);

    // REAL RECORDED HITS via Tone.Sampler (same FluidR3 GM CDN + buildSampleUrls URL shape
    // as the melodic PALETTE voices) — synthesized MembraneSynth/MetalSynth voicing was
    // rejected in the audition as drum-machine-like, not drums.
    const kick = new T.Sampler({ urls: buildSampleUrls(PERCUSSION_KICK_INSTRUMENT, …), /* PERCUSSION_KICK_* */ });
    const rim  = new T.Sampler({ urls: buildSampleUrls(PERCUSSION_RIM_INSTRUMENT, …),  /* PERCUSSION_RIM_* */ });
    kick.connect(volume);
    rim.connect(volume);

    // Starting the loop starts Tone.Transport — the ONE new Tone.js surface. Orthogonal to
    // the free-running note triggers (they fire off _tone.now(), not the Transport).
    // Placement is DETERMINISTIC (probabilistic hits read as procedural, not human): one
    // callback per 4/4 measure, each voice on its fixed PERCUSSION_*_BEATS quarter-note
    // offsets; only ±20ms jitter (HUMANIZE_TIME_JITTER_SEC) + velocity vary per hit.
    // NOTE: sample fetch/decode is async while buildPercussion is sync — every trigger is
    // gated on sampler.loaded so early ticks are skipped, never thrown, in the callback.
    const loop = new T.Loop((time) => {
        const beatSec = T.Time('4n').toSeconds();
        for (const beat of PERCUSSION_KICK_BEATS)  // default [1, 3]
            if (kick.loaded) kick.triggerAttackRelease(PERCUSSION_KICK_PITCH, '2n', time + (beat - 1) * beatSec /* + jitter */, _humanVelocity());
        for (const beat of PERCUSSION_RIM_BEATS)   // default [2, 4]
            if (rim.loaded) rim.triggerAttackRelease(PERCUSSION_RIM_PITCH, '4n', time + (beat - 1) * beatSec /* + jitter */, _humanVelocity());
    }, '1m');

    return { loop, kick, rim, volume };
}
```

Humanized (velocity + small time jitter) to match the file's existing note humanization.
**Exact voice/tempo/sparsity values come from the audition (`audition-tool.md`)**, pinned
as `PERCUSSION_*` constants grouped with the existing `NOISE_*` constants.

## Changes to existing functions

| Function | Current (verified) | Change |
|---|---|---|
| `getMasterBus` | ends `if (_audioEnabled) noise.start();` (`:270`) | replace with `_applyBackfillLayer()` after the noise node is wired, so first build honors the persisted mode |
| `setAudioEnabled` | starts/stops **only** the noise node (`:291–304`) | route through `_applyBackfillLayer()` so mute stops **both** layers and unmute restarts **whichever** `_backfillMode` selects |
| `dispose` | disposes samplers, `_masterBus = null` (`:551`) | additionally tear down `_percussion` (loop + kick + rim + volume) and null it |
| export map | `window.TransitSynth = { … setAudioEnabled }` (`:563`) | add `setBackfillTexture` |

## Invariants (acceptance)

- **INV-1** (FR-005): a plain load with `_backfillMode === 'noise'` sounds byte-for-byte
  like today — noise starts iff `_audioEnabled`.
- **INV-2** (FR-010, SC-005): while unmuted, exactly one layer runs across any sequence
  of `setBackfillTexture` calls; rapid toggling converges to the last selection.
- **INV-3** (FR-008/009, SC-004): `setAudioEnabled(false)` stops percussion too;
  `setAudioEnabled(true)` restores the selected texture.
- **INV-4** (edge: pre-bus call): `setBackfillTexture` before `getMasterBus` records the
  mode and does not throw; the mode is honored when the bus builds.
- **INV-5**: starting `Tone.Transport` does not alter note-trigger timing (notes fire
  off `_tone.now()`, unscheduled on the Transport).

## Out of scope

No change to `triggerNote`, `preload`, `warmProdSamplers`, sampler caching, the
crossing dispatcher, or any `NOISE_*` value.
