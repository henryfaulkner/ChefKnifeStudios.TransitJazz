# Research: Reported Vehicles

**Date**: 2026-10-13. Repository-grounded design; no database queried or deployment assumed. A bounded read-only source-research subagent inspected adapters/configuration; findings were reviewed before this consolidation.

## 1. Raw response extraction

**Decision**: Capture each decoded source response before any route/coordinate/synthesis guards, timestamped at that response boundary. Add immutable source observations to CityFetchResult while preserving existing map feed fields and behavior.

**Rationale/evidence**: GtfsRtCity.FetchVehiclesAsync fetches each URL then remaps/merges. MartaCity deserializes bus and rail independently; rail filters original records. NymtaCity.FetchAndSynthesizeSubwayAsync currently returns before fetching when EnsureTableAsync fails; SynthesizeEntity drops unmappable trains. Its geometry prerequisite must move after raw fetch/evidence. CityFetchResult.Combine currently preserves aggregate source counters but loses source identities/timing. Worker.CompleteCycle time occurs after processing/publication and is not a response receipt time.

**Alternatives considered**: Counting Worker.Feed misses NYC synthesis failures and rail filtering; merged outcome cannot prove each source's health. A city-cycle timestamp can assign earlier source responses to the wrong hour.

Relevant files under `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/`: `Cities/{GtfsRtCity,MartaCity,NymtaCity,CityConfig}.cs`, `Metrics/CityFetchResult.cs`, `Worker.cs`, `RailRealtime/{RailRealtimeAdapter,RailArrivalDto}.cs`. Both this project's `Program.cs` and WebAPI `Program.cs` construct NYC bus/subway settings separately; source configuration must reach both.

## 2. Identity domains and fallbacks

**Decision**: Default GTFS rule uses original VehicleDescriptor.Id on nondeleted vehicle records, ordinal and untrimmed after checking whitespace. MARTA original TRAIN_ID uses kind agency-train-id. Namespace denotes ID domain; overlapping NYC subway groups share one; separate bus/rail domains differ. Source keys are stable explicit configuration identifiers, not URLs or array indexes. Document fallback limitations; no generic FeedEntity.Id, trip ID, label or license plate fallback.

**Rationale/evidence**: Worker `GtfsRtModels.cs` exposes deletion and record kind. Worker and NYC synthesis use descriptor-or-entity fallback for map behavior, but this is not a verified stable identity contract. MARTA DTO supplies TRAIN_ID before filtering. Legacy RailRealtimeAdapter duplicates MARTA filters but has no current consumption/DI registration found; share the small rail identity extractor to prevent drift, retaining existing APIs.

| City | Proposed source keys | Proposed identity domains/rule |
| --- | --- | --- |
| atlanta | marta-bus, marta-rail | Separate domains; descriptor ID / original TRAIN_ID |
| washington-dc | wmata-bus, wmata-rail | Separate domains; descriptor ID |
| new-york-city | mta-subway-ace, -bdfm, -g, -jz, -nqrw, -l, -main, -si; mta-bus | All subway groups share mta-subway; bus mta-bus; descriptor ID only |
| boston | mbta-vehicles | mbta-vehicles; descriptor ID |
| toronto | ttc-vehicles | ttc-vehicles; descriptor ID |
| philadelphia | septa-vehicles | septa-vehicles; descriptor ID |
| denver | rtd-vehicles | rtd-vehicles; descriptor ID |

Names are recommendations pending inspection of actual configured enabled endpoints, not an assertion that each deployed feed exists or supplies permanent IDs. Disabled rail/empty URL arrays are not expected sources. Source certification records descriptor availability, domain overlap and explicit fallback contract before enabling a city; absence of proof means no fallback. This does not block implementing default descriptor counting. Mapped NYC train populations may therefore differ substantially from reported identifiable populations.

**Alternatives considered**: URL namespaces leak details and fail endpoint changes; source-per-domain overcounts overlap; trip/entity fallback can count journeys as vehicles; route-based identities double-count transfers.

## 3. What constitutes a successful raw response

**Decision**: HTTP success plus successful expected-body decoding is success even with zero raw vehicle records. Invalid/malformed or JSON null root is failure, not healthy empty. A valid empty list/feed is success. Count all original identifiable MARTA train records even if realtime=false: this feature measures records reported, not active trains. Deleted/nonvehicle GTFS records do not establish identity. Differential messages contribute actual nondeleted records received, without reconstructing prior unchanged population. Stale records still count as reported.

**Rationale**: The feature deliberately precedes map filters. Existing `Deserialize<List<RailArrivalDto>>() ?? new List` can misclassify null; evidence needs a stricter decode disposition without changing map result semantics. Current feed models contain deletion/differential data; remembering prior entities would inflate this observation definition.

**Alternatives considered**: Reusing synthesized entity counts conflates healthy raw feeds with geometry failures; adding schedule/realtime filters contradicts the requested broader population.

## 4. Exact history and integrity

**Decision**: Header plus memberships, atomic immutable city/hour batch, sticky conflict quarantine, separate definition and source/cadence policy. Compare canonical payload including complete memberships; hash may accelerate but never replace exact equality.

**Rationale/evidence**: `Server.Data/Statistics/CityRouteHourStatisticsStore.cs` already uses advisory lock plus read-committed refresh, immutable equality, whole-hour quarantine and bounded chunks. Existing category/route tables persist only distinct counts and cannot reconstruct unions. `WebAPI/Statistics/RouteHourStatisticsWriter.cs` supplies channel admission, retry/drain pattern; capture lifecycle supplies independent timer.

**Alternatives considered**: Sum hourly distincts overcounts; last-seen-only state cannot answer local-day and closed-hour windows after restart; an event ledger stores unnecessary volume. Merging restarted/concurrent partial sets cannot certify complete collection.

## 5. Per-source coverage

**Decision**: Constant-sized source timing summaries plus up to two city-hour populations. Complete requires successful continuity around both boundaries and no failed/missed source polls, missing identity records or evidence loss. Partial intact populations with success contribute qualified observations. NoData/loss/conflict excluded. Successor membership remains in successor hour. Stable run/source policy remains within cohort.

**Rationale**: Aggregate successful-source count cannot locate outages; a healthy source must not hide another's failure. Source receipt gap policy is independent of route cadence. Identity omissions limit population completeness while keeping identified population intact.

**Alternatives considered**: First/last city sightings, route eligibility and source header timestamps do not prove continuous raw capture. Retaining all polls is unnecessary for these windows.

## 6. Bounds and ownership

**Decision**: Item caps plus conservative byte reservation for extracted payloads, current/prior accumulators and queue; bounded store commands/retries and finite cleanup. Single capture owner/city required before enabling persistence. Contradictory retained cohorts quarantine; changed configuration excludes incompatible hours instead of mixing identity domains.

**Rationale**: 32 batches times 100,000 long identities can consume gigabytes; count-only bounds are insufficient. Observed fleets and allocation overhead must validate proposed bounds. Existing host registration order allows capture flush before writer admission closes. A shared explicit host guard reserves Persistence for WebAPI and rejects standalone Worker persistence before startup. Ownership among multiple WebAPI replicas remains operational; no distributed-election subsystem is added.

**Alternatives considered**: Unlimited sets/queue block or exhaust polling; silent truncation creates wrong counts; unioning replicas launders missing coverage. Separate new distributed ownership infrastructure is unnecessary for a default-disabled feature with explicit deployment prerequisite.

## 7. Snapshot/query/rollout

**Decision**: Add reported aggregates to existing single SQL statement, using its server-built hour grid independently of route eligibility. Select a separate disabled SQL variant with no new table references. Existing windows/cache/admission/deadline apply; add read setting/source fingerprint/policy/definition to cache identity.

**Rationale/evidence**: `Server.Data/Statistics/StatsOverlayQuery.cs` executes one parameterized scalar aggregate SELECT, which already provides a statement snapshot. `WebAPI/Statistics/StatsOverlayPolicy.cs` constructs windows; `StatsOverlayService.cs` owns cache/deadline/projection. A false SQL CASE still references absent tables at parse time.

**Alternatives considered**: A second browser read creates mismatched snapshots; application-loaded identity sets increase response/memory risk; joins through complete route cohorts discard valid raw observations. Catching missing-table errors after starting reported reads obscures rollout mistakes; use disabled variant before migration.

## 8. Presentation and acceptance

**Decision**: Existing Stats cards and neutral RouteFilterResources.resx, English integers, independent coverage and explicit city/All scope. Preserve current feature-058 native dialog, English-only and no-audio decisions. Test with independent arithmetic/coverage oracle, raw-filter fixtures and real PostgreSQL; benchmark combined existing budgets.

**Rationale/evidence**: `Client.Shared/Components/Stats/StatsOverlay.razor` already renders both distance cards; existing Shared DTOs explicitly preserve nulls. Existing feature-058 plan gives five-second deadline, 5% overhead and accepted UI boundaries.

**Alternatives considered**: New cards/navigation/request mechanisms broaden scope; reusing distance state mislabels raw evidence; visual zero for unavailable history invents data.

## Release prerequisites, separate from design blockers

No unresolved architectural decision prevents task generation. Release still requires actual source identity certification, migration in target database, configured single capture owner, retained-evidence audit, finite-limit memory/storage/query measurements, original matched performance budget and browser/visitor acceptance. Planning does not certify or perform these actions.
