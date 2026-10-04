# Feature Specification: City and Transit Type Insights

**Feature Branch**: `056-city-transit-type-insights`  
**Created**: 2026-10-01  
**Status**: Draft  
**Input**: User description: `docs/CITY_TRANSIT_TYPE_INSIGHTS_DESIGN_DOCUMENT.md`

Provide historical, server-observed movement, activity, and soundscape statistics for each city and its configured transit categories. Every result must explain what was observed, its unit, its time range, and how much of that range has trustworthy coverage.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Understand hourly observed movement (Priority: P1)

An analyst selects a city, transit category, and complete hour to learn how far its observed vehicles moved on average. Total distance and a per-update diagnostic explain the estimate.

**Why this priority**: Distance per observed vehicle-hour answers the primary movement question with a clear denominator.

**Independent Test**: Supply a complete hour with known observations and compare the returned numerators, denominators, and averages with hand calculations.

**Acceptance Scenarios**:

1. **Given** a complete hour with three distinct active vehicles, 1,200 accepted meters, and two eligible movement intervals, **When** movement is requested, **Then** the result reports 400 meters per observed vehicle-hour, 600 meters per vehicle update, 1,200 total meters, three observed vehicle-hours, and two intervals.
2. **Given** an active vehicle without accepted movement, **When** its complete hour is summarized, **Then** it contributes one vehicle to the denominator and zero distance.
3. **Given** eligible forward, reverse, and stationary intervals, **When** movement is summarized, **Then** absolute changes contribute distance and each stationary interval contributes zero meters and one interval.
4. **Given** first sightings, repeated or stale positions, route changes, invalid geometry, or jumps greater than 2,000 meters, **When** movement is summarized, **Then** those intervals contribute neither distance nor eligible interval counts and are accounted for diagnostically.
5. **Given** two complete hours containing 1,200 meters across three vehicles and 1,800 meters across two vehicles, **When** the period is summarized, **Then** the average is 3,000 / 5 = 600 meters per observed vehicle-hour; a vehicle present in both hours contributes to each hour's denominator.

---

### User Story 2 - Understand average observed activity (Priority: P1)

An analyst selects a city, category, and period to learn the average number of vehicles observed in eligible collection cycles, including stationary vehicles and valid zero counts.

**Why this priority**: Activity supports comparisons without confusing sampled counts with totals or a viewer's route selection.

**Independent Test**: Supply cycles with known distinct vehicle counts, duplicate observations, and valid zeros; verify the weighted mean and coverage independently of movement and crossings.

**Acceptance Scenarios**:

1. **Given** three eligible cycles with counts of two, zero, and four, **When** activity is summarized, **Then** the result reports two average active vehicles and three valid samples.
2. **Given** duplicate observations of a vehicle within a cycle, **When** activity is counted, **Then** the vehicle contributes once in its resolved category.
3. **Given** a successfully collected category with no active vehicles, **When** it is summarized, **Then** activity is a covered zero; an unavailable feed instead produces an unavailable or incomplete result.
4. **Given** a viewer changes selected routes or audio settings, **When** history is requested, **Then** it still reflects the server's city/category observations.

---

### User Story 3 - Compare published tone opportunities (Priority: P1)

An analyst asks how many crossing opportunities were successfully published for each category in an hour, with wording that explains these are opportunities for playback.

**Why this priority**: Publication is the observable server measure of soundscape cadence and needs no listener events.

**Independent Test**: Supply crossing batches with known categories and publication outcomes; verify successful category counts and reconciliation to successful city batches.

**Acceptance Scenarios**:

1. **Given** a complete hour with successful batches containing 40 bus and 15 rail crossing records, **When** cadence is requested, **Then** results report 40 and 15 published crossing opportunities per covered hour.
2. **Given** failed or indeterminate publication, **When** coverage is assessed, **Then** that cycle's cadence is unavailable and the affected hour cannot be presented as a complete zero.
3. **Given** a valid cycle without crossing records, **When** cadence is collected, **Then** it contributes a known zero without requiring a nonempty batch.
4. **Given** listeners mute, disconnect, or change route selections, **When** cadence is summarized, **Then** it remains published opportunities and makes no claim about notes heard.

---

### User Story 4 - Compare periods and local hours honestly (Priority: P2)

An analyst compares a period or a typical local hour of day, with usable coverage, exclusions, and weighted averages disclosed.

**Why this priority**: Local time and coverage make comparisons meaningful after base measures are reliable.

**Independent Test**: Request ranges containing unequal denominators, incomplete hours, and daylight-saving transitions; verify weights, boundaries, exclusions, and distinct displayed hours.

**Acceptance Scenarios**:

1. **Given** complete hours with unequal sample or interval counts, **When** combined, **Then** each average uses its summed numerator and denominator.
2. **Given** two complete observations of the same local hour containing 50 and 70 crossings, **When** typical-hour cadence is requested, **Then** it reports 60 opportunities per complete observed hour and two contributing hours.
3. **Given** a range with one complete and one incomplete hour, **When** summarized, **Then** definitive hourly measures use the complete hour and explicitly disclose the excluded hour and missing coverage.
4. **Given** a repeated local hour at a daylight-saving transition, **When** individual hours are shown, **Then** local date, UTC offset, and UTC identity distinguish them; both complete observations can contribute to the typical-hour bucket.
5. **Given** a range including only part of an hour, **When** requested, **Then** minute observations explain partial coverage without presenting that partial hour as definitive hourly distance or cadence.

---

### User Story 5 - Collect trustworthy history while transit stays live (Priority: P1)

An operator enables one city, checks reconciliation and coverage, and expands collection. Live transit continues through storage delays; analysts can distinguish collection gaps from inactivity.

**Why this priority**: Insights depend on new trustworthy observations while preserving the live experience.

**Independent Test**: Exercise healthy capture, storage failure, restart, repeated delivery, and conflicts; verify continuing cycles, visible gaps, and one unchanged aggregate per identity.

**Acceptance Scenarios**:

1. **Given** healthy observations of a configured category without vehicles, **When** its minute closes, **Then** it produces a complete zero; `unknown` appears only when actually observed.
2. **Given** 60 complete minutes and an intact hourly distinct population, **When** the hour closes, **Then** additive totals equal the minute sums and each vehicle counts once per category for that hour.
3. **Given** restart, excessive gap, lost aggregate, or failed collection cycle, **When** coverage is assessed, **Then** the affected period is incomplete or missing and cannot appear as healthy zero activity.
4. **Given** repeated identical finalized values, **When** stored, **Then** counts appear once; differing values for the same identity are flagged without silent addition or replacement.
5. **Given** slow or unavailable storage, **When** live cycles continue, **Then** cycles do not wait for storage and safe summaries identify delayed or lost aggregates.
6. **Given** city-only history predating category capture, **When** requested, **Then** category history is unavailable and the first collectible date is disclosed.

### Edge Cases

- A joined route lacks a configured category: otherwise eligible observations use `unknown`; a vehicle that cannot join a route is ineligible for activity and movement.
- A configured category is absent from a successful feed: emit a covered zero; failed or empty route catalogs do not establish valid zeros.
- A vehicle changes category within an hour: count it once in each applicable category's hourly population. Duplicate identities within one city cycle use the first eligible joined observation's category and count once across all categories. Changing route breaks its movement interval.
- No eligible movement intervals exist: total accepted distance may be zero, while the per-update mean is unavailable.
- A complete hour has no active vehicles: activity and crossings may be zero; distance per vehicle-hour is unavailable because its denominator is zero.
- A cycle occurs exactly at a UTC boundary: assign it once to the window beginning at that boundary.
- Collection begins or restarts mid-hour, or the distinct population is lost: the hour cannot be complete; minute distinct counts cannot reconstruct its denominator.
- Local clocks skip an hour: invent no observations. Repeated hours retain distinct UTC identities.
- Category or measure definitions change: retain observation-time categories and disclose versions rather than silently combining incompatible meanings.
- Overlapping producers submit different values: report a conflict and withhold authoritative completeness until reconciled.
- No complete hours exist in the requested period: return unavailable hourly measures with coverage, definitions, and the first collectible date.
- An unknown category first appears after earlier cycles in its activation minute: that minute and hour are incomplete because earlier category samples were not captured; do not invent preceding zero samples.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Collect historical aggregates for each enabled city and its configured transit categories. Normalize categories to lowercase; transit type MUST mean the configured route category.
- **FR-002**: Keep `unknown` distinct, emit it only when observed, and never default an unmatched route to `bus`. Category labels MUST be limited to the configured city catalog plus `unknown`.
- **FR-003**: Active vehicles MUST have a valid position, a successfully joined route, and successful current city feed and route processing. Stationary vehicles count; duplicate identities within a cycle count once.
- **FR-004**: Average activity MUST be the sum of eligible cycle counts divided by eligible sample count, including valid zeros; expose the sample count.
- **FR-005**: Observed movement MUST be the absolute change in estimated along-route meters between fresh observations of the same vehicle on the same route, including forward, reverse, and zero movement.
- **FR-006**: Exclude first sightings, repeated/stale positions, route changes, invalid geometry, and changes greater than 2,000 meters from movement and eligible interval counts. Account for rejected intervals diagnostically.
- **FR-007**: Complete-hour distance per observed vehicle-hour MUST equal accepted distance divided by distinct active vehicles in that city/category/hour, including vehicles without accepted movement in the denominator.
- **FR-008**: Expose accepted total distance, eligible interval count, and per-update distance, calculated as distance divided by eligible intervals including stationary intervals.
- **FR-009**: Published tone opportunities MUST count crossing records only after successful city-batch publication, attributed to resolved route category and UTC hour. Failed/indeterminate publication is unavailable; a valid cycle with no records is a known zero.
- **FR-010**: Identify distance as an observed along-route estimate, activity as observed vehicles, and crossings as published playback opportunities. Do not imply completed-trip distance, confirmed in-service status, or audible note counts.
- **FR-011**: Every measure MUST carry city, category, unit, definition version, requested/contributing time range, coverage, and relevant denominator. Hourly results include covered minutes; combined results include complete contributing hours and excluded/missing periods.
- **FR-012**: Missing observations MUST NOT become zero. A zero denominator produces an unavailable mean and discloses that denominator.
- **FR-013**: Use UTC time identities and nonoverlapping minute/hour windows with inclusive starts and exclusive ends.
- **FR-014**: Retain finalized minute and hour aggregates separately. Hourly distinct vehicles MUST represent whole-hour uniqueness and MUST NOT be the sum of minute distinct counts.
- **FR-015**: Minute evidence MUST include observed cycles, valid activity samples, valid publication cycles, failed cycles, first/last observation times, maximum observation gap, and status alongside additive activity, movement, and crossings.
- **FR-016**: A minute is `Complete` only with at least one valid active sample, valid feed/route processing and known successful or valid-zero publication outcomes for every observed cycle, and no gap across minute boundaries beyond a documented healthy-cadence limit. Calibrate and record that limit before enablement.
- **FR-017**: Useful observations with a failed/unknown cycle, excessive gap, or capture loss are `Partial`; observations without any eligible activity, movement, or publication samples are `NoData`. Failure and rejection diagnostics may remain in NoData observations. Identify entirely absent minutes against the requested range.
- **FR-018**: An hour is `Complete` only with 60 complete corresponding minutes, an intact distinct population, and additive totals matching minute sums. Other hours may retain diagnostic values but MUST NOT supply definitive hourly averages or cadence.
- **FR-019**: Across complete hours, vehicle-hour distance uses summed distance divided by summed hourly distinct counts. Activity and per-update distance use their respective summed numerators and denominators.
- **FR-020**: Typical local-hour cadence MUST be total crossings divided by complete observed hours in that city/category/local-hour bucket; disclose excluded incomplete coverage.
- **FR-021**: Local grouping MUST use each city's configured IANA time zone. Individual hours preserve local date, UTC offset, and UTC identity; skipped hours are not synthesized.
- **FR-022**: Arbitrary partial periods expose minute observations and coverage. Only complete UTC hours fully contained in the requested range contribute definitive vehicle-hour distance and hourly cadence.
- **FR-023**: Each aggregate has one city/category/minute or hour identity. Identical repeated writes are harmless; differing values MUST be reported without silent addition or replacement. Unresolved conflicts MUST NOT remain authoritative complete results.
- **FR-024**: Persistent statistics and queued historical summaries MUST contain aggregates only, without vehicle, route, trip, or listener identifiers. Temporary vehicle identities may establish cycle/hour uniqueness.
- **FR-025**: Historical storage MUST NOT make live transit cycles wait. Bound buffering and retries; expose aggregate loss through coverage and safe summaries.
- **FR-026**: `CityCategoryInsights.Enabled=true` MUST collect insights for all configured cities except explicit city exclusions. Operators MUST be able to exclude cities through `CityCategoryInsights__Disabled_0`, `Disabled_1`, and subsequent indexed settings whose values are city names. A one-city pilot excludes the other configured cities; removing exclusions expands capture. Expansion and insight query availability follow demonstrated initial-city coverage and reconciliation.
- **FR-027**: Safe operational summaries MUST expose failures, conflicts, lost aggregates, and coverage without credentials, raw feeds, or individual transit/listener identities.
- **FR-028**: Preserve existing city-only statistics, their definitions, existing city metrics, and live transit messages.
- **FR-029**: Begin category history with new eligible capture and disclose the first collectible date. Do not divide existing city-only samples into category history or sum them into exact crossing totals.
- **FR-030**: Retain observation-time categories and MUST NOT silently combine incompatible measure definition versions.

### Key Entities *(include if feature involves data)*

- **City**: Canonical identity, configured categories, local time zone, and collection enablement.
- **Transit Category**: Lowercase city route category or separately observed `unknown`, assigned at observation time.
- **City Category Minute Observation**: One finalized UTC minute with additive measures, eligibility counts, observation timing, definition version, and coverage.
- **City Category Hour Observation**: One finalized UTC hour with additive totals, exact whole-hour distinct vehicles, covered minutes, definition version, and completeness.
- **Insight Result**: Hourly, combined-period, partial-period, or typical-local-hour result containing measures, definitions, denominators, ranges, exclusions, and coverage.
- **Collection Summary**: Aggregate operational evidence for failures, delay, loss, conflicts, and rollout reconciliation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All deterministic complete-hour examples match documented calculations; category crossing totals reconcile exactly with successfully published city batches.
- **SC-002**: Every complete-hour example has 60 complete minutes, matching additive minute sums, and exactly one denominator contribution per eligible vehicle/category/hour.
- **SC-003**: Every exercised failed feed, unknown publication, restart, excessive gap, aggregate loss, and unresolved conflict withholds affected definitive hourly results; missing coverage never appears as zero.
- **SC-004**: All combined-period and local-hour examples match weighted hand calculations, including unequal denominators and daylight-saving transitions; every measure includes range, unit, definition, and coverage.
- **SC-005**: Under matched city/feed load, capture increases the 95th-percentile live processing cycle duration by no more than 5%; unavailable storage does not interrupt or make live cycles await historical storage.
- **SC-006**: One-city healthy capture over 24 hours achieves at least 99% complete minutes after the initial partial hour, with every incomplete/missing period accounted for before expansion.
- **SC-007**: Repeated finalized examples produce zero duplicate identities and zero doubled counts; every differing repeated value is reported.
- **SC-008**: At least four of five representative analysts can distinguish vehicle-hour distance from trip distance and published opportunities from notes heard using result definitions alone.
- **SC-009**: Persisted statistics and queued historical summaries contain zero individual vehicle, route, trip, or listener identifiers; pre-collection requests consistently report unavailable category history.

## Assumptions

- Initial consumers are analysts and operators using documented internal insight queries. Public endpoints, visitor dashboards, and chart design are deferred.
- Existing cities supply canonical identities and route categories; required city time zones can be completed during planning. Existing route processing and crossing publication supply observations.
- The healthy-cadence limit is an implementation-time policy calibrated from worker timings before enablement; it does not change measure definitions.
- Attribution and freshness rules will be documented before capture. An eligible distance interval spanning a boundary belongs once to its completing observation; no unobserved trajectory is reconstructed.
- The current deployment normally has one producer; revision overlap still requires conflict handling.
- Collection starts globally disabled. After storage structures are available, the global flag enables all configured cities except explicit exclusions; pilots can exclude the other cities. Existing city-only history remains independently usable under its original contract.
- The first release collects future history. Backfill requires a separately authorized extension and proof of an older category-tagged observation source.
- Completed-trip distance, listener playback measurement, durable recovery of aggregates lost before storage, and extrapolation from partial hours are outside scope.
- The 5% processing-overhead and 99% healthy-minute targets are initial rollout acceptance targets; incomplete source feeds cannot be assumed to meet them.
