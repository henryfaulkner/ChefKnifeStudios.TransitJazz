# Feature Specification: Atlanta Rail Recovery and Available Statistics

**Feature Branch**: `064-atlanta-rail-statistics`  
**Feature Directory**: `specs/063-atlanta-rail-statistics`  
**Created**: 2026-11-07  
**Status**: Ready for planning  
**Input**: [Atlanta Rail Recovery and Available Statistics](../../docs/ATLANTA_RAIL_RECOVERY_DESIGN_DOCUMENT.md), supported by [Atlanta data source investigation](../../docs/ATLANTA_DATA_SOURCE_INVESTIGATION.md).

Restore authenticated Atlanta rail ingestion and, independently, present trustworthy statistics from available sources. Missing rail must never hide valid bus measurements; missing buses must never hide valid rail measurements. Preserve the distinction between incomplete coverage, invalid measurements, and observed zero.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See measurements during a source outage (Priority: P1)

As a visitor viewing Atlanta statistics, I want measures from working sources with clear coverage information so I can understand actual observations while another source is unavailable.

**Why this priority**: The current combined-source gate hides valid bus statistics during rail failure. This improvement delivers value independently of live rail recovery.

**Independent Test**: Carry deterministic healthy bus observations and failed rail through a completed hour, persistence, and visible statistics without requiring a live credential.

**Acceptance Scenarios**:

1. **Given** valid bus observations contribute 120 km and rail fails for the period, **When** Atlanta statistics are viewed, **Then** distance is 120 km, bus timeline and valid bus activity bubbles remain visible, rail measures are unavailable rather than zero, and visible text identifies Partial coverage and unavailable rail.
2. **Given** valid rail observations and failed buses, **When** statistics are viewed, **Then** rail measures remain available and bus measures remain unavailable under the same coverage rules.
3. **Given** both sources fail throughout the window, **When** statistics are viewed, **Then** distance is unavailable and charts explain that no observations are available without inventing zeros.
4. **Given** successful empty observations with proven continuity and all expected coverage, **When** statistics are viewed, **Then** supported observed zeros are Complete while undefined ratios remain unavailable.
5. **Given** a source fails or recovers mid-hour, **When** the hour closes, **Then** valid pre/post-outage observations survive, failed polls add no zero activity samples, the hour remains Partial, and movement does not bridge an invalid outage gap.
6. **Given** publication fails while observation and processing remain valid, **When** measures are evaluated, **Then** valid distance and activity survive while publication measures carry their own limitation.

---

### User Story 2 - Restore Atlanta rail safely (Priority: P1)

As an operator, I want Atlanta to use MARTA's replacement rail source with an existing valid credential so live trains return without interrupting buses or exposing credentials.

**Why this priority**: Endpoint migration and authentication address active rail failure but cannot replace the independent partial-statistics work.

**Independent Test**: Verify request/response behavior using controlled responses; separately validate redacted authenticated live data and serving-environment evidence when operator-authorized access is available.

**Acceptance Scenarios**:

1. **Given** a valid existing key and the replacement source, **When** an authenticated response contains usable realtime positions, **Then** eligible trains are ingested once with correct routes and event times while buses continue.
2. **Given** missing/invalid credentials, unsuccessful response, timeout, or malformed content, **When** rail is polled, **Then** rail fails promptly, bus work continues, and diagnostics reveal neither credentials nor credential-bearing addresses.
3. **Given** a valid empty or schedule-only response, **When** evaluated, **Then** success-empty is distinguished from failure; unusable positions do not establish complete positioned-vehicle coverage.
4. **Given** cancellation, duplicate trains, or invalid coordinates, **When** handled, **Then** cancellation remains cancellation and existing identity, coordinate, realtime filtering, and deduplication rules prevent invalid or duplicate observations.
5. **Given** live recovery, **When** the recovery hour and next fully observed healthy closed hour are viewed after ingestion grace and refresh, **Then** recovery remains Partial and the later hour may be Complete only if every other coverage condition passes; older outage hours still in the window remain disclosed.

---

### User Story 3 - Understand selections and arithmetic (Priority: P2)

As a visitor selecting routes, periods, or cities, I want figures and charts to describe the same available observations so I do not mistake a partial selection for a complete city or fleet.

**Why this priority**: Available numbers need truthful selection, denominators, gaps, and explanations.

**Independent Test**: Compare known route-hour observations across bus-only, rail-only, city, and all-cities selections, including unequal sample counts and unavailable buckets.

**Acceptance Scenarios**:

1. **Given** Complete bus hours and unavailable rail, **When** only bus routes are selected, **Then** bus coverage may be Complete while Atlanta-wide coverage is Partial, and bus route options remain selectable.
2. **Given** only bus distance is available, **When** shares appear, **Then** bus may show 100% of available distance with that denominator explicitly described and rail listed as unavailable; zero available distance yields unavailable shares.
3. **Given** unequal route sample counts without proven aligned support, **When** aggregate activity is requested, **Then** the aggregate ratio is unavailable with a reason while valid distance and route bubbles survive; proven aligned support uses summed numerators divided by summed valid denominators.
4. **Given** partial and wholly unavailable timeline buckets, **When** drawn, **Then** observed increments accumulate, partial intervals are annotated, unavailable buckets remain gaps, and no connecting segment implies measured movement across a gap.
5. **Given** cities with different local dates, **When** all-cities statistics are viewed, **Then** available contributions are included with missing city/source coverage disclosed and each city's local-day/completed-hour rules preserved.
6. **Given** unknown historical route membership, **When** coverage is described, **Then** expected route hours remain unknown rather than inferred from today's catalog.
7. **Given** current Partial observations and an older Complete snapshot, **When** the current result arrives, **Then** it replaces old selection data; cancelled or obsolete results cannot replace current data.

---

### User Story 4 - Trust history and compatible rollout (Priority: P2)

As an operator and statistics consumer, I want new partial evidence to coexist safely with existing history and older clients without reinterpreting unsafe old data or silently changing contracts.

**Why this priority**: Previously discarded observations cannot be restored by relaxing admission, and compatible reading enables incremental delivery.

**Independent Test**: Read legacy and new hours together, exercise integrity exclusions and retries, and request old/new contracts with cache variations.

**Acceptance Scenarios**:

1. **Given** healthy legacy Complete and legacy Partial cohorts, **When** queried, **Then** healthy cohorts retain existing eligibility, Partial legacy cohorts stay excluded with disclosed reasons, and no history is rewritten.
2. **Given** compatible definitions with unchanged formula, units, and cadence, **When** a window spans versions, **Then** eligible contributions combine with a disclosed definition transition; incompatible versions remain excluded.
3. **Given** conflicts, mixed runs, capture loss, invalid catalogs, unknown provenance, overflow, or processing failure, **When** measures are admitted, **Then** only provably unaffected measures survive and unlocalizable loss excludes the cohort; a sibling-source outage alone does not exclude intact rows.
4. **Given** old, absent, new, or unsupported contract requests, **When** results are requested, **Then** absent/old retain old semantics, new receives qualified available measures, unsupported requests fail clearly, and caches never cross contract boundaries.
5. **Given** disabled sources or changed inventory, **When** hour coverage is assessed, **Then** historical expected dependencies remain explicit and removing a source does not manufacture Complete history.

### Edge Cases

- Independent failures/recovery, disabled sources, configuration changes, startup/shutdown fragments, and overlapping feeds; one successful feed is not full coverage without proven substitute equivalence.
- Realtime trains with unusable positions; valid empty and schedule-only content require distinct positioned-coverage assessment.
- NoData, unavailable, valid zero, Partial, and unsupported derived ratios remain distinguishable.
- Capture loss, bounded identity overflow, catalog changes, retries, conflicts, mixed runs, and unsupported cadence/version cannot be mistaken for ordinary source outage.
- Repeated vehicles across routes/hours cannot become a window-wide unique fleet count.
- Zero denominators, unaligned samples, or invalid bubble axes/sizes cannot blank unrelated charts.
- UTC/local midnight, daylight-saving changes, 24 completed hours, ingestion grace, and definition transitions.
- Previous-day fallback, rapid selection changes, cancellation, and out-of-order refreshes with usable Partial data.
- Rollback readers encountering new evidence must safely exclude unsupported definitions without rewriting history.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Atlanta MUST use MARTA's replacement rail source with an existing valid credential consistently across active Worker/WebAPI configuration paths and environments, without retired-source fallback or a second rail poll.
- **FR-002**: Rail handling MUST distinguish usable realtime observations, successful empty content, intentional disabling, failure, and cancellation; validate value shapes, positions, route alignment, deduplication, and event times. Unusable positions MUST NOT prove complete positioned coverage.
- **FR-003**: Rail failure MUST fail promptly without invalidating bus processing. Credentials MUST remain absent from committed settings, reports, diagnostics, command history, and deployment output; later serving-environment validation MUST use an existing managed secret reference.
- **FR-004**: Every expected source MUST carry bounded stable identity, outcome, receipt time, and failure classification independently of reported-vehicle capture. Expected sources and route dependencies MUST survive throughout the hour; unknown provenance MUST prevent completeness claims.
- **FR-005**: Atlanta MUST explicitly associate bus evidence with static bus routes and rail evidence with its four rail routes. Category MUST NOT universally identify source. Other cities MUST retain healthy existing-hour behavior until their adapters provide equivalent evidence.
- **FR-006**: Route eligibility MUST depend on its own sources, usable route membership, and successful processing. Failed siblings MUST NOT erase valid observations. Overlapping feeds MUST retain explicit dependencies and deduplication; complete substitute equivalence MUST be proven rather than assumed.
- **FR-007**: Successful empty polls MUST contribute valid activity denominators with zero numerators. Failed polls MUST contribute neither. Activity identities/samples MUST be accepted only with valid supporting evidence.
- **FR-008**: Movement MUST retain teleport, stale, nonpositive-delta, transfer, and gap checks. Source continuity, boundaries, and maximum gaps MUST be independent per source; recovery MUST NOT bridge invalid outage movement gaps.
- **FR-009**: Distance, activity, and crossing-publication validity MUST be independent. Publication-only failures MUST preserve valid movement/activity and existing publication diagnostics. Category capture MUST use consistent validity decisions while preserving its reconciliation rules; overlay figures MUST remain based on route-hour cohorts.
- **FR-010**: Newly captured route hours MUST retain versioned bounded evidence: expected/contributing sources and source-set identity; per-source successful/failed/empty counts, boundaries and maximum gaps; independent measure validity/reasons; sample numerators/denominators and populations; existing catalog/run/cadence/conflict/retention evidence. Evidence MUST be limited by configured source inventory without storing per-poll history or vehicle lists.
- **FR-011**: All new evidence MUST participate in validation, atomic persistence, immutable retry equality, and conflict detection. Ordinal route identity, bounded queues/memory, immutable history, quarantine, and retention MUST remain intact.
- **FR-012**: Complete MUST require healthy proven continuity for every expected dependency of the row/selection. Partial MAY carry valid measures. Disabled sources MUST remain limitations; NoData/unavailable MUST remain distinct from observed zero. Complete bus rows and NoData rail rows MUST yield Partial city coverage.
- **FR-013**: Healthy legacy Complete cohorts MUST retain existing admission checks; legacy Partial MUST stay excluded with reasons and without backfill/relabeling/upgrading. Versions MAY combine only with unchanged formulas/units/cadence and disclosed transitions; incompatible or unsupported definitions MUST be excluded.
- **FR-014**: Querying MUST separate cohort integrity from per-row/per-measure eligibility. Conflicts and mixed runs MUST remain conservatively excluded. Overflow, loss, invalid catalog, unknown provenance, and processing failure MUST exclude affected measures or, when impact cannot safely be localized, the affected cohort.
- **FR-015**: Distance MUST sum eligible observed contributions and return a number whenever valid observations support it, including observed zero; partial distance MUST NOT be scaled into estimated full-city/day measures. Route/category totals, timelines, and route options MUST remain independently available.
- **FR-016**: Coverage MUST distinguish contributing hours, fully covered hours, expected route hours, observed route hours, and unavailable route hours. A Partial hour contributes once and is fully covered zero times. Category hour counts MUST NOT be added into city hours; NoData rows MUST NOT count as available measures.
- **FR-017**: Selection and expected route hours MUST use historical route/source membership; unknown membership MUST keep expectations unknown rather than infer them from current catalogs or source lists.
- **FR-018**: Population sums MUST be identified as route/hour populations, never window-wide unique vehicles. Distance ratios MUST preserve route-vehicle-hour and active-clock-hour definitions, use the same contributing route/hour set for numerator and denominator, and be labeled observed ratios when Partial hours contribute.
- **FR-019**: Activity means MUST divide summed valid numerators by summed valid sample denominators within a coherent population. Aggregate activity MUST require proven aligned sample support; shared maximum/minimum denominators or averages of route means MUST NOT substitute. Unaligned support MUST yield a null ratio with a reason.
- **FR-020**: Bubbles lacking a valid axis or size MUST be omitted with reasons in the accessible data table. One unavailable derived metric MUST NOT hide distance or unrelated charts.
- **FR-021**: Category shares MUST explicitly use available distance as denominator, produce null percentages when it is zero, and retain unavailable categories in legends/tables even when an available category represents 100%.
- **FR-022**: Timeline increments, category sums, and displayed total MUST use the same admitted rows/window and reconcile. Partial intervals MUST be annotated; wholly unavailable buckets MUST remain gaps without implied movement or invented zero.
- **FR-023**: City/all-cities scopes MUST preserve existing request shapes, selection, windows, local-day/DST, completed-hour cutoff, ingestion grace, and hourly refresh. All-cities MUST sum available contributions with missing city/source coverage disclosed.
- **FR-024**: New responses MUST expose source coverage and measure-specific availability with bounded reasons for failure, disabling, recovery, invalid evidence, and unaligned samples. Explicit contract opt-in MUST isolate new semantics from absent/legacy requests and all cache variants; unsupported contracts MUST return clear client errors.
- **FR-025**: The client MUST accept valid current-selection Partial snapshots during refresh and previous-day fallback. Older Complete/previous-day values MUST NOT displace valid current Partial results; cancellation and obsolete results MUST remain unable to replace current selection data.
- **FR-026**: The overlay MUST display Partial beside affected cards/chart headings, source names and covered periods in details/data tables, and unavailable categories. No-observation/zero explanations MUST use existing units, localization, accessible descriptions, themes, and mobile layouts.
- **FR-027**: Reported-vehicle counts MUST remain city-wide and independently qualified, without derivation from positioned-route activity or making statistics correctness depend on their feature switch.
- **FR-028**: Feedback MUST remain within Stats and bounded structured diagnostics. No global warnings, live-music/delivery-payload changes, new service/runtime package, historical rewrite, changed category reconciliation, or per-vehicle diagnostic volume is in scope.
- **FR-029**: Verification MUST prove the completed-hour outage journey through persistence and visible selection independently of live credentials, plus recovery/disabling, arithmetic, integrity, version compatibility, time boundaries, cache isolation, stale results, and healthy-city regressions.
- **FR-030**: Implementation handoff MUST record validation commands/results, environment, revision, and UTC windows with unrun live/browser gates explicit. Feature release completion MUST require authenticated serving-environment recovery and controlled browser views showing nonzero bus data without rail and both sources after recovery; production rail MUST NOT be disabled for screenshots.
- **FR-031**: Operational guidance MUST sequence additive evidence storage before new writes and compatible server before client, preserve old reads, stop new writes before incompatible-reader rollback, retain captured history, and prohibit restoring the retired endpoint or manufacturing completeness. This planning workflow MUST NOT execute deployment, secret changes, or historical rewrites.

### Key Entities *(include if feature involves data)*

- **Source evidence**: Bounded expected source identity, outcomes/counts, receipt time, boundaries/gaps, and safe reasons, independent of vehicle-count capture.
- **Historical route/source membership**: Expected route dependencies and source-set identity during an hour, including overlapping feeds and proven substitutions.
- **Route-hour measurement evidence**: Immutable measures/populations, definition/run/catalog/cadence identity, source summary, and separate distance/activity/publication validity.
- **Cohort integrity**: Evidence that city/hour rows belong together without unsafe conflicts or unlocalized loss, separate from partial source coverage.
- **Coverage assessment**: Expected, contributing, fully covered, observed, and unavailable hours/sources for a measure/selection, allowing unknown historical expectations.
- **Statistics snapshot**: Contract-qualified current-selection measures, timeline increments, bubbles/gaps, reasons, and coverage for completed-hour windows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the completed-hour bus-success/rail-failure journey, 100% of admitted bus distance and valid activity samples survive capture/retrieval, rail distance stays unavailable, and Atlanta is visibly Partial; the symmetric journey satisfies the same rule.
- **SC-002**: Across complete-zero, both-failed, intermittent-outage, and recovery scenarios, every zero has valid zero-observation evidence, no failed poll becomes a zero sample, and no invalid outage gap contributes movement.
- **SC-003**: Every acceptance selection/window reconciles displayed distance, categories, shares, and timeline increments to the same admitted observations; every unsupported ratio/bubble has explicit unavailability.
- **SC-004**: Partial status and unavailable sources are visible beside affected figures or headings, with inspectable source/time limitations and omitted bubbles in accessible tables in both themes, both unit systems, and mobile layouts.
- **SC-005**: Every healthy legacy cohort retains its prior eligibility, every unsafe legacy Partial/incompatible/conflicting cohort remains excluded with a reason, and no historical row is rewritten or upgraded.
- **SC-006**: All absent/old-contract requests preserve old semantics, opted-in requests receive qualified new semantics, unsupported contracts fail clearly, and no cached result crosses contract boundaries.
- **SC-007**: Authenticated rail validation demonstrates eligible live trains on the serving revision with no duplicate polling, bus interruption, or exposed credential; a fully observed healthy closed hour can qualify as Complete after existing grace/refresh.
- **SC-008**: Release evidence contains both controlled browser views—nonzero bus statistics without rail and both sources after recovery—and exact validation results with every unrun gate explicitly identified.

## Assumptions

- The primary design defines full end-to-end scope. The investigation describes prior observations/local preparation, not proof of authenticated recovery or permission to change production.
- Existing Stats selections/windows, hourly refresh, identity rules, category history, and reported-vehicle semantics remain the baseline. Rail recovery and independent partial-statistics behavior are both required.
- An operator may possess an existing valid MARTA key; its availability and serving-environment validation remain external release dependencies. Deterministic partial-statistics work must proceed without it.
- Exact endpoint, source/version identifiers, storage representation, negotiation header, affected file inventory, and skill-sync procedure in the source design are planning constraints; this specification states the required evidence and observable outcomes.
- Missing-source coverage is not an integrity conflict. When measurement impact cannot be localized safely, conservative exclusion is required.
- The current authorization covers documentation and necessary Spec Kit metadata only. Migration/release/rollback guidance may be documented; application/test implementation, deployments, secret changes, commits, and historical modifications are not authorized by this workflow.
