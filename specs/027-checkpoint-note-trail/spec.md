# Feature Specification: Checkpoint Crossing Trail

**Feature Branch**: `027-checkpoint-note-trail`  
**Created**: 2026-06-23  
**Status**: Draft  
**Input**: User description: "Checkpoint Crossing Trail — when a bus crosses a checkpoint and plays an audible note, draw a route-colored line segment on the map that starts at the checkpoint, grows forward along the route for the duration of the note, and disappears when the note ends, matching the visual weight of the bus marker."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a bus "sing" as it crosses a checkpoint (Priority: P1)

As someone watching the transit map, when a bus crosses one of its route checkpoints and the corresponding note sounds, I see a short, route-colored streak grow forward along the route from the checkpoint. The streak lives for exactly as long as the note, then vanishes — giving the map a visible heartbeat synchronized to the soundscape.

**Why this priority**: This is the entire feature. Without the growing, route-colored, note-synchronized trail there is no value to deliver. It is the minimum viable slice and is independently demonstrable.

**Independent Test**: Watch a single route with checkpoint pulses visible. Confirm that each time a bus crosses a checkpoint and a note sounds, a colored line segment appears anchored at the checkpoint, grows forward along the route, and disappears when the note ends.

**Acceptance Scenarios**:

1. **Given** checkpoint pulses are visible and a bus is approaching a checkpoint, **When** the bus crosses the checkpoint and a note is triggered, **Then** a route-colored line segment appears anchored at the checkpoint coordinate and begins growing forward along the route.
2. **Given** a trail is growing, **When** the note's duration elapses, **Then** the trail is removed from the map immediately and completely.
3. **Given** two buses on different routes cross checkpoints at the same moment, **When** both notes sound, **Then** each bus shows its own correctly-colored trail with no visual interference between them.

---

### User Story 2 - Speed reads as trail length (Priority: P2)

As a viewer, I can intuit how fast a bus is moving from how long its trail grows: a fast bus leaves a longer streak than a slow bus for the same note, and a stopped bus still leaves a small visible mark.

**Why this priority**: Enhances the expressiveness of the P1 trail but the feature is still viable without speed-proportional length (a fixed-length trail would still demonstrate the core idea). Builds directly on P1.

**Independent Test**: Compare a fast-moving bus and a slow-moving bus crossing checkpoints with equal-duration notes; confirm the fast bus's final trail is visibly longer, and confirm a stopped/very-slow bus still produces a visible mark.

**Acceptance Scenarios**:

1. **Given** two buses cross checkpoints producing notes of equal duration, **When** one bus is moving faster than the other, **Then** the faster bus's final trail length is visibly longer.
2. **Given** a bus is stopped or moving below the speed floor, **When** it crosses a checkpoint and a note sounds, **Then** a visible (non-zero-length) trail still appears.
3. **Given** a bus is moving very fast, **When** its trail grows, **Then** the trail length does not exceed the maximum length cap.

---

### User Story 3 - Trail respects checkpoint visibility (Priority: P3)

As a viewer who has hidden checkpoint pulses, I do not want trails cluttering the map; turning checkpoint visibility off both prevents new trails and clears any trails currently on screen.

**Why this priority**: A consistency/cleanup behavior layered on top of the core feature. The feature delivers value without it, but it keeps the map coherent with the existing checkpoint-visibility control.

**Independent Test**: With checkpoint pulses hidden, confirm no trails appear on crossings. Toggle visibility off while a trail is active and confirm the active trail is cleared immediately.

**Acceptance Scenarios**:

1. **Given** checkpoint pulse visibility is OFF, **When** a bus crosses a checkpoint and a note sounds, **Then** no trail appears.
2. **Given** a trail is currently growing, **When** checkpoint pulse visibility is toggled OFF, **Then** the active trail is removed immediately.

---

### Edge Cases

- **Same bus, rapid re-crossing**: When a bus crosses a second checkpoint while its previous trail is still active, the new trail renders on top of (stacks above) the prior trail without removing it; both run their own lifecycles.
- **Muted or locked audio**: A crossing that would produce a note still draws the trail when checkpoint pulses are visible, even if audio is muted or the audio context is locked — the trail is a visual event tied to the crossing, not to whether sound is actually heard.
- **No route color available**: When the route has no associated data color, the trail falls back to a warm highlight color so it is still visible.
- **Checkpoint near the end of a route**: When the checkpoint is close to the end of the route polyline, the trail grows only as far as the route extends (it does not overshoot the end of the route).
- **Stopped bus**: A bus with zero or near-zero speed still produces a visible mark via the speed floor.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST render a trail for every checkpoint crossing that triggers a note, provided checkpoint pulse visibility is ON. The trail MUST still render when audio is muted or the audio context is locked (the trail is gated on checkpoint pulse visibility, not on audio state).
- **FR-002**: The trail's tail MUST be anchored at the checkpoint coordinate projected onto the route polyline, and its head MUST advance forward along the route, growing from zero length to its final length over the note's duration.
- **FR-003**: The trail's final length MUST scale with the bus's current speed and the note's duration (`speed × noteDurationSeconds × LENGTH_SCALE`), bounded below by a speed floor so a stopped bus still produces a visible mark, and capped above by a maximum length.
- **FR-004**: The trail MUST be removed from the map immediately and completely when its note ends.
- **FR-005**: The trail MUST use the route's data color, falling back to a warm highlight color when no route color is available.
- **FR-006**: System MUST suppress trails while checkpoint pulse visibility is OFF, and MUST clear any active trails immediately when checkpoint pulse visibility is toggled OFF.
- **FR-007**: A new crossing for the same bus MUST supersede any prior trail for that bus by rendering on top of it (stacking), without disrupting the prior trail's own lifecycle.
- **FR-008**: The checkpoint marker and its pulse animation MUST render together with the trail (the pulse and trail visually coexist on the crossing).
- **FR-009**: The trail line weight MUST match the visual size of the bus marker (a 12px line width matching the bus dot diameter).
- **FR-010**: Trail behavior MUST be governed by centralized tuning constants (`MIN_SPEED`, `MAX_SPEED`, `LENGTH_SCALE`, `MAX_LEN_M`, `TRAIL_WIDTH`) defined in one place.

### Tuning Constants

| Constant | Default | Purpose |
|---|---|---|
| `MIN_SPEED` | 2.0 m/s | Speed floor — ensures a stopped bus still produces a visible mark |
| `MAX_SPEED` | 30.0 m/s | Speed ceiling |
| `LENGTH_SCALE` | 1.0 | Exaggeration factor applied to computed length |
| `MAX_LEN_M` | 600 m | Hard cap on final trail length |
| `TRAIL_WIDTH` | 12 px | Line width — matches the bus circle diameter |

### Key Entities *(include if feature involves data)*

- **Crossing Trail**: A transient visual segment representing one checkpoint crossing. Attributes: anchor point (projected checkpoint coordinate on the route), associated route (for color), associated bus, current head position along the route, final length, note duration, and active/expired state. Lives only for the duration of its note; not persisted.
- **Checkpoint Crossing Event**: The triggering occurrence — a specific bus passing a specific checkpoint, producing a note of a given duration at the bus's current speed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of checkpoint crossings that trigger a note produce a trail when checkpoint pulses are visible.
- **SC-002**: Every trail is fully removed from the map within one animation frame of its note ending (no lingering or orphaned trails).
- **SC-003**: For two equal-duration notes, a faster bus's final trail is measurably longer than a slower bus's, and no trail exceeds the maximum length cap.
- **SC-004**: A stopped or below-floor-speed bus still produces a visible (non-zero) trail on 100% of qualifying crossings.
- **SC-005**: With checkpoint pulses hidden, zero trails appear, and toggling visibility off clears all active trails within one animation frame.
- **SC-006**: When multiple buses on different routes cross simultaneously, each trail displays its own route color with no color bleed or interference between trails.
- **SC-007**: The trail line weight visually matches the bus marker size (12px), confirmed by side-by-side comparison on the map.

## Assumptions

- **Existing crossing/note signal is reused**: The system already emits a per-crossing signal (the same one that triggers the checkpoint note and pulse) carrying the bus, route, checkpoint, speed, and note duration; the trail consumes this existing signal rather than introducing a new detection mechanism.
- **Route polylines and checkpoint projection already exist**: The route geometry used to anchor the tail and advance the head is the same route polyline data already rendered on the map; checkpoints already project onto it.
- **Checkpoint visibility control already exists**: The trail reuses the existing checkpoint pulse visibility setting/state; no new setting or UI control is introduced.
- **Frontend-only, no persistence**: Trails are purely client-side visual ephemera. There are no server, worker, or shared-library changes and no persistence of trail state.
- **Speed availability**: The bus's current speed is available at crossing time; when speed is unavailable or below the floor, the speed floor (`MIN_SPEED`) applies.
- **Warm highlight fallback**: A single warm highlight color serves as the fallback when a route has no data color; the exact shade is an implementation detail chosen to be visible against the basemap.
