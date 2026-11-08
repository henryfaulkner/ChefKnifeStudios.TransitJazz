# Research: Atlanta Rail Recovery and Available Statistics

**Date**: 2026-11-07. Based on both complete source documents, constitution v3.3.2, active spec and repository seams. Live evidence remains a verification dependency, not an assumed fact.

## R1. Production rail call path

**Decision**: Keep Worker → configured ITransitCity → MartaCity as the only production rail poll in both hosts. Both Program.cs files bind the named `TransitSources:atlanta:RailRealtime` section and register MartaCity/named RailRealtimeApi, independent of `Cities[]` ordering. A source search finds RailRealtimeAdapter only at its declarations; it is a separate implementation with no production registration/call. Keep normalization consistent using a small shared pure helper without invoking a second poll.

**Rationale**: Updating only the separate adapter misses production. Preserve all six prepared URL edits across four settings files, including inactive Marta legacy entries; validate Atlanta index zero and active overrides later.

**Alternatives considered**: A new service, calling both implementations, retired-endpoint fallback and keyless support are rejected.

## R2. Authentication, schema, positions and time

**Decision**: Exact replacement URL and existing apiKey parameter, encoded safely; missing credentials fail before HTTP, caller cancellation propagates, timeout is a bounded source failure. Set the named RailRealtimeApi HttpClient timeout to five seconds in both hosts (below the current ten-second cycle); do not add rail retries. No secret/credential-bearing URI in logs, exception text, HttpClient diagnostics, reports or history. Validate array/non-null JSON, DTO types, realtime filtering, finite/ranged coordinates, known rail line alignment and deterministic train-ID deduplication in both implementations.

**Rationale**: Investigation proves keyless rejection, not authenticated schema/timezone. Current RailArrivalDto uses strings; current parsing assumes timezone-free EVENT_TIME is UTC and lacks range checks. Fixtures must prove supported types; later authenticated sample confirms actual types and source timezone. Unverified/unparseable/ambiguous event time supplies no movement timestamp; do not substitute server-local time. Valid positioned receipt-time activity can remain separately eligible. Valid empty/schedule-only differs from realtime rows rejected for unusable positions: the latter cannot establish complete positioned zero.

**Alternatives considered**: Assuming UTC/local semantics or treating rejected positions/parse failures as empty manufactures evidence. A new DTO/schema or permissive coercion is unnecessary unless verified source data requires it.

## R3. Inventory independent of reported vehicles

**Decision**: Add separate bounded SourceEvidence to CityFetchResult, reusing stable configured marta-bus/marta-rail keys. Resolve expected source definitions from CityConfig/RailRealtimeOptions independently of optional reported-history runtime/catalog. Keep combined fetch outcome/counters and ReportedVehicleObservations for their existing consumers.

**Rationale**: MartaCity currently emits vehicle-count source observations only under ReportedVehicleCaptureEnabled; ReportedVehicleCaptureConfiguration creates an empty catalog when disabled. Statistics correctness must survive that switch.

**Alternatives considered**: Source identity from URLs, vehicle IDs, present records or another feature's empty catalog fails on outages/empty/disabled feeds.

## R4. Route dependencies and conservative localization

**Decision**: Explicit Atlanta adapter contract maps validated static bus routes to marta-bus and BLUE/GREEN/RED/GOLD to marta-rail through RouteHourCatalog aliases/RouteShapeProperties.JoinKey. Freeze historical dependency membership per hour; source/config changes retain necessary expected union and mark affected limitations. Unknown/ambiguous membership prevents Complete. Overlaps require explicit dependencies and existing identity deduplication; no complete substitute without declared equivalence.

**Rationale**: Category is not universal source identity. Other adapters remain on legacy capture until they supply explicit provenance, preserving healthy existing-hour behavior. Corruption survives only where unaffected measures are provable; unlocalizable loss excludes the cohort.

**Alternatives considered**: Applying Atlanta category mapping globally, deleting disabled dependencies, guessing affected routes or using today's catalog as past membership is unsafe.

## R5. Per-source continuity and independent measures

**Decision**: Replace city-wide activity/publication booleans at capture with route/source validity decisions. Empty success increments valid sample denominator with zero numerator; failure does neither. Track expected source successful/empty/failed/disabled counters, first/last successful receipts, boundaries and maximum gaps independently. Publication-only failure affects publication and completeness, not valid movement/activity. Feed the same decisions to category capture without changing its reconciliation.

**Rationale**: Worker currently gates activity on combined Success/Empty; RouteHourCycleSnapshot and accumulator spread common reasons/boundaries. Some movement survives despite discarded activity. CityState movement already checks timestamp watermarks, identity/fingerprint/transfer and 2 km jumps but does not explicitly break source outages. Reset relevant baselines on failure/disable/excessive gap; recovery reseeds. Preserve stale/nonpositive/teleport/transfer protections.

**Alternatives considered**: Relaxing only the Worker boolean or only SQL filtering leaves unsafe boundaries, publications and history unresolved.

## R6. Versioned storage and retry equality

**Decision**: Extend existing route-hour envelope/table with observed-city-route-hour-statistics-v2, nullable versioned JSONB and scalar measure validity/reasons/support fields. Legacy values remain null. Require v2 canonical evidence/scalar agreement; inventory bound 32, 64-byte source keys, checked nonnegative bigint counters, sorted unique route-relevant keys/reasons. No per-poll records or vehicle lists.

**Rationale**: Worker finalized row and Data entity validate separately; writer mapping and store Copy/SamePayload enumerate fields. Every new field must cross these seams, EF constraints/migration and immutable retry tests. Compare normalized typed JSON, not raw property ordering. Keep existing advisory lock identity across versions, atomic city/hour transaction, sticky conflict quarantine and bounded queues/retries.

**Alternatives considered**: Parallel store/per-poll journal exceeds scope. Defaulting legacy validity true or weakening v1 Complete admits unsafe history.

## R7. Admission, sample alignment and formula compatibility

**Decision**: Keep bounded PostgreSQL aggregation, separate cohort identity/integrity from per-row/per-measure eligibility. Conflicts/mixed runs stay excluded; validate cadence/version/catalog/loss before contributions. v1 Complete retains every existing check; v1 Partial never becomes eligible. v2 Partial may contain valid distance/activity. NoData retains membership but is not available data. ExpectedRouteHours is null without proven historical membership.

**Rationale**: Existing SQL requires all rows Complete/v1/equal sample counts. Its city denominator is max(route valid sample count), which cannot prove v2 alignment. Capture a constant-size streaming support fingerprint of accepted cycle identities with count, scoped to capture run/hour; equal counts alone never prove alignment. v2 aggregate activity uses summed valid numerators / summed valid denominators in a coherent, capture-proven aligned population. Missing alignment gives null unaligned_activity_samples; route measures/distance survive.

**Compatibility decision**: Combine only unchanged formulas, units and cadence, per measure. Distance, route/hour population sums and unchanged route ratios can mix admitted v1/v2 hours with a disclosed definition transition. Prior city activity sum/max differs from v2 sum/sum; never label them equivalent or mix them as one aggregate ratio. A v2 derived aggregate without compatible support/formula evidence is null with unsupported_definition or unaligned_activity_samples; independently valid distance and route bubbles remain. Absent/v1 HTTP retains original conservative formulas/semantics. No history rewrite adds missing support.

**Alternatives considered**: Average-of-means, shared max/min denominator, category-hour addition into city hours, scaled partial distance and missing-source zero violate the design.

## R8. HTTP negotiation and cache variants

**Decision**: Header X-TransitJazz-Stats-Contract accepts exact stats-overlay-v1/stats-overlay-v2; absent means v1. Empty/multiple/unsupported values return HTTP 400 ProblemDetails code unsupported_stats_contract. Pass normalized contract before cache/query/projection. Include it in full cache identity, retained selection slot, in-flight coalescing and client snapshot identity; emit Vary alongside existing no-store. The new server may serve validated v2 Complete cohorts to v1 only when all original eligibility predicates, common sample support and v1 formulas are proven equivalent; Partial measures never enter v1 responses. Otherwise exclude conservatively. This is response projection, never stored-history relabeling.

**Rationale**: StatsOverlayService already has a hardcoded contract in CacheIdentity, but CacheScopeKey contains only selection. Parameterizing only the full key creates shared-slot eviction and later reuse risk. Existing global byte/entry/read/concurrency limits remain across both variants. CORS already allows request headers.

**Alternatives considered**: Implicit client detection, changed body request shape or switching old semantics silently breaks old acceptance.

## R9. Existing client seams

**Decision**: Add per-request headers through StatsEndpointsService/HttpService rather than shared DefaultRequestHeaders; retain response disposal/cancellation/Retry-After. Extend strict contract/metric validation, source/measure validation and recursive Freeze in StatsSnapshotPolicy. It already accepts numeric Partial coverage; keep one store and preserve generation/current-selection/stale-result checks. Add unsupported-contract safe code to transport/store/localized request policy.

**Rationale**: StatsOverlayProjection currently selects PreviousDay whenever attached for nonhistorical city views. Use one fallback decision shared with server: current usable Partial including zero wins. Category coverage currently borrows whole-window coverage; add category-specific metadata. Existing ChartFrame supports visible CoverageDescription and accessible tables. PieChartModel retains null rows; BubbleChartModel retains omitted table rows while plotting valid points; SeriesChartConfig already uses SpanGaps=false. Extend those mechanisms rather than replace charts.

**Alternatives considered**: New stores, global warnings or changing reported-vehicle city-wide semantics are outside scope. One invalid ratio cannot hide unrelated charts. Neutral RouteFilterResources exists; add affected Spanish keys in the same resource family, without claiming unrelated language-control gaps fixed.

## R10. Testing, skill sync and release evidence

**Decision**: Existing xUnit units/components, HTTP boundary tests, real disposable PostgreSQL integration and a narrow closed-hour cross-layer fixture. Detailed testing enrichment follows separately. Browser source outages use controlled fixture/stub, never production disabling. Update canonical skills/mj-gtfs/SKILL.md during implementation, run tools/sync-skills.ps1 -Mode Sync, review changes and run -Mode Check for managed .claude/.agents/.opencode copies.

**Rationale**: Catalog targets all three hosts. RouteHourStatisticsDatabaseFixture guards an explicitly disposable connection and creates a separate database; skipped tests are not database passes. Existing November 7 production evidence is historical, not authenticated recovery proof.

**Alternatives considered**: Live-key-dependent core tests, hand-editing generated skill copies, or treating local URL parity as production recovery are rejected.

## Remaining verification dependencies

No unresolved technical clarification blocks planning. Later gates verify authenticated MARTA schema/value types/event-time timezone and serving revision overrides using an existing managed secret without disclosure; execute real disposable PostgreSQL and both controlled browser views; record exact commands/results/environment/revision/UTC windows. These are unrun verification dependencies, not authorization to deploy, change secrets or rewrite history.

