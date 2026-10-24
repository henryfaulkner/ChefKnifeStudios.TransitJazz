# Feature Specification: Add Seattle Transit

**Feature Branch**: Existing `059-reported-vehicles` (unchanged; no feature branch created)  
**Feature Directory**: `specs/060-add-seattle`  
**Created**: 2026-10-18  
**Status**: Draft — specification reviewed; release prerequisites remain open  
**Input**: Add Seattle as one city entry, slug `seattle`, combining King County Metro and Sound Transit. Follow the onboarding guide and Seattle compatibility evidence; use direct agency feeds only, intentionally omit route 512 from initial map/audio coverage, preserve raw reported-vehicle semantics, and require quota and service-calendar confirmation before release. Specification only; no implementation, branch changes, commits, push or deployment.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hear Seattle's live transit (Priority: P1)

A visitor selects Seattle or opens its direct link and sees one regional soundscape containing supported King County Metro and Sound Transit vehicles. After unlocking audio, moving vehicles on supported routes produce music.

**Why this priority**: This is the visitor value of adding the city; two agency entries would split the intended experience.

**Independent Test**: Open Seattle during active service, select representative supported routes from each agency, observe several updates and listen after audio unlock.

**Acceptance Scenarios**:

1. **Given** the city catalog, **When** a visitor selects Seattle or visits `/#seattle`, **Then** exactly one Seattle entry loads, the address identifies `seattle`, and the map centers on a transit-dense Seattle starting view.
2. **Given** fresh supported vehicles from both agencies, **When** updates arrive and audio is unlocked, **Then** vehicles appear on the expected routes, move plausibly, and generate audible output.
3. **Given** route 512 observations without direct-agency geometry, **When** Seattle loads, **Then** route 512 has no initial map/audio coverage, no invented geometry is shown, and visitor copy discloses the omission.
4. **Given** a Seattle session, **When** the visitor switches cities, filters routes, reconnects and returns, **Then** the selected city and route behavior remain consistent and existing cities continue to work.

### User Story 2 - Trust the combined agency view (Priority: P1)

A visitor sees one representation and musical contribution for each overlapping vehicle, while operators can distinguish source failure, empty service and unsupported observations without exposing credentials.

**Why this priority**: Duplicate vehicles distort both the soundscape and its statistics; shared authentication can disclose a private credential to an unrelated host.

**Independent Test**: Use sanitized representative observations covering overlapping and distinct vehicles, route mappings, missing fields, and independent source outcomes; compare displayed and captured populations to explicit expected results.

**Acceptance Scenarios**:

1. **Given** the same physical vehicle observed by both sources, **When** both observations are eligible for map/audio, **Then** one vehicle and one musical contribution result according to a documented deterministic overlap policy.
2. **Given** equal vehicle identifier text without evidence of a common identity namespace, **When** observations are combined, **Then** unrelated vehicles are not silently merged.
3. **Given** one source fails and the other succeeds, **When** Seattle updates, **Then** usable observations from the healthy source remain available and failed-source coverage is distinguishable from successful-empty coverage.
4. **Given** source requests, **When** authentication is applied, **Then** only the intended OneBusAway source receives its credential; KCM and public static sources receive none.

### User Story 3 - Understand Seattle coverage and statistics (Priority: P2)

A visitor reads accurate Seattle listening guidance and sees Seattle statistics with the same scope, time windows and evidence qualifications as other cities.

**Why this priority**: The initial feed inventory and historical evidence have limits that visitors must be able to understand.

**Independent Test**: Inspect the unlock overlay, information panel, and Stats for Seattle and All using known completed-hour evidence and pending/partial cases.

**Acceptance Scenarios**:

1. **Given** a fresh Seattle visit, **When** the unlock overlay and information panel open, **Then** both identify the live-transit music experience, explain listening, name the combined agencies and truthfully describe included coverage, route 512's omission and required attribution.
2. **Given** Seattle history, **When** Stats opens, **Then** its display name is Seattle, its local-date boundaries use `America/Los_Angeles`, and All includes Seattle once under existing aggregation rules.
3. **Given** raw identifiable observations removed from map/audio by route 512 exclusion, missing geometry or overlap selection, **When** reported-vehicle collection applies, **Then** those raw observations retain the existing capture eligibility and coverage semantics rather than inheriting display filters.
4. **Given** insufficient completed-hour evidence, **When** Stats opens, **Then** pending, unavailable or partial evidence is disclosed under existing rules and is not presented as a verified zero or complete total.

### User Story 4 - Release with evidence and recovery (Priority: P2)

The release owner can assess local and production evidence, withhold release while prerequisites remain unresolved, and restore the previous healthy release if Seattle causes a failure.

**Why this priority**: A selectable city is usable only when its actual running backend, feeds and client have been verified together.

**Independent Test**: Review a completed release evidence record against the prerequisites and acceptance requirements below; pending checks must prevent a claim of deployed-and-verified status.

**Acceptance Scenarios**:

1. **Given** unconfirmed quota, calendar validity or intended authentication access, **When** release readiness is assessed, **Then** the specific prerequisite remains open and release is withheld.
2. **Given** successful local verification and resolved prerequisites, **When** release is prepared, **Then** the owner can identify the actual targets, credential references, version identifiers, backend-first sequencing and previous healthy versions before publication.
3. **Given** failed readiness, authentication, publication or existing-city checks, **When** rollout is assessed, **Then** visitor exposure stops and the recorded recovery process restores healthy server/client behavior without destructive data rollback.

### Edge Cases

- The Sound Transit feed includes route 512, route-less vehicles or routes with no usable geometry: exclude ineligible map/audio observations while preserving applicable raw reporting evidence.
- Both sources describe one vehicle with different observation times or positions: use a deterministic overlap decision without duplicate motion or sound; conflicting identities must not become an unsupported cross-source union.
- A static refresh introduces route-key collisions or ambiguous source mappings: expose the conflict and withhold affected coverage rather than assigning a vehicle to an arbitrary route.
- A source is empty, fails authentication, times out, returns malformed data or has stale positions: retain distinct outcomes and existing cancellation, freshness and partial-source behavior.
- Missing vehicle descriptor IDs: no invented identity fallback; existing coverage qualification applies.
- Sunday or inactive-service samples omit a mode: neither infer absence for all service periods nor promise unobserved operation.
- Indexed environment overrides produce a different effective city/source configuration, or overlapping release instances duplicate capture ownership: fail readiness until the conflict is resolved.
- Newly added Seattle has no completed hours, or a Seattle-local day crosses daylight-saving boundaries: use existing evidence states and city-local window semantics.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The catalog MUST expose exactly one city with canonical slug `seattle`, display name Seattle and IANA time zone `America/Los_Angeles`, combining King County Metro and Sound Transit.
- **FR-002**: Seattle MUST use only the direct-agency realtime and static source set identified in the evidence baseline. The consolidated Puget Sound regional feed MUST NOT be used, including as a temporary bridge, fallback or geometry supplement.
- **FR-003**: Route 512 MUST be excluded from initial map/audio coverage. Its exclusion MUST NOT remove otherwise eligible raw reported-vehicle observations. Adding its map/audio coverage later requires durable direct-agency route/geometry evidence and a separately reviewed scope change.
- **FR-004**: Authentication MUST be isolated per source. The OneBusAway credential MUST be sent only to its intended agency-40 request, never to KCM, public static downloads, logs, public responses or visitor content. Static and realtime access MUST each be verified through their intended access mechanism.
- **FR-005**: The intended OneBusAway credential environment variable MUST be named consistently in future configuration and operational evidence, with owner, renewal and runtime secret-reference information recorded without values. Planning MUST resolve its exact name and request construction before implementation; generic shared-key behavior is insufficient.
- **FR-006**: Source route identifiers MUST resolve through verified direct-static route mappings to canonical display routes and geometry. Known examples include `100451` → `556`, `102767` → `547`, `100479` → `1 Line` and `2LINE` → `2 Line`. Unmatched or ambiguous observations MUST NOT be assigned invented routes or shapes.
- **FR-007**: Overlapping physical vehicles MUST contribute at most one map vehicle, one movement contribution to route history and one audio contribution at a time. Planning MUST document deterministic source precedence/selection and identity evidence; equal identifier text alone MUST NOT establish shared fleet identity.
- **FR-008**: Each raw source response MUST retain its own receipt-time observation and success, failure or successful-empty coverage before route mapping, coordinate/geometry eligibility, overlap selection or map/audio publication. Combining source results MUST preserve these distinctions independently of publication success.
- **FR-009**: Reported-vehicle identities MUST follow the existing source/namespace/rule contract, preserve distinct populations across hours and avoid double counting verified shared vehicle namespaces. Missing descriptor IDs MUST NOT introduce undocumented fallback identities. Route 512 exclusion and map deduplication MUST NOT redefine the raw reported population.
- **FR-010**: Seattle MUST have an explicit reported-source policy for every enabled endpoint, with stable source identity, justified vehicle namespace, supported identity rule and measured cadence/gap expectations. Unsupported coverage MUST be recorded explicitly; collection/read flags MUST NOT be enabled globally merely to add Seattle.
- **FR-011**: Both supported server hosts MUST recognize the same Seattle identity, sources, time zone and processing behavior. Effective environment-specific and deployed settings MUST preserve existing city order and avoid indexed override collisions or unrelated-city changes.
- **FR-012**: Seattle registration MUST include route catalog/geometry access, visitor selection/direct links, the initial map center, the Stats display identity and resource, intended history collection selectors/cadence, and relevant observability filters and configured limits. Local readiness MUST validate effective settings as well as successful startup.
- **FR-013**: Seattle statistics MUST preserve existing distance, route-selection, category, completed-hour, coverage and reported-vehicle semantics. Seattle MUST appear once in All; reported counts MUST retain city-wide scope under route filtering, and time boundaries MUST use Seattle's local date. No fabricated history or population backfill is permitted.
- **FR-014**: Seattle's unlock overlay MUST provide a header and three authored listening paragraphs, and its information panel MUST provide companion copy, using the existing visitor resource system. Both MUST name the combined coverage accurately, disclose route 512's omission, satisfy applicable attribution/terms obligations and avoid promises of Sounder or other unverified live modes.
- **FR-015**: Coverage claims MUST distinguish static availability from observed realtime support. The existing classification of streetcars as rail and water taxis as bus MUST be accounted for in presentation and acceptance; this feature does not redesign global mode categories or introduce position synthesis.
- **FR-016**: Source processing MUST preserve existing bounded requests, cancellation, safe diagnostics, freshness rules and partial-source handling. A healthy source MUST remain usable when its partner fails, with truthful source coverage and no credential or raw identity disclosure in public evidence.
- **FR-017**: Before release, the owner MUST confirm the permitted request rate with Sound Transit against actual polling intervals, endpoint counts, retries, all environments, active instances and overlapping revisions. The 10-second estimate of 8,640 requests per endpoint/day/instance is demand evidence only, not an approved quota.
- **FR-018**: Before release, both direct-static sources' service calendars and exceptions MUST be checked against the intended service period, and representative service-hour samples across multiple updates MUST confirm the claimed launch coverage. Sunday measurements alone MUST NOT certify full-service or weekday coverage.
- **FR-019**: Before implementation, planning MUST resolve source-specific access, overlap and route-mapping behavior and document any precise authentication capability gap. Before release, intended credential access MUST be established without treating a successful key-carrying response as proof of key validation where keyless access also succeeds.
- **FR-020**: Local verification MUST include successful solution build and affected existing tests, sanitized source/authentication/mapping/overlap/failure cases, Seattle-specific catalog and representative shapes, several service-hour publication updates, plausible visible motion and human listening after unlock. Browser checks MUST cover direct links, switching, route filters, reconnection, Stats, desktop/mobile sizes and both themes, plus existing-city regressions. These are required future checks, not results of this specification.
- **FR-021**: Local acceptance MUST use explicitly confirmed local client/backend destinations and one intended capture owner; it MUST NOT silently use production endpoints or run a second worker against the same capture pipeline.
- **FR-022**: Future verification MUST maintain `validation.md` in this feature directory with check, environment, UTC time, result, evidence link and unresolved issue. Planned checks MUST remain separate from measured results; evidence MUST record representative routes, version/workflow/revision identifiers, credential ownership references and accepted limitations without secrets or raw vehicle identities.
- **FR-023**: Release preparation MUST identify actual production targets and workflow triggers, runtime credential bindings, effective overrides, single-owner requirements and any schema prerequisites. A normal city addition MUST NOT assume a migration is needed. If pending migrations or historical-feature gates apply, their existing runbooks MUST be satisfied before release.
- **FR-024**: The human release owner MUST review the exact changes and control commits/publication. Backend acceptance MUST precede exposing the client picker entry. The separate MartaJazz site is outside this release unless separately authorized. Recovery MUST identify previous healthy server/client versions and preserve stored data.
- **FR-025**: Production acceptance MUST verify the intended server/client versions are serving, Seattle-specific readiness/catalog/geometry, multiple successful processing/publication updates, `https://transitjazz.com/#seattle`, audible output, switching and existing-city health. Historical acceptance MUST include its required completed-hour observation window. Workflow success alone MUST NOT count as deployed-and-verified acceptance.

### Key Entities *(include if feature involves data)*

- **Seattle city**: One visitor and statistics identity, with its canonical slug, display name, local time zone, starting map view and combined agency coverage.
- **Agency source**: An independently authenticated and observed realtime or static source, with access requirements, source identity, service scope and availability evidence.
- **Mapped route**: A source route reference associated with a canonical display route, geometry and existing transit category; route 512 is initially outside map/audio eligibility.
- **Vehicle observation**: A source's original identifiable vehicle report, receipt time and evidence quality, distinct from the eligible map/audio representation.
- **Cross-source vehicle identity**: A justified relationship between overlapping source reports used to avoid duplicate display and statistics while preserving raw coverage.
- **Release evidence**: Dated measured acceptance results, unresolved prerequisites and running version references under a human release owner.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Seattle is reachable through exactly one picker entry and `/#seattle`; all required direct-link, switch, reconnect and route-filter scenarios pass on desktop and mobile in both themes.
- **SC-002**: During local and production service-hour acceptance, multiple consecutive source updates produce verified route-following motion and audible output for representative supported vehicles from each agency; route 512 contributes zero map/audio vehicles.
- **SC-003**: All representative overlap cases yield exactly one displayed vehicle and musical contribution; all distinct-identity, missing-ID, raw route-512 and independent-source coverage cases match their declared expected populations without fabricated identities.
- **SC-004**: Every launch registration, visitor surface and enabled Seattle statistics surface resolves the Seattle identity correctly; known completed-hour fixtures match existing city-local and All aggregation rules, including pending/partial outcomes.
- **SC-005**: No credential is transmitted to an unintended source or appears in tracked artifacts, public output or acceptance evidence; every required source has a recorded access outcome and justified identity/coverage policy.
- **SC-006**: Release acceptance has zero unresolved quota, service-calendar, intended-access, readiness or existing-city regression blockers, and every required local/production check has dated evidence or an explicit pending result that prevents release completion.
- **SC-007**: A deployed-and-verified record identifies the exact running backend and client versions, public Seattle URL, successful service-hour observations, required completed-hour evidence and recoverable previous releases; publication remains incomplete until all required checks pass.

## Assumptions

- [Onboarding guide](../../docs/ADDING_A_TRANSIT_CITY.md) defines the current whole-release inventory; [Seattle compatibility report](../../docs/city-compat/seattle.md) is the dated evidence baseline, not proof of future availability or deployment.
- Approved source set: KCM realtime `https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb`; Sound Transit OneBusAway agency-40 GTFS-Realtime vehicle positions as identified in the report; direct static `https://www.soundtransit.org/GTFS-KCM/google_transit.zip` and `https://www.soundtransit.org/GTFS-rail/40_gtfs.zip`. Planning must resolve and verify the exact agency-40 URL from the existing evidence/official documentation without inserting credentials into artifacts.
- The baseline's five paired Sunday samples measured KCM route joins of 42/42 distinct routes, Sound Transit joins of 4/5 and four overlapping descriptor IDs per pair. These are sample denominators, not fixed release inventory or full-service guarantees.
- The separate key-carrying OneBusAway response decoded successfully, but key acceptance remains unverified because keyless requests also succeeded. Actual account quota, production interval/instance count and service-calendar validity are unresolved release prerequisites, not clarification questions or verified assumptions.
- Existing global music, route identity, mode classification, history retention and Stats contracts remain authoritative. No new mode taxonomy, position synthesis, unrelated-city redesign or global feature activation is requested.
- This invocation creates only the specification, its quality checklist and active-feature pointer. Implementation design, a running validation record, tests, plan/tasks and operational actions belong to later explicitly invoked phases. No feed requests, secret reads or release checks were performed for this specification.
