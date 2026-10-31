# Feature Specification: StatsOverlay Retrieval Optimization

**Feature Branch**: No feature branch required; feature directory selection is explicit.  
**Feature Directory**: `specs/061-stats-retrieval-optimization`  
**Created**: 2026-10-30  
**Status**: Ready for planning  
**Input**: Entire [StatsOverlay Retrieval Optimization design document](../../docs/STATS_OVERLAY_RETRIEVAL_OPTIMIZATION_DESIGN_DOCUMENT.md). Hourly default and backend-adjustable interval are locked decisions. This workflow produces planning artifacts only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open warmed statistics immediately (Priority: P1)

A visitor uses the map and audio while current-city and All-cities statistics become available in the background. Opening or reopening statistics shows the matching retained snapshot immediately, and ordinary refreshes preserve usable charts.

**Why this priority**: Removing repeated loading screens is the principal visitor benefit; preparation must preserve the map and listening experience.

**Independent Test**: Start a visible app with preparation enabled, wait for current-city and All preparation, then open, switch scope, close, and reopen statistics while observing displayed data, requests, map readiness, and audio readiness.

**Acceptance Scenarios**:

1. **Given** a visible app with statistics and background preparation enabled, **When** initial UI load completes, **Then** current-city retrieval begins before All-cities retrieval without delaying rendering, transit updates, or audio.
2. **Given** an admitted matching baseline snapshot before its boundary, **When** statistics opens, **Then** its data appears without a full loader or opening-triggered request and includes its original update time.
3. **Given** matching data is visible, **When** that selection refreshes or the dialog closes and reopens, **Then** retained data survives and refresh preserves usable charts, tables, and focus.
4. **Given** an open overlay, **When** only theme or the independently planned units preference changes, **Then** presentation updates without retrieval or a different data identity.
5. **Given** background preparation is disabled, **When** statistics opens without retained data, **Then** on-open retrieval and subsequent visible-overlay boundary refresh work through the same request ownership.

---

### User Story 2 - Follow an honest, centrally adjustable schedule (Priority: P1)

A visitor sees appropriately dated statistics refreshed at shared boundaries while the app is visible, even with the dialog closed. An operator can reduce the interval without rebuilding the client.

**Why this priority**: This provides freshness without minute polling and establishes the agreed cost/freshness tradeoff.

**Independent Test**: Control time, visibility, and refresh settings; verify returned boundaries govern retrieval, including local-day transitions and a changed interval.

**Acceptance Scenarios**:

1. **Given** hourly scheduling with a two-minute grace offset, **When** preparation completes at 10:59 UTC, **Then** the next scheduled refresh is 11:02 UTC plus only 0–5 seconds positive jitter, rather than 11:59 UTC.
2. **Given** baseline selections and a visible app, **When** a boundary arrives with the overlay closed, **Then** each due baseline is refreshed once subject to admission and cooldown.
3. **Given** the server interval changes from 60 to 15 minutes, **When** an existing client receives updated scheduling information, **Then** subsequent scheduling follows new boundaries without client code changes or rebuild.
4. **Given** a hidden tab misses several boundaries, **When** it becomes visible, **Then** overdue displayed data is prioritized and each needed selection is refreshed once without replaying missed ticks.
5. **Given** a represented city's local day ends before the ordinary boundary, **When** that day ends, **Then** retrieval is due and retained daily evidence is explicitly dated and stale or its daily presentation is withheld.

---

### User Story 3 - Preserve exact selections and truthful failures (Priority: P1)

A visitor switching cities, scopes, or routes always sees the requested selection and can distinguish fresh evidence, stale evidence, loading, and unavailable data.

**Why this priority**: Faster access must not substitute another selection's numbers or conceal outages and incomplete evidence.

**Independent Test**: Use controllable overlapping responses, route changes, invalid responses, failures, clock changes, and stale expiry to verify labels and retained evidence.

**Acceptance Scenarios**:

1. **Given** a new route set, **When** the 200 ms debounce completes, **Then** matching retained evidence or selection loading appears; previous-route or unfiltered numbers are never relabeled.
2. **Given** city A retrieval, **When** navigation changes to city B and A arrives late, **Then** A cannot populate B and the new city is prepared while independent All evidence remains eligible for retention.
3. **Given** usable data and a failed refresh, **When** failure is known, **Then** charts remain with explicit stale/update and safe retry messaging; known zero remains zero and missing evidence remains missing.
4. **Given** no replacement 30 seconds after its boundary, **When** state is read, **Then** evidence is visibly stale; after two configured intervals beyond that boundary it is unavailable.
5. **Given** an older, mismatched, or malformed successful response, **When** acceptance is evaluated, **Then** it cannot replace valid evidence or reset age, and malformed evidence records an invalid-response error.
6. **Given** retry cooldown, **When** a visitor opens, closes, changes routes, or retries manually, **Then** the applicable cooldown remains enforced without stealing focus.

---

### User Story 4 - Share work within measured resource limits (Priority: P2)

An operator serves repeated identical selections with bounded shared work, measures request and processing costs, and can disable preparation or selected-route retention independently.

**Why this priority**: Savings must hold under boundary bursts and diverse selections without sacrificing correctness or exceeding resource limits.

**Independent Test**: Exercise matching bursts, different selections, pressure, canceled callers, disabled settings, and multiple replicas; compare measured visitor latency and costs to a recorded baseline.

**Acceptance Scenarios**:

1. **Given** reordered or duplicated exact route keys, **When** simultaneous equivalent requests arrive, **Then** one matching fill serves callers; case-distinct keys and different selections remain isolated.
2. **Given** matching retained server evidence before its boundary, **When** another visitor requests it, **Then** no new underlying read occurs and original generation/schedule metadata is preserved.
3. **Given** selected-route retention is full, **When** another combination arrives, **Then** baseline entries are protected, totals stay bounded, and a valid uncached response remains possible.
4. **Given** expired server evidence and failed replacement, **When** a request completes, **Then** the existing safe failure returns without silent expired-server fallback.
5. **Given** preparation is off or filtered retention is zero, **When** statistics is used, **Then** the documented on-open path or exact-request shared-fill path remains available respectively.

### Edge Cases

- Opening during preparation joins matching work; cold sessions and uncached combinations can legitimately load.
- Zero, values above 60, fractional intervals, and integers not dividing 60 fail configuration validation.
- Exact-before/at/after boundaries, crossing fills, changed cutoffs/policies, DST, and distinct city-local days cannot extend invalid evidence.
- Caller cancellation cannot cancel others' shared work; deadlines, shutdown, failures, and admission rejection release resources.
- At 33 baseline scopes, no filtered entry need fit; oversized valid evidence may serve an active visitor without retention.
- All is independently consistent, not merged city snapshots; replicas can differ in generation time.
- Clock changes, invalid future metadata, unchanged responses, hidden boundaries, and long retry delays cannot extend freshness/retention.
- Absent additive schedule fields use legacy short behavior; supplied invalid fields are rejected.
- Complete, partial, pending, unavailable, and observed-zero evidence remains independent of freshness.
- Repeated lifecycle actions cannot create extra schedulers, competing visibility listeners, hidden charts, or unobserved failures.
- Repeated filtered activity cannot starve due baselines; only the latest filtered desire remains pending.
- Legacy clients can poll every minute until reload; rollout measurements include overlap and synchronized bursts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST prepare current-city statistics first and All-cities next after first visible UI load when statistics and preparation are enabled, without blocking map, transit updates, or audio readiness.
- **FR-002**: One app-lifetime retrieval owner MUST manage idempotent startup, all request triggers, retained evidence, scheduling, notifications, and teardown independently of dialog lifetime. Statistics disabled means no statistics work; a server-disabled result stops automatic attempts until explicit feature reevaluation or reload.
- **FR-003**: At most three snapshot slots MUST be retained: current unfiltered city, All-cities, and latest filtered selection, initially within 32 MiB estimated retained-object memory. Admission priority MUST be city, All, then filtered. Oversized valid responses MAY serve an active consumer without retention; missed retention MUST be recorded.
- **FR-004**: Navigation MUST discard prior-city slots and cancel/invalidate their work, retain independent All evidence, and prepare the new city. Reopening MUST preserve existing current-city/unfiltered initial controls without clearing retained evidence.
- **FR-005**: The server MUST control a fixed-origin UTC interval, default 60 minutes, offset by existing ingestion grace, default two minutes. Valid intervals MUST be integers 1–60 dividing 60 exactly; invalid configuration MUST fail startup.
- **FR-006**: Effective refresh/expiry MUST be the next configured boundary or earlier represented city-local day end. Hits MUST NOT extend boundaries. The former fixed 60-second server lifetime MUST stop governing schedule-aware responses.
- **FR-007**: The interval MUST change retrieval frequency only; completed-hour aggregation, metric definitions, coverage, reported-vehicle isolation, cutoff arithmetic, consistent All scope, existing request contract and endpoint MUST remain intact. Fifteen-minute refresh MUST NOT create quarter-hour statistics.
- **FR-008**: New-server responses MUST convey original interval, refresh-window start, and effective next refresh alongside existing generation, cutoff, selection, coverage, and day metadata. Hits MUST preserve these values; additive fields MUST remain compatible with existing clients.
- **FR-009**: Missing scheduling fields MUST use legacy 60-second scheduling/expiry and existing short retention until schedule-aware evidence arrives; clients MUST NOT infer hourly freshness. Supplied invalid interval, ordering, or timestamps MUST be invalid evidence.
- **FR-010**: Settings changes MUST follow existing reload/restart operations and consistent replica configuration. Open clients learn changes on their next accepted response or reload; no configuration push is required.
- **FR-011**: A visible app with preparation enabled MUST refresh city/All at returned boundaries even with the overlay closed. Filtered selections MUST refresh automatically only while displayed. Opening with usable matching evidence MUST NOT add a request.
- **FR-012**: Scheduling MUST wait for the earliest needed boundary, use independent positive 0–5 second jitter without early requests, and recompute after acceptance, resume, or changed metadata. Evidence satisfying a window MUST satisfy duplicate triggers. Local-day, stale-notice, and expiry transitions MUST be evaluated during backoff too.
- **FR-013**: Hidden tabs MUST pause scheduling, cancel app-owned waiting requests, and retain snapshots. Resume MUST refresh overdue selections once with displayed selection first, without replaying ticks. Closing MUST NOT count as hidden. Shared visibility ownership MUST remain intact.
- **FR-014**: At most one statistics request per app MUST run. Exact duplicates MUST join work; new route sets MUST preserve 200 ms debounce and only the latest filtered desire. Due work MUST prioritize displayed selection, city, then All while fairly alternating due baselines so interaction cannot starve them.
- **FR-015**: Retry MUST start at 60 seconds with exponential backoff capped at five minutes, honor longer server retry delays, retain per-selection failure state, and enforce endpoint-wide cooldown after rate limiting. Interaction/manual retry MUST NOT bypass cooldown. Transport success resets backoff; only valid acceptance clears evidence error.
- **FR-016**: Publication MUST validate supported contract/metric definitions, required structure, exact normalized selection, usable timestamps, and generation. Older generations or generation times MUST NOT replace newer evidence, including across replicas. Complete evidence MUST publish atomically.
- **FR-017**: Unchanged evidence MAY update contact state but MUST NOT reset age, deadlines, or presentation. Closing MUST NOT prevent valid baseline acceptance. Invalid successful responses MUST preserve valid evidence and record invalid-response error.
- **FR-018**: Matching usable evidence MUST appear immediately on open with original update time. Same-selection refresh MUST preserve charts, tables, and focus. Missing matching evidence MUST honestly load or show unavailable/retry rather than another selection's values.
- **FR-019**: Known refresh failure MUST immediately mark retained evidence stale. Missing replacement MUST be visibly overdue 30 seconds after its original effective boundary. Evidence MUST expire by two response-configured intervals after that boundary, independently of replacement success or browser wall-clock changes.
- **FR-020**: Generation time MUST describe age, cutoff the completed-hour limit, and next refresh freshness. Receipt MUST be separate, using elapsed-time deadlines and bounded skew validation so clock changes or future metadata cannot produce indefinite freshness.
- **FR-021**: At local-day rollover, old daily evidence MUST be explicitly dated historical evidence with prominent stale notice or its daily presentation MUST be withheld. Yesterday's totals MUST NOT use today's heading or become an authoritative previous-day fallback.
- **FR-022**: Coverage and freshness MUST remain independent: refresh failure MUST NOT change known zero into missing evidence, nor missing evidence into zero. Partial, pending, unavailable, and reported-vehicle semantics MUST remain intact.
- **FR-023**: Existing projection, metadata enrichment, route controls, accessible tables, focus, immediate dismissal, themes, and resource-backed English copy MUST remain. Failures/recovery MUST be politely announced without every tick or focus movement. Background work MUST NOT build hidden charts or repeatedly rerender the main app; only changed displayed evidence or presentation inputs rebuild presentation.
- **FR-024**: Theme and the independently planned units preference MUST affect presentation only, with no retrieval or identity change. This feature MUST NOT implement the separate units-toggle feature.
- **FR-025**: Server retention/shared fills MUST use identical exact normalized selection/reporting identity: scope/city, ordinal-sorted distinct case-sensitive route keys, refresh window/interval, cutoff, city-local date/time-zone policy, definition versions, configured-city/cadence policy, grace, and reported-vehicle policy/settings. Ambiguous identities MUST NOT conflate requests; changed policy/dates MUST invalidate reuse.
- **FR-026**: Matching concurrent requests, filtered included, MUST share one fill per full identity per replica. Unexpired matching hits MUST require no new underlying read. Fills MUST preserve four simultaneous reads per replica, busy rejection without unbounded queue, five-second whole-read deadline, existing response limits, and no lock held across external waits.
- **FR-027**: Caller cancellation MUST stop only its wait. Shared work MUST terminate on its own deadline or host shutdown and release resources on every outcome. Failed fills MUST be removable for retry.
- **FR-028**: Fills finishing after a boundary MUST NOT be retained or replace newer evidence. If returned, their original expired boundary MUST remain visible and permit admitted follow-up. Clock changes MUST NOT resurrect expired server entries.
- **FR-029**: Valid successful responses, including partial, pending, unavailable, and zero evidence, MUST be eligible for retention. Transport failures/problems MUST NOT be retained as statistics. Expiry followed by failure MUST return the existing safe failure without silent expired-server fallback.
- **FR-030**: Server retention MUST stay within 33 entries/64 MiB estimated objects. Filtered retention MUST initially stay within eight entries/16 MiB inside those totals. Configurable subset limits MUST validate against totals; zero entries MUST disable filtered retention while preserving bounded exact coalescing.
- **FR-031**: Expired entries MUST be removed first. Filtered admission MAY evict least-recently-used filtered entries but MUST NOT evict baselines. Baselines MUST evict filtered entries before older baselines when needed. Nonfitting valid responses MUST return uncached. Estimates MUST include key/entry overhead; transient memory MUST be measured separately.
- **FR-032**: Retention MUST remain process-local and bounded. Replicas MUST preserve equivalent semantics/bounded freshness without promising identical evidence or one global read/hour. All MUST remain one consistent read, not assembled from cached cities.
- **FR-033**: Existing rate limiting, safe errors, compression, response caps, anonymous access, and no-store transport behavior MUST remain. No new raw identities/sensitive fields may be exposed. Persistent browser storage, distributed retention, conditional retrieval, cross-tab coordination, statistics push, new bulk endpoints, worker/schema changes, and changed aggregation queries are out of scope.
- **FR-034**: A separate preparation rollout switch MUST sit beneath the existing statistics gate. With preparation off the same owner MUST support on-open retrieval and visible-overlay boundary refresh; with statistics off no work runs. Filtered retention MUST be independently reversible to zero without removing coalescing.
- **FR-035**: Existing diagnostics MUST measure bounded-label hits, misses, coalescing, evictions, skipped admission, entries/estimated bytes, fill duration, preparation completion, warm opens, loaders, age on open, and failures. Route sets, raw keys, hashes, and visitor identifiers MUST NOT be metric labels; no new telemetry platform is required.
- **FR-036**: Before broad enablement, baseline/matched evidence MUST cover warm/cold opens, never-opened sessions, map/audio readiness, response sizes, retained/transient memory, requests, compressed egress, serialization cost, underlying reads, snapshot age, boundary lag, and loader frequency. Verification MUST include same-scope bursts, diverse selections, pressure, replicas, shared-IP limits, 500 visible apps, and synchronized boundaries.
- **FR-037**: Delivery MUST update existing read/response contracts and deployment settings to remove unfiltered-only sharing/retention and fixed-minute freshness claims. Rollout MUST stage schedule-aware serving/filtered retention, retained-overlay retrieval, then preparation; measure legacy overlap and document rollback/actual evidence before expansion.

### Key Entities *(include if feature involves data)*

- **Statistics selection**: Scope, applicable city, and exact normalized route-key set, independent of labels, units, or theme.
- **Statistics snapshot**: Complete accepted selection metrics, coverage, generation/cutoff times, day windows, versions, and original schedule; immutable evidence after acceptance.
- **Refresh policy/window**: Backend interval/grace, fixed UTC window, effective boundary considering represented city-local days.
- **Retained selection state**: Optional snapshot, receipt/deadline, progress, error, and retry eligibility; outlives the dialog.
- **Shared fill**: Bounded work for one reporting identity serving independent callers.
- **Retention budget**: Total/filtered resource bounds and admission priority, separate from transient processing memory.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every tested warm open with admitted matching not-yet-due baseline evidence shows data without full loader or opening-triggered retrieval; target default data binding is at most 100 ms on the recorded supported device profile, with chart drawing measured separately.
- **SC-002**: For stable exact selections, one shared underlying read serves each matching cold burst per replica; unexpired warm reads require zero new reads. Target warm statistics response p95 is at most 250 ms under recorded representative load; existing cold/prepared goals and five-second whole-read deadline remain.
- **SC-003**: With 500 continuously visible apps, two baselines, hourly defaults, and no startup/navigation/retry/day-transition extras, steady-state baseline retrieval is approximately 1,000 responses/hour, about 98% below the equivalent minute schedule. Burst performance, serialization, and egress are reported separately.
- **SC-004**: Controlled-time cases show zero interval refreshes between returned boundaries, no early jittered requests, one needed refresh per selection after resume, and adoption of 15-minute backend policy without client rebuild.
- **SC-005**: All selection/race/failure/day-boundary scenarios preserve exact labels/original dates, never turn missing evidence into zero or expired evidence current, show known failures immediately and overdue evidence at 30 seconds after its boundary, and remove fallback by two configured intervals beyond it.
- **SC-006**: Representative city and All evidence fit together within proposed three-slot/32 MiB app retention; server evidence stays within total 33-entry/64 MiB and filtered eight-entry/16 MiB budgets under pressure. Actual retained/transient memory is reported before release.
- **SC-007**: Recorded matched runs show no measurable map-readiness or first-audio regression against baseline measurement variability, including never-opened sessions. Evidence states device/load profile and variability; no unmeasured threshold is claimed.
- **SC-008**: Correctness and browser acceptance cover all four stories, mixed versions, rollback, both themes, keyboard/focus, and disposal. Broad enablement requires passing recorded evidence and measured costs; this specification claims no runtime result.

## Assumptions

- Entire source design governs scope. Features 058/059 supply metric, cutoff, coverage, reported-vehicle, endpoint, and safety semantics; only conflicting retention/freshness statements change during implementation.
- Hourly scheduling is agreed; memory bounds and timing targets require representative measurement. This phase performs no implementation, measurement, enablement, deployment, or commit.
- Initially hidden tabs wait until visible to retrieve. Preparation off disables baseline background work while on-open retrieval and refresh of the displayed selection remain.
- Existing server reload/restart and client feature configuration are reused; no push to open tabs is required.
- Exact clock-skew tolerance, device/load profile, diagnostics plumbing, and existing short legacy retention are planning decisions grounded in project conventions; they cannot weaken bounded freshness or invent hourly legacy fallback.
- Units design is presentation context only; this feature preserves compatibility without implementing its control.
- Distributed/serialized retention, conditional requests, cross-tab coordination, persistent statistics, push, and reusable route/hour datasets are deferred. No new package, schema, worker behavior, or query arithmetic is required.
- Directory selection is explicit and independent of branch names; downstream phases must resolve this directory through the feature selector.
