# Contract: `tools/instrument-compat/` Backfill Audition Mode

Iterate the existing standalone tool (its "048 pass", analogous to how 047 built it).
The tool already reproduces the app's exact master bus, so the percussion loop
auditioned as a sibling node is fidelity-accurate by construction. **Do NOT build a
throwaway page.**

## New "Backfill" section (sibling of Transport/Density and Instruments)

- **AUD-1**: a **Noise / Percussion** selector mirroring the app's FAB two-mode model
  (not a bespoke model) — the tool exercises the same `_backfillMode` semantics.
- **AUD-2**: when **Percussion** is selected, expose **live controls** for every value
  that will be pinned into `transit-synth.js`:
  - loop interval (`1n` / `2n` / `4n`)
  - kick tuning + decay + volume
  - rim volume + probability
  - overall `PERCUSSION_VOLUME_DB`
- **AUD-3**: the percussion builder is the **same recipe** as `synth-engine.md`'s
  `buildPercussion`, wired to the tool's existing `getMasterBus()`, gated by the tool's
  existing `muted` flag (fire-time re-check) and its Enable-Audio unlock. **No new bus,
  no new unlock path.**
- **AUD-4**: reuse the tool's existing localStorage session shape — add `backfill` +
  `percussionParams` alongside `instruments` / `activityLevel` / `muted` so a dialed-in
  kit survives reload while tuning.
- **AUD-5**: auditionable **underneath a simulated soundscape** — the tool can load the
  app's real instruments and run the density sim, so the kit is tuned under a live mix,
  not in isolation (the true test of a backfill).

## Output

- **AUD-6**: the tuned parameter values become the `PERCUSSION_*` constants in
  `transit-synth.js` (`synth-engine.md`), transcribed **by hand**. The tool
  intentionally has **no** PALETTE/percussion-snippet export (matches 047's stance) —
  transcription is a deliberate manual step.

## Documentation

- **AUD-7**: update `tools/instrument-compat/DESIGN_DOCUMENT.md` to document the new
  Backfill audition mode.

## Sequencing note

The audition SHOULD happen **before** the `PERCUSSION_*` constants are pinned into the
.NET solution — it de-risks the one genuinely novel surface (the `Tone.Transport` loop).
Until then, the app can build/run with the design-doc **starting-point** values; they
are replaced by the audition output before the feature is considered done.
