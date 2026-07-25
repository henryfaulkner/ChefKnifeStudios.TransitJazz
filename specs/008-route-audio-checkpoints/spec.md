# Feature Specification: Route Audio Checkpoints

**Feature Branch**: `008-route-audio-checkpoints`
**Created**: 2026-05-18
**Status**: Draft
**Input**: User description: "I would like to create a POC marking arbitrary checkpoints along each route and for a playing audio when a vehicle passes a route checkpoint."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hear A Sound When A Vehicle Passes A Checkpoint (Priority: P1)

A visitor opens the transit map and watches live MARTA vehicles animate along their routes. As a vehicle reaches a pre-defined checkpoint on its route, the visitor hears a short audio sample. The map continues to display the vehicle as it moves past the checkpoint — the audio is the only feedback that a checkpoint trigger fired. Different routes (and different points along the same route) produce distinct sounds, turning the moving transit network into an evolving generative composition that fits the "TransitJazz" theme.

**Why this priority**: This is the entire purpose of the POC — proving the end-to-end flow that *a real-world vehicle crossing a virtual point produces a musical event in the browser*. Without this, the feature does not exist. Everything else (placing checkpoints, tuning sounds, scaling to all routes) only matters once a single checkpoint-pass produces an audible result.

**Independent Test**: Start the full local stack with a checkpoint configured on at least one active route. Wait for a vehicle to traverse that route segment (or replay a recorded scenario). When the vehicle's nearest-point on the route reaches the checkpoint, a sound MUST play in the browser. Mute behaviour, page reloads, and absence of vehicles MUST all behave correctly (no audio, no errors).

**Acceptance Scenarios**:

1. **Given** the transit map is loaded and at least one checkpoint is configured on an active route, **When** a live vehicle's snapped position on that route reaches the checkpoint, **Then** the associated audio sample plays once.
2. **Given** a vehicle has just triggered a checkpoint, **When** the same vehicle remains adjacent to or oscillates around the checkpoint over the next several seconds, **Then** the audio does NOT replay for that same vehicle within the cooldown window.
3. **Given** the page is loaded but the visitor has not yet interacted with the page, **When** a checkpoint trigger fires, **Then** the system handles the browser autoplay restriction gracefully (queued, deferred, or silently dropped — but not an unhandled error).
4. **Given** the transit map page is unloaded or the connection drops, **When** a vehicle passes a checkpoint, **Then** no audio plays and no error is logged on the client.

---

### User Story 2 - See Checkpoints On The Map (Priority: P2)

A visitor watching the transit map can visually see where the checkpoints are. Each checkpoint is rendered as a small marker on the route line at its configured location. This makes it possible to anticipate when a sound will play (because a vehicle is approaching a visible checkpoint) and to verify, by eye, that the checkpoint that just fired matched the vehicle that just crossed it.

**Why this priority**: This is the diagnostic and demonstration layer. Without it, the audio is the only feedback, which makes the POC hard to demo and hard to debug when something seems wrong. It is not strictly required for the core trigger mechanism to work, so it sits below P1.

**Independent Test**: With checkpoints configured but the audio system disabled, load the map and verify that checkpoint markers render at the configured positions on the correct routes, in the correct colours/styling, and that they do not interfere with vehicle markers or click handlers.

**Acceptance Scenarios**:

1. **Given** checkpoints are configured on one or more routes, **When** the transit map page loads and the routes are drawn, **Then** a marker is rendered at each checkpoint's position on its route line.
2. **Given** checkpoint markers are rendered, **When** vehicle markers animate over them, **Then** the vehicle markers visually pass over (not under) the checkpoint markers and remain clickable.
3. **Given** a checkpoint fires for a vehicle, **When** the audio plays, **Then** the corresponding checkpoint marker briefly highlights so the visitor can see which checkpoint triggered.

---

### User Story 3 - Configure Checkpoints Without Recompiling (Priority: P3)

A developer maintaining the POC can add, remove, or reposition checkpoints on routes without rebuilding the application binaries. The checkpoint definitions live in a place that can be edited and reloaded — at minimum, a configuration file or seed dataset that the system reads at startup. This makes the POC iterable: a demo can be re-tuned between sessions without a full CI/CD cycle.

**Why this priority**: The first two stories prove the system works; this story makes it useful for iteration. Without it, every demo tweak requires a code change. It is the lowest priority because the first demonstration only requires a hard-coded set to exist somewhere — refining the editing experience can follow.

**Independent Test**: Edit the checkpoint configuration source, restart the affected component(s), and confirm the new/moved/removed checkpoints appear (or disappear) on the map and trigger (or no longer trigger) audio accordingly.

**Acceptance Scenarios**:

1. **Given** the application is stopped, **When** a developer adds a new checkpoint to the configuration source and restarts the application, **Then** the new checkpoint is visible on the map and is eligible to fire audio.
2. **Given** a checkpoint exists in configuration, **When** a developer removes it from the configuration source and restarts, **Then** the checkpoint no longer renders and no audio fires for vehicles passing that location.

---

### Edge Cases

- **A checkpoint is defined off the route line**: If a checkpoint's coordinates do not lie on (or near) the configured route geometry, the system MUST either snap it to the nearest point on the route at load time or reject it with a logged warning — it MUST NOT cause runtime trigger logic to behave unpredictably.
- **A vehicle teleports past a checkpoint**: If a position update places a vehicle on the far side of a checkpoint without an intermediate update on the near side (e.g., a stale-data jump), the system MUST still fire the checkpoint once for that vehicle on that pass — the trigger is based on *crossing*, not on *being exactly at* the checkpoint.
- **Two checkpoints are very close together on the same route**: Both checkpoints MUST be capable of firing for a single vehicle as it traverses the segment containing both, in the order they are crossed.
- **A vehicle reverses direction near a checkpoint**: The cooldown window MUST suppress repeat fires for that vehicle within a short interval, regardless of approach direction.
- **The visitor has the tab muted or the browser has not yet received a user gesture**: The trigger MUST still be detected and logged client-side (so visual feedback in Story 2 still works); only the audio output is suppressed, with no console errors.
- **A route has no checkpoints**: Vehicles on that route MUST behave exactly as they do today — no audio, no markers, no overhead.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support associating zero or more *checkpoints* with each route, where each checkpoint identifies a single point along the route's geometry.
- **FR-002**: The system MUST detect, on the client, when a vehicle's animated position on a route reaches or crosses one of that route's checkpoints during normal real-time playback.
- **FR-003**: On detecting a checkpoint crossing, the system MUST play the audio sample associated with that checkpoint exactly once within a per-vehicle, per-checkpoint cooldown window of at least 10 seconds.
- **FR-004**: Each checkpoint MUST produce an audible musical note when crossed. The note pitch MUST be derived algorithmically from the checkpoint's route (and/or its position along the route) so the transit network plays a coherent generative composition as vehicles move. Notes are synthesized in the browser; no per-checkpoint audio files are shipped.
- **FR-005**: Checkpoints MUST be visibly rendered on the transit map as distinct markers on the route line they belong to, separate in appearance from vehicle markers.
- **FR-006**: When a checkpoint fires for a vehicle, the corresponding checkpoint marker on the map MUST give a brief visual indication (e.g., a highlight or pulse) so the visitor can correlate the audio with a specific location.
- **FR-007**: Checkpoint definitions MUST be loaded from a static JSON configuration file shipped with the client application (under `wwwroot/`) rather than being hard-coded in compiled source. Editing the file and reloading the app MUST be sufficient to add, move, or remove checkpoints — no rebuild of compiled code required.
- **FR-008**: The system MUST handle browser autoplay restrictions gracefully: if a checkpoint fires before the visitor has interacted with the page, the audio MAY be silently suppressed, but the system MUST NOT raise an unhandled error and MUST continue to function normally once interaction unlocks audio.
- **FR-009**: When no checkpoints are configured for a route, the existing transit map experience for that route MUST remain unchanged.
- **FR-010**: The system MUST tolerate vehicle position jumps (stale-data catch-up, GPS noise, dropped updates) such that a vehicle skipping over a checkpoint in a single position update still counts as a crossing for that vehicle on that pass.
- **FR-011**: The system MUST suppress repeat firings of the same checkpoint for the same vehicle while the vehicle remains within the cooldown window, regardless of whether the vehicle is moving forward, reversing, or stationary.

### Key Entities

- **Checkpoint**: A point of interest fixed to a specific position along a specific route. Attributes: the route it belongs to, its position on that route (a coordinate that lies on the route geometry), and the parameters used to derive its note when crossed. Visible on the map as a marker.
- **Musical Note**: A short pitched tone synthesized in the browser when a checkpoint is crossed. The pitch is derived algorithmically from the checkpoint's route (and/or its position along the route), so the running transit network produces a coherent generative composition. Notes are not pre-recorded audio files.
- **Trigger Event**: The transient occurrence of a specific vehicle crossing a specific checkpoint. Drives both the audio playback and the marker highlight, then disappears. Subject to a cooldown that prevents the same vehicle from re-triggering the same checkpoint immediately.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With at least one checkpoint configured on an active route, a visitor watching live data hears the corresponding audio sample within 2 seconds of a vehicle visibly reaching that checkpoint on the map, on at least 9 out of 10 observed crossings during a 30-minute live observation session.
- **SC-002**: For a stationary or oscillating vehicle adjacent to a checkpoint, the audio fires at most once per cooldown window — verifiable by counting audio fires against vehicle position logs over a 10-minute observation.
- **SC-003**: With checkpoints configured on at least 3 routes, the transit map page loads and renders all checkpoint markers within the same load-time budget as the current page (no observable regression in time-to-first-vehicle).
- **SC-004**: A developer can add, move, or remove a checkpoint and have the change reflected on the map after restart in under 5 minutes of total work (edit + restart + visual verify).
- **SC-005**: When a checkpoint fires before the visitor has interacted with the page, no error is reported in the browser console and the page continues to function normally; once the visitor interacts, subsequent checkpoint fires play audio as expected.

## Assumptions

- Checkpoints are defined per-route (not floating in space) and snap to the route's polyline geometry; this matches how V2 vehicle positions are already computed via `RouteNearestPointBatchEvent`.
- "Passing a checkpoint" is defined at the client, using the same animated position used to draw the vehicle — not at the server. This keeps the POC self-contained on the frontend and avoids new server-side spatial logic.
- The POC targets the existing transit map page (`/transit-map`) and inherits its existing data flow (SignalR → animator → MapLibre).
- Audio playback uses the browser's built-in audio capability (Web Audio synthesis) — no audio file library is shipped and no native app or external player is involved.
- A short cooldown (≥ 10 s) is acceptable for the POC. Production-grade composition controls (musical key selection beyond the chosen default, tempo locking, polyphony limits) are explicitly out of scope; the POC may pick any sensible default scale or mode.
- Checkpoint definitions live in a single static JSON file in `wwwroot/` (e.g., `wwwroot/checkpoints.json`) loaded on page load. Server-side authoring and in-browser editing of checkpoints are out of scope for the POC.
- The POC will demonstrate with a small, hand-picked set of checkpoints on a handful of routes. Generating checkpoints for every route automatically is out of scope.
- The visual style of checkpoint markers will be visibly distinct from vehicle markers but does not require a custom illustration; a simple shape/colour is sufficient for the POC.
- Persisting trigger history (e.g., "vehicle X fired checkpoint Y at time T") is out of scope for the POC. Logs are sufficient for debugging.
