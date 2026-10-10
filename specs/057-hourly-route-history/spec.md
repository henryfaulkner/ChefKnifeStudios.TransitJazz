# Feature Specification: Hourly Route History

**Feature Branch**: `056-city-transit-type-insights` (current branch retained at user request)  
**Feature Directory**: `specs/057-hourly-route-history`  
**Created**: 2026-10-10  
**Status**: Draft  
**Input**: User description: "Create a specification from docs/HOURLY_ROUTE_HISTORY_DESIGN_DOCUMENT.md; keep changes on the current branch."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Compare observed route activity and soundscape opportunities (Priority: P1)

An internal analyst selects a city, routes, and an hourly time range to compare vehicle activity, accepted movement, processing observations, and published crossing opportunities. The analyst distinguishes healthy inactivity from unreliable collection.

**Why this priority**: Route history answers questions unavailable from city/category summaries, while coverage prevents misleading comparisons.

**Independent Test**: Collect a stable full hour with active and inactive routes. Retrieve their history and compare measures and coverage with known observations.

**Acceptance Scenarios**:

1. **Given** a stable catalog and healthy collection bracketing a UTC hour, **When** history is retrieved, **Then** each configured resolved route has exactly one Complete result with measurements, denominators, metadata, and coverage evidence.
2. **Given** a configured route without positioned vehicles in that healthy hour, **When** its result is retrieved, **Then** activity, processing, and crossings are known zeros, activity samples are positive, and movement averages with zero denominators are unavailable.
3. **Given** several feed/static aliases resolve to one route, **When** history is collected, **Then** they produce one resolved-route result; identical route-key text in another city remains separate and case-distinct keys remain distinct.
4. **Given** duplicate vehicle entities in a cycle, **When** collection completes, **Then** activity uses the first eligible joined entity in feed order once, while processing and crossings retain actual live observations and records.
5. **Given** successful publication, **When** route counts are compared to the corresponding city batch, **Then** they reconcile exactly and are labeled published opportunities rather than notes heard.

---

### User Story 2 - Recognize collection gaps and uncertain history (Priority: P1)

An analyst or operator inspects coverage before drawing conclusions. Feed failures, uncertain publication, restarts, route changes, and conflicting producers remain visible rather than appearing as inactivity.

**Why this priority**: Trustworthy coverage is necessary for the minimum useful history feature.

**Independent Test**: Introduce failure, restart, catalog-change, and conflicting-finalization cases and verify none contributes definitive measures.

**Acceptance Scenarios**:

1. **Given** failed collection, uncertain publication, excessive gaps, or unproven boundaries, **When** an hour closes, **Then** retained observations are Partial or NoData with bounded reasons and do not contribute definitive summaries.
2. **Given** startup or shutdown during an hour, **When** it closes, **Then** its fragment is incomplete; restart fragments are never combined into Complete history.
3. **Given** loss before retention, **When** a route/hour is explicitly requested, **Then** coverage reports Missing rather than zero and counts the requested gap.
4. **Given** an unchanged catalog refresh, **When** collection continues, **Then** coverage and movement continuity remain valid. An actual membership, identity, category, or geometry change instead makes the whole city/hour incomplete and preserves its observed old/new route cohort without invented full-hour zeros.
5. **Given** retained history, **When** identical finalized results are retried, **Then** values remain unchanged. A different payload or route cohort instead quarantines every existing result for that city/hour as Conflict without replacing values or merging fragments; subsequent identical retries preserve quarantine.

---

### User Story 3 - Compare periods and local hours correctly (Priority: P2)

An analyst compares multiple hours or local times of day, including routes removed from today's catalog. Definitions explain what was measured and how many reliable hours contribute.

**Why this priority**: Weighted measures and time identity make trends useful without overstating coverage or uniqueness.

**Independent Test**: Retrieve unequal-sample hours, missing hours, historical routes, and a daylight-saving transition; independently verify measures and coverage.

**Acceptance Scenarios**:

1. **Given** complete hours with unequal sample counts, **When** period averages are requested, **Then** results divide summed numerators by their summed denominators instead of averaging hourly averages.
2. **Given** mixed complete, partial, missing, and conflicted coverage, **When** a period is requested, **Then** only fully contained complete nonconflicting hours contribute definitive measures, with requested/contributing counts and other coverage states disclosed.
3. **Given** repeated/skipped daylight-saving local hours, **When** local-hour results are requested, **Then** UTC identity, local date/hour, and offset remain available, repeated UTC hours contribute separately, and skipped local hours have no invented data.
4. **Given** a removed route or an explicit key with no retained history, **When** it is selected, **Then** historical labels remain selectable where retained and absent pairs are Missing; today's catalog does not establish historical membership.
5. **Given** no fully contained complete hours, **When** a period is requested, **Then** definitive means are unavailable and contributing-hour count is zero; partial-boundary context is labeled separately and never prorated.

---

### User Story 4 - Operate collection with existing history controls (Priority: P2)

An operator uses existing city-history enablement, dry run, and city selection to collect route history and inspect safe diagnostics without disrupting live transit processing.

**Why this priority**: Shared controls prevent contradictory scopes and use the established deployment process.

**Independent Test**: Exercise disabled, dry-run, and enabled collection with explicit/empty city selections, retention failures, and resource limits.

**Acceptance Scenarios**:

1. **Given** city-history collection is disabled, **When** the worker runs, **Then** no route capture, finalization, or writing occurs, regardless of dry run.
2. **Given** enabled dry run, **When** route hours finalize, **Then** validated bounded summaries are available without retained route history.
3. **Given** enabled persistence, **When** explicit city selection or its existing empty-selection fallback is used, **Then** route history covers the same resolved cities as city history, independently of category-insights enablement.
4. **Given** slow/failed retention or a resource limit, **When** live collection runs, **Then** processing and publication continue without waiting for history, loss is disclosed, and unreliable populations are never labeled exact.
5. **Given** retention fails for one route of a finalized city/hour, **When** history is inspected, **Then** no newly retained subset of that cohort is visible.

### Edge Cases

Verification requires automated reference checks for capture eligibility, clock boundaries, whole-hour retention/concurrent retries, and read-only query results, plus the operational coverage/performance and analyst handoff checks below.

- Completion exactly at a UTC boundary belongs to the new hour, including movement spanning that boundary.
- Stationary vehicles count as active and fresh zero-meter movement counts as an accepted interval. Reverse movement contributes absolute distance; exactly 2,000 meters is allowed, a larger delta is rejected.
- First sightings, missing/equal/older/repeated source timestamps, transfers, and geometry changes cannot invent movement or move freshness backward.
- A transfer can contribute to two route-hour populations; summing them is not an exact city unique-vehicle count.
- Missing/unmatched/invalid route keys produce city diagnostics, never arbitrary rows or an invented unknown route.
- Healthy empty feeds establish zeros with a ready stable catalog; empty/unavailable catalogs do not establish a full cohort.
- Partial processing or source success retains diagnostic counts and known successful publications without Complete coverage.
- A sweep can finalize an expired window while a pass is in flight; its later completion cannot reopen that hour.
- Long pauses leave missing intermediate windows rather than invented zeros or unbounded replay.
- Clock regression, instrumentation loss, and route/identity caps invalidate completeness; lost populations have unavailable denominators.
- Admission before retention is not proof of retention. Process loss, admission rejection, and exhausted writes leave missing history.
- Differing cohorts, definitions, fingerprints, or capture runs quarantine the whole stored city/hour; retries never merge restart fragments.
- An entirely missing historical hour has unknown route membership; current catalog membership cannot reconstruct it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Retain future hourly observations with one result per canonical city, case-sensitive resolved-route key, and aligned UTC hour. Preserve route-key spelling/case and existing city identity conventions.
- **FR-002**: Use one canonical catalog entry per resolved route, combining aliases/grouped shapes resolved together by live processing. Retain display name, category, a fingerprint of relevant route identity/geometry, and a static identifier only when exactly one contributes.
- **FR-003**: Actual catalog membership, identity, category, or geometry change MUST make the entire open city/hour incomplete, preserve the union of observed old/new routes with each entry's first metadata, and disclose the change. Identical content MUST preserve coverage/movement continuity; introduced/removed routes MUST NOT receive invented full-hour zeros.
- **FR-004**: Missing/unmatched input routes MUST remain city-level diagnostics without new route identities. Invalid route keys MUST invalidate affected capture and be reported without truncating identity.
- **FR-005**: Each eligible cycle MUST count distinct valid positioned vehicles once per vehicle/city/cycle using the first eligible joined entity in feed order, including stationary vehicles. Healthy empty cycles MUST contribute zero samples for every configured route.
- **FR-006**: Retain activity sum, eligible sample count, peak cycle activity, and exact per-route hourly distinct population when intact. Vehicle identities MUST remain transient and MUST NOT enter finalized or retained results.
- **FR-007**: Retain processed/stale observations following existing live processing count branches, including actual duplicate processing; these counts MUST remain distinguishable from unique vehicles.
- **FR-008**: Accepted movement MUST use absolute along-route delta with valid position/geometry, strictly advancing source time, the same resolved route/geometry, and delta at most 2,000 meters. Retain summed meters, accepted interval count including zero movement, and rejected representative-observation count; round accepted deltas once to six decimal places.
- **FR-009**: First sightings, unknown freshness, invalid geometry, transfers, and excessive deltas MUST NOT create accepted movement. Equal/older observations MUST NOT move freshness backward; unknown freshness/invalid geometry MUST clear the baseline; fresh transfers/excessive deltas MUST seed a new baseline.
- **FR-010**: Retain actual detected records and records in known successfully published batches by route. First-seen, non-advancing-position, teleport, and transfer suppression counts MUST count observations receiving those existing reasons, not estimated lost notes.
- **FR-011**: Commit activity/movement only with eligible source/processing/catalog evidence. Partial work MAY retain processing/detection/suppression and known successful publication diagnostics while keeping coverage incomplete. Successful processing with no publishable batch establishes known zero publication; false/indeterminate publication does not.
- **FR-012**: Assign additive observations exactly once using the single cycle completion time and start-inclusive/end-exclusive UTC hours. Source time establishes freshness only; cross-boundary movement belongs to its completing cycle.
- **FR-013**: Complete coverage MUST require an available stable full-hour cohort, healthy predecessor/successor evidence, eligible in-hour cycles, known publication, acceptable boundary/inter-observation gaps under the persisted cadence limit, and intact enabled capture without regression/loss/exceeded limits. Do not require an exact nominal sample count or claim independent minute reconciliation.
- **FR-014**: Distinguish Complete, Partial, and NoData capture states. Partial retains useful eligible observations without definitive measures. NoData has zero eligible activity/publication samples, activity, accepted movement, and published crossings; processing/detection diagnostics may remain. Absence is Missing; quarantined history has effective Conflict coverage.
- **FR-015**: Retain boundary flags; observed/eligible/failed counts; first/last completion times; maximum observed gap; cadence policy; capture-run provenance; definition version; and bounded ordered reasons. Reasons MUST be limited to startup/shutdown fragment, source failure, route catalog unavailable, processing failure, publication unavailable, boundary unproven, excessive gap, catalog change, clock regression, capture failure, and identity-limit loss; exclude exception text.
- **FR-016**: Startup/shutdown fragments MUST remain incomplete. Finalization MUST continue during hung collection, resolve expired observed windows within 15 seconds after the cadence deadline, seal once, and never reopen for late completion. Long pauses MUST leave missing intermediate hours without unbounded replay.
- **FR-017**: Retain a finalized city's whole hourly immutable route cohort together or none. Exact retries MUST preserve results. Different payloads/cohorts MUST quarantine all existing city/hour results, preserve values, never merge fragments/overwrite incomplete hours/add missing incoming routes, and never clear quarantine on identical retry.
- **FR-018**: Retention/instrumentation failures MUST NOT change live feed processing, resolution, crossings, publication, existing history/metrics, or live message contents. Live collection MUST NOT wait for history storage or admission capacity.
- **FR-019**: Enforce bounded route population, tracked vehicle state, hourly vehicle-route memberships, pending results, retention attempts/duration, and shutdown work. Exceeded limits MUST disclose incomplete/lost history without silent eviction/omission; unreliable distinct denominators MUST become unavailable. Resume only in a within-limit subsequent window with fresh movement evidence as needed.
- **FR-020**: Existing disabled city-history collection MUST disable all route work; enabled dry run MUST validate without retaining history; enabled persistence MUST use the existing resolved city selection and its fallback to all configured cities. Category-insights enablement MUST NOT govern route history.
- **FR-021**: Route resource settings MUST adjust only limits/cadence under existing history configuration without independent enablement, dry-run, city-selection, or exclusion controls. Enabled persistence MUST validate its retention capability before collection starts.
- **FR-022**: Operators MUST receive bounded aggregate inserted/unchanged/quarantined/dropped/retried/shutdown-undrained summaries. Summaries/history MUST exclude credentials, raw feeds, vehicle/trip/listener identities.
- **FR-023**: Provide documented internal read-only queries for a city, explicit resolved route keys or all routes retained in a range, a supported definition, an ordered UTC interval, and optional configured local zone. Removed historical routes MUST remain selectable.
- **FR-024**: Definitive measures MUST use only fully contained Complete nonconflicting hours, separating incompatible definitions/cadence policies. Partial rows MUST remain separately inspectable as diagnostics; intersected hourly boundaries MUST disclose exact context without proration.
- **FR-025**: Query results MUST disclose requested/contributing counts, coverage states, UTC bounds, definition/cadence, fingerprints/catalog changes, units, and numerators/denominators. Explicit selections MUST expose missing route/hour pairs; all-route discovery MUST use retained history without asserting today's membership in entirely missing hours.
- **FR-026**: Period activity MUST divide summed active counts by summed eligible samples; peak uses maximum hourly peak; stale fraction divides summed stale by processed; movement per route-vehicle-hour divides summed meters by summed hourly route populations; movement per accepted update divides summed meters by summed accepted intervals; published cadence divides published records by complete contributing UTC hours. Zero denominators produce unavailable means.
- **FR-027**: Definitions MUST distinguish observations from vehicles, route-vehicle-hours from period/city uniqueness, observed movement from trip distance, and published opportunities from audible notes. Never average displayed hourly averages or extrapolate incomplete hours.
- **FR-028**: Local-hour results MUST retain UTC key, local date/hour, and offset; repeated daylight-saving hours contribute separately and skipped hours have no invented rows. Disclose earliest retained history without implying continuous capture since that date.
- **FR-029**: Enforce nonnegative counts/distances, eligible samples no greater than observed cycles, stale no greater than processed, published no greater than detected, ordered in-hour timestamps only when observations exist, peak no greater than activity sum, and intact distinct population no smaller than peak. Enforce FR-013/FR-014 state conditions.
- **FR-030**: History MUST begin with new collection, without backfill from city/category aggregates or automatic deletion. Use existing collection settings and selected cities; apply the required history schema before updated collection through the existing deployment process.

### Key Entities *(include if feature involves data)*

- **Canonical Route Cohort**: Unique resolved routes for a city/catalog generation with metadata, contributing static identities, and identity/geometry fingerprints.
- **Route Cycle Observation**: Transient counters, deduplicated activity identities, movement evidence, cycle/publication outcomes, and one completion timestamp.
- **Route Hour Result**: One city/route/UTC-hour result containing metadata, counters, exact hourly population when intact, denominators, provenance, and coverage.
- **Finalized City Hour**: An immutable cohort retained together and forming the scope for retries/conflict quarantine.
- **Historical Query Result**: Hourly/period/local-hour measures, requested/contributing windows, coverage, units, definitions, and denominator evidence.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In deterministic healthy reference hours, 100% of canonical routes have one result, inactive routes have valid zeros, and route processing/detection/suppression/publication totals reconcile exactly to corresponding live observations and successful batches.
- **SC-002**: Zero hours affected by defined failure/restart/boundary/catalog/conflict/clock/resource-loss scenarios are falsely Complete or contribute definitive measures.
- **SC-003**: Across a 24-hour healthy observation of the existing selected cities, at least 99% of eligible route-hours are Complete after startup fragments, with zero admission/write loss and zero unexpected conflicts under normal single-producer operation.
- **SC-004**: Enabled capture adds at most 5% to matched-load 95th-percentile cycle duration, and delayed/failed retention changes zero live processing results or publication counts.
- **SC-005**: All reference period/historical-route/empty-range/boundary/daylight-saving queries return expected weighted measures and full coverage context, including unavailable means when no denominator exists.
- **SC-006**: Every retry/retention-failure test exposes a whole original cohort, whole new cohort, or no cohort: zero partially inserted cohorts or merged fragments; all conflicting retries quarantine the original cohort.
- **SC-007**: At least four of five representative analysts correctly explain known zero versus missing, route-vehicle-hour movement versus trip distance, and published opportunities versus audible notes using delivered definitions/examples.
- **SC-008**: All expired observed hours finalize within 15 seconds after their cadence deadline during stalled collection; all disabled/dry-run/city-selection reference cases obey existing controls without unintended retention.

## Assumptions

- The source design governs scope. Earlier category-specific independent enablement, minute reconciliation, and one-city pilot rules do not apply.
- Existing internal operator/analyst access is reused. Public dashboards/endpoints, client changes, and new permission models are excluded.
- Live resolved-route semantics remain authoritative; collapsed directions/branches/static IDs are not separate historical routes, and keys do not promise permanent transit-service identity.
- Existing city-history controls, deployment parameters, retention infrastructure, and configured local time zones are dependencies. Source timestamps and healthy cadence must be verified before definitive movement/coverage is relied upon.
- Capture is hourly only: no durable route-minute/per-cycle history, crash reconstruction, separate aggregation service, shared-cost allocation, route-duration measurement, partitioning, precomputed summaries, or separate route registry.
- The starting cadence bound is 30 seconds for the current ten-second interval, adjustable per city to greater than its interval and no more than 60 seconds; continuity governs coverage rather than a nominal 360 samples.
- Resource starting values/storage estimates are planning bounds requiring measurement. Acceptance checks are targets, not results already achieved.
- No automatic retention job is introduced; a later horizon must use measured growth, preserve whole cohorts, and disclose retained coverage.
- Analyst comprehension is an added handoff criterion inferred from the interpretation risks; it does not require a new interface.
