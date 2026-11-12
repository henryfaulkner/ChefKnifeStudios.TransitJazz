# Feature Specification: Client Experience Observability

**Feature Branch**: `065-client-experience-observability`  
**Created**: 2026-11-08  
**Status**: Draft  
**Input**: User description: `docs/CLIENT_TOAST_OBSERVABILITY_DESIGN_PLAN.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See whether core journeys succeed (Priority: P1)

An operator can see how often visitors open a city, reach a usable map, establish live delivery, and unlock or resume audio. Successful and failed attempts contribute to the same eligible population, including sessions with no notification.

**Why this priority**: Confirmed outcomes answer whether the experience works; notification counts cannot.

**Independent Test**: Complete successful and failed attempts for each journey, including an inline audio failure. Verify counts, elapsed readiness, and unresolved attempts without relying on toasts.

**Acceptance Scenarios**:

1. **Given** current-city routes and map become usable, **when** readiness is confirmed, **then** one eligible city-start attempt is counted as successful with its readiness time.
2. **Given** live transport connects without confirmed current-city membership, **when** live-start outcomes are reported, **then** the attempt is not counted as successful.
3. **Given** audio unlock fails inline without a toast, **when** audio attempts are reported, **then** its failed attempt appears with zero presentations.
4. **Given** an attempt has no confirmed terminal outcome, **when** its cohort is reported, **then** it remains unresolved rather than becoming an assumed success or failure.

---

### User Story 2 - Understand degraded time and recovery (Priority: P2)

An operator can compare requested capability health for routes, map, live delivery, and audio, including healthy, degraded, unavailable, pending, and unknown time. The view separates deliberate pauses and observation gaps from eligible time and identifies verified recovery.

**Why this priority**: Startup results do not show how much of an active visit remained usable.

**Independent Test**: Exercise a foreground visit with confirmed degradation and recovery, then hide or suspend the page. Verify eligible time and recovery duration exclude hidden and uncertain intervals.

**Acceptance Scenarios**:

1. **Given** requested live delivery becomes unavailable, **when** current-city membership is later confirmed, **then** the episode has one measured degraded interval and one confirmed recovery.
2. **Given** the page is hidden, delivery is deliberately paused, or audio is muted, **when** health is calculated, **then** excluded time is separate from eligible time.
3. **Given** a page stops reporting during degradation, **when** the episode is reviewed, **then** it is incomplete and no recovery is inferred.
4. **Given** a quiet live feed without an agreed update cadence, **when** freshness is displayed, **then** its expected freshness is unknown, not an outage.

---

### User Story 3 - Relate feedback to actual health (Priority: P3)

An operator can see which warning and error toasts the application presented, their issue classifications and displayed severity, beside independently recorded capability outcomes.

**Why this priority**: Toast volume can change through grouping and suppression without a change in visitor experience.

**Independent Test**: Cause a confirmed failure, a suppressed notification, a combined toast, and an escalation. Verify underlying outcomes remain observable and only accepted presentations are counted.

**Acceptance Scenarios**:

1. **Given** a combined toast is accepted for the current city, **when** presentations are counted, **then** it contributes one presentation with all represented issues and its displayed severity.
2. **Given** a notification is suppressed, stale, or fails to present, **when** presentations are counted, **then** it contributes zero while its eligible owner outcome remains observable.
3. **Given** an accepted warning later escalates to an accepted error, **when** presentations are reviewed, **then** they remain two distinct presentations.

---

### User Story 4 - Detect incidents and collection failures (Priority: P4)

An operator receives actionable alerts for evidenced journey failure, sustained capability degradation, urgent confirmed audio-stop failure, and collection loss. Independent browser checks cover startup failures the page cannot report itself.

**Why this priority**: A quiet dashboard can mean healthy traffic, low traffic, or failed collection.

**Independent Test**: Use known healthy, failing, low-traffic, delayed, and missing-data observations. Verify alert firing, routing, and recovery in a controlled environment.

**Acceptance Scenarios**:

1. **Given** enough fresh eligible attempts and high confirmed failure share, **when** the alert window is evaluated, **then** a journey incident is raised with its city and journey classification.
2. **Given** a confirmed stop request leaves audio playing, **when** that outcome is observed, **then** an urgent alert can fire even without a toast or high traffic.
3. **Given** observations are delayed, missing, or too sparse, **when** alerts are evaluated, **then** coverage is marked insufficient and an existing incident is not silently resolved.
4. **Given** the application fails before browser reporting starts, **when** an independent check runs, **then** startup failure remains detectable and is distinguished from check-runner failure.

### Edge Cases

- Duplicate callbacks, internal retries, lost acknowledgements, page reloads, and multiple tabs must not create duplicate attempts or stored observations.
- Results may arrive before starts; unmatched results remain visible quality issues and are excluded from primary rates until reconciled.
- Conflicting results, overlapping intervals, unsupported classifications, and malformed events must not bias valid totals or prevent other valid observations from being accepted.
- Delayed browser timers, frozen tabs, and abrupt termination create unknown gaps or incomplete episodes, never fabricated continuous health or recovery.
- A stop attempt remains eligible after mute is requested. An unlock attempt remains eligible while audio is locked; continuing muted before its result cancels it.
- A failed notification presenter, unavailable local storage, or failed reporting request must not interrupt map, audio, controls, or notification reading time.
- A valid zero count, zero denominator, unexpected no-data result, and failed report query must have distinct operator meanings.
- A stale result after city change must not be attributed to the new city. Full page navigation starts a new page session.
- Browser observations cannot prove a real person saw or heard the experience; views must state the coverage limits.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST record eligible starts and at most one terminal result for application bootstrap, city map startup, live delivery start/resume, audio unlock/resume, explicit settings and map/audio changes, audio stop, and observable source-city navigation. A new accepted user retry MUST create a new attempt; internal retries MUST stay within the existing attempt.
- **FR-002**: Journey success MUST require confirmation from its responsible capability. City readiness requires usable current-city routes and map; live readiness requires connected transport and confirmed city membership; audio unlock requires confirmed running audio; requested audio health also requires ready voices.
- **FR-003**: Confirmed failure, deadline exceeded, cancellation, supersession, and unresolved status MUST remain distinct. Measurement deadlines MUST NOT change application behavior or turn a missing report into an inferred product failure.
- **FR-004**: Readiness MUST use eligible foreground elapsed time and retain wall elapsed time for diagnosis. Initial measurement budgets are 60 foreground seconds for bootstrap/city startup, 30 for live start/resume, and 15 for audio unlock/resume and explicit settings/stop actions. Changes to these definitions MUST be versioned.
- **FR-005**: The system MUST distinguish a page session from each city activation and correlate attempts, episodes, and presentations only within valid scope. Attempt correlation MUST expire after 24 hours without converting an unresolved result into failure. Duplicate, stale, or out-of-scope outcomes MUST NOT create accepted observations.
- **FR-006**: The system MUST observe requested/eligible state and confirmed `pending`, `healthy`, `degraded`, `unavailable`, `unknown`, and `not requested` states for routes, map, live delivery, and audio. Unchanged state reports MUST NOT create new transitions.
- **FR-007**: The system MUST measure bounded non-overlapping foreground intervals, with capability-state durations, deliberate exclusions, and uncertain gaps. Hidden or suspended time MUST NOT be filled with an assumed prior state.
- **FR-008**: Recovery MUST require verified owner evidence within the same city/capability episode. Dismissal, toast expiry, online status, page disposal, absence of errors, and reporting loss MUST NOT establish recovery.
- **FR-009**: Live freshness MUST remain unknown without evidence of an expected update cadence. Coarse processing observations MAY be reported but MUST NOT assert that a quiet feed is unhealthy or that audio reached a listener's speakers.
- **FR-010**: A bounded page-start observation MUST be captured before the main application initializes and transferred into the same page-session record without duplication. Operator guidance MUST state that failures before this capture point require independent checks.
- **FR-011**: Every qualifying start, confirmed outcome, state transition, and accepted presentation MUST be captured without normal-operation sampling, including successful sessions and inline-only failures.
- **FR-012**: One presentation MUST be recorded only after a current-scope warning/error toast is accepted. It MUST retain displayed severity, participating and explained issue codes, safe message/action classifications, city, and release. Suppressed, stale, failed, success, and inline-only feedback MUST contribute no presentation.
- **FR-013**: Health measurement MUST be independent of notification grouping, grace, suppression, feature gating, and presenter availability. Known direct associations MUST be preserved; causation MUST NOT be inferred from timing alone.
- **FR-014**: Explicit settings effects and persistence MUST have separate outcomes under one parent action. Reporting MUST distinguish a failed effect, confirmed session-only change, and failed stop that leaves audio playing.
- **FR-015**: A fixed versioned classification catalog MUST cover event kinds, journeys, capabilities, actions, results, reasons, and every existing notification issue code. Unknown classifications MUST NOT corrupt accepted observations.
- **FR-016**: Submitted observations MUST exclude raw setting values, route/vehicle IDs, coordinates, full URLs, access tokens, raw exception text, and durable visitor IDs. This feature MUST NOT collect a clickstream, per-note/per-vehicle/per-frame activity, or session replay.
- **FR-017**: Observations MUST be queued within a browser budget initially set to 1,000 events or 4 MiB and retried across offline periods, reloads, and city changes for up to seven days. An observation MUST be removed only after durable acknowledgement, retain its identity on replay, and contribute bounded loss diagnostics if discarded by age, capacity, or storage limits.
- **FR-018**: A bounded submission MUST accept valid observations independently, reject invalid members with safe reasons, prevent duplicate storage, and acknowledge none when durable storage fails. Submission size and rate MUST be constrained without placing a secret in the browser.
- **FR-019**: Reporting, local storage, and collection failures MUST remain passive: they MUST NOT change map rendering, playback, controls, notification timing, or visitor-visible error behavior, and MUST NOT trigger recursive notifications.
- **FR-020**: Detailed observations MUST be available for 30 days and daily trends for 13 months by default. Late corrections within the supported delivery/correlation horizon MUST be incorporated without replay inflation; detail MUST NOT expire before its daily trend is safely available.
- **FR-021**: Journey reports MUST use eligible-start cohorts and show succeeded, confirmed bad, unresolved, and excluded counts; sample size; success/bad/unresolved shares; and provisional status. Zero-denominator rates MUST be `N/A`; unmatched results MUST appear as coverage issues.
- **FR-022**: Health reports MUST show per-capability healthy/degraded/unavailable/pending/unknown observed time, excluded time, gaps, verified recovery duration, and incomplete episodes. Overlapping capability time MUST NOT be summed into one application availability percentage.
- **FR-023**: Operator views MUST separate experience health, feedback, and collection health. Comparisons MUST show city, environment, release, compatible measurement definition, delivery delay, affected page-session count, and coverage.
- **FR-024**: Operator views MUST show readiness distributions, action reliability, recent safe evidence, long-term weighted trends, presentation classifications, collection loss, and data-quality exceptions. Multi-day rates MUST use summed eligible counts; approximate latency and daily session-day counts MUST be labeled.
- **FR-025**: Health alerts MUST use confirmed owner outcomes and observed degradation independently of toast volume. Initial journey failure criteria are at least 10% confirmed bad outcomes among at least 20 eligible attempts and three affected page sessions in a 15-minute start cohort. Initial sustained degradation criteria are at least 10% degraded/unavailable time among at least 10 observed session-minutes and three affected page sessions in 10 minutes. One fresh confirmed audio-stop failure MUST alert without a traffic minimum. More than 10% unresolved or unknown/gap coverage, or insufficient fresh evidence, MUST prevent a rate-based healthy/failing conclusion. These thresholds MUST be configurable and labeled provisional.
- **FR-026**: Collection alerts MUST identify ingestion/storage failure, unavailable collection/query paths, and stalled trend maintenance. Unexpected no data or insufficient coverage MUST NOT be treated as healthy recovery or client failure.
- **FR-027**: Independent browser checks MUST run every five minutes by default, cover page bootstrap and city/map/live readiness when page telemetry is absent, distinguish application failure from runner failure, and exclude controlled traffic from real-visitor denominators.
- **FR-028**: Operators MUST have a runbook explaining definitions, exclusions, delayed replay, low traffic, collection outages, version changes, alert evaluation, and coverage limits. Alert delivery and recovery MUST be verified in a controlled environment before operational routing.
- **FR-029**: Existing playback, notification timing, route identity, and background execution behavior MUST be preserved. Formal reliability objectives, source heartbeat contracts, cross-document navigation correlation, background-audio health, and general browser performance scores are outside initial scope.

### Key Entities *(include if feature involves data)*

- **Page session**: One observed document load; identifies a visit without identifying a person and contains city activations.
- **City activation**: One current-city scope within a page session; owns its attempts and capability episodes.
- **Journey attempt**: One accepted action or startup request, its eligibility, deadline, elapsed time, and at most one terminal result.
- **Capability episode**: Confirmed states for one capability and city, including degradation and any verified recovery.
- **Observation interval**: Bounded observed foreground time with capability-state durations, exclusions, and gaps.
- **Toast presentation**: One accepted warning/error notification with immutable displayed classification and known issue/episode associations.
- **Collection receipt and daily trend**: Durable observation and its derived, correction-aware long-term counts or durations, with quality and version context.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In controlled successful and failing journeys, 100% of eligible starts have one correlated attempt and each confirmed terminal result appears at most once, including after retry or replay.
- **SC-002**: A controlled healthy session with zero warning/error toasts still contributes eligible successful starts and observed healthy time; an inline unlock failure appears with zero presentations.
- **SC-003**: Known cohort examples produce exact succeeded, bad, unresolved, and excluded counts; zero-denominator rates display `N/A` in 100% of tested views.
- **SC-004**: In controlled visibility, suspension, and recovery scenarios, no unobserved interval is reported as healthy/degraded time and no episode is marked recovered without confirmed evidence.
- **SC-005**: Every accepted toast in controlled scenarios contributes exactly one presentation; suppressed, stale, and failed presentations contribute zero, while underlying eligible outcomes remain visible.
- **SC-006**: After a recoverable offline interval and return within seven days, 100% of retained controlled observations reach durable totals exactly once, including across reloads and multiple tabs.
- **SC-007**: Operator views show 30 days of detailed observations and 13 months of daily trends, with sample size and coverage visible for every rate and duration comparison.
- **SC-008**: Controlled exercises demonstrate a routed confirmed-health incident, an urgent audio-stop incident at low traffic, a collection failure, and recovery only after an adequately observed healthy window.
- **SC-009**: A controlled startup failure that prevents page reporting is detected by an independent check within two scheduled checks; runner failure is reported separately.
- **SC-010**: Under normal and flapping/offline test conditions, capture and delivery stay within the configured browser queue and event-rate budgets; 100% of required map, audio, and notification interaction scenarios still pass release acceptance checks.

## Assumptions

- Existing capability owners remain the authority for confirmed outcomes; this feature observes them without changing their behavior.
- Initial deadlines, cadence, collection limits, retention, and alert thresholds in the source design are provisional measurement defaults to validate under representative load.
- The browser retry horizon is seven days. A visitor who never returns, clears storage, or closes before local persistence may leave observations missing; coverage reporting must make this limitation visible.
- Tabs and reloads represent separate page sessions, not unique visitors. Source navigation and destination startup are measured separately until a cross-document correlation contract exists.
- Existing operator monitoring and alert-routing services can be configured for the new observations; production access and actual delivery require rollout verification.
- Formal reliability targets follow at least two representative weeks of observed traffic and coverage review. Initial alert thresholds are starting points, not established service objectives.
