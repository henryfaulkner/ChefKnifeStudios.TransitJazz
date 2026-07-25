# Feature Specification: Synchronized Checkpoints

**Feature Branch**: `033-synchronized-checkpoints`  
**Created**: 2026-06-30  
**Status**: Draft  
**Input**: User description: "docs\SYNCHRONIZED_CHECKPOINTS_DESIGN_DOCUMENT.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Two listeners hear the same checkpoint music (Priority: P1)

Two people open the app at the same time on different devices (or two browser windows side by side) and watch the same set of moving vehicles. As vehicles travel along their routes and pass checkpoints, every checkpoint that fires a sound or pulse on one screen also fires on the other screen, producing the same note, instrument, and pulse for that checkpoint. The exact moment a checkpoint fires may differ slightly between the two screens, but the complete set of checkpoints that fire — and the sounds they make — is identical.

**Why this priority**: This is the entire reason for the feature. Today two clients disagree on which checkpoints fire for which vehicles, so the soundscapes audibly diverge. Without this, the shared-listening experience is broken. Delivering only this story already resolves the reported bug and is a viable, demonstrable improvement.

**Independent Test**: Open two instances of the app side by side viewing the same vehicles for several minutes. Record the set of checkpoint firings (which vehicle crossed which checkpoint) on each instance and compare. The sets must match. Confirm the same checkpoint produces the same note/instrument/pulse on both.

**Acceptance Scenarios**:

1. **Given** two clients are watching the same live vehicles, **When** a vehicle passes a checkpoint, **Then** both clients eventually fire that same checkpoint for that same vehicle with the same note, instrument, and pulse.
2. **Given** two clients are watching the same live vehicles over an extended session, **When** the sessions are compared, **Then** the complete set of checkpoint firings is identical across both clients (timing may differ).
3. **Given** a vehicle is rendered a few metres apart on the two screens due to local visual drift, **When** that vehicle crosses a checkpoint, **Then** both clients still fire the identical checkpoint.

---

### User Story 2 - Existing checkpoint experience is preserved (Priority: P1)

A single user continues to use the app exactly as before: checkpoints fire as vehicles move, notes play, pulses flash, and the crossing trail draws. All existing controls that govern checkpoint effects — muting audio, hiding checkpoints, hiding the crossing trail, and filtering to selected routes — continue to suppress or allow checkpoint effects exactly as they do today.

**Why this priority**: The change reroutes where checkpoint firings come from. If it regresses the single-user behavior or breaks the existing toggles, the feature is not shippable even if it fixes the two-client bug. Must ship together with Story 1.

**Independent Test**: With a single client, verify checkpoints fire as vehicles move; toggle audio mute, checkpoint visibility, and crossing-trail visibility and confirm each suppresses the corresponding effect; select/deselect routes and confirm only selected routes' checkpoints produce effects.

**Acceptance Scenarios**:

1. **Given** a single client with audio enabled, **When** a vehicle passes a checkpoint, **Then** the note plays, the pulse flashes, and the crossing trail draws as before.
2. **Given** audio is muted, **When** a vehicle passes a checkpoint, **Then** no note plays (other effects still governed by their own toggles).
3. **Given** checkpoint visibility is off, **When** a vehicle passes a checkpoint, **Then** no pulse is shown, consistent with prior behavior.
4. **Given** a route filter is active, **When** a vehicle on a non-selected route passes a checkpoint, **Then** that checkpoint produces no effect.
5. **Given** a vehicle passes a checkpoint, **When** the firing is processed, **Then** it is fired exactly once — there is no duplicate firing from a leftover local detection path.

---

### User Story 3 - A late-joining client does not get flooded (Priority: P2)

A user opens the app (or reconnects after a network drop) while vehicles have already been moving for a while. They do not get hit with a sudden burst of accumulated past checkpoint firings; they simply start hearing new checkpoints as they happen from that point forward.

**Why this priority**: Without this guard, a reconnecting client could replay a backlog of crossings as a jarring burst of notes. It protects the listening experience but is secondary to the core parity fix; the core feature can be demonstrated without simulating reconnects.

**Independent Test**: Let vehicles run for several minutes, then open a fresh client (or force a reconnect). Confirm the new client does not immediately play a flurry of historical checkpoint notes and instead only fires checkpoints that occur after it joins.

**Acceptance Scenarios**:

1. **Given** vehicles have been moving for several minutes, **When** a new client connects, **Then** it does not replay historical checkpoint firings.
2. **Given** a client temporarily loses connection and reconnects, **When** it rejoins, **Then** it resumes firing only newly occurring checkpoints, not a backlog.

---

### Edge Cases

- **Vehicle first observed**: When a vehicle is seen for the first time, no checkpoints fire for it on that first observation; its checkpoint baseline is established silently (parity with prior behavior).
- **Vehicle switches/transfers routes**: When a vehicle moves to a different route, the checkpoint baseline resets and no checkpoints fire on the transfer cycle.
- **Teleport / large position jump**: When a vehicle's position jumps by an implausibly large distance (e.g., bad data, GPS snap error), no checkpoints fire for that jump; the baseline resets.
- **Backward movement**: Only forward progress along the route produces checkpoint firings; apparent backward movement fires nothing.
- **Fast vehicle crossing several checkpoints in one update cycle**: All checkpoints passed within a single update cycle are fired; the system must remain musically acceptable when several fire close together. (Whether light spreading is needed is decided during implementation.)
- **Vehicle disappears**: Per-vehicle checkpoint state for vehicles no longer reporting must be cleaned up over time so state does not grow unbounded.
- **Route geometry unavailable for a vehicle's route**: If checkpoints cannot be determined for a route, no checkpoint firings are produced for vehicles on it (no errors surfaced to the user).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST determine checkpoint crossings from a single authoritative source so that every client receives the identical set of crossings for the same vehicles over the same period.
- **FR-002**: The set of checkpoint firings (each identified by the vehicle and the specific checkpoint it crossed) MUST be identical across all clients viewing the same vehicles; the timing of individual firings MAY differ between clients.
- **FR-003**: For any given checkpoint firing, all clients MUST produce the same note, instrument, and pulse, with no client-local variation in the sound.
- **FR-004**: The system MUST stop deriving checkpoint crossings independently on each client; clients MUST fire only the crossings they receive from the authoritative source.
- **FR-005**: Each checkpoint firing MUST be delivered to clients as live, fire-and-forget information; clients MUST NOT replay a backlog of past firings when they first connect or reconnect.
- **FR-006**: The checkpoint set used to identify crossings MUST be generated identically wherever it is computed, so that a crossing identifier means the same checkpoint to every client. (The same checkpoint count and ordering for a given route on every side.)
- **FR-007**: When a vehicle is first observed, the system MUST establish its checkpoint baseline without firing any checkpoints for that first observation.
- **FR-008**: The system MUST fire only checkpoints passed by forward progress along the route between a vehicle's previous and current position; backward movement MUST fire nothing.
- **FR-009**: The system MUST NOT fire checkpoints across an implausibly large position jump (teleport); such a jump MUST reset the vehicle's baseline and fire nothing.
- **FR-010**: When a vehicle changes routes, the system MUST reset its checkpoint baseline and fire no checkpoints on the transfer.
- **FR-011**: When a vehicle passes multiple checkpoints within a single update cycle, the system MUST fire all of them.
- **FR-012**: The system MUST preserve all existing checkpoint effect behavior on the client — note playback, pulse, and crossing trail — unchanged in appearance and behavior.
- **FR-013**: The system MUST preserve all existing controls that gate checkpoint effects — audio mute, checkpoint visibility, crossing-trail visibility, and route filtering — so they suppress or allow effects exactly as before.
- **FR-014**: Each checkpoint crossing MUST be fired exactly once on a client; there MUST be no duplicate firing from a residual local-detection path.
- **FR-015**: The system MUST clean up per-vehicle checkpoint tracking state for vehicles that stop reporting, so tracking state does not grow without bound.
- **FR-016**: Checkpoint determination MUST tolerate routes whose geometry is unavailable by producing no firings for affected vehicles rather than failing.

### Key Entities *(include if data involved)*

- **Checkpoint (trigger point)**: A fixed point along a route's geometry at a regular spacing. Each route has an ordered set of checkpoints; a checkpoint is identified by its index within the route's set and the total count of checkpoints on that route. These two values fully determine the note that plays for a crossing.
- **Checkpoint crossing (firing)**: A single event that a specific vehicle, on a specific route, crossed a specific checkpoint. Carries enough information (vehicle, route, checkpoint index, total checkpoint count) for any client to reproduce the same note, instrument, and pulse.
- **Per-vehicle checkpoint state**: The authoritative source's record of how far along its route each vehicle has progressed (its last-crossed position), used to decide which new checkpoints a vehicle has passed since its previous position. Reset on first observation, route change, and teleport; cleaned up when a vehicle stops reporting.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two clients viewing the same vehicles over a 10-minute session fire identical sets of checkpoint crossings (0 differing crossings when the two recorded sets are compared).
- **SC-002**: For every checkpoint crossing fired, the note, instrument, and pulse match across all clients (100% agreement, 0 mismatches).
- **SC-003**: Each crossing is fired exactly once per client — no duplicate firings observed across a full test session.
- **SC-004**: A client connecting after vehicles have been moving for at least 5 minutes plays zero historical/backlog checkpoint notes on join.
- **SC-005**: All existing checkpoint gating controls (audio mute, checkpoint visibility, crossing-trail visibility, route filter) behave identically to the prior release (no regressions in manual verification).
- **SC-006**: For the same route, the checkpoint count and ordering used to identify crossings is exactly equal wherever it is computed (exact match, every route).

## Assumptions

- The note, instrument, and pulse for a crossing are already a pure function of the crossing's identifiers (route, vehicle, checkpoint index, total count); this feature does not change how sounds are chosen, only where crossings are detected.
- Smooth visual vehicle motion remains locally rendered and is allowed to drift slightly between clients; only the checkpoint crossing set needs to agree (per the user's explicit bar, 2026-06-30). A small visual offset between a note and the on-screen marker is acceptable for v1.
- Wall-clock-simultaneous firing across clients is explicitly NOT required; network delivery jitter in timing is acceptable.
- Checkpoint crossings are determined once per data update cycle (roughly the cadence at which fresh vehicle data already arrives), and bursts of multiple crossings in one cycle are acceptable unless they prove musically clumped during implementation.
- The existing real-time delivery mechanism that already carries vehicle position updates is sufficient to also carry checkpoint crossings; no new transport or new delivery channel is introduced.
- The regular checkpoint spacing along route geometry (and the route geometry source) is unchanged from current behavior.
