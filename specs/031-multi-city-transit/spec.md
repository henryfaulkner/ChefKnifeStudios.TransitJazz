# Feature Specification: Multi-City Transit Targets

**Feature Branch**: `031-multi-city-transit`
**Created**: 2026-06-26
**Status**: Draft
**Input**: User description: "docs/MULTI_CITY_TRANSIT_DESIGN.md — Extend TransitJazz from a single hardcoded agency (MARTA / Atlanta) to N transit cities"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View a city's live transit by visiting its link (Priority: P1)

A viewer opens a link for a specific city (e.g. Washington DC) and sees only that city's live vehicles moving on the map, hears only that city's soundscape, and sees only that city's route filters. A viewer who opens the app with no city specified sees Atlanta (the existing default) exactly as today.

**Why this priority**: This is the entire user-facing payoff. Without per-city scoping, every viewer would receive every city's vehicles — wasteful, confusing, and broken. Delivering this for one additional city proves the whole feature.

**Independent Test**: Open the app with a city link and confirm the map, audio, and route pills show only that city; open it with no city and confirm Atlanta appears unchanged. Open two browser tabs on two different cities and confirm neither shows the other's vehicles.

**Acceptance Scenarios**:

1. **Given** the app is opened with a city identifier present in the link, **When** the page loads, **Then** the viewer sees only that city's vehicles, routes, and audio.
2. **Given** the app is opened with no city identifier, **When** the page loads, **Then** the viewer sees Atlanta's transit, identical to current behavior.
3. **Given** the app is opened with an unknown/unconfigured city identifier, **When** the page loads, **Then** the viewer falls back to the default city rather than seeing a blank or broken map.
4. **Given** a viewer joins a city mid-stream, **When** the page connects, **Then** current vehicle positions appear within a moment rather than after waiting for the next refresh cycle.

---

### User Story 2 - Add a standard-feed city with configuration only (Priority: P2)

An operator adds a new transit city whose live feeds follow the standard real-time transit format. They do this by adding one configuration entry (and, if the feed needs a key, one stored secret) — no application code is written, compiled, or deployed beyond the configuration change.

**Why this priority**: The core promise of the design is that the common case is free. This is what makes the feature sustainable: cities accumulate without the codebase growing.

**Independent Test**: Add a configuration entry for a standard-feed city, supply its access secret, restart, and confirm that city's vehicles appear via its link — with no source-code change in the commit other than configuration.

**Acceptance Scenarios**:

1. **Given** a city whose live feeds use the standard real-time transit format, **When** an operator adds its configuration entry and any required secret, **Then** that city becomes viewable with no code change.
2. **Given** a standard-feed city whose rail lines use names that differ from their public identifiers, **When** the operator supplies an identifier mapping in configuration, **Then** rail vehicles render on the correct routes without code.
3. **Given** a city's feed requires an access key, **When** the operator stores it as a referenced secret (not in committed configuration files), **Then** the city works and no key is exposed in the repository.

---

### User Story 3 - Isolate a bespoke-feed city in one place (Priority: P3)

An operator adds a city whose live feed does not follow the standard format (e.g. a proprietary data source needing custom assembly). This requires exactly one new, self-contained unit of city-specific logic, and adding it changes nothing about the shared processing pipeline or any other city.

**Why this priority**: This guarantees the exceptional case stays contained. Atlanta itself is such a case (its rail data comes from a non-standard source), so this story is also what keeps the existing city working after the refactor.

**Independent Test**: Confirm the existing Atlanta city — whose rail data is non-standard — works end-to-end after the refactor, and that its special handling lives in one isolated unit that no other city or the shared pipeline references.

**Acceptance Scenarios**:

1. **Given** a city with a non-standard live feed, **When** its city-specific handling is added, **Then** the shared processing pipeline, the relay, the client, and every other city are unchanged.
2. **Given** Atlanta's existing non-standard rail data source, **When** the multi-city refactor is complete, **Then** Atlanta's vehicles, routes, and audio behave identically to before the refactor.

---

### Edge Cases

- **One city's feed is down or errors during a refresh cycle**: that city is skipped for that cycle and an error is recorded; all other cities continue to update normally.
- **Two cities reuse the same short route name (e.g. route "1" in both)**: each city's route "1" stays distinct; vehicles never render on another city's route.
- **Two cities reuse the same vehicle identifier**: each city's vehicle map stays separate; one city's vehicle never overwrites another's.
- **A viewer requests a city that exists in configuration but whose static route data failed to load**: the viewer still gets live vehicles where possible and a degraded-but-not-blank experience.
- **A city is configured without telemetry**: no diagnostic data is produced or stored for it, and this does not affect the city that does emit telemetry.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support an arbitrary number of configured transit cities running concurrently from a single processing service.
- **FR-002**: System MUST scope every viewer to exactly one city, such that a viewer receives only that city's vehicles, routes, and audio.
- **FR-003**: System MUST determine a viewer's city from the link they open and MUST default to Atlanta when no city is specified.
- **FR-004**: System MUST fall back to the default city when a viewer requests an unknown or unconfigured city, never presenting a blank or broken experience.
- **FR-005**: System MUST allow a city whose live feeds use the standard real-time transit format to be added with configuration only — no application code.
- **FR-006**: System MUST allow a standard-feed city to supply an optional rail-route identifier mapping via configuration.
- **FR-007**: System MUST allow a city with a non-standard live feed to be added as a single self-contained unit of city-specific logic, isolated from the shared pipeline and all other cities.
- **FR-008**: System MUST keep the shared processing pipeline free of any city-specific branching; adding a city MUST NOT require editing the shared processing loop, the relay, or the client.
- **FR-009**: System MUST isolate per-city processing faults, so a failure fetching or processing one city's feed does not interrupt updates for other cities.
- **FR-010**: System MUST keep each city's routes distinct even when route names collide across cities (a route name is unique only within its city, never globally).
- **FR-011**: System MUST keep each city's live vehicle state separate, so identical vehicle identifiers across cities never collide.
- **FR-012**: System MUST deliver a newly joining viewer the current vehicle positions for their city promptly, rather than only on the next refresh cycle.
- **FR-013**: System MUST serve each viewer only their city's route/shape data, scoped by the viewer's city.
- **FR-014**: System MUST keep access secrets for a city's feeds out of committed configuration, referencing them by name from secure secret storage instead.
- **FR-015**: System MUST treat diagnostic telemetry as a per-city capability that is on for Atlanta and off for all other cities by default, without introducing per-city branching to decide this.
- **FR-016**: System MUST NOT increase deployed infrastructure when a city is added (no additional process or container per city).
- **FR-017**: System MUST preserve Atlanta's existing end-to-end behavior (vehicles, routes, audio, telemetry) unchanged after the refactor.

### Key Entities *(include if feature involves data)*

- **Transit City**: A configured transit target. Has a stable, lowercase identifier used to scope a viewer's experience; a way to obtain and normalize its complete live vehicle feed (bus and rail combined); and a flag declaring whether it emits diagnostic telemetry. May be a standard configuration-only city or a bespoke isolated city.
- **City Configuration Entry**: The declared definition of a city — its identifier, its live-feed source(s), its static route-data source(s), an optional rail identifier mapping, an optional named secret reference for access keys, and its telemetry flag.
- **Route (scoped)**: A transit route identified by the pair (city, route identifier). The route name alone is not globally unique.
- **Vehicle State (scoped)**: The current position/state of a vehicle, scoped to its city so identifiers never collide across cities.
- **Route Shape**: The map geometry and display data for a route, now carrying its owning city so it can be partitioned and served per city.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A viewer opening a city link sees only that city's vehicles, routes, and audio — zero vehicles from any other city appear.
- **SC-002**: Adding a standard-feed city is achieved with a configuration-only change (plus any required secret) — the change set contains no new or modified application source code.
- **SC-003**: Adding a bespoke-feed city changes exactly one isolated unit of city-specific logic and zero lines of the shared pipeline, relay, client, or any other city.
- **SC-004**: After the refactor, Atlanta's behavior is indistinguishable from before across vehicles, routes, audio, and telemetry.
- **SC-005**: A single failing city feed during a refresh cycle leaves every other city updating normally (verified by inducing one city's feed to fail).
- **SC-006**: The number of deployed processes/containers is unchanged regardless of how many cities are configured.
- **SC-007**: A newly joining viewer sees current vehicle positions within a few seconds of connecting, rather than waiting a full refresh cycle.
- **SC-008**: No city access key appears anywhere in committed configuration or source.

## Assumptions

- The existing single processing service comfortably handles a dozen-ish cities, since per-city work is light, periodic, I/O-bound feed fetching — no city needs independent scaling today (per-city container isolation is deferred and reversible).
- A viewer's city is sourced from the link (path or query) as the single source of truth; an in-app city switcher is out of scope (a viewer switches by navigating to a different city link).
- Diagnostic telemetry remains Atlanta-only by deliberate choice; expanding it per city is out of scope.
- Display labels for city names (if any are shown) are English-only this pass, consistent with prior localization deferrals; the city identifiers themselves are stable internal keys, not display strings.
- The standard real-time transit feed format and the existing static route-data loading mechanism are reused; this feature generalizes them per city rather than replacing them.
- Washington DC (WMATA) is the reference second city used to validate the configuration-only path, but the feature is not specific to it.

## Out of Scope / Deferred

- In-app city switcher UI (link/navigation is the switching mechanism).
- Per-city container isolation (reversible later if a city needs independent scaling).
- Telemetry for non-Atlanta cities.
- Spanish/localized labels for any new city names.
