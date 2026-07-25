# Feature Specification: Selectable Backfill Texture

**Feature Branch**: `049-backfill-texture-selector`  
**Created**: 2026-07-25  
**Status**: Draft  
**Input**: User description: "docs\BACKING_TEXTURE_SELECTOR_DESIGN_DOCUMENT.md"

## Clarifications

### Session 2026-07-25

- Q: How should the selected texture be persisted across a browser refresh? → A: Stored the same way the app's existing settings are persisted (the shared settings-storage mechanism), so the choice survives page refreshes and return visits exactly like other app settings.

## User Scenarios & Testing *(mandatory)*

TransitJazz turns live transit movement into a continuous procedural soundscape.
Between the melodic notes triggered by real vehicle crossings, a quiet background
texture fills the gaps so the space is never dead silent. Today that texture is a
single, fixed pink-noise bed. This feature lets a listener choose *which* texture
fills those gaps — starting with a new lo-fi percussion option alongside the
existing noise — from a dedicated on-screen control, with the choice remembered
across visits and defaulting to today's sound so nothing changes until they opt in.

### User Story 1 - Switch the background texture live (Priority: P1)

A listener with audio playing opens the backfill-texture control and picks a
different background texture (e.g. lo-fi percussion instead of ambient noise). The
soundscape's background layer swaps to the chosen texture immediately, while the
melodic transit notes continue uninterrupted.

**Why this priority**: This is the core value of the feature — giving the listener
control over the atmospheric layer underneath the music. Without it, nothing else
in the feature matters. It is the minimum shippable slice: a working live swap
between the two textures delivers the whole point of the feature.

**Independent Test**: With audio enabled, open the control, select each texture in
turn, and confirm the background layer audibly changes to the selected texture
within a moment of selection while melodic notes keep playing. Fully testable on
its own without persistence or startup behavior.

**Acceptance Scenarios**:

1. **Given** audio is enabled and the default ambient-noise texture is playing,
   **When** the listener selects the percussion texture, **Then** the background
   changes to the percussion texture and the ambient noise stops, with melodic
   transit notes continuing without interruption.
2. **Given** audio is enabled and the percussion texture is playing, **When** the
   listener selects ambient noise, **Then** the background returns to ambient noise
   and the percussion stops.
3. **Given** the currently-selected texture is showing in the control, **When** the
   listener opens the control, **Then** the option matching the active texture is
   presented as the current/active choice (not re-selectable as a no-op change).
4. **Given** exactly one background texture is meant to play at a time, **When** the
   listener switches textures, **Then** only the newly-selected texture plays and
   the previously-playing one is fully stopped (never both at once).

---

### User Story 2 - Remembered texture choice on return (Priority: P2)

A listener who previously chose a background texture returns to the app later (new
visit or page reload) and unlocks audio. The soundscape starts with their
previously-chosen texture as the background, without them having to re-select it.

**Why this priority**: Persistence turns a one-time toggle into a durable
preference, which is what makes the control feel like a real setting rather than a
transient toggle. It is valuable but secondary to the live-swap capability, and it
can be layered on after P1 works.

**Independent Test**: Select a non-default texture, reload the app, unlock audio,
and confirm the background layer is the previously-chosen texture from the first
moment of playback — with no manual re-selection.

**Acceptance Scenarios**:

1. **Given** the listener previously selected the percussion texture, **When** they
   reload the app and unlock audio, **Then** the background plays the percussion
   texture from the first unlock without further interaction.
2. **Given** the listener has never changed the texture, **When** they open the app
   for the first time and unlock audio, **Then** the background plays the default
   ambient-noise texture (today's behavior, unchanged).
3. **Given** a saved preference exists from an older version of the app whose stored
   settings are no longer compatible, **When** the app loads, **Then** the texture
   preference falls back cleanly to the default without error.

---

### User Story 3 - Texture is subordinate to the master mute (Priority: P2)

A listener who has muted all audio expects total silence regardless of which
background texture is selected; unmuting restores both the melodic notes and their
chosen background texture together.

**Why this priority**: The texture selector and the existing master mute are two
distinct controls that must compose correctly. Getting this relationship wrong
(e.g. percussion audible while muted) would be a clear defect, so it must be
specified even though it is a correctness guarantee rather than a headline feature.

**Independent Test**: Select any texture, then mute all audio and confirm complete
silence (no notes, no background texture). Unmute and confirm both notes and the
selected background texture resume.

**Acceptance Scenarios**:

1. **Given** any background texture is selected and audio is muted, **When** the
   listener listens, **Then** there is total silence — no melodic notes and no
   background texture of any kind.
2. **Given** audio is muted with the percussion texture selected, **When** the
   listener unmutes, **Then** both the melodic notes and the percussion texture
   resume.
3. **Given** the listener changes the selected texture while muted, **When** they
   later unmute, **Then** the most-recently-selected texture is the one that plays.

---

### Edge Cases

- **No "off" texture**: There is always exactly one background texture selected.
  The control never offers a "silence" or "none" option — total silence is the job
  of the separate master mute, not this control.
- **Switching after a long idle/muted period**: Selecting a texture after audio has
  been idle or muted for a long time must still be able to produce sound (the audio
  environment is re-activated as needed on selection).
- **Selecting before audio is ready**: If the listener's preference is applied
  before the audio engine has finished initializing, the choice is recorded and
  honored once the engine is ready — it is not lost and does not need re-selection.
- **Rapid repeated switching**: Toggling back and forth between textures quickly
  always converges to exactly one running texture matching the last selection, never
  a stuck state with two or zero background textures.
- **Selecting the already-active texture**: Re-choosing the currently-playing
  texture is a harmless no-op and does not cause a gap or restart glitch (the active
  option is presented as already-selected).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST always have exactly one background texture playing while
  audio is enabled — the gaps between melodic transit notes are never left
  completely dry.
- **FR-002**: The app MUST offer the listener a choice of background texture from a
  fixed, mutually-exclusive set, initially comprising **Ambient Noise** (the
  current pink-noise bed) and **Lo-fi Percussion** (a sparse, slow, humanized
  percussive loop).
- **FR-003**: The listener MUST be able to change the selected background texture at
  any time via a dedicated on-screen control that is separate from the master audio
  mute and from other existing controls.
- **FR-004**: Changing the selected texture MUST take effect live — the background
  layer swaps to the newly-selected texture promptly without requiring a reload and
  without interrupting the melodic transit notes.
- **FR-005**: The default background texture MUST be Ambient Noise, reproducing the
  app's current sound exactly for any listener who never changes the setting.
- **FR-006**: The listener's texture choice MUST persist across page reloads and
  return visits using the app's existing settings-storage mechanism (the same
  persistence used for the app's other settings), and MUST be re-applied
  automatically when audio is next unlocked so the saved texture is heard from the
  first moment of playback.
- **FR-007**: The background texture control MUST NOT provide an "off" / "silence"
  option; silencing all audio remains exclusively the responsibility of the
  existing master mute.
- **FR-008**: The master audio mute MUST silence the selected background texture in
  addition to the melodic notes; while muted, no background texture of any kind
  plays.
- **FR-009**: Unmuting MUST restore both the melodic notes and whichever background
  texture is currently selected.
- **FR-010**: At any moment while audio is enabled, exactly one background texture
  MUST be running — selecting a new texture MUST stop the previously-running one so
  the two never play simultaneously and never both stop while unmuted.
- **FR-011**: The control MUST indicate which texture is currently active and MUST
  present the active texture as already-selected (so re-choosing it is a no-op
  rather than a disruptive restart).
- **FR-012**: The lo-fi percussion texture MUST play underneath the melodic notes as
  an atmospheric filler (sparse, slow, quiet, humanized) rather than reacting to
  individual transit events; it is decorative background, not a per-event sound.
- **FR-013**: Only the texture *choice* MUST be persisted (a single stored
  preference value within the app's existing settings) — never any live audio state;
  the chosen texture is reconstructed each time audio starts.
- **FR-014**: The control's labels MUST be presented through the app's existing
  localization mechanism (English keys at minimum), consistent with the app's other
  on-screen chrome.
- **FR-015**: The design MUST allow additional background textures (e.g. vinyl
  crackle, rain) to be added later as incremental options without reworking the
  selection model; only Ambient Noise and Lo-fi Percussion ship in this feature.
- **FR-016**: A sound-designer audition capability MUST allow the lo-fi percussion
  texture to be dialed in by ear — underneath a simulated soundscape and through the
  same audio-processing chain the live app uses — before its final parameters are
  fixed into the app.

### Key Entities *(include if feature involves data)*

- **Background Texture Selection**: The listener's current choice of which
  atmospheric layer fills the gaps between melodic notes. A single value from a
  fixed set (Ambient Noise, Lo-fi Percussion), always populated (never "none"),
  persisted as a preference, and defaulting to Ambient Noise.
- **Lo-fi Percussion Texture**: A new sparse, slow, humanized percussive background
  layer (a soft kick plus an occasional rim/brush) that plays continuously
  underneath the melodic notes as atmosphere. Its character is defined by a set of
  tunable sound parameters (tempo/sparsity, per-voice tuning and loudness) finalized
  through the audition step.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A listener with audio playing can switch between the two background
  textures and hear the change take effect within about one loop/beat of selecting,
  with melodic notes never dropping out during the swap.
- **SC-002**: A listener who never touches the new control hears an experience
  identical to today's — the default Ambient Noise texture — with zero perceptible
  change to existing behavior.
- **SC-003**: A previously-selected non-default texture is heard from the very first
  unlock on a fresh reload in 100% of cases, with no manual re-selection required.
- **SC-004**: While muted, 100% of the time there is complete silence regardless of
  which texture is selected; unmuting restores both notes and the selected texture.
- **SC-005**: At no point while audio is enabled are zero or two background textures
  playing — exactly one is always running, verified across rapid repeated switching.
- **SC-006**: The lo-fi percussion texture reads as an unobtrusive atmospheric bed
  underneath the music (not a foreground beat) when auditioned beneath a simulated
  soundscape, as judged in the audition step before its parameters are fixed.

## Assumptions

- **Two textures ship first**: This feature delivers exactly Ambient Noise and
  Lo-fi Percussion. Additional textures are explicitly deferred, though the model is
  designed to accommodate them later.
- **Percussion is synthesis-based**: The lo-fi percussion is generated, not sampled,
  so it adds no meaningful memory/asset cost — consistent with the app's prior move
  away from sample-based sound.
- **Dedicated control, shared settings storage**: The texture choice is *surfaced*
  on its own dedicated on-screen control rather than in the existing
  reflection-driven settings panel (which renders simple on/off toggles), so that
  panel's structure is unaffected — but the choice is *stored* using the same
  underlying settings-persistence mechanism as the app's other settings (per the
  2026-07-25 clarification). Surface and storage are decoupled.
- **No new consumers**: Nothing else in the app reacts to the texture choice today,
  so no broadcast/notification mechanism is introduced for it now; one would be
  added only if a second consumer later appears.
- **English-only labels for now**: Localized (e.g. Spanish) labels are deferred,
  matching precedent set by recent related features.
- **Frontend-only**: This feature touches only the client-facing experience; there
  are no server, worker, or shared-data changes.
- **Supersedes the deferred drumkit direction**: This continuous decorative-loop
  approach replaces a previously-deferred event-driven percussion concept, which is
  recorded as closed rather than merely stale.
- **Audition reuses the existing instrument-audition tool**: The percussion is
  dialed in within the existing sound-audition tool (which already reproduces the
  app's audio chain), rather than in a throwaway page, so the audition is faithful
  by construction. Exporting tuned values into the app remains a deliberate manual
  step.
- **Final percussion parameters are an open item**: The exact tempo, sparsity, and
  per-voice tuning of the percussion are produced by the audition step and are not
  fixed by this specification.
