# Quickstart: Instrument Compatibility Audition Tool

## Running it

1. **Easiest**: open `tools/instrument-compat/index.html` directly in a modern browser (double-click, or drag into a browser window).
2. **If `import()` is blocked on `file://`** (some browsers restrict ES module imports from the local filesystem): serve the folder with any static file server, e.g. from `tools/instrument-compat/`:
   ```
   python -m http.server 8080
   ```
   then open `http://localhost:8080/`.

No install step, no `npm install`, no build — it's a single static HTML file.

## First-run smoke test (2 minutes, matches spec SC-001)

1. Click **Enable Audio**. You should hear a faint, continuous ambient texture (the pink-noise bed) — this alone proves the master bus and unlock gesture work (spec Acceptance Scenario US1-1).
2. Add an instrument using these known-good sample URLs (cello, from the app's own soundfont source):
   - Note `C2` → `https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM/cello-mp3/C2.mp3`
   - Note `C3` → `https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM/cello-mp3/C3.mp3`
3. Wait for the instrument's card to show **Ready**.
4. Press its **Play note** button. You should hear one warm, reverb-tailed, filtered note — not a dry raw sample.
5. Set **Density** to Low, then Medium, then High. Confirm the rate of overlapping notes clearly increases at each step, then set it back to **Off** and confirm new notes stop.
6. Toggle **Mute**. Confirm everything (noise bed + any in-flight density notes) goes silent immediately; unmute and confirm it resumes.
7. Reload the page. Confirm your cello instrument, density level, and mute state are all still there.

If all seven steps behave as described, the tool is working end to end.

## Testing a real candidate instrument

1. Find hosted, cross-origin-fetchable MP3 URLs for the candidate voice (e.g. another instrument folder from the same FluidR3 GM soundfont host, or any other permissively-CORS'd host).
2. Add it via the form: name it, supply at least a low and a high anchor note + URL pair (the note name MUST be the actual pitch of that recording — this is what makes pitch-shifting sound correct).
3. Adjust attack/release/volume/durations if the defaults (`attack 0, release 1.0, volume 0dB, durations 8n/8n./4n`) don't suit the voice's character (e.g. give bowed/sustained instruments a small attack and shorter release/durations so they read as plucked rather than droning — see design doc §3.7 table for examples).
4. Solo-play it to confirm it loads and sounds acceptable alone.
5. Turn on Medium or High density (ideally with 2–3 other instruments also added) to judge how it sits in a busy mix.
6. Make the compatibility call. This tool does not export anything — if the instrument is a keeper, its final attack/release/volume/durations values are what you hand-enter into the app's `PALETTE` yourself, separately.

## Verifying a broken URL surfaces correctly

1. Add an instrument with a deliberately bad URL (typo the path, or use a URL that 404s).
2. Confirm the card shows a **Failed** state with a human-readable reason, and that every other instrument on the page keeps working normally.

## Acceptance checklist (full — see spec.md §Success Criteria and Requirements for the authoritative list)

Run through spec.md's acceptance scenarios (US1–US4) and edge cases in order; there is no automated test suite for this tool — manual verification against this checklist, in a real browser, with real audio output, is the intended and sufficient test method (per plan.md Technical Context: "Testing: Manual acceptance-checklist verification").
