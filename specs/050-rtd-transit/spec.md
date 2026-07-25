# Feature Specification: RTD Denver Transit City

**Feature Branch**: `050-rtd-transit`
**Created**: 2026-07-25
**Status**: Draft
**Input**: User description: "Add RTD (Regional Transportation District, Denver, Colorado) as a live-vehicle transit city, per docs/city-compat/rtd.md (92.4/100, Drop-in). This is a config-only fork: RTD's single keyless GTFS-RT feed carries buses, light rail (route_type=0), and commuter rail (route_type=2) together with 100% route_id/lat/lon coverage, so RTD falls into the existing `else` arm of the Worker's city-registry factory and is served by the config-driven GtfsRtCity — zero new classes, same as WMATA/MBTA/TTC/SEPTA. Bus route_id alignment is 89.2% verbatim (83/93 matched). Of the 10 unmatched RT route_ids, 8 are rail that need an 8-entry RailRouteIdMap config entry (identical mechanism to WMATA's existing RailRouteIdMap) to resolve to static's plain line-letter route_short_names. The remaining 2 unmatched bus IDs (BOND, FREE) are a small residual gap left as an out-of-scope follow-up. Static GTFS zip 308-redirects to a download endpoint — must follow redirects, no special unwrapping needed, unlike SEPTA's nested zip-of-zips. Both feeds are keyless. Standard registration touch-points: CityNames.Rtd constant, Worker+WebAPI appsettings.json Cities: entries (byte-identical, keyless, includes RailRouteIdMap), CityFab.razor picker button (\"Denver, CO\"), map origin at Denver Union Station / downtown transit core, AudioUnlockOverlay + InfoFab copy mentioning buses + light rail + commuter rail."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Listen to live Denver transit (Priority: P1)

A visitor opens the app, selects Denver from the city picker, and hears/sees RTD buses, light rail trains, and commuter rail trains moving through the Denver metro in real time, exactly as they can today for Atlanta, DC, Boston, New York, Toronto, and Philadelphia.

**Why this priority**: This is the entire feature — without it, nothing else in this spec has value. It's independently shippable: RTD's live-vehicle feed is keyless, single-endpoint, and 100% route/lat/lon-attributed, so this story can be delivered without any dependency on the BOND/FREE bus-ID follow-up.

**Independent Test**: Select "Denver, CO" from the city picker, confirm vehicle dots representing buses, light rail, and commuter rail render on the map near real Denver streets/rail corridors and move over successive poll cycles, and confirm audio plays for crossings.

**Acceptance Scenarios**:

1. **Given** the app is loaded, **When** a user opens the city picker, **Then** a "Denver, CO" option is present and selectable.
2. **Given** a user has selected Denver, **When** the map loads, **Then** it centers on Denver's downtown transit core and RTD route shapes (bus, light rail, commuter rail) are visible.
3. **Given** Denver is selected and live vehicles are being polled, **When** an RTD vehicle crosses a trigger point on a route the user hasn't muted, **Then** the corresponding instrument sounds.
4. **Given** a user has selected Denver, **When** they open the audio-unlock overlay and the info panel, **Then** both display Denver/RTD-specific descriptive copy (not another city's placeholder text).

---

### User Story 2 - Light rail and commuter rail resolve correctly despite RTD's prefixed route IDs (Priority: P1)

The system fetches RTD's live vehicle feed, which reports rail vehicles under numeric-prefixed route IDs (`101C`, `101E`, `101T`, `103W`, `107R`, `113B`, `113G`, `117N`) that don't verbatim-match the static schedule's plain line-letter route names (`C`, `E`, `T`, `W`, `R`, `B`, `G`, `N`). The system must remap these RT IDs to their static counterparts so rail vehicles are attributed to the correct route shape, color, and instrument, the same way WMATA's Metro lines are already remapped today.

**Why this priority**: Without this, User Story 1 only half-works for rail — light rail and commuter rail vehicles (54 of 357 vehicles in the compatibility snapshot, spanning all 8 lines) would render as unmatched/unknown rather than correctly attributed. This is a hard prerequisite for a usable RTD rail experience, not a nice-to-have.

**Independent Test**: Configure the city with the 8-entry rail route-ID map and confirm, over a live poll cycle, that vehicles reporting `101C`/`101E`/`101T`/`103W`/`107R`/`113B`/`113G`/`117N` resolve to static routes `C`/`E`/`T`/`W`/`R`/`B`/`G`/`N` respectively, with correct shape/color, while a vehicle reporting the already-matching `A` continues to resolve with no remap needed.

**Acceptance Scenarios**:

1. **Given** RTD is configured with its rail route-ID map, **When** a live vehicle reports route_id `103W`, **Then** it is attributed to static route `W` (the light rail line to Golden), not left unmatched.
2. **Given** RTD is configured, **When** a live vehicle reports route_id `A`, **Then** it resolves against static route `A` with no remap applied.
3. **Given** an existing city with no rail route-ID map configured, **When** the live-vehicle matching logic runs after this feature ships, **Then** its behavior and output are unchanged.

---

### User Story 3 - Existing cities remain unaffected (Priority: P2)

Operators and users of the previously-shipped cities (Atlanta, DC, Boston, New York, Toronto, Philadelphia) see no change in behavior, performance, or data correctness after RTD is added.

**Why this priority**: This is a regression-prevention story rather than new value delivery — important to verify, but it doesn't gate initial RTD usability the way Stories 1-2 do.

**Independent Test**: After deployment, run through the existing per-city smoke checks (feed reachability, shapes loading, live vehicles rendering/moving, audio) for each previously-shipped city and confirm no regressions.

**Acceptance Scenarios**:

1. **Given** the app has been updated to include RTD, **When** a user selects any previously-shipped city, **Then** that city's live vehicles, route shapes, and audio behave exactly as before this feature.

---

### Edge Cases

- What happens when RTD's static GTFS zip URL responds with the 308 redirect it currently uses? The static loader must follow the redirect to the actual download and process the resulting file as a normal flat zip — no special unwrapping (unlike Philadelphia's nested zip-of-zips).
- What happens for the two genuinely-unresolved bus route IDs (`BOND`, `FREE`)? They are not specially excluded or remapped in this feature — they fall into the platform's existing "unknown route" handling, unchanged from how every other city handles unmatched routes today. Resolving them is an explicit non-goal, left as documented follow-up in the compatibility report.
- What happens if RTD's rail route-ID scheme changes upstream (e.g., a prefix is renumbered) such that a configured remap entry no longer matches any live route_id? That line's vehicles fall back to "unknown route" handling, the same as any other unmatched live route — no crash, no partial data swap.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to select Denver/RTD from the existing city picker.
- **FR-002**: System MUST fetch and render live RTD vehicle positions (buses, light rail, commuter rail) on the map, matched to their static route shapes.
- **FR-003**: System MUST remap RTD's 8 numeric-prefixed rail route IDs (`101C`, `101E`, `101T`, `103W`, `107R`, `113B`, `113G`, `117N`) to their corresponding static plain-letter route short names (`C`, `E`, `T`, `W`, `R`, `B`, `G`, `N`) using the platform's existing config-only rail-route-ID-map mechanism, with no new code.
- **FR-004**: System MUST fetch RTD's static route/shape data from its published zip URL, following the 308 redirect to the actual file, and process it as a standard flat (non-nested) zip.
- **FR-005**: System MUST center the map on Denver's downtown transit-dense core (not the region's geographic centroid) when a user selects Denver.
- **FR-006**: System MUST display Denver/RTD-specific descriptive copy in the audio-unlock overlay (header + three paragraphs) and the info panel when Denver is the selected city, referencing buses, light rail, and commuter rail.
- **FR-007**: System MUST require no API key/credential for either the RTD GTFS-RT feed or the static GTFS zip.
- **FR-008**: System MUST NOT attempt to resolve the two residual unmatched bus route IDs (`BOND`, `FREE`) in this feature — they are left to fall into existing "unknown route" handling.
- **FR-009**: System MUST continue to process every existing city's live-vehicle matching and static-zip loading exactly as it does today — the RTD rail remap MUST be additive per-city configuration, not a change to shared matching logic behavior for cities without a rail route-ID map.

### Key Entities

- **RTD city configuration**: The set of feed URLs, keyless-auth marker, rail route-ID map, and telemetry flag that registers Denver as a live-vehicle city, mirroring the shape of existing city configs (Atlanta, DC, Boston, New York, Toronto, Philadelphia) and reusing WMATA's precedent for the rail-route-ID-map field.
- **Rail route-ID map entry**: A single RT-route-ID-to-static-route-short-name pair (e.g., `103W` → `W`); 8 such entries fully resolve RTD's rail lines.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can select Denver from the city picker and see live-moving vehicle dots on real Denver streets/rail corridors within one poll cycle of selecting the city.
- **SC-002**: At least 95% of live RTD vehicles observed over a verification session are route-attributed and rendered with correct route shape/color, consistent with the 100% route-ID alignment measured in the compatibility report once the rail remap is applied (89.2% verbatim + 8 remapped rail IDs covering the remaining rail vehicles).
- **SC-003**: All 8 RTD light-rail and commuter-rail lines (`A`, `B`, `C`, `E`, `G`, `N`, `R`, `W`) are visible and correctly attributed on the map after static data loads and the rail remap is applied.
- **SC-004**: All previously-shipped cities show zero behavioral regression in a post-deployment smoke pass.
- **SC-005**: A user who opens the audio-unlock overlay or info panel while Denver is selected sees Denver-specific copy, not another city's text.

## Assumptions

- RTD's static GTFS zip keeps its current 308-redirect-to-download-endpoint behavior and standard flat internal packaging (`trips.txt`, `shapes.txt`, `routes.txt` at the top level of the fetched file) — no nested-zip handling is needed, unlike Philadelphia.
- The `BOND` and `FREE` bus route-ID gap is accepted as a known, low-impact limitation at ship time (2 of 93 distinct route IDs), consistent with the compatibility report's framing of it as an optional follow-up rather than a blocking defect.
- Denver's map origin uses its downtown transit-dense core (near Denver Union Station), matching the pattern set by Toronto's King/Queen/Yonge-core and Philadelphia's Center City precedents rather than a geographic centroid.
- No new deployable, dependency, or credential is introduced — this is additive configuration only, reusing the existing `RailRouteIdMap` mechanism already proven by WMATA, consistent with every other config-only city onboarded so far.
