# Feature Specification: Mobile Map Controls & Wider Default Zoom

**Feature Branch**: `025-mobile-map-controls`  
**Created**: 2026-06-23  
**Status**: Draft  
**Input**: User description: "Mobile improvements. Have Map zoom be wider by default. Enable map zoom and drag controls"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wider map view on first load (Priority: P1)

When a user opens the app, the map presents a wider view of the transit area so more of the network — and more moving vehicles — are visible at a glance without the user needing to zoom out manually first.

**Why this priority**: This is the first thing every user experiences. A too-tight default view forces an immediate manual adjustment and hides the breadth of the transit system. A wider default makes the experience legible from the first frame and is the single most-requested mobile improvement.

**Independent Test**: Open the app fresh (cleared state) on both a phone-sized and desktop-sized viewport and confirm the initial map view shows a noticeably wider area than today, centered on the transit region, with multiple routes/vehicles visible.

**Acceptance Scenarios**:

1. **Given** a user opens the app for the first time, **When** the map finishes loading, **Then** the visible area covers a wider extent of the transit network than the previous default.
2. **Given** a user on a small (phone) viewport, **When** the map loads, **Then** the wider default still keeps the transit region centered and legible (no excessive empty space dominating the screen).
3. **Given** a user opens the app, **When** the map loads at the wider default, **Then** they can still read enough detail to recognize routes and vehicle markers.

---

### User Story 2 - Zoom the map on touch and desktop (Priority: P1)

A user can zoom the map in and out using natural gestures and controls on whatever device they're on: pinch-to-zoom and double-tap on touch devices, scroll/double-click on desktop, and an on-screen zoom control.

**Why this priority**: Without working zoom on mobile, users are stuck at whatever zoom level loads and cannot inspect a specific neighborhood or route. Pinch-to-zoom is currently disabled, which is the core mobile gap this feature closes.

**Independent Test**: On a touch device, pinch outward and inward and confirm the map zooms in and out smoothly; tap the on-screen zoom buttons and confirm each changes the zoom level.

**Acceptance Scenarios**:

1. **Given** a user on a touch device, **When** they pinch two fingers apart, **Then** the map zooms in centered on the pinch.
2. **Given** a user on a touch device, **When** they pinch two fingers together, **Then** the map zooms out.
3. **Given** a user on any device, **When** they use the on-screen zoom-in / zoom-out controls, **Then** the map zoom changes by a consistent step each press.
4. **Given** a user on desktop, **When** they scroll over the map, **Then** the map zooms in/out at the cursor.
5. **Given** a user zooms all the way in or out, **When** they reach the configured limits, **Then** zooming stops at the minimum/maximum bound rather than over-zooming into empty or pixelated views.

---

### User Story 3 - Pan/drag the map on touch and desktop (Priority: P2)

A user can drag the map to move around the transit area — one-finger drag on touch, click-and-drag on desktop — without accidentally rotating or tilting the map.

**Why this priority**: Zoom alone isn't useful without panning to a different part of the network. Panning should already work via defaults, but this story guarantees the mobile drag experience is intentional, smooth, and does not introduce unwanted rotation/tilt.

**Independent Test**: On a touch device, drag one finger across the map and confirm the view follows the finger; confirm two-finger or twist gestures do not rotate or tilt the map.

**Acceptance Scenarios**:

1. **Given** a user on a touch device, **When** they drag one finger across the map, **Then** the visible area moves to follow the drag.
2. **Given** a user on desktop, **When** they click and drag, **Then** the visible area pans with the cursor.
3. **Given** a user performs a rotating or two-finger twist gesture, **When** they release, **Then** the map remains north-up and flat (no rotation or tilt is applied).
4. **Given** the map is auto-following vehicles or fitting a route, **When** the user manually pans or zooms, **Then** their manual interaction is respected rather than being immediately overridden.

---

### Edge Cases

- What happens when a user pinch-zooms past the maximum or minimum zoom limit? Zoom clamps to the bound and does not over-shoot.
- How does the map handle a manual pan/zoom while an automatic camera move (route fit, vehicle follow) is in progress or pending? The most recent user gesture should take precedence and not be yanked back.
- What happens on devices that support both touch and mouse (e.g., touch laptops)? Both interaction modes should work.
- What happens to the existing keyboard/modifier-based pan affordances when standard drag is enabled? They should continue to work or be cleanly superseded without breaking pan.
- How does the wider default interact with users who previously had a saved/last-used view? (See Assumptions — initial view is not persisted today.)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The map MUST open at a wider default zoom level than the current default, showing more of the transit region on first load.
- **FR-002**: The default view MUST remain centered on the transit service area so the wider zoom does not push the network off-screen.
- **FR-003**: Users MUST be able to zoom the map in and out on touch devices using pinch gestures.
- **FR-004**: Users MUST be able to zoom the map in and out on desktop using scroll and/or double-click.
- **FR-005**: The map MUST provide on-screen zoom controls (zoom in / zoom out) usable by tap or click on all devices.
- **FR-006**: Users MUST be able to pan the map by dragging — one-finger drag on touch and click-drag on desktop.
- **FR-007**: The map MUST NOT rotate or tilt in response to user gestures; it MUST stay north-up and flat.
- **FR-008**: Zoom MUST be bounded by a minimum and maximum level so users cannot zoom into empty/over-pixelated views or out beyond a useful extent.
- **FR-009**: Manual user pan/zoom MUST take precedence over automatic camera movements so the user is not fighting the app for control.
- **FR-010**: All interaction improvements MUST work within a phone-sized viewport, not only on desktop.

### Key Entities

- **Map view / camera**: The current center point and zoom level of the map. Has a default initial value (wider zoom, transit-region center) and respects min/max zoom bounds.
- **User interaction state**: Whether the user has manually moved the camera, used to decide precedence over automatic camera moves.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On first load, the visible map area covers a wider geographic extent than the previous default on both phone and desktop viewports.
- **SC-002**: 100% of supported zoom interactions (touch pinch, double-tap, desktop scroll, on-screen buttons) successfully change the zoom level when exercised.
- **SC-003**: A user on a phone can move from the default view to a close-up of any single route in 5 seconds or fewer using gestures alone.
- **SC-004**: No user gesture results in the map rotating or tilting away from north-up/flat orientation.
- **SC-005**: After a manual pan or zoom, the user's chosen view is preserved (not overridden by an automatic camera move) in at least the next interaction cycle.

## Assumptions

- "Wider by default" means a lower (more zoomed-out) initial zoom level than today's default; the exact target value is an implementation tuning decision validated against the success criteria.
- The transit region center used for the default view is unchanged from the current center; only the zoom level widens.
- Map rotation and tilt remain intentionally disabled (consistent with current behavior) — "drag controls" means pan, not rotate/tilt.
- The initial map view is not persisted between sessions today; every load uses the default. Persisting a last-used view is out of scope unless separately requested.
- Existing min/max zoom bounds are retained or lightly adjusted; widening the default must stay within the minimum bound.
- These changes are client/front-end only — no server, worker, or shared-data changes are implied by this feature.
- "Mobile improvements" in this feature is scoped to map zoom/pan controls and the default zoom; other mobile UX concerns (layout, audio, blade) are out of scope.
