# Feature Specification: Map Style Toggle

**Feature Branch**: `017-map-style-toggle`
**Created**: 2026-06-14
**Status**: Draft
**Input**: User description: "look at appsettings.Development.json. I want to use the StyleUrls object to select the project's map display. I want to use LightOff by default. I want to be able to hot-switch between LightOff and LightOn. I want a boolean setting added to the SettingBlade to enable the toggle feature. I want to use the maptiler's mapStyle.setStyle method to achieve this."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Default map display uses the "LightOff" style (Priority: P1)

A user opens the app for the first time (or with no saved preference) and the map renders using the "LightOff" basemap style. They did not have to choose anything — the calmer "lights off" presentation is what the app shows by default.

**Why this priority**: The default presentation is the baseline experience every user sees on first load. It must be correct before any toggle behavior matters, and it is independently demonstrable simply by loading the app.

**Independent Test**: Clear any saved preference, load the app, and confirm the map renders with the "LightOff" basemap (not the previous default style). Delivers value because the app presents the intended default look without any user action.

**Acceptance Scenarios**:

1. **Given** a user with no saved map-style preference, **When** the app loads and the map first renders, **Then** the map uses the "LightOff" basemap style.
2. **Given** the app has loaded with the default style, **When** all transit data (routes, buses, checkpoints) is plotted, **Then** that data is visible over the "LightOff" basemap.

---

### User Story 2 - Hot-switch between "LightOff" and "LightOn" from the settings panel (Priority: P1)

A user opens the settings panel, finds an on/off control for the map style, and toggles it. The map immediately changes between the "LightOff" and "LightOn" basemap presentations without reloading the page. All transit data already on the map (route lines, buses, checkpoints) remains present after the switch.

**Why this priority**: This is the core of the feature — the ability to switch the map display on demand. Without it, the only outcome is a changed default, which is a fraction of the requested value.

**Independent Test**: Open the settings panel, toggle the map-style control, and observe the basemap change between the two presentations instantly with no page reload, with all plotted transit data still showing afterward. Toggle back and confirm it returns.

**Acceptance Scenarios**:

1. **Given** the settings panel is open and the map is showing the default ("LightOff") style, **When** the user toggles the map-style control on, **Then** the map switches to the "LightOn" basemap immediately, with no page reload.
2. **Given** the map is showing the "LightOn" style, **When** the user toggles the map-style control off, **Then** the map switches back to the "LightOff" basemap immediately.
3. **Given** route lines, buses, and checkpoints are currently plotted on the map, **When** the user toggles the map style, **Then** all of that transit data remains visible after the basemap changes (no data disappears and nothing must be re-fetched).
4. **Given** the settings panel renders its list of on/off controls, **When** it is opened, **Then** it includes a clearly labeled control for the map style alongside the existing settings.

---

### User Story 3 - Chosen map style persists across sessions (Priority: P2)

A user who has switched the map style expects that choice to still be in effect the next time they return to the app, without having to set it again.

**Why this priority**: Persistence makes the toggle a durable preference rather than a per-visit novelty. It builds on Stories 1 and 2 and matches the persistence behavior of the other settings, but the feature still delivers value for a single session without it.

**Independent Test**: Toggle the map style, reload the app, and confirm the map opens in the previously chosen style and the settings control reflects that state.

**Acceptance Scenarios**:

1. **Given** the user has toggled the map style to "LightOn", **When** the user reloads the app, **Then** the map renders in "LightOn" from first display and the settings control shows the "on" state.
2. **Given** the user has toggled the map style back to "LightOff", **When** the user reloads the app, **Then** the map renders in "LightOff" and the settings control shows the "off" state.

---

### Edge Cases

- **Toggle while a basemap switch is mid-flight**: Rapid repeated toggling must leave the visible basemap and the stored preference consistent with the final toggle state; intermediate switches must not leave the map in a torn state or drop transit data.
- **Transit data re-application after a style change**: Switching the basemap replaces the underlying map presentation; the plotted transit data layers (routes, buses, checkpoints) must be re-applied to the new presentation rather than re-fetched, and must respect any current visibility settings (e.g., if checkpoints are hidden, they stay hidden after the switch).
- **Style switch before the map is fully ready**: If a switch is requested before the map has finished its initial load, the request must either be deferred until the map is ready or safely ignored, never erroring or leaving a blank map.
- **Missing or misconfigured style entry**: If the configured style entry for the chosen state is absent, the map must remain on its current valid style rather than going blank.
- **Configured style set includes more than two entries**: The available configuration lists additional presentations (e.g., dark variants); this feature only switches between the two named states and must not be affected by the presence of the extra entries.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST render the map using the "LightOff" basemap style by default when the user has no saved map-style preference.
- **FR-002**: The system MUST source both the "LightOff" and "LightOn" basemap presentations from the project's existing map-style configuration (the configured set of named style entries).
- **FR-003**: The settings panel MUST present a single, clearly labeled on/off control for the map style, rendered alongside the other available settings.
- **FR-004**: When the user toggles the map-style control on, the system MUST switch the basemap to the "LightOn" presentation; when toggled off, it MUST switch the basemap to the "LightOff" presentation.
- **FR-005**: The basemap switch MUST take effect immediately (hot-switch), with no page reload.
- **FR-006**: After a basemap switch, the system MUST keep all currently plotted transit data (route lines, buses, and checkpoints) visible by re-applying those data layers to the new basemap; it MUST NOT re-fetch that data to do so.
- **FR-007**: After a basemap switch, the re-applied transit data layers MUST respect the current visibility settings (e.g., checkpoint visibility) rather than resetting them.
- **FR-008**: The system MUST persist the user's chosen map-style state so it survives an app reload.
- **FR-009**: On app load with a saved map-style preference, the system MUST render the map in the saved style from first display and reflect that state in the settings control.
- **FR-010**: On first read with no saved map-style preference, the system MUST seed and persist the default ("LightOff") state, consistent with how the other settings seed their defaults.
- **FR-011**: The map-style control MUST be wired so that the rest of the app is notified of the change and the map reacts, using the same in-app notification mechanism as the other settings (i.e., the toggle is decoupled from the map).
- **FR-012**: The map-style control's label MUST be sourced from the app's existing localized string resources, consistent with the other settings' labels (no hardcoded inline copy).
- **FR-013**: If the configured style entry for a requested state is missing, the system MUST leave the map on its current valid style rather than rendering a blank map.

### Key Entities *(include if feature involves data)*

- **Map-Style Preference**: A user-adjustable on/off preference representing which of the two named basemap presentations ("LightOff" when off, "LightOn" when on) is active. Has a stable identity, a localized human-readable label, a default of off ("LightOff"), and a current boolean value persisted alongside the other settings as part of the single settings unit in local browser storage.
- **Named Style Set**: The project's configured collection of named basemap style entries (including, at minimum, "LightOff" and "LightOn"). The source of truth for which presentation each preference state maps to; may contain additional entries that this feature does not use.
- **Plotted Transit Data**: The route, bus, and checkpoint layers currently displayed on the map. Independent of the basemap presentation and must be preserved (re-applied, not re-fetched) across a basemap switch, honoring current visibility settings.
- **Map-Style-Changed Signal**: The in-app notification broadcast when the preference changes, used to decouple the settings control from the map so the map can react and switch its basemap.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With no saved preference, the app opens with the "LightOff" basemap in 100% of fresh loads.
- **SC-002**: A user can open the settings panel, toggle the map style, and see the basemap change in under 5 seconds without instruction.
- **SC-003**: Toggling the map style changes the basemap with no page reload, perceived as immediate (within one render/transition cycle).
- **SC-004**: After a basemap switch, 100% of the transit data that was visible beforehand (routes, buses, and visible checkpoints) remains visible afterward, with no loss of data and no re-fetch.
- **SC-005**: A map-style choice made by the user is preserved across an app reload in 100% of attempts, with the map opening in the chosen style and no visible flash of the wrong style.
- **SC-006**: Repeatedly toggling the map style leaves the visible basemap and the stored preference consistent with the final toggle on every attempt.

## Assumptions

- **Two named states only**: This feature switches strictly between the two configured presentations named "LightOff" and "LightOn". The configuration also defines additional presentations (e.g., dark variants); selecting among those is out of scope and reserved for a possible future setting.
- **Boolean control**: The map style is presented as a single on/off toggle (off = "LightOff" default, on = "LightOn"), consistent with the existing boolean-only settings model. A multi-option selector is out of scope.
- **Built on the existing settings panel**: This feature adds one setting to the already-shipped settings panel and reuses its persistence, defaulting, notification, and localized-label mechanisms; it does not introduce a new settings surface.
- **Persistence mechanism**: The chosen style is stored with the other settings in per-browser local storage as a single unit, matching the existing settings behavior.
- **Data preservation pattern**: The transit data layers are re-applied to the new basemap after the switch rather than re-fetched, consistent with the project's cartography principle that data layers persist across basemap changes.
- **Default rationale**: "LightOff" is the default per the explicit user request, replacing the prior single fixed style as the app's out-of-the-box map presentation.
