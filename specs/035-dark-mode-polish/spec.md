# Feature Specification: Dark Mode Polish

**Feature Branch**: `035-dark-mode-polish`
**Created**: 2026-07-03
**Status**: Draft
**Input**: User description: "The Audio FAB is no longer visible (move it left btw info and darkmode). Flap the icons used on the Darkmode FAB. When light mode is enabled, show sun icon. When dark mode is enabled, show moon icon. You need to adjust both the AudioUnlockOverlay and the InfoOverlay to respond to darkmode. The TransitRunningLabel should respond to darkmode. The Routes filter is not responding to darkmode."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Audio FAB Repositioning (Priority: P1)

A user taps the Audio FAB to mute or unmute transit sounds. After the 034 dark-mode FAB was inserted, the Audio FAB is now hidden because it occupies the same position (right: 124px) as the newly shifted MapStyle FAB. The Audio FAB must be moved further left so all FABs in the bottom row are visible and clickable.

**Why this priority**: The Audio FAB being invisible/unreachable is a regression — users cannot mute or unmute the transit soundscape.

**Independent Test**: Open the app and confirm the Audio FAB is visible and tappable without overlapping any other FAB.

**Acceptance Scenarios**:

1. **Given** the app is loaded with all FABs rendered, **When** the user looks at the bottom-right FAB row, **Then** the Audio FAB, DarkMode FAB, MapStyle FAB, Info FAB, and City FAB are each visible with no overlap.
2. **Given** the Audio FAB is visible, **When** the user taps it, **Then** audio mutes or unmutes as expected.

---

### User Story 2 — Dark Mode FAB Icons (Priority: P1)

A user taps the dark-mode FAB to toggle between light and dark modes. The icon on the FAB must communicate the current state: a sun icon when light mode is active (indicating that tapping will switch to dark), and a moon icon when dark mode is active (indicating that tapping will switch to light).

**Why this priority**: Correct icon semantics are fundamental UX — a sun-when-light / moon-when-dark convention is the platform standard users expect, and the current "light_mode"/"dark_mode" Material icon names implement this backwards.

**Independent Test**: Toggle dark mode on and off; verify the icon shown at each state matches the convention above.

**Acceptance Scenarios**:

1. **Given** the app is in light mode, **When** the user looks at the DarkMode FAB, **Then** a sun icon is displayed.
2. **Given** the app is in dark mode, **When** the user looks at the DarkMode FAB, **Then** a moon icon is displayed.
3. **Given** dark mode is toggled, **When** the page reloads with the dark setting persisted, **Then** the correct icon is shown on first render.

---

### User Story 3 — AudioUnlockOverlay Dark Mode (Priority: P2)

When the app loads for the first time (or audio is not yet unlocked), an AudioUnlockOverlay covers the screen. In dark mode this overlay must use dark styling — dark background, light text — so it is visually consistent with the rest of the app.

**Why this priority**: An all-white overlay jarring against a dark map and chrome is a visual regression that breaks immersion.

**Independent Test**: Enable dark mode, then reload the app (or simulate a fresh session) so the AudioUnlockOverlay appears; verify it renders with dark background and appropriate text contrast.

**Acceptance Scenarios**:

1. **Given** dark mode is enabled and the AudioUnlockOverlay is shown, **When** the user views the overlay, **Then** the background is dark and text is light.
2. **Given** light mode is enabled and the AudioUnlockOverlay is shown, **When** the user views the overlay, **Then** the background is light and text is dark (unchanged from current behavior).

---

### User Story 4 — InfoOverlay Dark Mode (Priority: P2)

The Info FAB opens an informational overlay panel. In dark mode this overlay must use dark styling consistent with the rest of the app.

**Why this priority**: Same visual consistency concern as the AudioUnlockOverlay — a bright overlay against a dark app is jarring.

**Independent Test**: Enable dark mode, tap the Info FAB, verify the overlay appears with dark background and legible light text.

**Acceptance Scenarios**:

1. **Given** dark mode is enabled, **When** the user taps the Info FAB, **Then** the info overlay renders with a dark background and light-colored text and controls.
2. **Given** light mode is enabled, **When** the user taps the Info FAB, **Then** the overlay renders light (unchanged from current behavior).
3. **Given** the user toggles dark mode while the Info overlay is open, **When** the mode switches, **Then** the overlay updates its appearance without requiring a close-reopen.

---

### User Story 5 — TransitRunningLabel Dark Mode (Priority: P2)

The TransitRunningLabel (showing e.g. "12 buses running") is visible on the map. In dark mode it must use dark-appropriate colors — dark background or transparent with light text — so it is readable against the dark basemap.

**Why this priority**: Unreadable labels defeat their purpose; light text on dark map is the correct contrast pair.

**Independent Test**: Enable dark mode, observe the TransitRunningLabel; verify text and background are legible.

**Acceptance Scenarios**:

1. **Given** dark mode is enabled, **When** the user views the TransitRunningLabel, **Then** the label text is legible against the dark basemap with appropriate contrast.
2. **Given** the user toggles dark mode, **When** the mode switches, **Then** the label updates its appearance immediately without a page reload.

---

### User Story 6 — Route Filter Dark Mode (Priority: P2)

The Route Filter panel (listing routes with checkboxes/toggles) is not responding to dark mode. In dark mode the panel must use dark background, light text, and dark-appropriate control styling so it is visually consistent.

**Why this priority**: The Route Filter is a primary interaction surface; a white panel against a dark app is a strong visual inconsistency.

**Independent Test**: Enable dark mode, open the Route Filter panel, verify the panel background and all text/controls are styled for dark mode.

**Acceptance Scenarios**:

1. **Given** dark mode is enabled, **When** the user opens the Route Filter panel, **Then** the panel renders with a dark background and light text.
2. **Given** dark mode is enabled, **When** the user views route filter checkboxes/toggles, **Then** the controls are legible and appropriately styled for dark mode.
3. **Given** the user toggles dark mode while the Route Filter is open, **When** the mode switches, **Then** the panel updates its appearance without requiring close-reopen.

---

### Edge Cases

- What happens when the user rapidly toggles dark mode multiple times — do overlays and panels reflect the final state correctly?
- If the persisted dark-mode setting is `true` on first load, do ALL of the above surfaces (overlay, label, filters) render dark from first paint rather than flashing light then switching?
- Does toggling dark mode while the AudioUnlockOverlay is actively visible update it in real time?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Audio FAB MUST be repositioned so it does not overlap any other FAB in the bottom row after the DarkMode FAB was inserted.
- **FR-002**: The DarkMode FAB MUST display a sun icon when the app is in light mode.
- **FR-003**: The DarkMode FAB MUST display a moon icon when the app is in dark mode.
- **FR-004**: The AudioUnlockOverlay MUST apply dark styling (dark background, light text) when dark mode is enabled.
- **FR-005**: The AudioUnlockOverlay MUST apply light styling when light mode is enabled (preserving current behavior).
- **FR-006**: The InfoOverlay MUST apply dark styling (dark background, light text/controls) when dark mode is enabled.
- **FR-007**: The InfoOverlay MUST apply light styling when light mode is enabled (preserving current behavior).
- **FR-008**: The TransitRunningLabel MUST apply dark-appropriate text and background colors when dark mode is enabled.
- **FR-009**: The Route Filter panel MUST apply dark styling (dark background, light text, legible controls) when dark mode is enabled.
- **FR-010**: All dark/light style transitions MUST take effect immediately when the user toggles dark mode, without a page reload.
- **FR-011**: When the app loads with dark mode persisted as active, ALL affected surfaces MUST render in dark mode from first paint (no flash of light).
- **FR-012**: The close/dismiss behavior of the AudioUnlockOverlay, InfoOverlay, and Route Filter panel MUST be unaffected by this change.

### Key Entities

- **DarkMode FAB**: Bottom-row FAB controlling the dark mode toggle; icon reflects current mode state.
- **AudioUnlockOverlay**: Full-screen overlay shown before audio is unlocked; must respond to dark/light mode.
- **InfoOverlay**: Panel overlay opened by the Info FAB; must respond to dark/light mode.
- **TransitRunningLabel**: On-map label displaying vehicle counts; must respond to dark/light mode.
- **RouteFilters**: Side panel listing transit routes with selection controls; must respond to dark/light mode.
- **FAB row layout**: The ordered set of mini FABs in the bottom-right corner; positions must be conflict-free after Audio FAB move.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All five bottom-row FABs (City, DarkMode, MapStyle, Audio, Info in layout order) are simultaneously visible and tappable with no overlap on a standard mobile viewport.
- **SC-002**: The DarkMode FAB icon correctly reflects the active mode (sun = light, moon = dark) in 100% of tested states including first paint after page reload.
- **SC-003**: Every UI surface listed in FR-004 through FR-009 renders with correct dark or light styling within one render cycle of toggling dark mode (no perceptible flash or delay).
- **SC-004**: When dark mode is persisted and the app is reloaded, zero light-mode flashes are observed on any dark-mode-aware surface before the UI settles.
- **SC-005**: Text contrast in dark mode across all affected surfaces meets the minimum legibility standard (text is comfortably readable against its background without strain).

## Assumptions

- The existing `ThemeChangedEventArgs` event bus mechanism (introduced in 034) is the correct channel for propagating dark mode state to components that are not the Settings blade.
- Components that currently use hardcoded light CSS colors (e.g., `#fff`, `#888`, `rgba(0,0,0,...)`) will receive dark-mode overrides scoped to their own stylesheets rather than a global CSS variable refactor.
- The AudioUnlockOverlay is shown once per session (before audio unlock); it does not need to live-update if the user toggles dark mode while the overlay is open, though doing so is acceptable.
- The InfoOverlay and RouteFilters live-update on dark mode toggle (they remain open during normal use).
- No new dependencies or interop modules are required — existing CSS class toggling, event subscription, or MDC theme variable flow covers all cases.
- Mobile viewport (small screen) is the primary target; desktop layout is not in scope for this feature.
- Spanish (`.es`) localization is out of scope per the deferred pattern established in 015/016.
