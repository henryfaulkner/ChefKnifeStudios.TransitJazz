# Feature Specification: Compact FAB Controls

**Feature Branch**: `026-compact-fab-controls`  
**Created**: 2026-06-23  
**Status**: Draft  
**Input**: User description: "Add smaller audio and map MatBlazor FAB buttons. Deprecate settings blade."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toggle audio on/off via dedicated FAB button (Priority: P1)

A user wants to quickly mute or unmute the transit soundscape without navigating into a settings panel. They tap a dedicated audio FAB button that is always visible on screen, and the audio toggles immediately with a clear visual indicator of the current state (audio on vs. muted).

**Why this priority**: Audio toggle is the most-frequently used setting. A dedicated FAB gives one-tap access from anywhere, reducing the journey from two taps (open settings + toggle) to one. This is the primary value of the feature.

**Independent Test**: Tap the audio FAB and confirm the soundscape stops/starts immediately and the icon changes to reflect the new state; tap again and confirm it reverts.

**Acceptance Scenarios**:

1. **Given** the audio FAB is visible on screen, **When** the user taps it while audio is currently enabled, **Then** audio stops immediately and the FAB icon changes to a muted state.
2. **Given** the audio FAB is visible on screen, **When** the user taps it while audio is currently muted, **Then** audio resumes immediately and the FAB icon changes to an unmuted state.
3. **Given** the app is loaded for the first time with audio enabled by default, **When** the user looks at the audio FAB, **Then** the FAB shows the unmuted icon.
4. **Given** the user toggles audio via the FAB, **When** the app is reloaded, **Then** the persisted audio setting is still reflected in the FAB's icon state.

---

### User Story 2 - Toggle map style via dedicated FAB button (Priority: P1)

A user wants to switch between the street-map and dark/canvas basemap style with one tap, without opening a settings panel. They tap a dedicated map-style FAB button that is always visible on screen, and the basemap switches immediately with a clear visual indicator of which style is active.

**Why this priority**: Map style toggle is the second-most-frequently used setting. Like audio, a dedicated FAB eliminates the two-tap settings panel workflow and makes the toggle discoverable and instant.

**Independent Test**: Tap the map-style FAB and confirm the basemap switches between street map and dark canvas; tap again and confirm it reverts; icon reflects the current style.

**Acceptance Scenarios**:

1. **Given** the map-style FAB is visible on screen, **When** the user taps it while the street map is active, **Then** the basemap switches to the dark/canvas style and the icon changes to indicate the dark style.
2. **Given** the map-style FAB is visible on screen, **When** the user taps it while the dark/canvas style is active, **Then** the basemap switches to the street map style and the icon changes to indicate the street style.
3. **Given** the app is loaded for the first time with the dark/canvas style active by default, **When** the user looks at the map-style FAB, **Then** the FAB shows the dark-style icon.
4. **Given** the user toggles the map style via the FAB, **When** the app is reloaded, **Then** the persisted style setting is still reflected in the FAB's icon state.

---

### User Story 3 - Settings blade is deprecated and removed (Priority: P2)

The settings blade that previously housed the audio and map-style checkboxes is no longer shown, since both settings are now exposed via dedicated FAB buttons. The blade code is removed so it no longer appears in the UI, and any remaining dead code paths (event handlers, services specific to the blade) are cleaned up.

**Why this priority**: Removing a deprecated UI component reduces maintenance overhead, eliminates dead code, and simplifies the layout. However, it can only be done after the FABs are working (stories 1 and 2), making it P2.

**Independent Test**: Open the app and confirm no settings blade slides in when tapping any area or control other than the new FABs; verify the gear FAB and blade-related components are no longer present in the layout.

**Acceptance Scenarios**:

1. **Given** the new audio and map-style FABs are in place, **When** the user opens the app, **Then** no settings blade or gear FAB is present in the UI.
2. **Given** the settings blade code has been removed, **When** the user taps any area on screen, **Then** no slide-out panel appears (confirming the blade is gone).
3. **Given** the blade is removed, **When** the user toggles audio or map style via the new FABs, **Then** the setting takes effect and persists correctly (no regression).

---

### Edge Cases

- **Rapid FAB toggling**: Tapping either FAB rapidly multiple times must leave the setting in a consistent state matching the final tap.
- **Audio context restriction (mobile)**: On mobile browsers that require a user gesture to unlock the Web Audio API, the first audio FAB tap must also serve as the audio context unlock — audio may not start until the first tap, but subsequent taps should work immediately.
- **FAB overlap on small viewports**: The two FABs must not overlap each other or the existing map zoom controls; they should be positioned with adequate spacing on the smallest supported viewport.
- **Deprecation cleanup completeness**: Removing the settings blade must not break the audio or map-style toggle pathways — the underlying settings model, persistence, and event broadcasting should remain intact and only the blade UI component is removed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST display a persistent audio-toggle FAB button on screen at all times.
- **FR-002**: The system MUST display a persistent map-style-toggle FAB button on screen at all times.
- **FR-003**: Each FAB button MUST be visually smaller than the existing default-sized SettingsFab; users must perceive them as compact, unobtrusive controls.
- **FR-004**: Each FAB button MUST display an icon that clearly communicates its function (e.g., speaker for audio, map/layers for map style).
- **FR-005**: Each FAB button MUST visually indicate its current state (on/off, which style is active) through its icon, color, or both.
- **FR-006**: Tapping the audio FAB MUST toggle audio on/off and fire the same `AudioSettingChanged` effect as the current settings blade.
- **FR-007**: Tapping the map-style FAB MUST toggle the basemap style and fire the same `GisSettingChanged` effect as the current settings blade.
- **FR-008**: The audio FAB's initial icon state MUST reflect the persisted audio setting on app load.
- **FR-009**: The map-style FAB's initial icon state MUST reflect the persisted map-style setting on app load.
- **FR-010**: The two FABs MUST be positioned so they do not overlap each other or the existing map zoom controls at any supported viewport size.
- **FR-011**: The settings blade (`SettingsBlade`) and its trigger (`SettingsFab`) MUST be removed from the layout, and their related event wiring (opening/closing blade) MUST be cleaned up.
- **FR-012**: Removing the settings blade MUST NOT remove or alter the underlying settings persistence, the `Settings` model, or the effect event broadcasting for audio and map-style toggles.
- **FR-013**: The deprecated blade components (`BladeContainer`, `SettingsBlade`, `SettingsFab`) and their associated code should be removed from the codebase to eliminate dead code.

### Key Entities

- **Audio FAB button**: A floating action button that toggles the audio-on/off setting. Shows an unmuted or muted icon based on the current persisted state. Fires `AudioSettingChanged` events when tapped.
- **Map-Style FAB button**: A floating action button that toggles between street-map and dark/canvas basemap styles. Shows an icon reflecting the active style. Fires `GisSettingChanged` events when tapped.
- **Application Settings**: The set of persisted boolean preferences (`IsAudioEnabled`, `IsStreetMapEnabled`). These are unchanged from the existing settings model and continue to be persisted in local storage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can toggle audio on and off with a single tap on the audio FAB, perceiving the change within 500ms, without any intermediate navigation or menu.
- **SC-002**: A user can toggle the map basemap style with a single tap on the map-style FAB, perceiving the change within 1 second, without any intermediate navigation or menu.
- **SC-003**: Each FAB occupies no more than 44×44 CSS pixels (the minimum recommended touch target) and is visually at least 25% smaller than the previous default SettingsFab.
- **SC-004**: On the smallest supported mobile viewport (320px wide), the two FABs plus the existing zoom controls are all visible and non-overlapping.
- **SC-005**: No settings blade or gear FAB appears in the UI after the deprecation is applied.
- **SC-006**: All existing settings (audio, map style) continue to persist correctly across app reloads after the blade is removed — zero regression in persistence behavior.
- **SC-007**: The total number of taps to toggle audio or map style is reduced from 2 (open blade + toggle checkbox) to 1 (tap dedicated FAB).

## Assumptions

- **FAB size**: "Smaller" means using MatBlazor's mini FAB variant or a comparable reduced size that is unobtrusive while remaining tappable. The existing SettingsFab uses default size; the new FABs will use the smallest available variant.
- **FAB positioning**: The audio and map-style FABs will be placed in a stack at the bottom-right or bottom-left area, positioned such that they do not interfere with the existing MapLibre zoom controls (bottom-left). Exact positioning is a layout decision confirmed during planning.
- **Persistence unchanged**: The existing `Settings` model and its local-storage persistence mechanism remain untouched. Only the UI trigger (blade) is removed; the settings storage and event broadcasting survive intact.
- **Dead code removal**: The `BladeContainer`, `SettingsBlade`, and `SettingsFab` components and their associated CSS, event args, and service wiring can be safely removed once the FABs are verified to work. This is a cleanup activity performed after stories 1 and 2 are complete.
- **Icons**: Existing MatBlazor icon names (e.g., `volume_up`, `volume_off`, `map`, `layers`) will be selected during implementation to match the MatBlazor icon set available to the project.
- **Cross-cutting concern**: This is a client/front-end only change — no server, worker, or shared-data changes are implied.
