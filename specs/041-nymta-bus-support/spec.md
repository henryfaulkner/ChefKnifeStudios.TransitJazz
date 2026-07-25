# Feature Specification: NYC MTA Bus Support

**Feature Branch**: `041-nymta-bus-support` (spec authored on branch `040-nymta-subway-interpolation` per user instruction; no branch switch)
**Created**: 2026-07-12
**Status**: Draft
**Input**: User description: "docs/nymta-bus-support-design.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See NYC buses moving on the map (Priority: P1)

A rider or curious visitor opens the app, chooses New York City buses from the city picker, and watches real MTA buses move across the map in real time — the same live-vehicle experience already available for Atlanta, DC, and Boston.

**Why this priority**: This is the entire point of the feature. Without live buses appearing on the map, nothing else matters. It is the minimum viable slice — if only this ships, the feature delivers value.

**Independent Test**: Select the NYC buses city entry and confirm bus markers appear and move over a few refresh cycles, positioned on real streets, without needing any other story.

**Acceptance Scenarios**:

1. **Given** the app is open, **When** the user selects the "New York Buses" entry in the city picker, **Then** the map view switches to New York City and begins showing live bus vehicles.
2. **Given** the NYC buses view is active, **When** the underlying vehicle feed refreshes, **Then** bus positions update on the map to reflect their latest reported locations.
3. **Given** the NYC buses view is active, **When** a bus is traveling a known route, **Then** its marker is associated with the correct route so its shape/identity lines up with the static route registry.

---

### User Story 2 - Buses match their real routes (route-ID reconciliation) (Priority: P1)

A user viewing NYC buses expects each bus to be correctly matched to its route (e.g., an "M15 Select Bus Service" bus, a "Q6" bus, a "Bx3" bus) so that route-based coloring, filtering, shapes, and audio behave correctly — not show up as "unknown route."

**Why this priority**: The live feed labels routes differently from the static route registry (letter casing, a `+` Select-Bus-Service suffix, zero-padded numbers). Without reconciling these, a large share of buses would fail to match a known route and be dropped or mislabeled, gutting the value of Story 1. It is co-critical P1: Story 1 is not truly "done" until route matching works for the great majority of buses.

**Independent Test**: Compare the share of NYC bus vehicles that resolve to a known route before vs. after normalization; confirm near-total matching (see Success Criteria) including the tricky cases (Select Bus Service `+`, zero-padded numbers like Q06, mixed casing like Bx3).

**Acceptance Scenarios**:

1. **Given** a bus reports a route labeled with a trailing Select-Bus-Service marker (e.g., "M15+"), **When** it is matched against the static registry, **Then** it resolves to the corresponding Select Bus Service route (e.g., "M15-SBS").
2. **Given** a bus reports a zero-padded route number (e.g., "Q06"), **When** it is matched, **Then** it resolves to the un-padded route (e.g., "Q6").
3. **Given** a bus reports a route in different letter casing than the registry (e.g., "bx3" / "BX3"), **When** it is matched, **Then** it resolves to the registry's route (e.g., "Bx3"-equivalent) regardless of casing.
4. **Given** a bus route cannot be matched even after normalization, **When** the tick is processed, **Then** it is counted as an unknown-route skip and does not crash or corrupt the render (existing behavior).

---

### User Story 3 - Buses from both NYC operators appear (Priority: P2)

A user expects to see not just the numbered/lettered borough buses run by the primary transit authority, but also the separately-operated express and additional local routes (e.g., certain Q-series locals and express routes) that only exist in the second operator's data.

**Why this priority**: Missing an entire operator's routes would look like a coverage bug to New Yorkers, but the app still delivers clear value with just the primary operator's routes if the second source is temporarily unavailable. Hence P2, not P1.

**Independent Test**: Confirm that routes unique to the second bus operator resolve to known routes and their buses render, in addition to the primary operator's routes.

**Acceptance Scenarios**:

1. **Given** both bus operators' static route data is loaded, **When** a bus from the second operator's exclusive routes reports in, **Then** it resolves to a known route and renders on the map.
2. **Given** the second operator's static data is temporarily unavailable, **When** the app refreshes route data, **Then** the primary operator's routes still resolve and render (graceful degradation), and the second operator's exclusive routes resolve again once the source recovers.

---

### Edge Cases

- **Missing/invalid feed credential**: If the NYC bus feed credential is unset or rejected, the bus feed is simply empty for that refresh and retries on the next cycle — no crash, no user-facing error state beyond "no buses currently shown."
- **Unmatched route after normalization**: A bus whose route still doesn't match any known route is quietly skipped and counted (existing unknown-route handling), never breaking the render.
- **Misconfigured normalization rule**: A typo in the configured normalization steps degrades match rate at worst; it must never crash a refresh cycle.
- **One static source of several fails to load**: The system continues with whatever sources succeeded (last-good-wins), same as other cities today.
- **Second operator source unavailable**: Only that operator's exclusive routes fail to resolve for that cycle; primary operator routes are unaffected.
- **No behavior change for other cities**: Atlanta, DC, Boston, and NYC subway must behave exactly as before — this feature must be additive only.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST offer NYC buses as a selectable city experience in the app's city picker, distinct from the existing NYC subway experience.
- **FR-002**: The system MUST display live NYC bus vehicles on the map, updating their positions each refresh cycle from the real-time bus feed.
- **FR-003**: The NYC bus experience MUST be its own independent map/audio session (its own view), separate from the NYC subway session, consistent with how every other city is one selection = one view today.
- **FR-004**: The system MUST reconcile route identifiers reported by the live bus feed with the static route registry so that the great majority of buses match a known route.
- **FR-005**: Route reconciliation MUST handle, at minimum: letter-case differences, the Select-Bus-Service suffix convention (trailing `+` → the registry's SBS route form), and zero-padded route numbers (e.g., leading-zero stripping).
- **FR-006**: Route reconciliation rules MUST be applied in a defined, repeatable order and be configurable per city, so that other cities are unaffected (their rule set is empty and behavior is unchanged).
- **FR-007**: Route reconciliation MUST be resilient to misconfiguration — an unrecognized rule is ignored (no-op) rather than causing a failure.
- **FR-008**: The system MUST load static route data covering both NYC bus operators so buses from either operator can be matched to a known route.
- **FR-009**: The system MUST load enough static route/shape data to draw bus route shapes across all NYC boroughs, not just the borough(s) that happen to supply the shared route list.
- **FR-010**: When any single static data source fails to load, the system MUST continue with the sources that succeeded (graceful degradation), preserving the most recent good data.
- **FR-011**: When the real-time bus feed is unavailable or its credential is rejected, the system MUST treat that refresh as empty and retry on the next cycle without erroring.
- **FR-012**: A bus whose route cannot be matched even after reconciliation MUST be counted as an unknown-route skip and MUST NOT disrupt rendering of matched buses.
- **FR-013**: NYC bus live positions MUST be included in telemetry, consistent with all other cities whose vehicles report real positions.
- **FR-014**: The feature MUST NOT change the behavior of the existing NYC subway experience or of any other existing city.
- **FR-015**: The real-time bus feed credential MUST be supplied via configuration/secret (not hard-coded), consistent with how other credentialed cities are configured.

### Key Entities *(include if feature involves data)*

- **NYC Bus City**: The new selectable NYC bus experience/registration, distinct from the existing NYC subway registration. Attributes: its own name/identity, its real-time feed source, its static route data sources, its credential reference, its route-reconciliation rule set, and its telemetry-enabled flag.
- **Live Bus Vehicle**: A single reporting NYC bus with a real position and a reported route identifier that must be reconciled to a known route before rendering.
- **Route Reconciliation Rule Set**: An ordered list of named transformation steps applied to a reported route identifier to make it match the static route registry (e.g., case-fold, SBS-suffix rewrite, leading-zero strip). Empty for all cities except NYC bus.
- **Static Route Registry**: The known-routes/shapes reference assembled from NYC's static bus data across both operators and all boroughs, against which live vehicles are matched.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can select the NYC buses experience and see live buses moving on the map within the same time-to-first-vehicles a user experiences for existing cities.
- **SC-002**: At least 98% of live NYC bus vehicles resolve to a known route after reconciliation (target: effectively 100% for the known route-ID mismatch patterns), measured over a normal daytime service window.
- **SC-003**: Buses from both NYC bus operators appear on the map (the second operator's exclusive routes are not silently missing when its data source is available).
- **SC-004**: Existing cities (Atlanta, DC, Boston, NYC subway) show zero behavioral change — same vehicles, same routes, same match rates as before this feature.
- **SC-005**: No single external data-source failure (feed or any one static source) causes a crash or a blank/broken map; the experience degrades gracefully and recovers on the next successful cycle.
- **SC-006**: The route-reconciliation logic is verifiable in isolation, with representative inputs producing the expected normalized outputs (e.g., an SBS `+` route, a zero-padded route, and a mixed-case route each resolve correctly).

## Assumptions

- **One picker entry per NYC mode (Option A)**: NYC bus is a separate city view/session from NYC subway ("New York Buses" vs the existing "New York Subway"), matching the one-selection-one-view model of every other city. A single unified "all NYC modes on one map" experience is explicitly out of scope for this feature (it would require new multi-source client behavior and can be a future feature).
- **Full-coverage static data (all NYC boroughs + both operators)**: All NYC borough static sources plus the second operator's source are loaded, even though the shared route list is identical across boroughs — this avoids partial route-shape coverage at no extra cost. The strict minimum (one borough source + the second operator) would match routes but risk missing some borough shapes; full coverage is chosen for v1.
- **Telemetry enabled for NYC bus**: Because NYC bus positions are real live GPS (unlike synthesized subway positions), telemetry is enabled, consistent with all other real-position cities.
- **Reuse of existing city plumbing**: NYC bus reuses the existing generic live-vehicle city mechanism (the same one running DC and Boston); it is not a bespoke adapter like NYC subway. The only genuinely new capability is the reusable route-reconciliation rule set.
- **Bus only**: This feature is bus-only. NYC subway and its synthesis/interpolation machinery are untouched. Alternate NYC data formats (e.g., the SIRI JSON feed) are out of scope; the standard protobuf bus feed is used.
- **Credential dependency**: A valid credential/key for the NYC real-time bus feed must be obtained and configured as a secret before buses will appear; this is an operational prerequisite, not code.
- **Confirm feed credential query-parameter naming**: The exact query-parameter name the bus feed expects for its credential must be confirmed before shipping and configured accordingly, so the credentialed request succeeds.
