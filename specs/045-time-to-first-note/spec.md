# Feature Specification: Time-to-First-Note

**Feature Branch**: `045-time-to-first-note`  
**Created**: 2026-07-20  
**Status**: Draft  
**Input**: User description: "docs\TIME_TO_FIRST_NOTE_DISCOVERY_DOCUMENT.md"

## Overview

When a listener dismisses the audio-unlock overlay, the app is completely silent for an average of 10–15 seconds before the first audible note. During that gap there is no sound of any kind, so listeners cannot tell whether the experience is working, whether they interacted correctly, or whether the app is broken. Diagnostic telemetry attributes this to two compounding causes: (1) the app produces no ambient or confirming sound at the moment of unlock, and (2) the supply of musical events (checkpoint crossings) is far sparser and burstier than the moving fleet should produce, so the first note can be many seconds away.

This feature makes the experience feel alive the instant a listener unlocks it, increases the rate and evenness of musical events so notes arrive sooner and keep flowing, eliminates the guaranteed-silent first moments after page load, and closes an edge case where unlocking can silently fail and leave a session permanently muted. It also adds the measurement needed to prove each change worked and to keep the experience healthy over time.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Immediate audible confirmation at unlock (Priority: P1)

A first-time visitor opens the site, is shown the audio-unlock overlay, and taps "Enable." They immediately hear a soft ambient presence (and optionally a brief welcome motif), confirming that audio is working and the experience has begun — even before the first transit-driven note plays.

**Why this priority**: This is the single change that fixes the "am I using this correctly?" confusion regardless of how long the first transit note takes. It converts a silent, seemingly-broken wait into a confident "it's working" moment, and it is achievable without touching the server or the transit data pipeline. It delivers standalone value as an MVP.

**Independent Test**: Unlock audio on a fresh session and confirm that audible output begins at (or within a fraction of a second of) the unlock gesture, before any transit note plays. Confirm the ambient presence respects the saved mute setting (silent when muted).

**Acceptance Scenarios**:

1. **Given** a fresh session with audio muted-setting OFF (audio enabled), **When** the listener performs the unlock gesture, **Then** audible ambient output begins effectively immediately (well under 1 second) without waiting for a transit event.
2. **Given** the listener has previously set audio to muted, **When** they unlock, **Then** no ambient sound plays until they re-enable audio.
3. **Given** audio has been unlocked and ambient output is present, **When** the first transit-driven note occurs, **Then** that note plays with no perceptible additional build delay (the sound engine for the active routes is already prepared).

---

### User Story 2 - Faster and steadier stream of notes (Priority: P1)

A listener who is watching a busy city expects to hear notes arrive at a pace that reflects the visibly moving fleet, not long stretches of silence punctuated by short bursts. After unlocking, the first transit-driven note arrives within a few seconds, and notes continue to arrive at a musically satisfying cadence.

**Why this priority**: This addresses the dominant, structural cause of the latency: musical events are suppressed to roughly a small fraction of what the moving fleet should produce, and they arrive in ~30-second bursts with long silent gaps. Roughly half the moving fleet can never produce a note today because of how travel direction is handled, which is the largest single lever on note rate.

**Independent Test**: Measure the average number of musical events produced per processing cycle and the fraction of cycles that produce zero events for a representative city, before and after the change, at a comparable time of day. Confirm the per-cycle event rate increases materially and the silent-gap distribution shrinks.

**Acceptance Scenarios**:

1. **Given** a fleet with a substantial share of vehicles travelling in the direction opposite to the one currently favored, **When** those vehicles advance along their route, **Then** they produce musical events (they are no longer permanently silent).
2. **Given** a representative busy city at a representative time of day, **When** measured over a rolling window, **Then** the average musical events per cycle increases relative to the pre-change baseline and the expected wait from a random unlock moment decreases.
3. **Given** the change is deployed, **When** the per-path suppression counts are inspected, **Then** the cause of remaining suppression is attributable to specific, named reasons rather than unknown.

---

### User Story 3 - No guaranteed silence right after page load (Priority: P2)

A listener who loads the page and unlocks quickly (before waiting on the overlay) still hears transit-driven notes soon after unlocking, rather than being forced to wait for a fresh cycle that may carry no events.

**Why this priority**: The current cold-start path deliberately withholds musical events from the initial replayed snapshot, so a fast unlock can face an extra multi-second wait on top of the steady-state gap. Fixing this narrows the worst-case first-note time and makes the fast-unlock experience match the steady-state experience. It is valuable but secondary to the always-on ambient confirmation and to raising the overall note rate.

**Independent Test**: Compare first-note timing for a "fast unlock" scenario (unlock immediately on page load) against a "dwell" scenario (wait before unlocking), at a comparable time of day. Confirm the fast-unlock first-note time converges toward the dwell first-note time rather than being materially worse.

**Acceptance Scenarios**:

1. **Given** a listener who unlocks immediately after page load, **When** recent musical events exist from just before they joined, **Then** those still-relevant events are surfaced so the listener hears notes without waiting a full fresh cycle.
2. **Given** recent events are surfaced on join, **When** they are played, **Then** they do not produce a rapid unnatural burst of overlapping notes (events too old to be relevant are not replayed, and timing respects where vehicles actually are).

---

### User Story 4 - Unlock never leaves the session permanently silent (Priority: P2)

A listener on a slower connection or a mobile browser who taps "Enable" reliably ends up with working audio for the rest of their session, rather than a session that appears unlocked but never produces any sound.

**Why this priority**: On slow connections and certain mobile browsers, unlocking through the fallback path can run outside the browser's trusted-gesture window and leave audio permanently suspended for the whole session — a total failure for those users. It is lower frequency than the average-case latency but severe when it happens.

**Independent Test**: Simulate a slow audio-engine load, tap "Enable" before the engine finishes loading, and confirm audio still unlocks and produces sound (no permanently silent session), including on a mobile browser that enforces gesture-trust rules.

**Acceptance Scenarios**:

1. **Given** the audio engine has not finished loading, **When** the listener performs the unlock gesture, **Then** the unlock still occurs within the trusted-gesture window and audio is not left permanently suspended.
2. **Given** a mobile browser that enforces strict gesture-trust rules, **When** the listener unlocks, **Then** the audio context transitions to running and subsequent notes are audible.

---

### User Story 5 - Ongoing measurement and health monitoring (Priority: P3)

The team can measure the real time-to-first-note per deployed version and can detect when a city's musical density has regressed, using data the system already produces plus a lightweight in-browser measurement.

**Why this priority**: Measurement is what turns each of the above from a hopeful change into a verified one, and prevents silent regressions in the future. It has no direct user-facing effect, so it is lowest priority, but it underpins confidence in the other stories.

**Independent Test**: On a deployed build, obtain a single reported time-to-first-note number split into its "wait for a note" and "prepare the sound" halves. Separately, from existing per-cycle telemetry, compute the zero-event-cycle fraction per city and confirm a defined threshold flags an unhealthy city.

**Acceptance Scenarios**:

1. **Given** a deployed build with the measurement probe, **When** the first note sounds, **Then** a single measurement is reported that separates time-waiting-for-a-note from time-preparing-the-sound, tagged with the deployed version.
2. **Given** per-cycle telemetry over a rolling hour, **When** a city's zero-event-cycle fraction exceeds the defined threshold, **Then** that city is flagged as having degraded musical density.

---

### Edge Cases

- **Muted at unlock**: Ambient sound and any welcome motif MUST stay silent if the listener's saved setting is muted, and MUST begin when they later enable audio.
- **Prepared sound engine evicted before first note**: A route whose sound engine is prepared at unlock may go quiet long enough to be reclaimed before its first note; the experience MUST still produce that route's first note correctly (either by protecting recently-prepared routes briefly or by transparently rebuilding).
- **Replayed events too old**: On join, events older than a small freshness bound MUST NOT be replayed, to avoid a jarring burst and to avoid notes for positions vehicles have already left.
- **Very high-volume city on join replay**: Surfacing recent events on join MUST NOT overflow the size limits of the real-time update channel for the busiest cities at peak.
- **Increasing musical density**: If event density is later increased (e.g., by tightening event spacing), the busiest cities MUST stay within the real-time update channel's size ceiling and the client MUST remain responsive at peak event volume.
- **Direction ambiguity on out-and-back routes**: Vehicles on routes that double back on themselves MUST NOT be spuriously reset (losing their events) when their position appears to jump between overlapping legs.

## Requirements *(mandatory)*

### Functional Requirements

**Immediate audible feedback (User Story 1)**

- **FR-001**: The system MUST begin audible ambient output at the moment audio is unlocked, without waiting for any transit-driven event, whenever audio is enabled.
- **FR-002**: The system MUST suppress ambient output (and any welcome sound) when the listener's saved audio setting is muted, and MUST begin ambient output when audio is subsequently enabled.
- **FR-003**: The system MUST prepare the sound engine for the currently active/shipped routes at unlock time so that the first transit-driven note plays without a perceptible build delay.
- **FR-004**: Preparing sound engines at unlock MUST NOT materially increase client memory usage beyond the known safe footprint for the fixed set of active routes.

**Tone supply (User Story 2)**

- **FR-005**: The system MUST allow vehicles travelling in the direction opposite the currently favored one to produce musical events, so that this portion of the moving fleet is no longer permanently silent.
- **FR-006**: The system MUST record, per processing cycle and per city, the count of vehicles suppressed by each distinct suppression reason (e.g., first-seen, no-forward-progress, position-jump reset, route change), so the cause of missing events is measurable rather than assumed.
- **FR-007**: The system MUST NOT spuriously discard a vehicle's musical events due to position jumps caused by routes that overlap or double back on themselves.

**Cold-start (User Story 3)**

- **FR-008**: When a listener joins, the system MUST surface recent, still-relevant musical events so the listener can hear notes without waiting for a full fresh processing cycle.
- **FR-009**: The system MUST NOT replay musical events older than a small freshness bound, and MUST time replayed events so they do not produce an unnatural rapid burst or fire for positions vehicles have already passed.

**Unlock robustness (User Story 4)**

- **FR-010**: The system MUST perform the audio unlock within the browser's trusted-gesture window even when the audio engine has not finished loading, so that unlocking never leaves a session permanently silent.
- **FR-011**: After unlocking, the system MUST ensure the audio context reaches a running state on browsers that enforce strict gesture-trust rules.

**Measurement (User Story 5)**

- **FR-012**: The system MUST report a single time-to-first-note measurement, split into "time from unlock until the first qualifying note" and "time from that note to it becoming audible," tagged with the deployed version.
- **FR-013**: The measurement MUST indicate whether the session was a fast-unlock (cold-start) or dwell (steady-state) case, so results are attributable to the correct cause.
- **FR-014**: The system MUST support flagging a city as having degraded musical density when its zero-event-cycle fraction over a rolling window exceeds a defined threshold, using telemetry the system already produces.

**Cross-cutting constraints**

- **FR-015**: Every change MUST carry a stated, falsifiable numeric forecast and the metric that verifies it; a deployed change that misses its forecast MUST trigger re-diagnosis before further changes are stacked on top.
- **FR-016**: Any change to the per-cycle telemetry contract (e.g., new suppression-count fields) MUST keep the downstream telemetry query and validation layers consistent with the new fields.
- **FR-017**: Surfacing recent events on join MUST remain within the size limits of the real-time update channel for the busiest cities at peak.

### Key Entities *(include if feature involves data)*

- **Musical event (checkpoint crossing)**: A moment when a vehicle passes a spacing-defined point along its route, which the client turns into an audible note. Its supply rate (per cycle, per city) and evenness are the core quantities this feature improves. *Terminology:* this single quantity appears under several names across artifacts — "musical event per cycle" (this spec), "crossing/cycle" (plan), "tone/tick" (tasks + discovery doc), and the telemetry column `tones_emitted`. They all refer to the same thing; SC-002's "≥2×" is stated here in musical-event terms and verified against `tones_emitted` avg per tick.
- **Processing cycle**: One periodic pass in which vehicle positions are fetched, snapped to routes, and evaluated for musical events. Cycles are the unit for measuring event rate and zero-event fraction.
- **Suppression reason**: A named cause for which a vehicle produced no musical event in a cycle (e.g., first-seen, no-forward-progress, position-jump reset, route change). Counting these per cycle turns cause attribution from hypothesis into measurement.
- **Ambient bed / welcome sound**: The always-present low-level audio (and optional brief motif) that provides immediate confirmation at unlock, independent of transit events.
- **Prepared sound engine (per route)**: The per-route sound resources that must be built before that route's first note; preparing them at unlock removes first-note build delay.
- **Join replay snapshot**: The set of recent state (and now recent still-relevant events) delivered to a listener when they join, used to avoid a guaranteed-silent first moment.
- **Time-to-first-note measurement**: A per-session, per-version metric split into supply and build halves, plus a fast-unlock/dwell label.
- **Musical-density health signal**: A per-city rolling-window indicator (e.g., zero-event-cycle fraction) used to detect regressions from existing telemetry.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a fresh session with audio enabled, listeners hear audible output within 1 second of the unlock gesture in effectively all cases, independent of when the first transit note arrives.
- **SC-002**: For a representative busy city at a representative time of day, the average number of musical events per processing cycle increases by at least 2× relative to the pre-change baseline.
- **SC-003**: For that same city and time, the median time from unlock to first transit-driven note in the steady-state (dwell) scenario is under 5 seconds.
- **SC-004**: For a listener who unlocks immediately after page load (fast-unlock), the median time to first note converges to within a small margin of the steady-state (dwell) median, rather than being materially worse.
- **SC-005**: Once a route's sound engine is prepared at unlock, the delay between a note being triggered and it becoming audible is effectively zero (no perceptible build wait) for the active routes.
- **SC-006**: Unlock leaves no session permanently silent: in a slow-load / strict-gesture test, audio reliably becomes audible in effectively all trials (no permanent-silence failures).
- **SC-007**: The cause of event suppression is fully attributable — the per-reason suppression counts for a cycle account for the difference between vehicles processed and vehicles that emitted an event (no unexplained remainder).
- **SC-008**: Each deployed change can be verified against its numeric forecast from a recorded per-version measurement, and any city whose zero-event-cycle fraction exceeds the defined threshold over a rolling window is flagged.

## Assumptions

- **Representative baseline**: The pre-change baseline for note-rate and first-note timing is measured for a busy city (e.g., the primary demo city) at a comparable time of day and day of week, since musical supply varies with transit service levels.
- **Fixed active-route set**: The set of routes whose sound engines are prepared at unlock is the small, fixed shipped set, keeping the memory footprint flat and known — matching the current safe footprint.
- **Existing measurement channels reused**: Ongoing musical-density monitoring reuses the per-cycle telemetry the system already emits; only new suppression-count fields are added, and downstream query/validation layers are updated to match.
- **Real-time channel ceiling respected**: There is an existing size ceiling on the real-time update channel for the busiest city; join-replay and any density increase must stay under it.
- **Independent delivery**: The immediate-audible-feedback story (US1) is deliverable on its own as an MVP without server or data-pipeline changes; the tone-supply and cold-start stories are server-side and can ship independently and incrementally.
- **Spacing revisit deferred**: Tightening the event spacing to further increase density is out of scope for the initial delivery and is only revisited after the direction fix and cold-start fix are measured, so as not to over-shoot the real-time channel ceiling before the true post-fix density is known.
- **Read-only toward agencies**: Any local re-measurement of the data pipeline against live agency feeds is read-only and does not affect agency systems.

## Out of Scope

- Tightening the musical-event spacing (density lever) — deferred until the direction and cold-start fixes are measured.
- Any change to how notes map to routes/vehicles (instrument/pitch assignment).
- Solving the near-silence of any specific secondary city as its own fix (tracked separately); this feature's density work targets the primary demo city, though the health signal will surface other cities.
