# Feature Specification: Reported Vehicles in Stats

**Feature Branch**: `059-reported-vehicles`  
**Created**: 2026-10-13  
**Status**: Draft  
**Input**: User description: "Place vehicles reported today and vehicles reported over last 24hr under the distance metrics; count every identifiable vehicle reported by the feed, including vehicles that cannot be placed on the map." Source: [reported vehicles design](../../docs/REPORTED_VEHICLES_DESIGN_DOCUMENT.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See reported vehicles beside distance (Priority: P1)

A visitor opens Stats for a city and sees its distinct reported vehicles today and during the last 24 completed hours beneath the corresponding distance values. Vehicles need not appear on the map to contribute.

**Why this priority**: Directly delivers the two requested metrics alongside distance.

**Independent Test**: Open city Stats with a known population spanning completed hours, repeated identities, and unmappable vehicles. Both counts match independently calculated distinct populations without requiring route selection or All mode.

**Acceptance Scenarios**:

1. **Given** complete city evidence, **When** Stats opens, **Then** "Vehicles reported today" appears beneath today's distance and "Vehicles reported over last 24 hours" beneath the last-24-completed-hours distance with English-formatted integers.
2. **Given** one identity in repeated observations, multiple hours, and different routes, **When** a period is reported, **Then** it contributes once in that period.
3. **Given** identifiable records fail route matching, positioning, or map synthesis, **When** source responses succeed, **Then** they contribute alongside mapped vehicles.
4. **Given** an unfinished current hour, **When** Stats opens, **Then** identities observed only in that hour are excluded and both count windows match adjacent distance windows.
5. **Given** overlapping feeds share an identity domain and independent bus/rail domains reuse ID text, **When** counts are calculated, **Then** overlapping duplicates contribute once and independent identities separately.

---

### User Story 2 - Understand coverage and identity limits (Priority: P2)

A visitor distinguishes complete evidence, qualified observed counts, unavailable history, and today's pending first completed hour, including source and identity limitations.

**Why this priority**: Prevents outages and missing identifiers from appearing as empty fleets or exact physical-vehicle totals.

**Independent Test**: Open Stats using complete, empty, partial, absent, conflicted, and early-day fixtures. Verify independent count state, contributing/expected city-hours, and explanations.

**Acceptance Scenarios**:

1. **Given** intact observations cover only some expected hours or sources, **When** Stats opens, **Then** the distinct count is labeled "observed; partial coverage" with contributing and expected city-hour counts.
2. **Given** disabled capture, absent history, failed persistence, or no intact eligible evidence, **When** Stats opens, **Then** the metric shows Unavailable rather than zero.
3. **Given** usable successful empty responses, **When** Stats opens, **Then** the observed count is zero with actual coverage; **Given** no eligible completed today-hour, **Then** today's count shows Pending.
4. **Given** missing identifiers or documented identity fallbacks, **When** counts appear, **Then** limitations are disclosed and counts describe observed identities rather than a verified physical fleet.
5. **Given** contradictory or identity-loss evidence for an hour, **When** counts are calculated, **Then** that hour is excluded and explained while other intact hours can contribute.
6. **Given** distance coverage differs from feed coverage, **When** Stats opens, **Then** each metric retains independent eligibility and state.

---

### User Story 3 - Understand route and All-city scope (Priority: P3)

A visitor selects routes or switches to All and understands that reported vehicles describe all city feeds, while distance follows existing route filters.

**Why this priority**: Prevents selection controls from implying unmatched vehicles belong to selected routes or All deduplicates vehicles between cities.

**Independent Test**: Compare fixed evidence under route selections and All. City counts remain unchanged under filters; All equals the sum of independently deduplicated city populations.

**Acceptance Scenarios**:

1. **Given** known city evidence, **When** routes are selected or cleared, **Then** reported counts remain city-wide and the persistent label "All city feeds; independent of route selection" appears when routes are selected.
2. **Given** multiple cities, **When** All is selected, **Then** counts sum city populations and disclose this scope; identical IDs in separate cities contribute separately.
3. **Given** cities in different time zones, **When** All today is reported, **Then** each city contributes its local-day completed hours with separate window and coverage details.

### Edge Cases

- A vehicle appearing in every eligible hour counts once, so hourly distinct counts cannot be added.
- Ordinal case/spelling distinguish identities; separate city or source domains preserve distinct identities.
- Blank identifiers, deleted/non-vehicle entities, trip IDs, and undocumented unstable entity IDs do not establish vehicles. Stale identifiable records still count as reported.
- Receipt exactly at an hour boundary belongs to the new hour regardless of source timestamp; successor observations may prove continuity without moving identities backward.
- Local midnight, DST changes, and fractional UTC offsets use fully contained completed UTC hours, with today's local date determined before ingestion-grace adjustment.
- Startup, restart, shutdown, source changes/outages, clock regressions, and storage delays qualify or remove coverage; one healthy source cannot conceal another's outage.
- Exact retries do not inflate counts; contradictory evidence or concurrent capture ownership excludes the hour rather than silently combining partial populations.
- Identity limits and queue loss never produce silently truncated exact counts.
- Intact empty partial evidence yields observed zero with a partial label; absent evidence never yields zero.
- Long headings, mobile widths, zoom, both themes, and assistive reading preserve heading/value associations and distance hierarchy.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Stats MUST display the two counts beneath corresponding existing distances using User Story 1 headings, keeping distance visually primary.
- **FR-002**: Counts MUST represent distinct identifiable vehicles reported by successful configured source responses received during contributing completed hours, including records excluded by route matching, positioning, or map synthesis.
- **FR-003**: Identity deduplication MUST span observations, hours, routes, and overlapping feeds across the entire reporting window; hourly or route counts MUST NOT be summed to reconstruct period populations.
- **FR-004**: Identity equality MUST include canonical city, documented vehicle-ID domain, identity kind, and ordinal value; overlapping feeds share domains and independent bus/rail domains remain separate.
- **FR-005**: Nonblank vehicle identifiers MUST be preferred. Only documented stable source-specific fallbacks MAY substitute. Blank identifiers, deleted/non-vehicle records, trip IDs, and undocumented unstable identifiers MUST be excluded; unidentifiable record counts and fallback limitations MUST be disclosed in coverage details.
- **FR-006**: Today MUST use the same city-local date and fully contained completed UTC hours as distance; last 24 hours MUST use the same 24 completed UTC hours and ingestion-grace-adjusted cutoff. The unfinished hour MUST be excluded.
- **FR-007**: Response receipt time MUST determine observation hour. Source timestamps and successor-hour boundary observations MUST NOT reassign identities to previous hours.
- **FR-008**: Reported counts MUST remain city-wide under route selection, with the User Story 3 scope label when routes are selected. Existing distance filtering MUST retain its behavior.
- **FR-009**: All MUST sum per-city distinct populations, use each city's local today window, and disclose that physical vehicles are not deduplicated across cities.
- **FR-010**: The system MUST retain sufficient identity evidence to reproduce both counts after restart. Count-only historical aggregates MUST NOT be treated as reconstructable identity populations or backfilled with invented identities.
- **FR-011**: Each summary MUST include its window, nullable integer count, independent coverage state, expected/contributing/fully covered city-hour counts, and per-city limitations. Distance eligibility MUST NOT determine feed eligibility.
- **FR-012**: Complete coverage MUST require a stable expected-source set, healthy successful observations from every source under documented cadence and boundary continuity rules, intact identity evidence, and no unidentifiable vehicle records. Documented fallback limitations MUST remain disclosed even with complete observed-identity coverage.
- **FR-013**: Intact partial hours MAY contribute qualified observed counts. Missing, no-usable-observation, contradictory, and identity-loss hours MUST be excluded and explained instead of treated as zeros.
- **FR-014**: Presentation MUST distinguish Complete, Partial, Unavailable, and Pending. Partial MUST show "observed; partial coverage" and contributing/expected hours; Unavailable means no usable identity evidence; Pending means no eligible completed today-hour. Zero requires usable successful empty-population evidence.
- **FR-015**: Identical evidence retries MUST be idempotent. Contradictory evidence for an immutable city/hour MUST exclude that hour. Restart or concurrent ownership MUST NOT silently merge partial evidence into a complete hour.
- **FR-016**: Collection/retention MUST have documented configurable finite limits. Exceeded identity, source, timing, or delivery limits MUST visibly invalidate or qualify evidence without silent truncation; retention MUST preserve active windows.
- **FR-017**: Collection MUST remain independent of map readiness/music publication. Storage failure MUST not block transit polling. Existing map, audio, route-selection, and distance behavior MUST be preserved.
- **FR-018**: Public reporting MUST expose aggregate counts and coverage only, keeping raw identities and source credentials private. Count and distance windows MUST use one consistent reporting snapshot.
- **FR-019**: Existing Stats request, refresh, cancellation, and failure behavior MUST include both metrics. Disabled or not-yet-deployed reported history MUST return Unavailable without breaking distance presentation.
- **FR-019a**: Capture MUST default to Disabled and enable no cities in that mode. DryRun and Persistence MUST apply to every configured city with an explicit valid source catalog, without a per-city selector. Missing policies MUST disable capture for that city; incomplete or invalid explicit policies MUST fail startup. DryRun MUST NOT persist. StatsOverlay.ReportedVehiclesEnabled MUST remain independent so evidence can be collected and audited before reads are exposed. Persistence MUST be restricted to the WebAPI host; standalone Worker persistence MUST be rejected before startup. Unsupported Stats cities MUST retain bounded expected windows and unavailable/null results without invented identities, coverage or zero counts.
- **FR-020**: Counts MUST use English integer formatting, support both themes/responsive layouts, and associate values with accessible headings. Copy MUST state: "Distinct vehicles reported by transit feeds during completed hours, including vehicles that could not be placed on the map."
- **FR-021**: Acceptance MUST include independent arithmetic/coverage fixtures, source-before-filtering verification, restart/conflict checks, and browser checks. Release evidence MUST document source identity contracts, actual retained coverage, finite limits, performance, and safe rollout/disablement.

### Key Entities *(include if feature involves data)*

- **Reported identity**: Private city-scoped vehicle-ID domain, identity kind, and exact value with documented fallback limitations.
- **Source observation**: Configured source response outcome, receipt time, population, and unidentified-record evidence independent of map processing.
- **City-hour evidence**: Completed UTC-hour population with expected sources, timing, definition/cadence policy, integrity, ownership, and conflict status.
- **Reporting window**: Today or last-24-completed-hours period with per-city dates, UTC boundaries, and cutoff.
- **Reported summary**: Nullable distinct count, independent coverage/state, scope/window details, and limitations; All combines city populations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Both displayed counts equal independently calculated distinct populations in 100% of deterministic fixtures, including repeated sightings, cross-hour appearances, route changes, overlapping sources, and independent domains.
- **SC-002**: Every identifiable unmappable vehicle in source-before-filtering fixtures contributes exactly once per applicable period; deleted, non-vehicle, and unidentifiable records never establish identities.
- **SC-003**: All complete, partial, empty, missing, conflicted, restarted, and pending fixtures show specified states/denominators; missing evidence is never zero or silently complete.
- **SC-004**: Count windows match adjacent distance boundaries in local-midnight, ingestion-grace, DST, and fractional-offset fixtures. Route filtering preserves counts and All totals match independently calculated city populations.
- **SC-005**: At widths of 320 pixels and above and at 200% zoom, counts/headings/coverage remain readable without horizontal page scrolling in both themes; assistive reading associates each value with its heading.
- **SC-006**: At least four of five representative visitors correctly explain periods, route-filter independence, observed identities versus fleet size, and partial/unavailable evidence after viewing Stats.
- **SC-007**: Under existing matched-load Stats acceptance conditions, reporting meets the existing five-second budget and total history-plus-Stats overhead stays within the existing 5% budget against its recorded baseline, without a second allowance or blocking normal polling.

## Assumptions

- Adopt the design's proposed product defaults: completed-hour windows match distance, route-filter independence, and All sums city populations using city-local today windows.
- Existing Stats, configured city/time-zone/source definitions, and distance cutoff rules are dependencies. Charting, navigation, audio, and route selection are not redesigned.
- Initial retention is seven days of completed evidence, with configurable finite limits validated before release; concrete storage/capture architecture belongs in planning.
- This is observed feed identity counting, not guaranteed permanent physical fleet identity. Source contracts must be verified and missing/unstable IDs disclosed.
- Automated independent fixtures and manual browser/visitor verification are requested by the proposed design and FR-021.
- The four-of-five visitor threshold, five-second reporting limit, and total 5% history-plus-Stats overhead budget carry forward existing Stats acceptance assumptions.
- Collection, storage, reporting, and presentation are eventual implementation scope. This workflow performs no application implementation, production migration, backfill, capture enablement, or deployment.
