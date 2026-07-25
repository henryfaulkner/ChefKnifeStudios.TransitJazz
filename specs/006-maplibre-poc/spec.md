# Feature Specification: MapLibre + MapTiler Side-by-Side POC

**Feature Branch**: `006-maplibre-poc`
**Created**: 2026-05-17
**Status**: Draft
**Input**: User description: "MapLibre + MapTiler side-by-side POC to evaluate replacing Azure Maps"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Project Owner Decides Whether to Migrate Off Azure Maps (Priority: P1)

The project owner suspects that the current map provider is heavier and more expensive than the project's slim map requirements justify. To make an informed migration decision, they need a working, side-by-side comparison: the existing production map experience continues to run untouched, and a parallel page renders the same live transit data through a lightweight alternative. By the end of a single working day, the owner can sit with concrete measurements of cold-load time and animation smoothness from both pages — taken under live MARTA SignalR data during peak service hours — and make a binary migrate / don't-migrate decision based on pre-agreed pass/fail gates.

**Why this priority**: This is the entire purpose of the POC. Every other story exists only to make this decision possible. Without the comparison artifact and the pre-agreed gates, the migration question remains open indefinitely while operational and load-time costs continue to accrue.

**Independent Test**: Can be fully validated by opening the existing production transit map page and the new POC page back-to-back in a cold browser session during peak MARTA service hours, recording cold-load time and animation frame rate for both, and producing a written go/no-go decision against the pre-agreed gates.

**Acceptance Scenarios**:

1. **Given** the POC page is deployed and live MARTA data is flowing, **When** the project owner opens both pages in a cold browser session, **Then** they can measure cold-load time for each and the POC page's measurement is at least as fast as the existing page's measurement.
2. **Given** both pages are open during peak MARTA service hours, **When** approximately 200 vehicles are simultaneously rendered and animated, **Then** the POC page sustains a frame rate of at least 45 FPS during a typical 10-second data refresh interval.
3. **Given** the POC page is open with live data, **When** the user clicks a vehicle marker and then clicks an empty area of the map, **Then** the marker-click and map-body-click events fire and reach the application in the same way they do on the existing page.
4. **Given** all hard pass/fail gates have been measured, **When** the project owner reviews the results, **Then** a written decision (migrate / don't-migrate / explicit timeboxed extension) is produced before end-of-day with a clear reference to which gates passed or failed.

---

### User Story 2 - Visitor Experiences the Alternative Map Rendering With Live Data (Priority: P2)

A casual visitor (or the project owner standing in for one) opens the POC page in their browser and sees the Atlanta transit map render quickly, watches MARTA buses move smoothly along their routes in real time, and can click on individual buses. The experience must feel comparable to or better than the existing production page — particularly in how quickly the map first appears and how fluidly buses move. The route polylines must render correctly even when multiple routes are visible simultaneously.

**Why this priority**: This is the experiential evidence behind Story 1. Numeric measurements alone don't capture whether the result *feels* right for a soundscape-themed transit visualization. The visitor experience is the validation that the numbers translate to a usable product.

**Independent Test**: Can be fully tested by opening the POC page during peak MARTA service hours, observing the visual experience without instrumentation, and confirming the map renders, animates, and responds to clicks in a way that subjectively matches or exceeds the existing page.

**Acceptance Scenarios**:

1. **Given** a fresh browser session with cleared cache, **When** the visitor navigates to the POC page, **Then** the base map tiles become visible within roughly 1.5 seconds.
2. **Given** live MARTA data is flowing and at least 5 routes are visible, **When** the visitor watches the map, **Then** route polylines render correctly with no visible breaks or distortion and vehicles move smoothly along those routes.
3. **Given** a vehicle is mid-animation between data refreshes, **When** the next data batch arrives, **Then** the vehicle continues moving without a visible teleport or jump.
4. **Given** the map is rendering correctly, **When** the visitor judges the aesthetic look of the map style, **Then** the result is acceptable for a soundscape-themed transit visualization (subjective).

---

### User Story 3 - Future Maintainer Understands the POC Outcome (Priority: P3)

After the POC day concludes, a future maintainer (likely the project owner returning weeks later, or a collaborator) needs to understand what was tried, what was measured, and what was decided. The POC artifacts — the new page, the supporting interop code, the measurement notes, and the written decision — must be self-documenting enough that the migration question does not need to be re-litigated from scratch.

**Why this priority**: Without persistent artifacts of the POC outcome, the question reopens every few months as costs and load-time concerns recur. A clear record closes the question durably.

**Independent Test**: Can be tested by a person unfamiliar with the POC reading only the artifacts in the feature directory and the new POC page itself, and being able to explain (a) what was being evaluated, (b) what the pass/fail gates were, (c) what the measured outcomes were, and (d) what was decided.

**Acceptance Scenarios**:

1. **Given** the POC concludes with a migrate decision, **When** a future reader reviews the artifacts, **Then** they can locate the pass/fail gate results and the rationale for the decision.
2. **Given** the POC concludes with a don't-migrate decision, **When** a future reader reviews the artifacts, **Then** they can identify which gate(s) failed and what would need to change for the question to reopen.
3. **Given** the POC concludes inconclusively (e.g., noon checkpoint missed), **When** a future reader reviews the artifacts, **Then** the named blocker is documented along with the reasoning for the shelf decision.

---

### Edge Cases

- **MARTA service is unusually quiet during the POC measurement window** (e.g., weather event, off-peak): the 200-vehicle animation gate cannot be measured fairly. The POC must define how this case is handled — either reschedule the measurement window or use a synthesized 200-marker stress test as a fallback, with that fallback explicitly documented in the decision record.
- **The tile vendor's free tier issues a temporary rate limit or credential issue mid-POC**: the page must visibly degrade in a way that makes the cause obvious (rather than silently rendering blank tiles), so the POC day is not lost to debugging a false negative.
- **The POC page reaches the noon checkpoint with tiles visible but no marker**: this counts as a missed checkpoint per Story 1, scenario 4. The decision is "inconclusive with named blocker," not "extend."
- **A hard gate result is borderline** (e.g., 43 FPS instead of ≥45 FPS): the default decision is don't-migrate, not "close enough." Borderline results may justify a future deeper investigation but do not satisfy this POC's gates.
- **The visitor switches browser tabs during animation and returns**: the animation must resume correctly without leaving stale or duplicated markers on the map.
- **A vehicle's data shows it transferring to a different route between refreshes**: the animation must handle this without rendering a jagged line across the city; the existing production page handles this via a teleport, and the POC must match that behavior.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a new page within the existing client application that renders an interactive map using the alternative (lightweight) map provider stack, accessible without affecting any existing page.
- **FR-002**: The existing production transit map page MUST remain fully functional and untouched for the duration of the POC, to serve as the baseline for comparison.
- **FR-003**: The POC page MUST consume live MARTA vehicle data from the same real-time data source used by the existing production page (no synthesized or replayed data for the primary measurement run).
- **FR-004**: The POC page MUST render route polylines for active MARTA bus routes, supporting at least 5 routes simultaneously visible, each containing up to approximately 3,000 geographic points.
- **FR-005**: The POC page MUST render and animate vehicle markers along their assigned route polylines, interpolating each vehicle's position smoothly between successive real-time data refreshes.
- **FR-006**: The POC page MUST handle approximately 200 simultaneously rendered and animated vehicle markers during peak service hours.
- **FR-007**: The POC page MUST allow programmatic and user-driven zoom and pan of the map.
- **FR-008**: The POC page MUST support clicking on a vehicle marker and surface that click to the application layer in a way functionally equivalent to the existing page.
- **FR-009**: The POC page MUST support clicking on an empty area of the map (a "body click") and surface that event to the application layer.
- **FR-010**: The POC page MUST support programmatically centering the map on a specified vehicle marker.
- **FR-011**: The POC page MUST render the base map at a quality and style appropriate to a soundscape-themed transit visualization (single style — no style switcher, no satellite or traffic overlay).
- **FR-012**: The system MUST allow measurement of the cold-load time of the POC page (time from navigation start to base map tiles being visible) under typical home-internet conditions and a cold browser cache, using standardized in-browser performance instrumentation rather than visual estimation alone.
- **FR-013**: The system MUST allow measurement of the sustained animation frame rate of the POC page during a typical real-time data refresh interval (approximately 10 seconds), with at least 200 active markers, using browser performance-recording tooling that captures per-frame timing rather than visual estimation alone.
- **FR-014**: The POC page and the baseline page MUST be instrumented with the same set of named performance measurements so that side-by-side comparison is exact rather than impressionistic. At minimum, both pages must measure: cold-load time (navigation start to tiles visible), per-frame rendering time during a live data refresh interval, count of long tasks (frames exceeding 50 milliseconds) during that interval, and total transferred bytes for first-page load.
- **FR-015**: The POC MUST produce a written decision record at the end of the POC day that captures: which hard gates passed or failed, the measured values for each gate quoted directly from the performance instrumentation (not from subjective impression), and the resulting decision (migrate / don't-migrate / explicit timeboxed extension with named blocker).
- **FR-016**: The POC MUST be timeboxed to a single working day with a noon checkpoint; if the noon checkpoint (base map tiles plus at least one rendered marker on the POC page) is missed, the POC outcome is "inconclusive with named blocker" and not a silent extension. The noon checkpoint is intentionally qualitative ("are tiles and one marker on screen?") — performance instrumentation is added in the afternoon once the rendering pipeline is working, not before.
- **FR-017**: The POC MUST be conducted during peak MARTA bus service hours to ensure realistic data volume; if peak hours cannot be reached on the POC day, the measurement window is rescheduled rather than substituted with synthesized data.
- **FR-018**: The POC page MUST visibly indicate any tile-provider failure (e.g., authentication or rate-limit error) so that map issues are not silently misattributed to other causes during measurement.
- **FR-019**: All POC artifacts — the new page, supporting interop assets, the captured performance measurements, and the decision record — MUST be discoverable in a way consistent with how the existing per-provider test page is organized in the codebase.

### Key Entities *(include if feature involves data)*

- **POC Page**: A new client page hosting the alternative map provider stack. Consumes the same live real-time vehicle data stream as the existing production page. Exists in parallel to, not as a replacement for, the existing production page during the POC.
- **Baseline Page**: The existing production transit map page. Untouched for the duration of the POC. Provides the reference measurements that the POC page's measurements are compared against.
- **Vehicle Animation Record**: One real-time data event describing a single vehicle's movement between two successive snapshots, including the prior and current snap-to-route positions, the data interval duration, and the vehicle's current route. Same shape as is used by the baseline page.
- **Route Geometry Record**: An ordered set of geographic points defining a single MARTA bus route's path. Same shape as is used by the baseline page; loaded once per session.
- **Hard Gate**: A pre-agreed pass/fail criterion that, if failed, blocks the migration decision. The four hard gates for this POC are cold-load time, sustained animation frame rate, multi-route polyline rendering, and click-event interop.
- **Performance Measurement Set**: The standardized set of named, instrumented measurements captured identically on both the POC page and the baseline page. Includes at minimum: cold-load time (navigation start to tiles visible), per-frame rendering time during a live data refresh interval, count of long tasks during that interval, and total transferred bytes for first-page load. The measurement set exists so that the comparison between pages is reproducible rather than impressionistic.
- **Decision Record**: A written artifact produced at end-of-POC-day capturing the measured value of each hard gate (sourced from the performance measurement set, not from subjective impression) and the resulting migration decision with its rationale.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The POC concludes within one working day with a written, binary decision — migrate, don't migrate, or extend with a named blocker — and does not silently roll over.
- **SC-002**: By noon on the POC day, the POC page renders base map tiles and at least one vehicle marker; if not, the POC is closed as inconclusive with the named blocker recorded.
- **SC-003**: The POC page achieves a cold-load time (from navigation start to base map tiles visible) of approximately 1.5 seconds or less on a typical home internet connection with a cold browser cache, and is measurably faster than the baseline page under the same conditions. "Measurably faster" means a difference observable in the instrumented measurement, not in subjective impression.
- **SC-004**: The POC page sustains an animation frame rate of at least 45 FPS during a typical 10-second real-time data refresh interval, while rendering and animating approximately 200 vehicle markers driven by live MARTA data during peak service hours, as captured by browser performance-recording tooling rather than visual judgment.
- **SC-005**: During the same 10-second measurement interval, the POC page produces zero or near-zero long tasks (frames exceeding 50 milliseconds), comparable to or better than the baseline page measured under the same conditions.
- **SC-006**: The POC page renders at least 5 simultaneously visible route polylines (each up to approximately 3,000 points) without visible rendering defects or sustained frame-rate degradation below the 45 FPS floor.
- **SC-007**: Marker-click and map-body-click events from the POC page reach the application layer with behavior functionally equivalent to the baseline page, verified by direct interaction.
- **SC-008**: The decision record produced at end-of-POC-day quotes specific numeric measurements (cold-load milliseconds, median and worst-case frame timings, long-task counts, transferred bytes) from the captured performance measurement set for both pages; subjective phrases such as "felt smoother" or "seemed faster" alone are not sufficient evidence for any gate.
- **SC-009**: A future reader unfamiliar with the POC can determine the evaluated alternative, the pass/fail gates, the measured outcomes (numeric, not impressionistic), and the decision by reviewing only the artifacts in the feature directory and the POC page itself.
- **SC-010**: A migrate decision is reached only when all four hard gates pass against the instrumented measurements; any single hard-gate failure or borderline result defaults to don't-migrate, with the reason recorded.

## Assumptions

- The POC is a vendor and stack evaluation only. A "migrate" decision from this POC authorizes a follow-on migration feature; it does not itself perform the migration.
- The project's required map feature set has already been agreed: base map tiles, vehicle markers, route polylines, per-route show/hide, click handlers, zoom/pan, and programmatic center-on-pin. Style switching, satellite/night/hybrid styles, and traffic overlays are explicitly out of scope and need not be evaluated.
- Per-route show/hide and programmatic center-on-pin are considered low-risk and are deferred to the migration phase; they are not gated as part of this POC.
- The POC consumes the same live data source as the existing production page; standing up replay or synthesized streams is out of scope for the primary measurement.
- "Peak MARTA service hours" means a weekday rush window during which roughly 200 active vehicles are reporting positions.
- "Cold load" means a fresh browser session with no relevant cached assets, on a typical home internet connection (broadly representative of ~50 Mbps residential service).
- The project budget tolerance is "hobby" — the POC's purpose includes confirming that the alternative provider's free tier and operational cost profile fit a hobby budget with hard ceilings and resilience to traffic spikes.
- The decision record produced at end-of-POC-day is the authoritative artifact for closing the migration question for the foreseeable future; reopening it later requires new information beyond what this POC measures.
- The existing production page's animation interpolation logic is largely portable between map providers; this assumption is itself one of the things the POC validates implicitly by passing or failing the animation frame-rate gate.
- The selected alternative tile provider's free tier limits are adequate for the POC measurement window; the POC does not test sustained production traffic against those limits.
- Browser-native performance instrumentation (per-frame timing, long-task observation, navigation timing, transferred-byte accounting) is sufficient for the POC's measurement needs; specialized profiling tooling beyond what a standard developer browser provides is out of scope.
- Both the POC page and the baseline page are measured in the same browser, on the same machine, on the same network, in the same session, with the same cache state for each cold-load measurement; cross-machine or cross-browser variation is out of scope for this POC.
