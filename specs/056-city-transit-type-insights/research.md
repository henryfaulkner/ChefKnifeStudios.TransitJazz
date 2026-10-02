# Research: City and Transit Type Insights

**Date**: 2026-10-01  
**Feature**: [spec.md](spec.md)  
**Source design**: [design document](../../docs/CITY_TRANSIT_TYPE_INSIGHTS_DESIGN_DOCUMENT.md)

## 1. Capture source and dependency boundary

**Decision**: Capture directly within the existing worker city cycle. Worker owns the transient accumulators, movement baseline, immutable aggregate records, and a synchronous aggregate-sink interface. The deployed WebAPI supplies the bounded channel/writer adapter and Data store. Worker retains its existing Shared-only project dependency.

**Rationale**: `Worker.cs` already has the joined route category, snapped cumulative meters, crossing records, and publish result. `city_minute_statistics` and its Grafana-backed collector cannot reconstruct category history or exact crossing totals. WebAPI already co-hosts the worker and registers `IDbContextFactory<AppDbContext>` when a database connection exists (`WebAPI/Program.cs:172-182`).

**Alternatives considered**: A Grafana category backfill lacks source history; a new event bus/service adds deployment and transport; a Worker-to-Data reference unnecessarily moves persistence into the live process boundary. No public endpoint or UI is introduced.

## 2. Activity and movement eligibility

**Decision**: Validate finite/in-range positions and count distinct joined identities before snapping. Capture movement after snapping, before live state replacement, using a dedicated insights baseline containing canonical route key, along-route meters, strictly monotonic per-vehicle source timestamp, and route geometry generation. Reuse the already computed snap and cumulative-distance array; do not call the snapper again.

**Rationale**: `Worker.cs:558-584` separates join from snapping; existing `VehiclesProcessed` counts snapped records and is not a distinct activity measure. `Worker.cs:658-679` provides the state replacement and distance insertion points. Crossing baselines suppress pivots and stationary observations, so they are unsuitable movement denominators.

**Alternatives considered**: Reusing crossing suppressions would reject valid zero intervals and turnarounds. Modifying live stale detection would change the message/metric contract. The separate transient baseline leaves those semantics intact and is pruned with vehicle state; geometry refresh reseeds it.

## 3. Freshness without manufactured movement

**Decision**: V1 requires known, strictly increasing `entity.Vehicle.Timestamp` for movement. Equal/older timestamps and unknown freshness reject intervals. Unknown freshness invalidates the insights baseline so the next known fresh position seeds a new interval. Never substitute poll time, a merged city timestamp, or changing interpolated coordinates for source freshness. Rejections are counted; eligible activity still contributes.

**Rationale**: Current live stale logic checks only equality (`Worker.cs:596-598`). `CityFetchResult.Combine` removes headers and retains the newest city timestamp, which cannot prove freshness for every merged source. MARTA rail parses `EventTime` into the entity timestamp but has no reliable fallback on parse failure. NYC train entities retain upstream timestamps even when synthesized coordinates change. Require source timestamp semantics to be checked during the pilot; otherwise use conservative exclusions.

**Alternatives considered**: Source-header fallback needs entity/source provenance that is not currently preserved. Add it only in a separate extension if exclusion evidence justifies it. Position hashes prove change but not freshness. This release adds no feed-provenance model.

## 4. Publication evidence and failed cycles

**Decision**: Reuse the existing `Task<bool> PublishBatchAsync` contract. Count all actually prepared crossing records by resolved route category only after `true`; no crossings is known zero on valid processing. Preserve attempted/failed/unknown publication evidence in the capture cycle even if reconciliation throws.

**Rationale**: `SignalRHubPublisher.cs:84-99` returns false when disconnected and true after awaited invocation. Invocation exceptions propagate, while `Worker.cs:786-793` currently replaces its result with an unhealthy result. Capture must retain its own outcome. Publication proves opportunities, not listener playback.

**Alternatives considered**: Counting `TonesEmitted` counts prepared records even on failed publication. Client settings and playback counts answer different questions.

**Coverage decision**: `PartialFailure`, missing/empty city route index, processing exceptions, and failed/unknown publication prevent complete city/category coverage. A successful empty feed with a ready route index supplies eligible zero samples. On a mixed-source failure, retain useful observed numerators for diagnosis but do not count the cycle as an eligible activity sample.

## 5. Windows, boundary evidence, and unknown categories

**Decision**: Sample one UTC observation timestamp per completed city cycle, before submitting its observations. Attribute movement and successful crossings to that cycle's window. Use an injected `TimeProvider`, per-city serialized accumulator, and a lightweight boundary sweep that performs no I/O. Seal minute rows only after successor evidence proves the gap across the boundary, or after the healthy-gap deadline expires. Startup/shutdown fragments are partial.

**Rationale**: Existing timers do not finalize historical windows. Immediate sealing at the exact boundary cannot prove subsequent observation continuity. Sweep/finalization and city commits share a short in-memory lock; no lock encloses feed, publish, or database operations.

**Policy**: Start with `MaxObservationGapSeconds = 30` for the ten-second worker interval, overridable per enabled city after measuring healthy spacing. Validate `CycleIntervalSeconds < limit <= 60`, persist the actual limit on each aggregate, and require calibration before enablement. Coverage includes boundary intervals, failed cycles, and known capture loss; clock reversal makes the affected window partial.

**Unknown decision**: An eligible actual `unknown` observation activates its bucket for that UTC hour; subsequent healthy zero samples may be emitted for that observed hour. Do not synthesize earlier unknown minutes or cycles. Its activation minute is Partial when earlier city cycles in that minute were omitted, including activation during minute zero. The hour can be complete only when all its cycle samples and boundaries are represented. Reset activation at the next hour; hours with no actual unknown observations have no unknown bucket. This conservative behavior avoids replaying old coverage.

**Alternatives considered**: Keeping/replaying 60 minute snapshots could establish preceding known zeros but adds buffering and retrospective emission. Choosing incomplete coverage is adequate for the data-quality category.

## 6. Durable aggregates, retries, and conflict quarantine

**Decision**: Add exactly the two proposed aggregate tables with composite city/category/time keys. Use UTC `DateTime`, fixed-precision decimal meters, and integer counts. Canonicalize distance/gap precision to six decimal places and observation timestamps to PostgreSQL microsecond precision before freezing records.

**Rationale**: Whole-hour distinct vehicles cannot be summed from minutes. Npgsql requires UTC-compatible inputs for `timestamptz`; it stores an instant, not the named city zone. See [Npgsql date/time mapping](https://www.npgsql.org/doc/types/datetime.html). City/category-first composite keys match the initial query shape.

**Write decision**: Use parameterized insert-on-conflict plus locked comparison in one transaction. Exact duplicate payloads remain unchanged. Preserve established values on disagreement and set a monotone `has_conflict` flag; conflicting minutes also quarantine an existing parent hour. Identical retries cannot clear quarantine. No automatic fill/merge/replacement of finalized values.

**Durable-hour decision**: Persist a candidate `Complete` hour only after all 60 durable minute keys are complete, unconflicted, definition-compatible, and additive totals match. Write any bundled final minute before verifying the hour. Missing backing minutes after bounded retry cause the complete candidate to be omitted, with a safe summary. Do not rewrite its immutable payload into a different status. Queries repeat backing-minute verification to catch later conflicts.

**Alternatives considered**: Log-only conflicts leave falsely authoritative rows. The old collector's nullable-field improvement policy is incompatible with immutable exact counts. A new reconciliation table is unnecessary: the monotone marker and existing safe logs suffice. [PostgreSQL INSERT](https://www.postgresql.org/docs/17/sql-insert.html) documents unique-key conflict handling; the subsequent comparison must account for concurrent committed rows, using a separate statement and row lock rather than a single-statement snapshot assumption.

## 7. Bound storage work and preserve live cadence

**Decision**: Use `Channel.CreateBounded` with capacity 256 aggregate envelopes, `FullMode.Wait`, `SingleReader=true`, `SingleWriter=false`, and `AllowSynchronousContinuations=false`. The producer calls only `TryWrite`; it never awaits a channel write. One envelope contains only one city's finalized minute/hour aggregate rows.

**Rationale**: A full channel immediately returns false from `TryWrite` in Wait mode, making loss observable. Silent drop modes can report a successful write despite losing the submitted item. See [Microsoft Channels documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).

**Policy**: The writer uses a fresh context/transaction per bounded batch, at most 128 rows and three transient retry attempts with one- and two-second delays, five-second command timeout, and a bounded 15-second shutdown drain. Exhausted writes produce missing keys; queries detect them. Enqueue loss marks an affected live hour partial when still open, and durable verification protects already frozen candidates. No vehicle identifiers enter envelopes.

**Alternatives considered**: Awaiting storage/channel space changes live cadence. Durable queues and per-cycle transactional records provide stronger recovery but are explicitly deferred.

## 8. Query surface, time zones, and scale

**Decision**: Deliver documented read-only parameterized SQL for hourly results, weighted periods, minute coverage, and typical local hours. Bind version, city, category, UTC range, and IANA zone. Withhold definitive hourly measures unless durable verification passes. Expose raw diagnostic totals separately for partial observations; never extrapolate.

**Time-zone configuration**: Add `Cities[].TimeZoneId` and validate enabled cities: `America/New_York` for Atlanta, Washington DC, Boston, New York City, and Philadelphia; `America/Toronto` for Toronto; `America/Denver` for Denver. Named zones are absent from the current server configs. Keep UTC identities, local date, and UTC offset when listing individual hours. Typical-hour grouping pools complete UTC observations only. See [PostgreSQL date/time functions](https://www.postgresql.org/docs/17/functions-datetime.html) and [series generation](https://www.postgresql.org/docs/17/functions-srf.html).

**Scale**: Seven configured cities. Actual category cardinality comes from the loaded catalog, not a hardcoded bus/rail list. A planning allowance of four categories per city gives 40,320 minute rows and 672 hour rows per day; a 30-day window has 1,209,600 minute rows. Measure actual row size and query plans during rollout. V1 adds no automatic retention deletion, partitioning, or category metrics series.

## Research resolution

All design choices are resolved. Implementation validation must still prove source timestamp interpretation, healthy-cadence calibration, database migration/key semantics, bounded loss behavior, and pilot coverage/overhead. These are explicit release checks rather than unanswered feature decisions.
