# Quickstart: Selectable Backfill Texture

Manual verification walkthrough. Frontend-only; no server/worker/shared build needed
beyond the client. There is no automated test project for the client synth layer — the
sound is judged by ear (matches feature 047).

## A. Audition the percussion first (de-risk the one novel surface)

1. Open `tools/instrument-compat/index.html` in a browser.
2. Click **Enable Audio** (gesture unlock).
3. In the new **Backfill** section, select **Percussion**.
4. Turn the live knobs (loop interval, kick tuning/decay/volume, rim volume/probability,
   overall volume) until the kit reads as an *unobtrusive atmospheric bed* underneath a
   simulated soundscape — load a couple of real instruments and run the density sim so
   you're tuning under a live mix, not in isolation (**AUD-5**, SC-006).
5. Reload — confirm the dialed-in `backfill` + `percussionParams` persisted (**AUD-4**).
6. Record the final values; these become the `PERCUSSION_*` constants.

## B. Pin the constants + engine changes

7. In `transit-synth.js`, add `PERCUSSION_*` constants (from step 6) grouped with the
   `NOISE_*` constants; add `_backfillMode` / `_percussion` state, `setBackfillTexture`,
   `_applyBackfillLayer`, `buildPercussion`; update `getMasterBus` /`setAudioEnabled` /
   `dispose`; add `setBackfillTexture` to the `window.TransitSynth` export map
   (`contracts/synth-engine.md`).

## C. C# settings + interop + FAB + init

8. `Settings.cs`: add the `BackfillTexture` enum + `[HiddenSetting]` property; bump
   `CurrentVersion` **4 → 5** (`contracts/settings-interop.md`).
9. `ITransitSynthJsInterop` + `TransitSynthJsInterop`: add `SetBackfillTextureAsync`.
10. `Resources/RouteFilterResources.resx`: add `BackfillNoise` / `BackfillPercussion` EN keys.
11. `Components/FABs/BackfillTextureFab.razor` (+`.razor.css`): new FAB
    (`contracts/backfill-fab.md`) — light + dark CSS.
12. `MainLayout.razor`: mount `<BackfillTextureFab />`.
13. `TransitMap.razor.cs`: push the persisted texture on init beside
    `SetAudioEnabledAsync` (~line 110).
14. `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md`: add the one-line SUPERSEDED banner.

## D. Acceptance checks (run the app)

Build + run the Blazor WASM client. Unlock audio via the overlay, then:

| # | Steps | Expected | Maps to |
|---|---|---|---|
| D1 | Fresh profile (no saved settings), unlock audio, don't touch the new FAB | Sounds exactly like today — ambient noise bed under the notes | FR-005, SC-002 |
| D2 | Open the `graphic_eq` FAB → select **Lo-fi percussion** | Background swaps to percussion within ~one loop/beat; melodic notes keep playing without a gap | FR-004, US1, SC-001 |
| D3 | The active option in the menu | Shown as disabled/selected; re-clicking it does nothing disruptive | FR-011 |
| D4 | Select **Ambient noise** again | Background returns to noise; percussion stops; only one texture ever audible | FR-010, SC-005 |
| D5 | With percussion selected, mute via the AudioFab | Total silence — no notes, no percussion | FR-008, SC-004 |
| D6 | Unmute | Both notes and **percussion** resume (not noise) | FR-009 |
| D7 | Select percussion, reload the page, unlock audio | Percussion plays from the first unlock — no re-selection | FR-006, US2, SC-003 |
| D8 | Toggle Noise↔Percussion rapidly several times | Always converges to exactly one running texture matching the last click; never stuck silent or doubled | FR-010, SC-005 |
| D9 | Mute, wait (long idle), switch texture while muted, then unmute | The last-selected texture plays; audio context resumed so it actually sounds | edge: long-idle switch; FR-009 |
| D10 | Simulate an older saved-settings blob (Version 4), load the app | Settings fall back to defaults cleanly (no error); backfill = Noise | US2 scenario 3 |

## E. Constitution spot-checks

- **XII**: FAB labels come from the resx, not inline. `.es` intentionally absent (deferred).
- **XIII**: the FAB looks correct in both light and dark themes (toggle via DarkModeFab).
- **VIII**: the melodic crossing/held notes are unchanged; percussion is a decorative
  bed, not a per-transit-event sound.

## Done criteria

All D1–D10 pass, E spot-checks pass, and the `PERCUSSION_*` constants hold the
audition-approved values (not the design-doc placeholders).
