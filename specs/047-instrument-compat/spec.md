# Feature Specification: Instrument Compatibility Audition Tool

**Feature Branch**: `047-instrument-compat`
**Created**: 2026-07-25
**Status**: Draft
**Input**: User description: "tools/instrument-compat/DESIGN_DOCUMENT.md"

## Clarifications

### Session 2026-07-25

- Q: Real MARTA telemetry (103 ticks today, ~11.7s avg cadence) shows tones/tick is bimodal: quiet ticks ~5-8 tones (≈0.4-0.7/sec), busy ticks ~49-103 tones (≈4.2-8.8/sec) — there's little in between. The spec's current Medium guess (2-3/sec) sits in a gap the real data rarely occupies. How should Medium be defined? → A: Match the real busy-tick floor: Low ≈0.5–1/sec (quiet-tick, p25), Medium ≈4–5/sec (busy-tick, p75), High ≈7–9/sec (p90–peak) — Medium represents "a typical busy tick," not an interpolated midpoint the live app rarely produces.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Audition a candidate instrument solo (Priority: P1)

A sound designer evaluating a new instrument for TransitJazz wants to hear exactly how a candidate sampled voice will sound once it's wired into the app's real audio chain, without touching any app code or running the full application. They open the tool, unlock audio, paste in the hosted sample URLs for the candidate instrument (labeling which note each sample represents), and press a button to hear a single note played through the same filtering, stereo width, and reverb the live app uses.

**Why this priority**: This is the minimum viable slice — without it, the tool provides no value. Everything else (density audition, persistence) builds on top of "can I hear this candidate voice through the real chain."

**Independent Test**: Can be fully tested by opening the tool, unlocking audio, adding one instrument with valid sample URLs, and pressing its solo-play control — delivers a pass/fail "does this sound right" judgment on its own.

**Acceptance Scenarios**:

1. **Given** the tool is freshly opened, **When** the user clicks "Enable Audio", **Then** audio becomes unlocked and a faint continuous ambient texture becomes audible (proving the shared sound bed is running).
2. **Given** audio is unlocked, **When** the user adds an instrument by supplying two labeled sample URLs (a low reference note and a high reference note) and confirms, **Then** the instrument reaches a "Ready" state once its samples have loaded.
3. **Given** an instrument is Ready, **When** the user presses that instrument's solo-play control, **Then** they hear one correctly-pitched note, shaped by the same filtering/width/reverb character as the live app (not a dry/raw sample).
4. **Given** the user supplies a broken or unreachable sample URL, **When** the instrument attempts to load, **Then** the instrument's card shows a clear failed state with a reason, and the rest of the tool keeps working normally.

---

### User Story 2 - Audition an instrument inside a realistic multi-voice soundscape (Priority: P2)

Having confirmed a candidate instrument loads and plays correctly on its own, the sound designer now wants to judge how it sits inside a busy, multi-voice mix — since a voice that sounds great alone can clash or get lost once several instruments and notes overlap, as happens during real transit activity. They set a density level and listen to synthetic note activity at low, medium, and high transit-activity levels, with their added instrument(s) taking part.

**Why this priority**: This is the differentiating value of the tool over just playing a sample in isolation — it answers the actual question ("does this fit the soundscape"), but it depends on User Story 1's add/build/play mechanics already working.

**Independent Test**: With one or more Ready instruments added, can be tested independently by switching the density control through Off → Low → Medium → High and confirming a clearly increasing, distinguishable rate of overlapping notes, then back to Off to confirm the stream stops.

**Acceptance Scenarios**:

1. **Given** at least one instrument is Ready, **When** the user sets density to Low, **Then** they hear sparse, occasional single notes drawn from their added instrument(s).
2. **Given** the same setup, **When** the user raises density to Medium then High, **Then** the rate of overlapping notes clearly and audibly increases at each step.
3. **Given** density is active, **When** the user sets density back to Off, **Then** no new notes are scheduled (any notes already in flight may finish naturally).
4. **Given** no instruments have been added yet, **When** density is set to a non-Off level, **Then** nothing audible related to notes occurs (the ambient texture bed may still play), and the tool hints that an instrument is needed to hear the density audition.
5. **Given** two or more instruments are Ready, **When** density runs for an extended period, **Then** each added instrument audibly takes part (no instrument is silently excluded from the mix).

---

### User Story 3 - Mute and resume without losing place (Priority: P3)

While auditioning, the sound designer wants a fast, reliable way to silence everything (e.g., to talk to a colleague) and bring it back, without re-unlocking audio or re-adding instruments, and without stray notes sneaking through right after muting.

**Why this priority**: Important for a comfortable working session but not required to reach the tool's core judgment — the tool is still useful without a mute control, just less pleasant to operate.

**Independent Test**: Can be tested independently once audio is unlocked and density is running: toggle mute and confirm immediate silence including of near-future notes, then toggle again and confirm sound resumes.

**Acceptance Scenarios**:

1. **Given** audio is unlocked and density is producing notes, **When** the user mutes, **Then** all sound — including the ambient texture bed and any notes that were about to fire — stops immediately.
2. **Given** the tool is muted, **When** the user unmutes, **Then** sound resumes, including the ambient texture bed and any active density stream.
3. **Given** audio has never been unlocked, **When** the user toggles mute, **Then** nothing becomes audible until "Enable Audio" is also used — the two controls are independent.

---

### User Story 4 - Resume a session after reloading the page (Priority: P4)

The sound designer closes the browser tab or reloads mid-session and expects their added instruments, chosen density, and mute state to still be there, rather than having to re-enter every sample URL and setting from scratch.

**Why this priority**: A convenience/quality-of-life improvement. The tool is fully usable within a single session without it; it just avoids repetitive re-entry across sessions.

**Independent Test**: Add one or more instruments and set a non-default density/mute state, reload the page, and confirm the instruments reappear (reloading their samples) with the same density/mute state restored.

**Acceptance Scenarios**:

1. **Given** one or more instruments have been added, **When** the page is reloaded, **Then** each instrument reappears and re-reaches Ready (or Failed, if its URLs are no longer valid) without the user re-entering its details.
2. **Given** a non-default density level and/or mute state was set, **When** the page is reloaded, **Then** that density level and mute state are restored.
3. **Given** the user wants to start over, **When** they use a "Clear all" control, **Then** all saved instruments and settings are removed and the tool returns to its first-run state.

---

### Edge Cases

- What happens when a sample URL loads successfully but is not actually audio (e.g., wrong content type or corrupt file)? The instrument must surface a failed state rather than silently doing nothing or crashing the page.
- What happens when the user removes an instrument that is currently taking part in an active density audition? Its sound must stop taking part immediately and its resources must be released.
- What happens if the user tries to play a note or start density before audio has been unlocked via the required user gesture? These controls must be disabled or clearly no-op with a hint to unlock audio first, since browsers block unprompted audio.
- What happens when the user edits an already-Ready instrument's shaping settings (e.g., its envelope or duration choices)? The instrument must pick up the new settings (rebuilding if needed) without corrupting the rest of the mix.
- What happens when a previously-saved instrument's sample URLs no longer resolve on reload (link rot, host down)? That instrument should show a failed state on reload rather than silently vanishing, so the user knows to fix or remove it.
- What happens when the same session has many instruments loaded and density is set to High? All Ready instruments should keep taking part without the page becoming unresponsive.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The tool MUST require an explicit, direct user action ("Enable Audio") before any sound can play, and MUST NOT attempt to produce sound automatically on page load.
- **FR-002**: The tool MUST clearly indicate the current audio state at all times (locked/not-yet-enabled, enabled, muted).
- **FR-003**: Users MUST be able to add a candidate instrument by supplying one or more hosted sample URLs, each labeled with the true musical note that sample represents.
- **FR-004**: The tool MUST support at least two labeled sample rows per instrument by default (a low and a high reference note) and MUST allow the user to add or remove rows (minimum one).
- **FR-005**: Users MUST be able to set, per instrument, its onset sharpness (attack), fade-out length (release), an optional loudness trim, and which note-length choices it can use — with sensible defaults pre-filled so a user can add an instrument without touching every field.
- **FR-006**: The tool MUST reproduce the exact same shared musical pitch vocabulary (scale) and position-to-pitch mapping used by the live TransitJazz app, so pitches heard in the tool match what the app would produce for equivalent input.
- **FR-007**: The tool MUST play every note — solo or during density audition — through the same per-voice sound-shaping character (filtering, stereo widening, reverb) and the same shared mix bus (glue compression, gentle overall filtering, continuous quiet ambient texture bed) that the live app uses, so what is heard in the tool matches what the app produces.
- **FR-008**: An instrument MUST reach a visibly distinct "Ready" state only once its samples have fully loaded and its full sound chain is built and connected; it MUST NOT be playable before that point.
- **FR-009**: An instrument whose samples fail to load (unreachable, blocked, or not valid audio) MUST reach a visibly distinct "Failed" state with a human-readable reason, and MUST NOT crash the page or affect any other instrument.
- **FR-010**: Users MUST be able to trigger a single, solo audition note for any Ready instrument on demand.
- **FR-011**: Users MUST be able to select an activity level — Off, Low, Medium, or High — that governs a continuously-generated stream of synthetic note events, each drawn from the set of currently Ready instruments.
- **FR-012**: The three active levels (Low/Medium/High) MUST be clearly and subjectively distinguishable from one another, ranging from sparse/occasional (Low) to busy/overlapping (High), and switching among them MUST take effect immediately.
- **FR-013**: Setting activity level to Off MUST stop the scheduling of further note events (already in-flight notes may still complete naturally).
- **FR-014**: Each synthetic note event MUST choose fairly among all currently Ready instruments (no added instrument is systematically excluded) and MUST choose a pitch drawn from across the full shared scale over time, not just a fixed note.
- **FR-015**: Users MUST be able to mute and unmute all sound with one control; muting MUST immediately silence the ambient texture bed and MUST prevent any note already scheduled to fire imminently from sounding, even if it was queued just before the mute action.
- **FR-016**: Unmuting MUST restore the ambient texture bed and allow subsequent notes (including an already-running density stream) to sound again.
- **FR-017**: The audio-unlock control and the mute control MUST behave independently — muting/unmuting must never itself unlock audio, and unlocking must not depend on mute state.
- **FR-018**: Users MUST be able to remove an added instrument; removal MUST stop that instrument from taking part in any further solo or density playback and MUST release its underlying resources.
- **FR-019**: The tool MUST persist the list of added instruments (their labels, sample rows, and per-instrument settings) across a page reload, without requiring the user to re-enter them.
- **FR-020**: The tool MUST persist the chosen activity level and mute state across a page reload.
- **FR-021**: On reload, each restored instrument MUST attempt to reload its samples and reach Ready or Failed on its own merits (a previously-working sample URL that has since gone bad must show Failed, not silently disappear).
- **FR-022**: Users MUST be able to clear all saved instruments and settings in one action, returning the tool to its first-run state.
- **FR-023**: The tool MUST run as a single self-contained page with no build step, no backend, and no dependency on the TransitJazz application itself — a user can audition a candidate instrument without running or modifying any other part of the system.
- **FR-024**: The tool MUST NOT alter, generate, or export any configuration back into the TransitJazz application; the outcome of a session is a human judgment only, carried out of the tool by the user themselves.

### Key Entities

- **Instrument (candidate)**: A user-added sound source under evaluation. Has a display name, a set of one-or-more labeled sample rows (each: a note label + a hosted audio URL), an onset-sharpness value, a fade-out-length value, an optional loudness trim, a set of allowed note-length choices, and a load state (loading / Ready / Failed with reason).
- **Activity level**: The current density setting for the synthetic note stream — one of Off, Low, Medium, or High — governing how frequently synthetic note events are generated.
- **Synthetic note event**: A single generated "crossing" used to audition the mix — picks one currently-Ready instrument and one pitch from the shared scale, then plays it with the app's humanized timing/loudness variation.
- **Session state**: The saved snapshot of added instruments plus the current activity level and mute state, persisted across reloads until cleared.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from opening the tool to hearing a candidate instrument's first solo note in under 2 minutes, without writing or editing any code.
- **SC-002**: A person already familiar with the live TransitJazz soundscape can listen to the tool and correctly recognize it as sounding like the same app (same scale/character/space) without being told.
- **SC-003**: Users can distinguish Low, Medium, and High activity levels from each other by ear alone, with no explanation needed, in a single short listening pass. Target rates, grounded in real single-city telemetry (MARTA, 2026-07-25, 103 worker ticks, ~11.7s average tick cadence, `tones_emitted` per tick): Low ≈0.5–1 crossings/sec (quiet-tick floor, ~25th-percentile tick), Medium ≈4–5 crossings/sec (typical busy-tick rate, ~75th-percentile tick), High ≈7–9 crossings/sec (90th-percentile-to-peak tick). Real tick-to-tick activity is bimodal (quiet ticks cluster near 5–8 tones/tick, busy ticks cluster near 49–103 tones/tick, with little in between) rather than a smooth ramp — Medium is deliberately set at the busy-tick floor rather than an interpolated midpoint the live app rarely produces.
- **SC-004**: A candidate instrument with a broken sample link produces a clear, understandable failure indication rather than confusion about "why is nothing happening," on the first attempt.
- **SC-005**: A user's added instruments and settings survive a page reload with no re-entry required, verified by reloading immediately after adding an instrument.
- **SC-006**: Zero changes to the TransitJazz application's code or running state are required at any point to reach an instrument-compatibility judgment.

## Assumptions

- The person using this tool is a developer or sound designer with access to hosted, cross-origin-fetchable MP3 sample URLs (e.g., existing soundfont hosts) — the tool does not provide sample hosting or file upload.
- "Sounding like the app" is judged subjectively by ear against the live TransitJazz experience; no automated audio-similarity check is in scope.
- A modern desktop browser with Web Audio support is assumed; mobile support is best-effort, not guaranteed.
- Reproducing the app's exact shared scale, position-to-pitch mapping, per-voice effect chain, and shared mix bus (as opposed to an approximation) is required for the tool's core purpose (fidelity to the real app) and is treated as a hard behavioral requirement rather than an implementation nicety, even though the spec avoids naming the specific technology used to achieve it.
- Density-level target rates (SC-003) are grounded in one day's real single-city (MARTA) telemetry rather than chosen purely by ear; a single-city read was used (rather than the combined 5-city rate, which ran ~3.7–88.6 crossings/sec and would represent the whole multi-city app's load, not what one candidate instrument realistically competes against). Rates are a starting target to tune further by ear, not a frozen exact contract — the telemetry evidence exists to keep "by ear" from drifting arbitrarily far from real observed activity, not to demand bit-exact rate matching.
- Choosing which note-length token to use for a given synthetic note may be randomized rather than tied to any visual element, since this tool has no visual trail to stay in sync with.
- localStorage-equivalent browser storage is an acceptable, sufficient persistence mechanism for session state; no server-side or account-based persistence is required.
- This tool produces a human judgment call as its output; it does not need to integrate with, notify, or update any other part of the TransitJazz codebase.
