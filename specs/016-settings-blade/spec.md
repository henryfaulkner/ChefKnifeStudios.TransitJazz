# Feature Specification: Settings Blade

**Feature Branch**: `016-settings-blade`
**Created**: 2026-06-13
**Status**: Draft
**Input**: User description: "docs/SETTINGS_BLADE_DESIGN_DOCUMENT.md — a slide-out settings panel (blade) triggered by a floating action button, rendering a checkbox per boolean application setting, persisting changes to local storage, applying a dark-mode theme change immediately, and broadcasting the change so the rest of the app re-themes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open and adjust application settings (Priority: P1)

A user wants to change how the app behaves — for example, turning the soundscape audio on or off, or switching to a darker visual theme that is easier on the eyes. They tap a floating gear button visible on screen, a panel slides in from the right showing the available on/off settings, they toggle the ones they want, and the panel can be dismissed by tapping the close control or anywhere outside the panel.

**Why this priority**: This is the core of the feature — without the ability to open a panel and toggle a setting, nothing else matters. It is the minimum that delivers user value (control over app behavior) and is independently demonstrable.

**Independent Test**: Tap the gear button, confirm the panel slides in showing a labeled toggle for each setting, toggle one, then close the panel via the close control and via an outside tap. Delivers value because the user can read and change every available setting.

**Acceptance Scenarios**:

1. **Given** the app is open and the gear button is visible, **When** the user taps the gear button, **Then** the settings panel slides in from the right edge of the screen.
2. **Given** the settings panel is open, **When** the user taps the close (✕) control, **Then** the panel slides out and is dismissed.
3. **Given** the settings panel is open, **When** the user taps anywhere outside the panel, **Then** the panel slides out and is dismissed.
4. **Given** the gear button is tapped to open the panel, **When** that same opening tap propagates, **Then** the panel does NOT immediately re-close (it stays open).
5. **Given** the settings panel is open, **When** it renders, **Then** it shows exactly one labeled on/off control per available boolean setting, each labeled with a human-readable description.

---

### User Story 2 - Settings persist across sessions (Priority: P1)

A user who has configured their preferences expects those choices to still be in effect the next time they return to the app, without having to set them again.

**Why this priority**: A settings panel that forgets every choice on reload provides little value; persistence is essential to the feature being useful rather than a novelty. It is tightly coupled to P1 in importance.

**Independent Test**: Toggle a setting, reload the app, reopen the panel, and confirm the toggled state is preserved.

**Acceptance Scenarios**:

1. **Given** the user has toggled a setting to a new state, **When** the user reloads the app and reopens the settings panel, **Then** the setting reflects the previously chosen state.
2. **Given** a brand-new user with no stored preferences, **When** the settings panel is opened for the first time, **Then** each setting shows its default state and those defaults are recorded so subsequent reads are consistent.

---

### User Story 3 - Dark mode applies immediately and app-wide (Priority: P2)

A user toggles the dark-mode setting and expects the entire interface — not just the settings panel — to switch to the dark theme right away, without reloading. On the next visit, the app should open already in the previously chosen theme.

**Why this priority**: Dark mode is the one setting with an immediate, visible, app-wide effect and the highest expectation of "instant" feedback. It is a distinct, higher-effort slice on top of the generic toggle behavior, so it is separated as P2.

**Independent Test**: Toggle dark mode and observe the whole interface re-theme instantly with no reload; reload and confirm the app opens in the chosen theme.

**Acceptance Scenarios**:

1. **Given** the app is in light theme, **When** the user toggles dark mode on, **Then** the entire interface (panel and surrounding app) switches to the dark theme immediately, with no page reload.
2. **Given** the app is in dark theme, **When** the user toggles dark mode off, **Then** the entire interface switches back to the light theme immediately.
3. **Given** the user previously enabled dark mode, **When** the user reloads the app, **Then** the app renders in the dark theme from first paint, before any interaction.

---

### Edge Cases

- **Opening tap re-close**: The tap that opens the panel must not be interpreted as an "outside tap" that immediately closes it. A short minimum-open guard (≈300 ms) prevents this race.
- **Outside-tap listener cleanup**: When the panel or the screen hosting it is torn down (e.g., navigating away), the outside-tap listener and any event subscriptions must be removed so they do not accumulate or leak across navigations.
- **Theme on cold load with no stored preference**: A first-time user with no saved theme defaults to light theme consistently across both the visual styling and any themed components.
- **Non-boolean settings**: Only on/off (boolean) settings are presented as toggles. Any future non-boolean setting must not be silently rendered as an unchecked toggle (it would misrepresent the value); such settings are out of scope for this feature.
- **Rapid repeated toggling**: Toggling a setting several times quickly must leave the stored value and the visible state consistent with the final toggle.
- **Multiple panels**: Only a single settings panel exists app-wide; the design must not depend on placing more than one settings panel on screen at once.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST display a persistent on-screen trigger control (a floating gear button) from which the user can open the settings panel.
- **FR-002**: The system MUST present the settings panel as a panel that slides in from the right edge of the screen when opened and slides out when dismissed.
- **FR-003**: The settings panel MUST render exactly one on/off control for each available boolean application setting, each labeled with a human-readable description of that setting.
- **FR-004**: Users MUST be able to dismiss the settings panel by activating an explicit close control within the panel.
- **FR-005**: Users MUST be able to dismiss the settings panel by interacting outside the panel's bounds.
- **FR-006**: The system MUST prevent the action that opened the panel from immediately closing it, by enforcing a minimum-open interval (≈300 ms) before an outside interaction can dismiss it.
- **FR-007**: When a user toggles a setting, the system MUST persist the new value so it survives an app reload.
- **FR-008**: On first read with no stored preferences, the system MUST seed and persist default values for all settings.
- **FR-009**: When the dark-mode setting is toggled, the system MUST apply the corresponding theme to the entire interface immediately, without a reload.
- **FR-010**: When the dark-mode setting is toggled, the system MUST notify other parts of the interface so they re-theme themselves consistently with the new setting.
- **FR-011**: On app load, the system MUST apply the persisted theme before the user interacts, so the app renders in the previously chosen theme from first paint.
- **FR-012**: The system MUST clean up the outside-interaction listener and any event subscriptions when the panel or its host is torn down, so listeners and handlers do not accumulate across navigations.
- **FR-013**: The settings panel MUST be available from anywhere in the app where the trigger control is shown (i.e., hosted once globally rather than re-created per screen).
- **FR-014**: The panel MUST present, at minimum, an audio/soundscape on-off toggle and a dark-mode on-off toggle; the set of settings shown is driven by the available boolean application settings (see Assumptions).

### Key Entities *(include if feature involves data)*

- **Application Settings**: The set of user-adjustable on/off preferences (e.g., audio enabled, dark mode enabled). Each setting has a stable identity, a human-readable label/description, and a current boolean value. Persisted as a single unit in local browser storage.
- **Theme State**: The currently active visual theme (light or dark), derived from the dark-mode setting. Drives both the panel's own styling and the surrounding app's styling, and must be consistent between the two.
- **Settings-Open Signal / Theme-Changed Signal**: In-app notifications used to (a) open the panel from the trigger control and (b) broadcast that the theme changed so other components re-theme. These are the decoupling mechanism between the trigger, the panel, and the rest of the app.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a closed state, a user can open the settings panel, change at least one setting, and dismiss the panel in under 10 seconds without instruction.
- **SC-002**: 100% of the available boolean settings appear in the panel as labeled toggles whose visible state matches their stored value.
- **SC-003**: A setting toggled by the user is preserved across an app reload in 100% of attempts (no lost changes).
- **SC-004**: Toggling dark mode re-themes the entire visible interface within one render cycle (perceived as instant, no reload), and a reload opens the app in the chosen theme with no visible flash of the wrong theme.
- **SC-005**: The opening interaction never closes the panel; outside-interaction dismissal works on every attempt after the panel has been open for at least the minimum-open interval.
- **SC-006**: After repeatedly opening, dismissing, and navigating around the app, no duplicate outside-interaction listeners or event handlers accumulate (dismissal and theme behavior remain single-fire, not multiplied).

## Assumptions

- **Platform**: The app is a single-session browser application; per-session in-browser local storage is the persistence mechanism, and a single shared in-app notification bus connects the trigger, panel, and layout. (Derived from the reference design document and the existing project.)
- **Settings are boolean-only** for this feature. Non-boolean preferences are out of scope; if added later they require a different control and are explicitly excluded here.
- **Settings shown**: At minimum an audio/soundscape toggle and a dark-mode toggle. The reference document also lists an "Always Show App Tour" toggle from the source application; whether TransitJazz includes that (or other app-specific toggles) is to be confirmed during planning against the project's actual settings model. The feature does not depend on any particular setting beyond audio and dark mode.
- **Trigger placement**: A single floating gear trigger is sufficient; an elaborate multi-button stack ("FAB list") and any sibling buttons (e.g., a help button) are optional and out of scope for the minimum feature.
- **No gameplay context**: The reference document's "LEAVE GAME" button and its multiplayer/solo navigation and leave-game API calls are specific to the source application (a card game) and are **out of scope** for TransitJazz. They are omitted from this specification.
- **Theme scope**: "Dark mode" toggles a light/dark visual theme across the whole app; the exact color palette is an implementation detail to be supplied during planning and is not specified here.
- **Default values**: Audio/soundscape defaults to on; dark mode defaults to off. (Reasonable defaults consistent with the reference document; adjust if the project requires otherwise.)
- **Single panel instance**: Exactly one settings panel exists at a time, hosted once in the app's main layout.
