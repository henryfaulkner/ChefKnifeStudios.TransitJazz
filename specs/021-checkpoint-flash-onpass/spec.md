# Feature Specification: Checkpoint Flash on Bus Pass & Bus-Visibility Toggle

**Feature Branch**: `021-checkpoint-flash-onpass`  
**Created**: 2026-06-19  
**Status**: Draft  
**Input**: User description: "Flash checkpoints when a bus passes it & hide buses from map. Checkpoints should flash when a bus passes them. The effect that I want is almost a pulsing of the checkpoint. Checkpoints should pulse the same color as the route they are on."

## Clarifications

### Session 2026-06-19

- Q: Should hiding buses be a fixed behavior or a user-toggleable setting? → A: A user-toggleable setting (a bus-visibility toggle in the Settings Blade).
- Q: What should the default state of the bus-visibility toggle be on first load? → A: Buses hidden by default (toggle OFF) — pulsing checkpoints carry the sense of motion for first-time/default viewing.
- Q: Should the pulse have its own visibility control, or follow the existing checkpoint-visibility setting? → A: The pulse's visibility is governed by the existing checkpoint-visibility setting (`AreCheckpointsVisible`) — no separate control. When checkpoints are hidden, pulses are suppressed; when shown, pulses occur on passes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Checkpoints pulse when a bus passes (Priority: P1)

As a viewer of the transit map, when a bus reaches a checkpoint along its route, I see that checkpoint briefly pulse — growing and brightening, then settling back — in the same color as the route the checkpoint belongs to. This gives me an at-a-glance, rhythmic sense of transit activity flowing across the network, visually echoing the audio soundscape that already triggers at these same checkpoints.

**Why this priority**: This is the core, novel behavior the user is asking for. It is the primary value of the feature and is independently demonstrable on its own. The pulse turns the static checkpoint dots into a living visualization of motion.

**Independent Test**: With buses running on at least one route, watch a checkpoint as a bus travels toward and past it. Confirm the checkpoint visibly pulses at the moment the bus passes, and that the pulse color matches that route's line color. Multiple checkpoints on different routes pulse in their respective route colors.

**Acceptance Scenarios**:

1. **Given** a bus is moving along a route and approaches a checkpoint on that route, **When** the bus passes (reaches/crosses) the checkpoint, **Then** that checkpoint pulses once — visibly enlarging and brightening, then returning to its resting appearance.
2. **Given** a checkpoint belongs to a route rendered in a specific color, **When** that checkpoint pulses, **Then** the pulse is rendered in that route's color.
3. **Given** two checkpoints on two different routes are passed at nearly the same time, **When** both passes occur, **Then** each checkpoint pulses independently in its own route's color.
4. **Given** a checkpoint has just finished pulsing, **When** no bus is passing it, **Then** it returns to and remains in its normal resting appearance.
5. **Given** the checkpoint visibility setting is turned OFF (checkpoints hidden), **When** a bus passes a checkpoint, **Then** no pulse is shown (there is nothing visible to pulse).

---

### User Story 2 - Toggle bus visibility from settings (Priority: P2)

As a viewer who wants a cleaner, more abstract visualization driven by the pulsing checkpoints (and the audio), I want a setting that controls whether bus markers are shown on the map. By default buses are hidden so the checkpoints' pulses — not the bus dots — carry the sense of motion, but I can turn buses back on whenever I want to see them.

**Why this priority**: Complementary to Story 1 and rooted in the original "hide buses from map" request, now scoped as a user-controllable toggle. With buses hidden (the default), the pulsing checkpoints become the primary visual signal of transit activity, but the user retains control. It is independently testable and deliverable, but secondary to the pulse itself.

**Independent Test**: Open the settings panel and locate the bus-visibility toggle. Confirm it defaults to OFF and that buses are not shown on first load. Toggle it ON and confirm bus markers appear; toggle it OFF and confirm they disappear. Throughout, routes and checkpoints remain visible and checkpoints still pulse as buses pass them. Reload the app and confirm the chosen setting is remembered.

**Acceptance Scenarios**:

1. **Given** a first-time load with no saved preference, **When** the map is displayed with active buses, **Then** the bus-visibility setting is OFF and no bus markers are visible.
2. **Given** the bus-visibility toggle, **When** the user switches it ON, **Then** bus markers appear on the map without a reload.
3. **Given** buses are currently visible, **When** the user switches the toggle OFF, **Then** bus markers are hidden without a reload.
4. **Given** the user has set a bus-visibility preference, **When** the app is reloaded, **Then** the map honors the saved setting from first render.
5. **Given** buses are hidden, **When** a bus passes a checkpoint, **Then** the checkpoint still pulses (the underlying bus motion still drives the pulse even though the bus is not drawn).
6. **Given** any bus-visibility state, **When** the map renders, **Then** route lines and checkpoints remain visible and unaffected.

---

### Edge Cases

- **Rapid re-passing / clustering**: If a bus lingers near, oscillates around, or re-crosses the same checkpoint in quick succession, the checkpoint MUST NOT flicker uncontrollably; repeated pulses for the same checkpoint within a short window are suppressed (reusing the existing pass-detection cooldown).
- **Multiple buses passing the same checkpoint**: If two buses on the same route pass the same checkpoint nearly simultaneously, the checkpoint pulses (one coherent pulse per pass event is acceptable; overlapping pulses MUST NOT leave the checkpoint stuck in an enlarged/bright state).
- **Pulse interrupted by a new pass**: If a bus passes a checkpoint while that checkpoint is still mid-pulse from a prior pass, the pulse restarts/continues cleanly and always settles back to the resting appearance afterward (no checkpoint left permanently enlarged or brightened).
- **Checkpoints toggled off mid-pulse**: If checkpoint visibility is turned off while a pulse is animating, the checkpoint disappears with the rest and no orphaned animation persists; turning visibility back on shows checkpoints in their resting appearance.
- **Route color missing**: If a route has no defined color, its checkpoints pulse using a sensible default highlight color rather than failing to pulse.
- **Basemap style switch**: After a basemap (street/dark) style change re-adds the checkpoint layer, pulses continue to work and use the correct route colors.
- **No buses / no passes**: With no buses moving, checkpoints simply rest with no pulsing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST visibly pulse a checkpoint when a bus passes (reaches/crosses) that checkpoint along its route.
- **FR-002**: The pulse MUST be a brief, self-resolving animation — the checkpoint enlarges and brightens, then returns to its resting appearance ("pulsing" effect).
- **FR-003**: The pulse MUST be rendered in the color of the route to which the checkpoint belongs.
- **FR-004**: When no defined color exists for a route, the system MUST pulse that route's checkpoints in a default highlight color rather than skipping the pulse.
- **FR-005**: The system MUST reuse the existing bus-passes-checkpoint detection signal that already drives the audio soundscape; it MUST NOT introduce a separate or re-computed proximity/crossing detection.
- **FR-006**: The system MUST suppress repeated pulses for the same checkpoint within the existing pass-detection cooldown window so the checkpoint does not flicker on jitter or rapid re-crossing.
- **FR-007**: Every pulse MUST settle the checkpoint back to its resting appearance; no checkpoint may remain permanently enlarged, brightened, or otherwise altered after a pulse completes.
- **FR-008**: Pulse visibility MUST be governed by the existing checkpoint-visibility setting (`AreCheckpointsVisible`); the pulse MUST NOT have its own separate visibility control. When that setting is OFF, the system MUST NOT show pulses (no visible checkpoints to pulse); when it is ON, pulses occur on passes. Turning the setting on/off MUST NOT leave orphaned pulse animations.
- **FR-009**: The system MUST provide a user-toggleable setting (in the existing settings panel) that controls whether bus markers are shown on the map, alongside the other settings.
- **FR-009a**: The bus-visibility setting MUST default to OFF (buses hidden) when no preference has been saved, so first-time/default viewing shows the checkpoint-driven visualization.
- **FR-009b**: Toggling the bus-visibility setting MUST show or hide bus markers immediately, without requiring a page reload.
- **FR-009c**: The system MUST persist the bus-visibility preference and honor it from first render on subsequent loads.
- **FR-010**: When buses are hidden, the underlying bus motion MUST still drive checkpoint pulses (hiding the markers MUST NOT disable pass detection or pulsing).
- **FR-011**: Changing bus visibility (either direction) MUST NOT affect the visibility or appearance of route lines or checkpoints.
- **FR-012**: Pulses MUST continue to function correctly and use the correct route colors after a basemap style switch re-adds the checkpoint layer.
- **FR-013**: Multiple checkpoints MUST be able to pulse concurrently and independently, each in its own route's color.

### Key Entities *(include if feature involves data)*

- **Checkpoint (trigger-point)**: A point along a route where a bus pass is detected. Belongs to exactly one route; has a resting visual appearance and, transiently, a pulsing appearance. The route association determines the pulse color.
- **Route**: A transit line with an associated color (sourced from existing route data). Provides the color used when its checkpoints pulse.
- **Bus pass event (crossing)**: The existing signal indicating that a specific bus on a specific route has reached/crossed a specific checkpoint. Carries enough information to identify which checkpoint (and thus which route/color) should pulse. Already used to trigger audio.
- **Bus-visibility setting**: A persisted user preference (boolean) controlling whether bus markers are drawn on the map. Defaults to OFF (hidden). Lives in the existing settings panel alongside audio and checkpoint-visibility settings.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a bus passes a checkpoint, the corresponding checkpoint begins pulsing within a fraction of a second of the pass, perceived as immediate by viewers.
- **SC-002**: In 100% of observed passes (with checkpoints visible), the pulse color matches the route's line color.
- **SC-003**: Each pulse fully resolves back to the resting appearance; across an extended observation session (e.g., 10+ minutes of live traffic), zero checkpoints are left visibly stuck in an enlarged or brightened state.
- **SC-004**: On a first load with no saved preference, zero bus markers are visible (toggle defaults OFF) while routes and checkpoints remain visible and checkpoints continue to pulse on passes.
- **SC-004a**: Toggling bus visibility ON then OFF shows then hides 100% of bus markers without a reload, and the chosen state is correctly restored after reloading the app.
- **SC-005**: A checkpoint does not pulse more than once within the established cooldown window, eliminating rapid flicker on jitter or re-crossing.
- **SC-006**: Multiple checkpoints can be observed pulsing at the same time, each in its own route's color, without visual interference between them.
- **SC-007**: Turning the checkpoint-visibility setting OFF stops all pulses (zero pulses shown on subsequent passes); turning it back ON resumes pulses on passes — with no separate pulse-only control existing anywhere in the UI.

## Assumptions

- **Reuses existing pass detection**: The bus-passes-checkpoint crossing signal already exists (it drives the audio soundscape) and fires per checkpoint pass with the route identity available. This feature consumes that same signal to trigger pulses; it does not add new proximity logic. (The existing detection already includes a cooldown to prevent rapid re-triggering, which this feature inherits.)
- **Route colors already available**: Each route already has an associated color used to draw its line, and that color is already accessible at render time for coloring related markers. Checkpoint pulses reuse that color.
- **Pulse trigger is independent of audio mute**: Audio muting only affects sound; checkpoint pulses are visual and occur on a pass regardless of whether audio is enabled (subject only to checkpoints being visible). This is treated as the default; if a different coupling is desired, it can be revisited.
- **Bus-visibility scope**: Bus visibility applies to the bus markers on the map. The mechanism to show/hide vehicles already exists. Per clarification, this is exposed as a user-toggleable setting in the existing settings panel that defaults to OFF (buses hidden) and is persisted across loads — mirroring the existing audio/checkpoint settings pattern. The setting label/copy follows the existing settings localization pattern (EN only, consistent with prior settings).
- **Pulse styling defaults**: The pulse is a short, single enlarge-and-brighten-then-return animation (on the order of well under a second), tuned to read as a "pulse" without being distracting. Exact duration, size, and easing are tuning details left to planning.
- **Default pulse color**: A neutral highlight color is used for any route lacking a defined color, consistent with how checkpoints are already drawn.
- **Frontend-only**: This is a client/map visualization change; it does not require server or worker changes, since the pass-detection signal and route colors are already delivered to the client.
- **Platform consistency**: Behavior is expected to be consistent across the supported desktop/mobile web views with no platform-specific divergence in scope.
