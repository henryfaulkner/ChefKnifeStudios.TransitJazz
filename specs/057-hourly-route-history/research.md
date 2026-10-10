# Research: Hourly Route History

**Date**: 2026-10-10  
**Scope**: Resolve implementation decisions for [spec.md](spec.md) using [source design](../../docs/HOURLY_ROUTE_HISTORY_DESIGN_DOCUMENT.md), the existing implementation, and primary documentation. No production capture, migration, or deployment is performed by this research.

## 1. Collection ownership and configuration

**Decision**: Reuse `HistoricalStatistics.Enabled`, `DryRun`, and the host-resolved `Cities` exactly. Bind a limits-only Worker-owned `RouteHourHistoryOptions` from `HistoricalStatistics:RouteHours`; map selected cities and mode into a separate immutable runtime selection owned by Worker. No route-specific enable/city-exclusion flag.

**Rationale**: `WebAPI/Program.cs` resolves an empty city list to configured city names before validation/registration. `WebAPI/Statistics/HistoricalStatisticsOptions.cs` owns the existing settings and credentials. Worker references Shared only, while WebAPI references Worker and Data. Its current enabled-history validation requires a database connection even in dry run; preserve that host behavior, while dry-run route finalization never calls the route store. Existing source credentials/grace/overlap remain requirements of the existing collector and never bucket route observations.

**Alternatives considered**: Independent category-style enablement/exclusions (contradicts scope); making Worker reference WebAPI or Data (reverses dependencies); changing existing collector validation for dry run (unrelated behavior change).

## 2. Canonical catalog and unchanged refreshes

**Decision**: Build a separate immutable canonical route cohort while `Worker.BuildRouteIndex` consumes shapes, using `RouteShapeProperties.JoinKey`. Carry it with the same `RouteCatalogSnapshot` used for processing. Preserve ordinal route identities; raw-ID index aliases are not cohort entries. Store contributing static IDs and fingerprint the actual final ordered geometry used for each resolved route.

**Rationale**: `_routeIndex.Keys` includes aliases. `Worker.ApplyRouteIndex` currently increments geometry generation and invalidates category capture on every refresh, including unchanged content. Route capture therefore needs content comparisons and per-route content tokens; do not change category's existing generation behavior. Baselines and Complete coverage survive byte-identical route refreshes. Recheck captured content after fetch/processing to detect a changed catalog during an observation.

**Encoding decision**: Version the fingerprint encoding `route-catalog-v1`: length-prefixed UTF-8 strings; ordinal-sorted distinct contributing IDs; key and category; ordered actual route points encoded using invariant, round-trip finite coordinate values. SHA-256 lower-case hex is the persisted 64-character fingerprint. Define one exact fixture before capture integration. A cohort signature hashes sorted keys/fingerprints. Display labels remain metadata; aliases/unused shapes must not accidentally change actual-geometry selection.

**Case collision decision**: Current cumulative-distance/trigger maps use case-insensitive comparison while route indexes are ordinal. Preflight and capture validation must detect ambiguous auxiliary geometry for case-distinct canonical keys rather than merge/truncate identities or change live routing. Capture that cannot establish unambiguous geometry reports `route_index_unavailable` and cannot be Complete. Test ordinary case-distinct keys and actual collisions separately.

**Alternatives considered**: Raw feed/static ID grouping (duplicates aliases); generation-only coverage (invalidates identical refreshes); a durable route dimension (outside v1); changing live/category comparers as part of instrumentation (unnecessary regression risk).

## 3. Capture and movement semantics

**Decision**: Implement focused route capture, cycle counters, and an hourly accumulator. Mirror category movement rules and reuse its fixtures without replacing category capture with a generic framework. Count activity before snap rejection after valid positioned route join, deduplicated by first eligible entity per vehicle/city/cycle. Record processing at the existing stale/stationary/unchanged/moved branches; record suppression where it is assigned and crossings from actual prepared records.

**Rationale**: `Worker.ProcessSpatialReconciliationAsync` contains source time, joined route, snap, suppression, and actual crossing lists. `ExecuteAsync` has final source/processing/publication/catalog outcomes and provides the one commit timestamp. `CityCategoryStatisticsCapture` already establishes freshness, transfer/geometry reset, 2,000-meter guards, and exact populations. A missing source time rejects movement but does not alone make positioned activity unavailable. Actual crossing/processing totals are not deduplicated like activity. Successful publication is credited only after publisher returns true, including diagnostic publication when another outcome makes the hour partial.

**Alternatives considered**: Rereading feeds/Grafana (unnecessary and inconsistent with worker counts); inferring crossings from movement (changes live semantics); persisting identities/events (privacy and scope violation).

## 4. Coverage and lifecycle

**Decision**: Retain at most current and boundary-pending city/hour state, exact per-route population sets, and bounded movement state using the current 20-minute pruning horizon. Prove continuity with healthy predecessor/successor observations and bounded gaps. Sweep independently using `TimeProvider` at <=15-second intervals; flush once at shutdown. Freeze immutable rows and release identity sets before retention.

**Rationale**: No route-minute records exist, so `covered_minutes=60` or category durable-minute reconciliation would be a false claim. A successor proves only the preceding boundary; its counts belong to its completion hour. An expired hour cannot reopen during an in-flight cycle. Capture/commit/sweep use one short lock; fetch/publication/storage/admission/logging occur outside it. Catalog changes retain the old/new cohort union with first metadata and invalidate the whole city/hour. Startup/shutdown, cap loss, or clock regression cannot establish Complete history.

**Alternatives considered**: Durable checkpoints/per-cycle journal or merging restarts (outside v1 and cannot recover exact populations); one accumulator per missed hour (unbounded); finalization only from worker progress (hung source blocks deadlines).

## 5. Atomic immutable retention and conflict quarantine

**Decision**: Add one route-hour table and a focused store. Use a fresh read-committed transaction per whole city/hour envelope. Acquire a transaction-scoped advisory lock before the first cohort lookup, then insert all rows or compare the full existing route set and payload. A difference quarantines every existing row without adding/replacing rows. Equality includes capture run, definition, cadence, metadata, counters, and coverage; exclude only generated persistence time and monotone conflict.

**Rationale**: `CategoryStatisticsWriter` splits envelopes and commits slices; category's store additionally demands durable minutes. Neither contract fits route-hour atomicity. Advisory locking protects even empty-key races and differing cohorts. PostgreSQL read committed gives the post-lock lookup a new statement snapshot, so a waiting writer can see the preceding committed cohort. The stored atomic row set is the historical cohort and avoids a manifest table. All future table writes/maintenance must honor the protocol. Hash the namespaced city/hour key deterministically; a lock-hash collision serializes unrelated hours without changing identity.

**Alternatives considered**: Per-row upserts (partial cohorts/fragments); ordinary unique-key checks alone (different-key-set races); category field-improvement store (overwrites immutable evidence); a manifest table or ownership service (unnecessary in v1).

**Primary sources**: [PostgreSQL advisory locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS), [read-committed statement snapshots](https://www.postgresql.org/docs/current/transaction-iso.html), [Npgsql transactions and parameters](https://www.npgsql.org/doc/basic-usage.html).

## 6. Admission, budgets, shutdown, and failure reporting

**Decision**: A dedicated WebAPI writer owns a bounded channel of frozen city/hour envelopes, `FullMode.Wait`, synchronous producer `TryWrite`, one reader, and disabled synchronous continuations. Defaults: capacity 16; 128 rows/command in the same transaction; five-second command timeout; 30-second whole attempt; three attempts with one/two-second delays; 15-second shutdown drain. Retry recognized transient failures only with the exact same envelope.

**Rationale**: `TryWrite` under Wait mode immediately returns false at capacity, and disabling synchronous continuations prevents producer execution of reader work. Admission does not promise retention. A lost commit acknowledgement is resolved through immutable retry. A linked attempt token covers opening, lock waits, all commands, and commit, rather than letting many five-second chunks create an unbounded attempt. Register writer before lifecycle and Worker so reverse shutdown stops capture before draining; verify host ordering and disable concurrent service stop if it undermines this dependency. Existing category lifecycle offers the sweep pattern, not the route persistence contract.

**Alternatives considered**: `WriteAsync` on the worker (blocks live processing); silent drop modes (obscure loss); independent slice commits (break atomicity); unbounded retries/drain (disrupts host).

**Primary source**: [Microsoft Channels documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).

## 7. Queries and time identity

**Decision**: Ship a read-only SQL contract with explicit expected UTC hour keys, coverage, hourly diagnostics, correctly weighted periods, and local-hour grouping. Complete nonconflicting fully contained hours alone supply definitive measures. Keep definition/cadence partitions, source bounds, fingerprints, denominators, and units visible. Execute related result sets under one read-only repeatable-read transaction when multiple statements are used.

**Rationale**: Explicit routes need Missing pairs even without any retained rows. All-route selection can discover only retained keys within the requested range; entirely missing city/hour membership remains unknown. Current catalog cannot reconstruct it. Summed per-route/hour populations are route-vehicle-hours, not city or period unique vehicles. Repeated DST hours keep UTC identity and offset and contribute separately; hourly data cannot be prorated.

**Alternatives considered**: A public endpoint/dashboard (outside release); average-of-averages; filtering failures away without coverage; reconstructing old routes from today's catalog; independently read coverage/metrics snapshots.

## 8. Verification and release scope

**Decision**: Use project-native xUnit fixtures and an independently provisioned disposable PostgreSQL fixture for route storage/query tests; never delete operator databases. Add focused eligibility/clock/atomicity/config tests, update the existing model-wide entity count from three to four, and preserve old source contract assertions. Extend schema-only migration CI narrowly for the new migration. Validate all selected cities' cardinality/time/cadence, matched-load overhead, and 24-hour route-hour coverage, then deliver analyst definitions.

**Rationale**: Existing `CategoryStatisticsDatabaseFixture` is opt-in with a disposable-name guard; the legacy city store test's destructive setup must not be copied. Existing migration Dockerfile builds the EF 10.0.4 Linux bundle and the server workflow has schema-first operator approval. Reuse `enableHistoricalStatistics`/`historicalStatisticsDryRun` and preserve disabled/dry-run committed defaults. The source design expressly does not require an independent pilot city or rollout switch.

**Alternatives considered**: New test framework/packages, data backfill, automatic retention job, live deployment during planning, or copying category pilot/exclusion policy (all unnecessary/out of scope).

## Resolution status

All design decisions are resolved for task generation. Actual feed timestamp behavior, resource/storage growth, database execution, overhead, and 24-hour coverage remain implementation validation work; they are not claimed completed here.

Planning verification: all 16 specification quality checks pass; local artifact links, requirement/outcome numbering, all 52 sequential task IDs, story labels and unchecked implementation status pass. A PostgreSQL parser accepts the query recipe as a single read-only SELECT. Isolated scratch-database validation was rejected by automatic approval review with "blocked by policy" and was not executed; schema binding, result correctness and all operational acceptance remain pending implementation checks.
