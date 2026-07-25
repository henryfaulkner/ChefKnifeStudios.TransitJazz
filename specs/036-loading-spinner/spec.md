# Feature Specification: Loading Spinner

**Feature Branch**: `036-loading-spinner`  
**Created**: 2026-07-03  
**Status**: Draft  
**Input**: User description: "Add loading spinner to app. Should be darkmode reactive by reading local storage. Example spinner: a rotating ring (border-right visible arc, 2s linear infinite spin) inside a square container, with centered 'loading...' text. Translate to project style standards."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a branded loading indicator while the app boots (Priority: P1)

When a visitor opens the app, the client application takes a few seconds to
download and initialize before the map and controls appear. Today they see only
a bare "Loading..." text on a default background. Instead, they should see a
polished animated spinner — a rotating ring with a centered "loading..." label —
so the wait feels intentional and on-brand.

**Why this priority**: This is the entire feature. The spinner is the only
user-visible artifact, and it must appear during the app's cold-start window,
which is the moment it exists to cover.

**Independent Test**: Load the app with a throttled/slow connection (or a cold
cache) and confirm an animated rotating spinner with "loading..." text is shown
continuously until the app UI renders, then disappears.

**Acceptance Scenarios**:

1. **Given** a visitor opens the app on a slow connection, **When** the app is
   still initializing, **Then** an animated rotating spinner and "loading..."
   text are displayed, centered.
2. **Given** the spinner is showing, **When** the app finishes initializing,
   **Then** the spinner is removed and replaced by the app UI with no residual
   overlay.
3. **Given** the app has fully loaded, **When** the visitor uses the app,
   **Then** the spinner is not visible at any point.

---

### User Story 2 - Spinner matches the visitor's saved light/dark preference (Priority: P2)

A returning visitor who previously enabled dark mode expects the whole
experience — including the loading screen — to honor that choice. The spinner's
background and ring/text colors should reflect the saved light-or-dark
preference from the very first frame, before any of the app's own theming code
runs, so there is no jarring flash of the wrong theme.

**Why this priority**: Depends on P1 (there must be a spinner to theme). It is a
polish requirement, not the core deliverable, but it is explicitly requested and
directly affects perceived quality for returning users.

**Independent Test**: Toggle dark mode on in a session, reload the app, and
confirm the spinner appears in dark styling immediately (no light flash);
toggle it off, reload, and confirm the spinner appears in light styling.

**Acceptance Scenarios**:

1. **Given** a visitor previously enabled dark mode, **When** they reload the
   app, **Then** the spinner is shown in dark styling from the first frame.
2. **Given** a visitor previously used light mode (or has never changed the
   setting), **When** they load the app, **Then** the spinner is shown in light
   styling.
3. **Given** a first-time visitor with no saved preference, **When** they load
   the app, **Then** the spinner is shown in the default (light) styling without
   error.

---

### Edge Cases

- **No saved preference / first visit**: No stored setting exists. The spinner
  falls back to the default (light) styling.
- **Corrupt or unreadable saved preference**: The stored value cannot be
  interpreted. The spinner falls back to the default (light) styling rather than
  failing to render.
- **Instant load (warm cache / fast connection)**: The app initializes almost
  immediately. The spinner may appear only briefly or be effectively
  imperceptible; it must still be removed cleanly with no flicker or leftover
  overlay.
- **Visitor changes theme mid-session, then reloads**: On the next load the
  spinner reflects the most recently saved preference.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST display an animated loading spinner during the
  initial application boot/initialization window, before the main UI is ready.
- **FR-002**: The spinner MUST consist of a continuously rotating ring/arc and a
  centered "loading..." text label.
- **FR-003**: The rotation MUST be smooth and continuous (constant-speed,
  looping) for as long as the spinner is shown.
- **FR-004**: The spinner MUST be removed once the app has finished initializing,
  leaving no visible overlay or residue.
- **FR-005**: The spinner's appearance (background, ring, and text colors) MUST
  reflect the visitor's saved light/dark preference, read from persisted local
  browser storage, from the first rendered frame.
- **FR-006**: When no saved preference is present, the spinner MUST fall back to
  the default (light) styling.
- **FR-007**: When the saved preference cannot be read or interpreted, the
  spinner MUST fall back to the default (light) styling without preventing the
  spinner from displaying.
- **FR-008**: The spinner MUST be visually consistent with the project's
  existing style standards (colors/tokens, sizing conventions), rather than
  using the raw hard-coded values from the supplied example.

### Key Entities *(include if feature involves data)*

- **Saved theme preference**: The visitor's persisted light/dark choice, already
  stored in local browser storage as part of the application's settings. The
  spinner reads only the light-vs-dark aspect of it; it does not create or modify
  any settings.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a cold load, the animated spinner is visible within the first
  render and remains until the app UI appears, with no gap showing the old bare
  "Loading..." text.
- **SC-002**: For a visitor with dark mode saved, 100% of reloads show the
  spinner in dark styling on the first frame, with no observable flash of light
  styling.
- **SC-003**: For a first-time visitor (no saved preference) and for a visitor
  with a corrupt saved preference, the spinner still renders correctly in default
  styling in 100% of loads (no blank screen, no error).
- **SC-004**: After the app finishes loading, the spinner is fully removed in
  100% of loads, with no leftover overlay, ring, or "loading..." text.

## Assumptions

- The visitor's light/dark preference is already persisted in local browser
  storage by the existing dark-mode feature; this feature only reads it and does
  not introduce a new storage mechanism.
- The spinner must be able to render and theme itself before the main
  application's theming logic runs (i.e., during the earliest boot phase), which
  is why it reads the saved preference directly rather than through the
  application's runtime settings service.
- Only two visual variants are required: light and dark. The default when
  unknown is light, mirroring the app's default theme.
- The "loading..." label text is fixed for this feature; localization of that
  label is out of scope (consistent with prior features deferring translations).
- The spinner covers the application's own boot/initialization window; it is not
  a general-purpose loading indicator for later in-app operations.
