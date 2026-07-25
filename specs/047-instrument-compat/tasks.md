---

description: "Task list for Instrument Compatibility Audition Tool"
---

# Tasks: Instrument Compatibility Audition Tool

**Input**: Design documents from `C:\Projects\ChefKnifeStudios.TransitJazz\specs\047-instrument-compat\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/engine-contract.md, quickstart.md

**Tests**: No automated test framework is used (plan.md Technical Context: "Manual acceptance-checklist verification... no automated test framework — this is a throwaway audition bench"). No test tasks are generated; each user story's "Independent Test" in spec.md and the corresponding section of quickstart.md serve as the manual verification step.

**Organization**: This is a **single self-contained file** (`tools/instrument-compat/index.html`) — there are no separate model/service/endpoint files to split across tasks. Tasks are still organized by user story per the required workflow, but most tasks touch the same file (`index.html`), building it up incrementally in dependency order (add markup/section → wire behavior). `[P]` is used only where two tasks genuinely touch independent, non-overlapping regions of the file (e.g., unrelated CSS additions or an isolated helper function) and could be written in either order without conflict — most tasks in a single-file build are inherently sequential and are NOT marked `[P]`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can be done in either order without conflict (rare in this single-file build)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- All tasks reference `tools/instrument-compat/index.html` unless noted

## Path Conventions

Single self-contained tool per plan.md Project Structure:
```
tools/instrument-compat/
├── DESIGN_DOCUMENT.md   (already exists)
└── index.html           (this feature's only deliverable file)
```

---

## Phase 1: Setup

**Purpose**: Create the file skeleton that every later phase builds on.

- [X] T001 Create `tools/instrument-compat/index.html` with a minimal HTML5 skeleton: `<!doctype html>`, `<html>`, `<head>` with `<title>TransitJazz Instrument Compatibility</title>` and inline `<style>` block (empty for now), `<body>` with placeholder region comments for Header, Transport/Density Controls, Instruments List, and Status/Log Area (per plan.md/design doc §5.1), and a `<script type="module">` block (empty for now). Include a short HTML comment at the top with the two run methods (open directly, or `python -m http.server 8080` if `file://` `import()` is blocked) per design doc §8/quickstart.md.
- [X] T002 [P] Add base inline CSS in `tools/instrument-compat/index.html`: system font stack, dark background (per design doc §5.1, "suits an audio tool but is not required" — choose dark), spacing/legibility basics, and placeholder `.disabled` state styling for controls that require audio-unlock first. This is layout/typography only — no component-specific styling yet.
- [X] T003 Add the fidelity constants as named consts at the top of the `<script type="module">` in `tools/instrument-compat/index.html`, copied verbatim from data-model.md's "Fidelity constants" section: `SCALE`, `FILTER_CUTOFF_HZ`, `STEREO_WIDTH`, `REVERB_DECAY`, `REVERB_PRE_DELAY`, `REVERB_WET`, `MASTER_COMPRESSOR`, `MASTER_FILTER_HZ`, `NOISE_VOLUME_DB`, `NOISE_FILTER_HZ`, `HUMANIZE_TIME_JITTER_SEC`, `HUMANIZE_VELOCITY_MIN`, `HUMANIZE_VELOCITY_MAX`, and the allowed duration-token list `['16n','16n.','8n','8n.','4n','4n.','2n']`.

**Checkpoint**: File exists, opens in a browser with no console errors, shows empty placeholder regions.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The synthesis engine core (Tone.js loading, master bus, note-position mapping) that every user story depends on. Per the engine contract (contracts/engine-contract.md), no user-story behavior can be demonstrated until this exists.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete — every story plays sound through this engine.

- [X] T004 Implement `getTone()` in `tools/instrument-compat/index.html`: a memoized async function that performs `await import('https://esm.sh/tone@15')` on first call and returns the cached module on subsequent calls (research.md "Tone.js version & load mechanism"; contracts/engine-contract.md `getTone`).
- [X] T005 Implement `noteForPosition(scale, triggerIndex, totalTriggers)` in `tools/instrument-compat/index.html`, copied verbatim from design doc §3.3 (clamp `totalTriggers` to minimum 1, clamp `triggerIndex` into range, round-to-nearest scale index via linear interpolation). This is a pure function with no Tone.js dependency — implement and keep it byte-for-byte identical to the app's version (data-model.md Fidelity constants).
- [X] T006 Implement `getMasterBus()` in `tools/instrument-compat/index.html`: a lazily-built, cached-once-per-page-life async function per contracts/engine-contract.md `getMasterBus` and data-model.md's master-bus fidelity constants — `Compressor(MASTER_COMPRESSOR) → Filter(MASTER_FILTER_HZ, 'lowpass') → Destination`, plus a `Noise('pink') → Filter(NOISE_FILTER_HZ, 'lowshelf') → Volume(NOISE_VOLUME_DB) → compressor` chain that starts only when audio is unlocked and unmuted. Return `{ input: compressor, noise }` (design doc §3.6).
- [X] T007 Implement the Enable Audio button markup (in the Header region) and its `enableAudio()` click handler in `tools/instrument-compat/index.html`: the handler's first statement must be `await Tone.start()` with nothing else awaited beforehand in the same call chain (design doc §3.2, research.md "Audio unlock gesture handling"). On success, set an internal `audioUnlocked = true` flag and, if not muted, call `getMasterBus()` and start the noise bed. Update the header's visible audio-state indicator (locked / enabled / muted) per FR-002.
- [X] T008 Wire disabled/enabled states in `tools/instrument-compat/index.html` so that density controls and any per-instrument Play-note buttons are disabled (or clearly no-op with a hint) until `audioUnlocked === true` (spec Edge Cases: "play a note or start density before audio unlocked"; FR-002).

**Checkpoint**: Opening the page and clicking Enable Audio produces an audible, continuous, quiet pink-noise texture. No instruments exist yet — this validates the master bus and unlock gesture in isolation (spec US1 Acceptance Scenario 1 / quickstart step 1).

---

## Phase 3: User Story 1 - Audition a candidate instrument solo (Priority: P1) 🎯 MVP

**Goal**: A user can add one candidate instrument via labeled sample URLs, see it reach Ready (or Failed with a reason), and hear a single correctly-shaped, correctly-pitched note through the full chain via a solo Play-note button.

**Independent Test**: Open the tool, unlock audio, add one instrument with the known-good cello anchor URLs from quickstart.md, wait for Ready, press Play note, and confirm one warm, reverb-tailed, filtered note is heard (not dry/raw). Then add one instrument with a deliberately bad URL and confirm it shows Failed with a reason instead of crashing the page.

### Implementation for User Story 1

- [X] T009 [US1] Implement the `InstrumentSpec` shape and an "Add instrument" form in the Instruments List region of `tools/instrument-compat/index.html`, per data-model.md's `InstrumentSpec` table: `name` (text input), a repeatable **anchor rows** UI of `{ noteName, url }` seeded with two rows (`C2`/empty, `C3`/empty), add/remove-row buttons (minimum 1 row enforced), `attack` (number, default 0), `release` (number, default 1.0), `volumeDb` (number, default 0), and a `durations` multi-select/comma field over the seven allowed tokens (default `8n, 8n., 4n`) — per design doc §5.6 and spec FR-003/FR-004/FR-005.
- [X] T010 [US1] Implement `buildInstrument(spec)` in `tools/instrument-compat/index.html` per contracts/engine-contract.md: construct a `Tone.Sampler` from `spec.anchors` (mapped to a `{noteName: url}` object), and inside its `onload` callback build `Filter(FILTER_CUTOFF_HZ,'lowpass')`, `StereoWidener(STEREO_WIDTH)`, `Volume(spec.volumeDb)`, `Reverb({decay: REVERB_DECAY, preDelay: REVERB_PRE_DELAY, wet: REVERB_WET})`, `await reverb.generate()`, then `.chain(filter, widener, volume, reverb, (await getMasterBus()).input)` — only then mark the resulting `InstrumentVoice.state = "ready"` (FR-008). Wire the Sampler's `onerror` to set `state = "failed"` with a human-readable `errorMessage` (FR-009), without throwing an unhandled exception.
- [X] T011 [US1] Implement instrument card rendering in `tools/instrument-compat/index.html`: on "Add / Load" form submission, create an `InstrumentSpec`, call `buildInstrument`, and render a card showing name, compact anchor notes/URLs, and a load-state chip that transitions loading… → **Ready ✓** (green) or **Failed** (red, with the error message) per design doc §5.6 and FR-008/FR-009.
- [X] T012 [US1] Implement `triggerNote(voice, triggerIndex, totalTriggers)` in `tools/instrument-compat/index.html` per contracts/engine-contract.md: precondition-check `voice.state === "ready"`; re-check current mute state and no-op if muted (fire-time gate, needed now so US1's solo play respects mute even though the dedicated mute UI lands in US3); compute `note = noteForPosition(SCALE, triggerIndex, totalTriggers)`; pick a random `durationToken` from the voice's spec `durations`; compute humanized `velocity` (`HUMANIZE_VELOCITY_MIN` + random range) and `startTime` (`Tone.now()` + ±`HUMANIZE_TIME_JITTER_SEC` jitter); call `sampler.triggerAttackRelease(note, durationToken, startTime, velocity)`.
- [X] T013 [US1] Add a "Play note" solo button to each Ready instrument card in `tools/instrument-compat/index.html` that calls `triggerNote` with a random `totalTriggers` in [8,24] and random `triggerIndex` in `[0, totalTriggers)` (design doc §5.6, "a random scale degree each press is fine and mirrors real variety") — disabled while the instrument is not Ready.
- [X] T014 [US1] Add a minimal rolling log/status area to `tools/instrument-compat/index.html` (design doc §5.1 region 4) that appends a line on each instrument's load success/failure, so failures are visible even if the card itself is scrolled out of view.

**Checkpoint**: User Story 1 is fully functional and independently testable — the MVP slice per spec.md priority. Validate against quickstart.md steps 1-4 before proceeding.

---

## Phase 4: User Story 2 - Audition an instrument inside a realistic multi-voice soundscape (Priority: P2)

**Goal**: A user with one or more Ready instruments can select Off/Low/Medium/High density and hear a synthetic, evidence-grounded stream of overlapping notes drawn fairly from their added instruments.

**Independent Test**: With 1-2 Ready instruments from US1, switch density Off → Low → Medium → High and confirm audibly increasing, distinguishable overlap at each step (per spec.md SC-003 telemetry-grounded rates: Low ~0.5-1/sec, Medium ~4-5/sec, High ~7-9/sec), then back to Off and confirm new notes stop.

### Implementation for User Story 2

- [X] T015 [US2] Add the Density selector (Off/Low/Medium/High) to the Transport/Density Controls region of `tools/instrument-compat/index.html` (design doc §5.1 region 2) and implement `setActivityLevel(level)` per contracts/engine-contract.md: updates the current level immediately, persists nothing yet (persistence lands in US4).
- [X] T016 [US2] Implement the density scheduler in `tools/instrument-compat/index.html` as a self-rescheduling timer with randomized inter-arrival gaps (research.md "Density simulation approach"): target rates Low ≈0.5-1/sec, Medium ≈4-5/sec, High ≈7-9/sec (spec.md SC-003, telemetry-grounded, not the earlier ear-tuned 2-3/sec guess for Medium). Scheduler must stop generating new events the instant level is set to `"off"` (already-scheduled `setTimeout`s may still fire) per FR-013.
- [X] T017 [US2] Implement the synthetic-crossing generation step in `tools/instrument-compat/index.html`: on each scheduled tick, pick uniformly at random among currently `"ready"` instrument voices (skip the tick with no-op if none are ready — do not error), generate a random `totalTriggers` in [8,24] and random `triggerIndex` in `[0,totalTriggers)`, and call `triggerNote` (FR-014, data-model.md `SyntheticNoteEvent`).
- [X] T018 [US2] Add a visible hint in `tools/instrument-compat/index.html` ("add an instrument to hear the density sim") shown when density is non-Off but zero instruments are Ready (spec Acceptance Scenario US2-4).

**Checkpoint**: User Stories 1 AND 2 both work independently. Validate against quickstart.md step 5.

---

## Phase 5: User Story 3 - Mute and resume without losing place (Priority: P3)

**Goal**: A single Mute control silences everything (ambient bed + any notes about to fire) immediately, independent of the Enable Audio unlock, and resumes cleanly on unmute.

**Independent Test**: With audio unlocked and density running (from US1/US2), toggle Mute and confirm immediate silence including near-future scheduled notes, then toggle again and confirm full resume — without needing to re-unlock audio or re-add instruments.

### Implementation for User Story 3

- [X] T019 [US3] Add a Mute toggle to the Transport/Density Controls region of `tools/instrument-compat/index.html` and implement `setMuted(muted)` per contracts/engine-contract.md: on mute→true, `noise.stop()` immediately; on mute→false, if `audioUnlocked`, resume the AudioContext if suspended (`Tone.getContext().rawContext.resume()`) then restart the noise bed if not already running (design doc §5.5, FR-015/FR-016).
- [X] T020 [US3] Verify/harden the fire-time mute re-check already added to `triggerNote` in T012 so it reads the *current* mute flag (not a value captured at schedule time) — this is what silences notes queued just before a mute (FR-015's "even if it was queued just before the mute action"); add a code comment only if the non-obviousness of the re-check timing isn't otherwise clear from the surrounding code.
- [X] T021 [US3] Confirm/wire independence between `enableAudio()` (T007) and `setMuted()` (T019) in `tools/instrument-compat/index.html`: muting/unmuting must never call `Tone.start()` or otherwise unlock audio, and the Enable Audio handler must not read or depend on the mute flag beyond the "start noise bed only if unmuted" check already in T007 (FR-017).

**Checkpoint**: User Stories 1, 2, AND 3 all work independently. Validate against quickstart.md step 6.

---

## Phase 6: User Story 4 - Resume a session after reloading the page (Priority: P4)

**Goal**: Added instruments (as specs, re-fetched), density level, and mute state all survive a page reload without re-entry; a Clear-all control resets to first-run state.

**Independent Test**: Add 1+ instruments, set a non-default density and mute state, reload the page, and confirm everything reappears and re-reaches Ready/Failed on its own merits; then use Clear all and confirm a full return to first-run state.

### Implementation for User Story 4

- [X] T022 [US4] Implement the `SessionState` persistence envelope in `tools/instrument-compat/index.html` per data-model.md: a single `localStorage` key (e.g. `instrument-compat:instruments`) holding `{ instruments: InstrumentSpec[], activityLevel, muted }`. Write the whole envelope on every mutating action (add/edit/remove instrument, density change, mute toggle) — FR-019/FR-020.
- [X] T023 [US4] Implement session restore on page load in `tools/instrument-compat/index.html`: read the `localStorage` envelope (if present) before/independent of the Enable Audio gesture, restore `activityLevel` and `muted` into the UI controls, and for each saved `InstrumentSpec` re-render a card and re-call `buildInstrument` so it independently reaches Ready or Failed (FR-021, spec Edge Case "previously-saved instrument's URLs no longer resolve").
- [X] T024 [US4] Implement per-instrument Remove (dispose `sampler` and all `chainNodes` via `.dispose()`, remove the card, remove it from the persisted envelope and from the density scheduler's candidate pool) per FR-018 and contracts/engine-contract.md's disposal note.
- [X] T025 [US4] Implement rebuild-on-edit for an already-Ready instrument's shaping fields (attack/release/volumeDb/durations) in `tools/instrument-compat/index.html`: dispose the old voice, build a fresh one from the updated spec, and persist the updated spec (design doc §5.6, "rebuilding is simplest and always correct — dispose the old Sampler first"; spec Edge Case on editing a Ready instrument).
- [X] T026 [US4] Implement a "Clear all" control in `tools/instrument-compat/index.html` that disposes every live `InstrumentVoice`, removes the `localStorage` key entirely, and resets the UI to first-run defaults (`instruments: []`, `activityLevel: "off"`, `muted: false`) per FR-022.

**Checkpoint**: All four user stories are independently functional. Validate against quickstart.md steps 7 and the "Verifying a broken URL surfaces correctly" section.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final fidelity/robustness pass across all stories — no new user-facing behavior, just hardening and verification.

- [X] T027 [P] Add a `file://` import-blocked fallback note check in `tools/instrument-compat/index.html`'s top-of-file comment (already drafted in T001) — verify it matches the actual behavior observed when testing under `file://` vs. a local static server (design doc §8).
- [X] T028 Run the full acceptance checklist (spec.md §Success Criteria, design doc §6, all ten items) end-to-end in a real browser with real audio output, per quickstart.md's "Acceptance checklist" section — this is the project's sole verification method (plan.md: manual acceptance-checklist verification, no automated tests).
- [X] T029 Sanity-check `tools/instrument-compat/index.html` against the Fidelity contract table in contracts/engine-contract.md (Tone.js version, SCALE array, noteForPosition, per-voice chain constants, master bus constants, humanization ranges, mute fire-time re-check) to catch any drift introduced during implementation — diff the constants in the file against data-model.md's "Fidelity constants" section verbatim.
- [X] T030 Manually verify performance under High density with several (e.g. 5+) Ready instruments loaded simultaneously in `tools/instrument-compat/index.html`, confirming no audio glitching or UI jank (plan.md Performance Goals, spec Edge Case "many instruments loaded and density set to High").

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS all user stories — every story plays sound through `getMasterBus`/`getTone`/`noteForPosition`.
- **User Story 1 (Phase 3)**: Depends on Foundational only. This is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational AND on US1's `buildInstrument`/`triggerNote`/card rendering (T009-T013) existing, since the density scheduler fires notes on already-added Ready instruments. Not independent of US1's code, but independently *testable* once US1 exists.
- **User Story 3 (Phase 5)**: Depends on Foundational (T006's noise-bed start/stop hook) and on T012's `triggerNote` (to add the fire-time mute gate). Can be built in parallel with US2 if desired, since mute and density are separate controls, though both touch the shared engine.
- **User Story 4 (Phase 6)**: Depends on US1 (InstrumentSpec shape, card rendering, buildInstrument) and benefits from US2/US3 existing so density/mute state has something meaningful to persist — implement last.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each User Story

- Engine/data functions before UI wiring that calls them (e.g., T010 `buildInstrument` before T011 card rendering).
- Card rendering before the controls that act on a card (e.g., T011 before T013 Play-note button).
- Story complete and checkpoint-validated before moving to the next priority.

### Parallel Opportunities

Because this is a single HTML file, true `[P]` parallelism is rare — most tasks are sequential edits to the same file's growing script/markup. The few exceptions:
- T002 (CSS) can be done alongside T003 (JS constants) — different regions of the file, no shared state.
- T027 (documentation-comment check) can be done alongside T029/T030 (verification passes) in Phase 7.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003).
2. Complete Phase 2: Foundational (T004-T008) — CRITICAL, blocks everything.
3. Complete Phase 3: User Story 1 (T009-T014).
4. **STOP and VALIDATE**: Run quickstart.md steps 1-4 — Enable Audio, add the known-good cello instrument, confirm Ready, confirm Play-note sounds correct, confirm a bad URL shows Failed.
5. This alone delivers the tool's core value: "does this candidate instrument sound right through the real chain."

### Incremental Delivery

1. Setup + Foundational → engine ready, noise bed audible.
2. Add User Story 1 → validate independently → this is the MVP.
3. Add User Story 2 → validate independently (density audition).
4. Add User Story 3 → validate independently (mute).
5. Add User Story 4 → validate independently (persistence) → run full quickstart.md + acceptance checklist.
6. Polish pass (Phase 7).

---

## Notes

- No `[Story]` label on Setup/Foundational/Polish tasks per the required format.
- Every task after Phase 2 names `tools/instrument-compat/index.html` as its file — there is only one deliverable file for this feature (plan.md Project Structure).
- Verify each user-story checkpoint against its "Independent Test" in spec.md before moving to the next phase.
- Commit after each task or logical group, per the user's standing workflow preference (never auto-commit without being asked).
