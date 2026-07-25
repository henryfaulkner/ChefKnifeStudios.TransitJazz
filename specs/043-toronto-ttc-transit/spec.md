# Feature Specification: Toronto TTC Transit City

**Feature Branch**: `043-toronto-ttc-transit`
**Created**: 2026-07-14
**Status**: Draft
**Input**: User description: "Add Toronto TTC as a new transit city data source, based on the compatibility report at docs/city-compat/ttc.md."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See Toronto surface vehicles moving on the map (Priority: P1)

A rider or curious visitor opens the app, chooses Toronto (TTC) from the city picker, and watches real TTC buses and streetcars move across the map in real time — the same live-vehicle experience already available for Atlanta, DC, Boston, and New York.

**Why this priority**: This is the entire point of the feature. Without live vehicles appearing on the map, nothing else matters. It is the minimum viable slice — if only this ships, the feature delivers value.

**Independent Test**: Select the Toronto (TTC) city entry and confirm vehicle markers appear and move over a few refresh cycles, positioned on real streets, without needing any other story.

**Acceptance Scenarios**:

1. **Given** the app is open, **When** the user selects the Toronto (TTC) entry in the city picker, **Then** the map view switches to Toronto and begins showing live TTC surface vehicles.
2. **Given** the Toronto view is active, **When** the underlying vehicle feed refreshes, **Then** vehicle positions update on the map to reflect their latest reported locations.
3. **Given** the Toronto view is active, **When** a vehicle is traveling a known route, **Then** its marker is associated with the correct route so its shape/identity lines up with the static route registry.

---

### User Story 2 - Vehicles match their real routes and voice on the soundscape (Priority: P1)

A user viewing Toronto expects each bus and streetcar to be correctly matched to its route (e.g., a "504" King streetcar, a "32" Eglinton West bus) so that route-based coloring, filtering, shapes, and audio all behave correctly — not show up as "unknown route."

**Why this priority**: Route matching is what turns raw vehicle dots into the route-attributed soundscape that is the app's core value. TTC's live feed reports `route_id` as a plain integer string that matches the static `route_short_name` verbatim, so matching should be near-total with no transform — but the feature is not "done" until that match rate is confirmed and route-less/unknown vehicles are handled cleanly.

**Independent Test**: Confirm the share of live TTC surface vehicles that resolve to a known route is near-total (see Success Criteria), and that route-less (out-of-service/deadheading) and unknown-route vehicles are skipped and counted without disrupting the render.

**Acceptance Scenarios**:

1. **Given** a vehicle reports a route identifier (e.g., "504"), **When** it is matched against the static registry, **Then** it resolves to the corresponding route with no identifier transformation.
2. **Given** a vehicle reports no route identifier (out-of-service / deadheading), **When** the tick is processed, **Then** it is counted as a route-less skip and not rendered.
3. **Given** a vehicle reports a route not present in the static schedule (e.g., an internal/special service), **When** the tick is processed, **Then** it is counted as an unknown-route skip and does not crash or corrupt the render.
4. **Given** a matched surface vehicle, **When** it drives the soundscape, **Then** it voices on the palette dictated by the existing GTFS `route_type` classification — buses (`route_type=3`) as Bus, and streetcars (`route_type=0`) as Rail (per the as-built classifier; see FR-007).

---

### User Story 3 - Toronto subway lines draw without live trains (Priority: P3)

A user viewing Toronto may see the subway line geometry drawn on the map, but understands that subway trains do not animate because no public live subway feed exists.

**Why this priority**: Subway geometry is present in the static data and could be drawn, but there is no public live train-position source to animate it. This is a cosmetic/geometry-only concern, not core value, and the feature is fully viable without any subway treatment at all — hence P3 (and likely deferred).

**Independent Test**: Confirm the app does not attempt to fetch a (nonexistent) live subway feed and does not error; whatever subway geometry appears is static-only and no train markers are expected.

**Acceptance Scenarios**:

1. **Given** the Toronto view is active, **When** the app processes realtime data, **Then** it never attempts to fetch a live subway/train position feed for Toronto and never errors on its absence.
2. **Given** subway geometry is present in the static data, **When** the map renders, **Then** no live train markers appear for subway lines (there is no feed to drive them).

---

### Edge Cases

- **Route-less vehicles**: A large, normal share of surface vehicles (out-of-service / deadheading) report no route identifier; these are quietly skipped and counted, never breaking the render.
- **Unknown route after match**: A vehicle whose route is not in the public static schedule (e.g., an internal/special service) is quietly skipped and counted, never breaking the render.
- **Static source URL contains a space**: The Toronto static data URL contains a literal space and must be handled (encoded/quoted) so the fetch succeeds.
- **Static resource identifier rotates**: The static data source identifier can change when Toronto publishes a schedule update; a stale identifier must fail gracefully (last-good data retained) and be correctable via configuration.
- **No live subway feed exists**: The system must not attempt to fetch a Toronto subway live feed and must not treat its absence as an error.
- **No behavior change for other cities**: Atlanta, DC, Boston, and New York must behave exactly as before — this feature must be additive only.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST offer Toronto (TTC) as a selectable city experience in the app's city picker, distinct from all existing cities.
- **FR-002**: The system MUST display live TTC surface vehicles (buses and streetcars) on the map, updating their positions each refresh cycle from the real-time vehicle feed.
- **FR-003**: The Toronto experience MUST be its own independent map/audio session (its own view), consistent with the one-selection-one-view model of every other city.
- **FR-004**: The system MUST match route identifiers reported by the live feed against the static route registry with no identifier transformation (the reported route identifier equals the static route key verbatim).
- **FR-005**: The system MUST skip and count vehicles that report no route identifier (out-of-service / deadheading) without rendering them and without erroring.
- **FR-006**: The system MUST skip and count vehicles whose route cannot be matched to the static registry without disrupting rendering of matched vehicles.
- **FR-007**: The system MUST classify TTC vehicles by the existing GTFS `route_type` rule with no new code: buses (`route_type=3`) as Bus and streetcars (`route_type=0`) as Rail. Streetcars therefore load, snap, and voice correctly on the Rail treatment for v1. (This overrides the compat report's "streetcars ride the bus palette" note, which was written against a Worker-side classifier that does not exist; the as-built WebAPI classifier maps `route_type` 0/1/2 to Rail.) Dedicated streetcar voicing is a tracked follow-up (see Assumptions).
- **FR-008**: The system MUST NOT attempt to fetch or require any live subway/train position feed for Toronto, and MUST NOT treat the absence of one as an error.
- **FR-009**: The system MUST source Toronto static route/shape data from Toronto's keyless open-data static feed, correctly handling a source URL that contains a space.
- **FR-010**: The system MUST source Toronto real-time surface-vehicle positions from TTC's keyless real-time vehicle feed; no authentication credential is required for either the static or real-time feed.
- **FR-011**: When any single external data source (static or real-time) fails to load, the system MUST continue with the most recent good data and retry on the next cycle without erroring.
- **FR-012**: TTC live vehicle positions MUST be included in telemetry, consistent with all other cities whose vehicles report real positions.
- **FR-013**: The feature MUST NOT change the behavior of any existing city.

### Key Entities *(include if feature involves data)*

- **Toronto (TTC) City**: The new selectable Toronto experience/registration, distinct from all existing cities. Attributes: its name/identity, its real-time surface-vehicle feed source, its static route data source, no credential reference (keyless), no route-identifier transform, no rail/subway realtime adapter, and its telemetry-enabled flag.
- **Live Surface Vehicle**: A single reporting TTC bus or streetcar with a real position and a reported route identifier (which may be absent for out-of-service vehicles) that must be matched to a known route before rendering.
- **Static Route Registry**: The known-routes/shapes reference assembled from Toronto's static open-data feed, against which live vehicles are matched by route identifier verbatim. Includes subway line geometry with no live counterpart.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can select the Toronto (TTC) experience and see live surface vehicles moving on the map within the same time-to-first-vehicles a user experiences for existing cities.
- **SC-002**: At least 99% of route-attributed live TTC surface vehicles resolve to a known route (verbatim match, no transform), measured over a normal daytime service window.
- **SC-003**: Route-less vehicles (out-of-service / deadheading) and the small share of unknown-route vehicles are skipped and counted, and never cause a crash or a blank/broken map.
- **SC-004**: Existing cities (Atlanta, DC, Boston, New York) show zero behavioral change — same vehicles, same routes, same match rates as before this feature.
- **SC-005**: No external data-source failure (feed or static source) causes a crash or a blank/broken map; the experience degrades gracefully and recovers on the next successful cycle.
- **SC-006**: The app never attempts to fetch a Toronto live subway feed and never surfaces an error for its absence.

## Assumptions

- **Surface-only, no subway sonification**: TTC's real-time feed is surface-only (buses + streetcars); it carries no subway vehicle positions, and no public live subway feed exists. Subway sonification is therefore out of scope. Drawing static subway geometry is P3 / likely deferred, since there is no live source to animate it.
- **Streetcars voice as Rail (v1), revisit later**: The as-built classifier maps GTFS `route_type` 0/1/2 to Rail (`GtfsStaticLoader`), so TTC's ~20 streetcar routes (`route_type=0`) render and voice on the **Rail** treatment — not the Bus palette the compat report assumed. This is accepted for v1 to keep the feature config-only (forcing streetcars to Bus would require changing a classifier shared by every city). Dedicated streetcar voicing (a distinct tram treatment) is an explicit tracked follow-up, out of scope here.
- **Zero route-ID transform**: The live feed's route identifier equals the static route key verbatim, so no per-city reconciliation rule set is needed (unlike NYC bus). If a future schedule change breaks this, it would be handled as a follow-up.
- **Keyless feeds**: Both the static and real-time feeds require no authentication; no secret must be provisioned before Toronto vehicles appear.
- **Reuse of existing city plumbing**: Toronto reuses the existing generic live-vehicle city mechanism (the same one running DC and Boston); it is not a bespoke adapter. The only new artifacts are the Toronto city registration/config and its feed sources.
- **Static source volatility**: The static data source identifier can rotate on schedule updates; pinning/mirroring the source is a recommended operational follow-up but not required for v1.
- **Route-less share is normal**: A substantial fraction of surface vehicles report no route (out-of-service / deadheading); this is normal for any agency's feed and is handled by existing route-less skip logic.
