# Feature Specification: Add Boston (MBTA) as a Transit City

**Feature Branch**: `031-multi-city-transit` (no branch switch — added on the current branch per request)
**Created**: 2026-06-28
**Status**: Draft
**Input**: User description: "Add Boston's transit system, MBTA, as a data source and city target. Review their data compatibility here, docs/city-compat/mbta.md"

## Summary

Add Boston / MBTA as a third selectable transit city, alongside Atlanta (MARTA) and Washington DC (WMATA). MBTA is the cleanest possible addition under the multi-city design: a single public, keyless real-time feed carries **all modes at once** (bus, light rail, commuter rail, and heavy rail), every vehicle already carries a route identifier and position, and the rail line identifiers align with the static route data verbatim. This means MBTA is the **configuration-only** case the multi-city design promised — no new processing logic, no per-city secret, and (unlike Atlanta) no separate rail data source. The only non-configuration work is making Boston reachable and selectable: a stable city identifier and a menu entry in the existing city picker.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Boston's live transit (Priority: P1)

A viewer opens Boston in TransitJazz and sees only Boston's live vehicles — buses, light rail, and heavy-rail trains (Red / Orange / Blue lines) — moving on the map, hears only Boston's soundscape, and sees only Boston's route filters. Atlanta and Washington DC viewers are unaffected.

**Why this priority**: This is the entire user-facing payoff of the feature. If Boston's vehicles appear correctly scoped, the feature is delivered.

**Independent Test**: Open the app scoped to Boston and confirm the map shows Boston vehicles on Boston routes (including live heavy-rail trains), the audio reflects Boston, and the route pills are Boston's. Open Atlanta in a second tab and confirm no Boston vehicles bleed in and Atlanta is unchanged.

**Acceptance Scenarios**:

1. **Given** the app is scoped to Boston, **When** the page loads, **Then** the viewer sees only Boston's vehicles, routes, and audio.
2. **Given** Boston's single real-time feed carries all modes, **When** vehicles render, **Then** buses, light rail, and heavy-rail trains all appear on their correct routes from that one feed — no separate rail source is required.
3. **Given** a viewer is scoped to Atlanta or Washington DC, **When** Boston is added, **Then** those cities behave exactly as before.

---

### User Story 2 - Select Boston from the city picker (Priority: P2)

A viewer uses the existing in-app city picker and sees Boston listed next to Atlanta and Washington DC. Selecting it takes them to Boston's transit.

**Why this priority**: Without a picker entry, Boston is only reachable by hand-editing the link. Adding the entry is what makes the new city discoverable, but the underlying transit (Story 1) is the value.

**Independent Test**: Open the city picker and confirm Boston appears as a choice; select it and confirm the app loads Boston's transit; confirm the currently-viewed city is shown as the disabled/active entry.

**Acceptance Scenarios**:

1. **Given** the city picker is open, **When** the viewer looks at the list, **Then** Boston appears alongside the existing cities.
2. **Given** the viewer selects Boston, **When** the app reloads, **Then** it is scoped to Boston.

---

### Edge Cases

- **Boston's feed is down during a refresh cycle**: Boston is skipped for that cycle and the failure is recorded; Atlanta and Washington DC continue updating normally (existing per-city fault isolation).
- **A vehicle in Boston's feed reports no speed**: position still renders; speed is optional and degrades gracefully (only ~12% of Boston vehicles report speed).
- **A Boston real-time route identifier has no matching static route shape** (e.g. a replacement shuttle): that vehicle still appears as a live position where possible; it simply has no route shape to render against, rather than breaking the map.
- **No Boston telemetry**: Boston produces no diagnostic telemetry by default (telemetry stays Atlanta-only), and this does not affect Atlanta's telemetry.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST make Boston (MBTA) a configured transit city with a stable, lowercase identifier, scoped exactly like the existing cities.
- **FR-002**: System MUST source Boston's live vehicles from its single public real-time feed, which carries all transit modes (bus, light rail, commuter rail, heavy rail) together.
- **FR-003**: System MUST render Boston's heavy-rail trains (Red / Orange / Blue) from that same single feed, without any separate rail data source or rail-specific adapter.
- **FR-004**: System MUST match Boston's real-time vehicles to their static route shapes using the route identifier carried in the feed, which aligns with Boston's static route data (no route-name remapping required for Boston).
- **FR-005**: System MUST add Boston without any city access key or secret, since Boston's feeds are public and keyless.
- **FR-006**: System MUST add Boston with configuration only for the data pipeline — no new or modified processing logic in the shared pipeline or any city-specific processing unit.
- **FR-007**: System MUST list Boston in the existing in-app city picker so a viewer can select it, and MUST indicate the currently-viewed city as the others do.
- **FR-008**: System MUST keep Boston's routes, vehicles, and audio fully isolated from every other city (no cross-city bleed), consistent with existing per-city scoping.
- **FR-009**: System MUST leave Atlanta's and Washington DC's existing end-to-end behavior unchanged after Boston is added.
- **FR-010**: System MUST NOT emit diagnostic telemetry for Boston (telemetry remains Atlanta-only by default).
- **FR-011**: System MUST NOT add any deployed process or container to support Boston.

### Key Entities *(include if feature involves data)*

- **Boston Transit City**: A configured transit target with a stable lowercase identifier (`mbta`). Sources all vehicles (all modes) from one public real-time feed and its route geometry from one public static dataset. Carries no access secret, no rail mapping, and no telemetry flag.
- **City Configuration Entry (Boston)**: Boston's declared definition — its identifier, its single real-time feed source, its single static route-data source. No secret reference, no rail identifier mapping, no separate rail source.
- **City Picker Entry (Boston)**: The selectable list item that lets a viewer navigate to Boston, plus the stable identifier the rest of the app already keys on.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A viewer scoped to Boston sees only Boston's vehicles, routes, and audio — zero vehicles from Atlanta or Washington DC appear.
- **SC-002**: Boston's live heavy-rail trains appear on the Red / Orange / Blue lines from the single feed, with no separate rail source configured.
- **SC-003**: The data-pipeline portion of adding Boston is achieved with configuration only — the data-pipeline change set contains no new or modified processing source code (the only source changes are the city identifier constant and the picker entry).
- **SC-004**: Atlanta and Washington DC are indistinguishable from before across vehicles, routes, audio, and telemetry after Boston is added.
- **SC-005**: Inducing Boston's feed to fail during a refresh cycle leaves Atlanta and Washington DC updating normally.
- **SC-006**: The number of deployed processes/containers is unchanged after adding Boston.
- **SC-007**: No Boston access key appears anywhere in committed configuration or source (Boston needs none).

## Assumptions

- Boston's real-time feed matches vehicles to static route shapes by the feed's route identifier, which the existing route index is already keyed on. The compatibility review confirms 100% route-identifier alignment for Boston with this keying. (The compatibility document also notes a `route_short_name`-based keying would only reach ~90%; the current implementation already keys by route identifier, so no remapping is needed for Boston.)
- Boston's public CDN endpoints (real-time vehicle positions and static GTFS) are reachable from the deployment environment and require no authentication.
- A viewer's city continues to come from the link as the single source of truth; the picker entry navigates to Boston's link and reloads, exactly as the existing entries do.
- City display labels are English-only this pass, consistent with prior localization deferrals; the Boston identifier is a stable internal key, not a display string.
- This feature is added on the current `031-multi-city-transit` branch at the user's request; it does not introduce a new branch and builds directly on the multi-city machinery delivered there.
- The static route data for Boston includes some routes without shapes (~30 of ~403); those routes simply have no map geometry, consistent with how the loader already skips shapeless routes.

## Out of Scope / Deferred

- Telemetry for Boston (or any non-Atlanta city).
- Boston's V3 JSON API / its API key — not used; the public protobuf feed is sufficient.
- Any rail-specific adapter or route-name remapping for Boston (neither is needed).
- Localized (non-English) labels for the Boston city name.
- Commuter-rail or light-rail special handling beyond what the standard feed already provides (they ride the same feed as everything else).
