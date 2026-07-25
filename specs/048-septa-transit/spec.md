# Feature Specification: SEPTA Philadelphia Transit City

**Feature Branch**: `048-septa-transit`
**Created**: 2026-07-25
**Status**: Draft
**Input**: User description: "Add SEPTA (Southeastern Pennsylvania Transportation Authority, Philadelphia) as a new live-vehicle transit city, per the compatibility report at docs/city-compat/septa.md (score 92/100, Drop-in). SEPTA's buses, trackless trolleys, streetcars, and the Norristown High Speed Line (route_type=1, route_id "M1") all ride a single keyless GTFS-RT vehicle-positions feed with 100% route_id/lat/lon coverage and a verbatim route_id == static route_short_name match — no ID transform needed. This part is config-only, same generic GtfsRtCity path as WMATA/MBTA/TTC. The static GTFS zip is a zip-of-zips (google_bus.zip nested inside gtfs_public.zip) — the existing GtfsStaticLoader.cs has no support for a nested zip and must gain that capability without changing behavior for any existing single-level-zip city. Broad Street Subway and Market-Frankford Line share the same feed/ID scheme but showed zero live vehicles in the compat report; treat as a known open question, do not build a bespoke rail adapter for them in this feature. Follow the existing city-onboarding pattern (CityNames constant, Worker + WebAPI appsettings.json Cities: entries, CityFab.razor picker button, map origin coordinate, AudioUnlockOverlay copy, InfoFab copy) plus the new nested-zip-extraction change to GtfsStaticLoader.cs."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Listen to live Philadelphia transit (Priority: P1)

A visitor opens the app, selects Philadelphia from the city picker, and hears/sees SEPTA buses, trackless trolleys, streetcars, and the Norristown High Speed Line moving through the city in real time, exactly as they can today for Atlanta, DC, Boston, New York, and Toronto.

**Why this priority**: This is the entire feature — without it, nothing else in this spec has value. It's also independently shippable: SEPTA's live-vehicle feed is 100% route-attributed and keyless, so this story can be fully delivered without any dependency on the Broad Street Subway / Market-Frankford open question.

**Independent Test**: Select "Philadelphia, PA" from the city picker, confirm vehicle dots representing buses, trolleys, streetcars, and NHSL trains render on the map near real Philadelphia streets/rail corridors and move over successive poll cycles, and confirm audio plays for crossings.

**Acceptance Scenarios**:

1. **Given** the app is loaded, **When** a user opens the city picker, **Then** a "Philadelphia, PA" option is present and selectable.
2. **Given** a user has selected Philadelphia, **When** the map loads, **Then** it centers on Philadelphia's transit-dense core and SEPTA route shapes (bus, trolley, streetcar, NHSL) are visible.
3. **Given** Philadelphia is selected and live vehicles are being polled, **When** a SEPTA vehicle crosses a trigger point on a route the user hasn't muted, **Then** the corresponding instrument sounds.
4. **Given** a user has selected Philadelphia, **When** they open the audio-unlock overlay and the info panel, **Then** both display Philadelphia/SEPTA-specific descriptive copy (not another city's placeholder text).

---

### User Story 2 - Static route shapes load correctly despite SEPTA's nested-zip packaging (Priority: P1)

The system fetches SEPTA's static GTFS data (needed to draw route line geometry and resolve route metadata like short names and colors) from a zip file that itself contains nested zip files, unlike every other onboarded city's flat single-level zip. The system must unpack the correct nested archive and load routes/shapes from it exactly as it would from a normal flat zip.

**Why this priority**: Without this, User Story 1 only half-works — live vehicle dots would have no route line/shape context, and route short-name/color metadata (used for route matching and rendering) would be silently empty. This is a hard prerequisite for a usable SEPTA experience, not a nice-to-have.

**Independent Test**: Point the static-GTFS loader at SEPTA's zip URL in isolation (e.g., via existing loader unit tests or a manual fetch-and-inspect) and confirm route shapes, short names, and colors are extracted correctly, with route counts matching the compatibility report (147 routes / 145 with shapes).

**Acceptance Scenarios**:

1. **Given** the static loader is configured with SEPTA's top-level zip URL, **When** it runs a refresh cycle, **Then** it locates and extracts the nested bus/rail archive containing `trips.txt`, `shapes.txt`, and `routes.txt`, rather than failing to find those files at the top level.
2. **Given** SEPTA's static data has loaded successfully, **When** a live SEPTA vehicle reports a `route_id`, **Then** it resolves against the correct static route shape/metadata with no transform.
3. **Given** an existing city configured with a normal flat (non-nested) static zip, **When** the static loader runs after this feature ships, **Then** its behavior and output are unchanged.

---

### User Story 3 - Existing cities remain unaffected (Priority: P2)

Operators and users of the five already-shipped cities (Atlanta, DC, Boston, New York, Toronto) see no change in behavior, performance, or data correctness after SEPTA is added.

**Why this priority**: This is a regression-prevention story rather than new value delivery — important to verify, but it doesn't gate initial SEPTA usability the way Stories 1-2 do.

**Independent Test**: After deployment, run through the existing per-city smoke checks (feed reachability, shapes loading, live vehicles rendering/moving, audio) for each previously-shipped city and confirm no regressions.

**Acceptance Scenarios**:

1. **Given** the app has been updated to include SEPTA, **When** a user selects any previously-shipped city, **Then** that city's live vehicles, route shapes, and audio behave exactly as before this feature.

---

### Edge Cases

- What happens when SEPTA's top-level zip is fetched but the expected nested archive is missing or renamed upstream? The refresh cycle should log the failure and keep serving the last-known-good static data for SEPTA, the same as any other city's fetch failure today (no crash, no partial/corrupt data swap).
- What happens for the Broad Street Subway / Market-Frankford Line (`B1`/`B2`/`B3`/`L1`), which share SEPTA's feed and ID scheme but showed no live vehicles during compatibility testing? They are not specially excluded or filtered — if SEPTA ever emits live vehicles under those route IDs, they appear automatically through the same generic path NHSL (`M1`) already uses today. No dedicated rail adapter is built for them in this feature.
- What happens if a live SEPTA vehicle reports a `route_id` with no static counterpart (e.g., an owl/off-peak variant not in the matched set)? It falls into the platform's existing "unknown route" handling, unchanged from how every other city handles unmatched routes today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to select Philadelphia/SEPTA from the existing city picker.
- **FR-002**: System MUST fetch and render live SEPTA vehicle positions (buses, trackless trolleys, streetcars, and Norristown High Speed Line) on the map, matched to their static route shapes with no route-ID transform.
- **FR-003**: System MUST fetch SEPTA's static route/shape data from its zip-of-zips packaging, extracting the nested archive that contains the bus/trolley/streetcar/NHSL GTFS files, and use it to build route shapes and metadata identically to how flat zips are processed for other cities today.
- **FR-004**: System MUST continue to process every existing city's flat (non-nested) static zip exactly as it does today — the nested-zip handling MUST be additive, not a replacement of the existing single-level extraction path.
- **FR-005**: System MUST center the map on Philadelphia's transit-dense downtown core (not the city's geographic centroid) when a user selects Philadelphia.
- **FR-006**: System MUST display SEPTA/Philadelphia-specific descriptive copy in the audio-unlock overlay (header + three paragraphs) and the info panel when Philadelphia is the selected city.
- **FR-007**: System MUST treat the Regional Rail portion of SEPTA's static data (`google_rail.zip`, `route_type=2`) as out of scope for this feature — it is not loaded or rendered.
- **FR-008**: System MUST NOT build a separate rail-realtime adapter or bespoke merge logic for the Broad Street Subway / Market-Frankford Line; those routes flow through the same generic live-vehicle path as every other SEPTA route and will appear automatically if SEPTA ever emits live positions under their route IDs.
- **FR-009**: System MUST require no API key/credential for either the SEPTA GTFS-RT feed or the static GTFS zip.
- **FR-010**: System MUST log and retain the last-known-good static data for SEPTA (rather than clearing it) if a scheduled static refresh fails to fetch or extract the nested archive.

### Key Entities

- **SEPTA city configuration**: The set of feed URLs, keyless-auth marker, and telemetry flag that registers Philadelphia as a live-vehicle city, mirroring the shape of existing city configs (Atlanta, DC, Boston, New York, Toronto).
- **Nested static archive**: The GTFS zip file containing bus/trolley/streetcar/NHSL route and shape data, itself packaged one level inside SEPTA's top-level static zip download — a new shape of static-data source not previously handled.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can select Philadelphia from the city picker and see live-moving vehicle dots on real Philadelphia streets/rail corridors within one poll cycle of selecting the city.
- **SC-002**: At least 95% of live SEPTA vehicles observed over a verification session are route-attributed and rendered with correct route shape/color, consistent with the 100% route-ID alignment measured in the compatibility report.
- **SC-003**: Route shapes for all SEPTA bus, trolley, streetcar, and NHSL routes (145 of 147 static routes per the compatibility report) are visible on the map after static data loads.
- **SC-004**: All five previously-shipped cities show zero behavioral regression in a post-deployment smoke pass.
- **SC-005**: A user who opens the audio-unlock overlay or info panel while Philadelphia is selected sees Philadelphia-specific copy, not another city's text.

## Assumptions

- SEPTA's `google_bus.zip` (the nested archive holding bus/trolley/streetcar/NHSL data) keeps the same internal GTFS file names (`trips.txt`, `shapes.txt`, `routes.txt`) as every other agency's flat zip — only the extra packaging layer differs, not the file contract inside it.
- The Broad Street Subway / Market-Frankford Line's current lack of live vehicles is accepted as a known limitation at ship time, per the compatibility report's own recommendation to revisit at a different time of day rather than block onboarding on it.
- Regional Rail (`google_rail.zip`, commuter rail, `route_type=2`) is out of scope, consistent with how the rest of the platform's "Rail" category is reserved for `route_type` 0/1/2 heavy/light rail vehicles riding the *live-vehicle* feed — Regional Rail has no live-vehicle presence being onboarded here, only its static data is being excluded.
- Philadelphia's map origin uses its downtown transit-dense core (Center City, near the SEPTA hub at 15th & Market), matching the pattern set by Toronto's King/Queen/Yonge-core precedent rather than a geographic centroid.
- No new deployable, dependency, or credential is introduced — this is additive configuration plus a static-loader enhancement, consistent with every other config-only city onboarded so far.
