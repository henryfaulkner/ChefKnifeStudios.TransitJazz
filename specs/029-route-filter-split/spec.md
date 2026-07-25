# Feature Specification: RouteFilter Rail / Bus Split

**Feature Branch**: `028-marta-rail-realtime`
**Created**: 2026-06-25
**Status**: Draft
**Input**: Design document: "specs/028-marta-rail-realtime/route-filter-split-design.md — Split the route filter into a distinct Rail section and Bus section so MARTA heavy-rail lines (RED / GOLD / BLUE / GREEN) are visually grouped apart from bus routes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rail lines are grouped apart from buses (Priority: P1)

A person using the live transit map opens the route filter and immediately sees the four
MARTA heavy-rail lines presented together in their own labeled "Rail" group, separate from
the buses, which appear in their own labeled "Buses" group below. The grouping is correct
from the first moment the filter appears — rail pills are never briefly shown among the bus
pills.

**Why this priority**: This is the entire feature — grouping rail apart from buses. Without
it there is no visual distinction, which is the value this feature delivers. It is
independently demonstrable on its own.

**Independent Test**: Open the running app, open the route filter, and confirm that the four
rail lines appear together under a "Rail" label and all bus routes appear together under a
"Buses" label, with rail correctly classified from the first paint of the filter.

**Acceptance Scenarios**:

1. **Given** the route filter is shown, **When** it first appears, **Then** the four rail
   lines (RED, GOLD, BLUE, GREEN) appear together under a "Rail" label and bus routes
   appear together under a "Buses" label.
2. **Given** the route filter has just loaded, **When** no transit vehicles have yet been
   observed, **Then** rail lines are still correctly placed in the Rail group (classification
   does not depend on live vehicle observation).

---

### User Story 2 - Selecting routes works the same across both groups (Priority: P2)

A person selects and hovers over route pills in either group and the existing filtering and
dimming behavior works exactly as before: selecting any pill dims all non-selected pills
across both groups, and the map filters to the selected route regardless of which group it
belongs to.

**Why this priority**: Preserves existing interaction so the split is purely visual and does
not regress the established selection/dimming/clear behavior users already rely on.

**Independent Test**: Select a rail pill and a bus pill, confirm non-selected pills dim
across both groups, confirm the map filters accordingly, and confirm the Clear control
clears selections in both groups.

**Acceptance Scenarios**:

1. **Given** routes from both groups are shown, **When** a rail pill is selected, **Then**
   all non-selected pills across both groups dim and the map filters to that route.
2. **Given** one or more pills are selected, **When** the Clear control is used, **Then** all
   selections across both groups are cleared.

---

### User Story 3 - Empty groups are hidden (Priority: P3)

When a group has no routes to show, that group's label and pills are hidden entirely rather
than leaving an empty labeled section.

**Why this priority**: Avoids orphaned section chrome (a label with nothing under it) and
keeps the layout clean when only one transit mode has routes.

**Independent Test**: With routes present for only one mode, confirm the other mode's label
and group do not appear.

**Acceptance Scenarios**:

1. **Given** there are no rail routes, **When** the filter is shown, **Then** the Rail label
   and Rail group are not displayed.
2. **Given** there are no bus routes, **When** the filter is shown, **Then** the Buses label
   and Buses group are not displayed.

### Edge Cases

- What happens when only rail routes exist? The Buses group (label + pills) is hidden; the
  Rail group is shown.
- What happens when only bus routes exist? The Rail group (label + pills) is hidden; the
  Buses group is shown (matching today's behavior).
- What happens on cold start before any vehicle data arrives? Rail/Bus classification still
  resolves correctly because it comes from the route's static transit mode, not live
  vehicle observation.
- The Clear control remains visible and in its current position regardless of selection
  state or which groups are shown.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each route MUST carry a transit mode (Rail or Bus) determined from the route's
  static transit-mode metadata, available the moment routes load.
- **FR-002**: A route's transit mode MUST NOT depend on live vehicle observation, so
  classification is correct from the first paint of the filter.
- **FR-003**: Rail routes MUST be presented together in a "Rail" group above the "Buses"
  group.
- **FR-004**: Bus routes MUST be presented together in a "Buses" group, in the same grid
  layout used today.
- **FR-005**: Each group MUST be preceded by a section label ("Rail" / "Buses") sourced from
  the existing localized resource set.
- **FR-006**: A group (label and its pills) MUST be hidden entirely when it contains zero
  routes, applied symmetrically to both groups.
- **FR-007**: Route selection MUST remain a single global pool — selecting any pill dims all
  non-selected pills across both groups.
- **FR-008**: The map MUST filter by the selected route regardless of the route's group.
- **FR-009**: The Clear control MUST remain in its current full-width position above both
  groups and remain always visible regardless of selection state.
- **FR-010**: Rail classification MUST be derived from the route-type metadata where the
  rail transit mode maps to Rail and all other types map to Bus, with no hardcoded route
  names.

### Key Entities *(include if feature involves data)*

- **Route**: A transit route shown as a selectable pill. Gains a transit-mode attribute
  (Rail or Bus) used to place it in the correct group.
- **Transit Mode**: The classification (Rail or Bus) derived from the route's static
  route-type metadata, carried from route data through to the filter display.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From the first paint of the route filter, 100% of rail lines appear in the
  Rail group and 0% briefly appear among bus pills.
- **SC-002**: All four MARTA rail lines (RED, GOLD, BLUE, GREEN) are grouped under the Rail
  label whenever rail routes are present.
- **SC-003**: Selection and dimming behavior is unchanged — selecting any pill dims all
  non-selected pills across both groups in 100% of cases.
- **SC-004**: When a group has zero routes, neither its label nor its pills are displayed in
  100% of cases.

## Assumptions

- The four MARTA heavy-rail lines (RED, GOLD, BLUE, GREEN) are the only Rail routes; all
  other routes are Buses.
- Section labels are English-only for this iteration (Spanish deferred, consistent with
  prior features in this codebase).
- The existing route data pipeline already carries (or can carry) static route-type
  metadata from which transit mode is derived.
- This is a frontend-and-data-shape change only; no change to selection/dimming/clear
  interaction model.
