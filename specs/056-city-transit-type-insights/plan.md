# Implementation Plan: City and Transit Type Insights

**Branch**: `056-city-transit-type-insights` | **Date**: 2026-10-01 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/056-city-transit-type-insights/spec.md` and [source design](../../docs/CITY_TRANSIT_TYPE_INSIGHTS_DESIGN_DOCUMENT.md)

## Summary

Capture new city/category observations within the existing transit worker, then persist finalized minute and hour aggregates through a bounded asynchronous writer in the deployed WebAPI host. Use the exact whole-hour distinct-vehicle population for the primary distance average, eligible cycle samples for activity, accepted movement intervals for the diagnostic average, and successful publication outcomes for crossing cadence.

Add two aggregate tables alongside the existing city-only table. Preserve the existing metrics, crossing logic, live messages, and Grafana collector contract. A complete hour is authoritative only when its 60 durable minute records reconcile; failures, loss, and conflicts are disclosed as incomplete coverage. Deliver documented internal read-only queries after the pilot demonstrates the coverage rules.

## Technical Context

**Language/Version**: C# / .NET 10.0; PostgreSQL SQL  
**Primary Dependencies**: Existing EF Core 10 / Npgsql provider, ASP.NET Core hosted services, `System.Threading.Channels`, `TimeProvider`, current route snap/cumulative-distance code and `Task<bool>` publisher; no new runtime package  
**Storage**: Existing TransitJazz PostgreSQL database; new `city_category_minute_statistics` and `city_category_hour_statistics` tables; existing `city_minute_statistics` remains separate  
**Testing**: Existing xUnit worker and WebAPI test projects; deterministic clock/feed/publisher fixtures; real disposable PostgreSQL tests for concurrency, key semantics, reconciliation, and read queries  
**Target Platform**: .NET 10 Linux server in the existing Azure Container App; standalone worker remains disabled for insights unless an aggregate sink is supplied  
**Project Type**: Server capture and persistence extension with operator/analyst SQL contracts; no public endpoint or client UI  
**Performance Goals**: At most 5% matched-load p95 cycle-duration increase; no live database/channel awaits; at least 99% complete minutes in a 24-hour healthy pilot after startup fragments  
**Constraints**: Aggregate-only persistence/queue; source-timestamp freshness; exact denominator definitions; bounded memory/retries; schema-first rollout; no category backfill from city samples; no changes to existing metric series or wire format  
**Scale/Scope**: Seven cities and dynamic catalog categories; planning allowance of four categories/city gives 40,320 minute and 672 hour rows/day. Hour sets are retained only for current/boundary-pending windows; movement state follows the existing 20-minute pruning horizon. Measure actual cardinality and growth.

## Constitution Check

### Initial gate — Pass

Evaluated against constitution v3.3.2 before research.

| Principle | Result | Response |
| --- | --- | --- |
| I. Decoupled cloud architecture | Pass | Reuse current deployed host and worker; no new deployment or network interface |
| II. No frontend secrets | Pass | Existing server-only database secret; no frontend configuration changes |
| III. Two-pass processing | Pass | Add observation hooks around current reconciliation without changing live passes, stale guards, or state updates |
| IV. OpenTelemetry observability | Pass | Preserve instruments and labels; emit bounded safe write/coverage summaries through current server logging |
| V. GitHub Actions CI/CD | Pass | Preserve migration image and manually gated schema-first deployment; add disabled-default/check validation |
| VI. GTFS ID mapping | Pass | Use city-scoped resolved `RouteJoinKey` for transient eligibility only; persist no route/vehicle identity |
| VII–XIII. Map/music/interaction/presentation | Pass | No client/map/tone assignment or presentation changes |
| Repository commit instruction | Pass | Skip every commit hook; leave generated artifacts for user review |

No constitution violations require an exception. The earlier feature's one-table constraint applies to its Grafana city-only contract; this feature explicitly authorizes two additional category tables to retain exact hourly uniqueness.

### Post-design gate — Pass with implementation release checks

The two-table design, Worker-to-sink boundary, immutable payloads, durable conflict quarantine, and SQL result contract preserve the above principles. No unresolved design choices remain. Actual schema application, source timestamp interpretation, cadence calibration, capture overhead, and 24-hour pilot evidence must pass before general capture/query availability. These checks are not claimed complete by this planning run.

## Project Structure

### Documentation (this feature)

```text
specs/056-city-transit-type-insights/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/requirements.md
└── contracts/
    ├── observed-category-statistics-v1.md
    └── hourly-insights.sql
```

`tasks.md` is generated later by `/speckit-tasks`.

### Source Code (proposed changes)

```text
src/
├── Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
│   ├── Worker.cs
│   ├── Program.cs
│   ├── Cities/CityConfig.cs
│   └── Statistics/
│       ├── CityCategoryInsightsOptions.cs
│       ├── CityCategoryStatisticsCapture.cs
│       ├── CategoryStatisticsAccumulator.cs
│       └── FinalizedCategoryStatisticsBatch.cs   # DTOs + aggregate sink interface
├── Server/ChefKnifeStudios.TransitJazz.Server.Data/
│   ├── AppDbContext.cs
│   ├── Models/
│   │   ├── CategoryCollectionStatus.cs
│   │   ├── CityCategoryMinuteStatistic.cs
│   │   └── CityCategoryHourStatistic.cs
│   ├── Configurations/
│   │   ├── CityCategoryMinuteStatisticConfiguration.cs
│   │   └── CityCategoryHourStatisticConfiguration.cs
│   ├── Statistics/CityCategoryStatisticsStore.cs
│   └── Migrations/                              # new schema-only migration
├── Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
│   ├── Program.cs
│   ├── appsettings.json
│   └── Statistics/CategoryStatisticsWriter.cs   # channel, adapter, reports
├── Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
│   └── Statistics/                              # eligibility, windows, outcomes
└── Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/
    └── Statistics/                              # mappings, writer, PostgreSQL, SQL

bicep/modules/containerApp.bicep                 # independent nonsecret capture settings
bicep/main.bicep
bicep/main.json                                 # regenerate from Bicep when changed
.github/workflows/server.yml                    # disabled-default/validation checks
```

**Structure Decision**: Worker holds capture state and aggregate DTOs and keeps its current Shared-only project reference. WebAPI already references Worker and Data, so its writer maps immutable capture DTOs into the Data-owned entities. Data owns the focused persistence store. The documented SQL is the initial analyst interface; a REST endpoint is not required.

## Design Details

### Cycle capture and eligibility

1. When enabled for a city, start a transient category cycle accumulator using the current city route catalog. Count unique finite/in-range positioned identities after successful route join and before snapping, once per vehicle/city/cycle across all categories. The first eligible joined entity in feed order supplies its representative category and movement observation; this insights-only selection does not alter live entity/crossing processing. Keep these identities only in process.
2. Reuse the current snap/cumulative meters. Before replacing live state, evaluate a dedicated insights movement baseline using the resolved route key, shape generation, strictly increasing per-vehicle timestamp, finite geometry, and the 2,000-meter guard. Round accepted absolute deltas once to six decimal places; zero delta remains an accepted interval.
3. First sightings, repeated/older/missing timestamps, transfers, geometry changes, and invalid/teleport values have bounded rejection reasons. Unknown freshness/invalid geometry clears the insights baseline; a fresh transfer/teleport reseeds it. Repeated/older observations never move the watermark backward. Keep existing live baseline and crossing behavior untouched.
4. Count all actual crossing records by resolved category; these counts reconcile to the successful city batch, including records that the activity deduplicator does not affect. Credit them only after the existing publisher returns true. Preserve exception/unknown outcome in capture evidence; do not infer success from prepared tones.
5. Complete the capture observation at the outer city-cycle boundary, where fetch and processing outcomes are available, with one UTC completion timestamp. A successful empty feed and ready nonempty city route index create known-zero category samples. Partial source failure or entity processing exceptions prevent complete coverage. Valid activity may remain eligible on publication failure, while cadence is unavailable.
6. Guard instrumentation failures so they produce safe capture-loss evidence and do not change live-cycle results. Skip all capture work on disabled cities. Share a short in-memory accumulator lock with the boundary sweep; no feed, publication, storage, or logging I/O runs under it.

### Minute/hour finalization

- Use UTC start-inclusive/end-exclusive windows. An interval and its publication opportunities belong to their completing city cycle; source timestamps establish freshness rather than historical bucketing.
- A lightweight `TimeProvider`-driven sweep seals pending minutes only with successor timing evidence or expiration of the healthy-gap deadline. The initial default is 30 seconds for the ten-second interval; validate a per-city limit greater than the interval and at most 60 seconds, calibrate it before enablement, and persist it with rows.
- Boundary gaps include predecessor and successor evidence. An absent start/end observation, failed/unknown cycle, clock regression, catalog/coverage-policy change, or known capture loss prevents complete coverage. Missing whole minutes are either finalized NoData with known timing evidence or absent after process loss; queries generate the expected grid.
- Maintain exact hour-scoped identity sets and additive counters. Retain at most the current and boundary-pending hour; close only after the last minute is sealed, then discard all vehicle identities. Restart and incomplete startup/shutdown hours are partial.
- Activate `unknown` only after actual eligible observation for that hour, then permit known zeros for its remaining healthy cycles. Do not reconstruct earlier unknown minutes or cycles. If earlier cycles in its activation minute were omitted, mark that minute and hour Partial, including activation within minute zero. Completeness requires all hour cycles and boundaries to be represented. Reset activation at the next hour; configured categories continue receiving valid zeros.
- A route catalog/geometry refresh invalidates movement baselines and makes affected open windows partial. Capture checks generation before and after a city observation to prevent mixed geometry from supplying definitive movement.
- Every finalized DTO is immutable. No cycle/entity collections or recyclable live event lists survive in its queue entry.

### Bounded writer and durable reconciliation

Use the writer/sink adapter in WebAPI, registered before Worker so capture stops before writer shutdown drain. Provide a no-op sink when disabled; validate that enabled capture has database registration independently of `HistoricalStatistics.Enabled`. Standalone Worker defaults to disabled and must reject enabled capture without a real sink.

Starting settings:

| Setting | Default / validation |
| --- | --- |
| `CityCategoryInsights.Enabled` | false |
| `EnabledCities` | empty while disabled; explicit nonempty configured subset when enabled |
| `MaxObservationGapSeconds` | 30; per-city overrides, each greater than cycle interval and <=60 |
| `QueueCapacity` | 256 one-city aggregate envelopes; positive bounded setting |
| `MaxBatchRows` | 128; positive and bounded, split larger envelopes in deterministic order |
| `CommandTimeoutSeconds` | 5; finite positive value |
| `MaxWriteAttempts` | 3 total transient attempts |
| Retry delays | one and two seconds for the default three-attempt budget |
| `ShutdownDrainSeconds` | 15; finite positive value |

Use `FullMode.Wait` with only synchronous `TryWrite` at the producer. Keep the writer's reader single and disable synchronous continuations. Failed admission or exhausted writes produce safe loss summaries and incomplete/missing evidence. Retries use the same frozen rows, not rebuilt counters.

The store creates a fresh context and transaction per bounded batch, handles keys in deterministic minute-then-hour order, uses parameterized unique-key insert protection, and locks existing rows before immutable comparison. Exact repeated payloads are unchanged. A different payload sets a monotone `has_conflict` flag without changing counters; a minute conflict quarantines an existing parent hour. The older source collector's partial-field improvement store is not reused.

A Complete hourly candidate is inserted only after its 60 durable complete/nonconflicting minute keys have compatible versions/policies and matching additive sums. Insert bundled final minutes first and lock backing rows before accepting the hour. If the writer's bounded budget cannot establish these facts, omit the candidate and report the reason. Existing immutable Partial/NoData candidates remain diagnostic. Queries recheck durable evidence in one snapshot to exclude later quarantine.

### Query and time-zone contract

The initial SQL surface is defined in [observed-category-statistics-v1.md](contracts/observed-category-statistics-v1.md) and [hourly-insights.sql](contracts/hourly-insights.sql). It returns verified complete hours; accompanying coverage queries generate requested keys and report missing/partial/conflicting windows. Do not filter away incomplete coverage without reporting it.

Period summaries sum numerators and their proper denominators. Group compatible outputs by definition version and cadence policy. Typical local-hour cadence divides summed crossings by complete contributing UTC hours, retaining dates, offsets, and UTC identity for individual results.

Add `Cities[].TimeZoneId` to enabled server city configuration: America/New_York for the five Eastern US cities, America/Toronto for Toronto, and America/Denver for Denver. Validate named zones with `TimeZoneInfo` and database conversion support. Time zones are applied only at query grouping, not to keys.

Only fully contained UTC hours supply definitive hourly measures. Subminute request boundaries expose whole-minute context with exact bounds; never prorate aggregated samples. The earliest retained category observation discloses the start of retained category capture.

### Schema and deployment

Add the two explicit EF entities/keys/checks and one schema-only migration. Preserve the current table/migration and existing database secret. The existing Data Dockerfile builds the EF bundle; no capture or feed query runs during migration.

Extend server settings/Bicep with independent capture enablement, enabled cities, and policy settings; regenerate `main.json` if Bicep changes. Keep capture disabled in committed defaults and deployment defaults. Existing Grafana collection remains independently configured. Do not introduce category metrics series in v1; safe logs and database coverage provide operational evidence.

Deploy schema, then disabled capture code, then enable one explicitly selected city after timestamp/cadence preflight. Expand only after reconciliation and the 24-hour pilot pass. Preserve existing single-replica deployment; conflicts still protect revision overlap. Current history is future-only and no automatic retention deletion is added.

### Validation strategy for implementation

- Worker tests: position validity, route join/category normalization, duplicate identity, stationary intervals, reverse/pivot movement, 2,000-meter boundary, older/equal/missing timestamps, source interpolation repeats, route/geometry changes, empty feed, mixed-source failure, publication false/exception/zero, and successful crossing reconciliation.
- Clock-driven accumulator tests: exact boundaries, predecessor/successor gaps, cadence expiration, startup/restart/shutdown, catalog changes, mid-hour unknown activation, hourly uniqueness across cycles and category transfers, lost population, and minute/hour additive equality.
- Writer tests: disabled registration, nonblocking full channel, bounded attempts/timeouts, independent database registration, shutdown drain, and secret/identity-free reports.
- Real disposable PostgreSQL tests: composite keys, UTC/numeric canonicalization, concurrent identical/differing writers, monotone quarantine, late minute conflict, missing minute/hour candidate, all additive comparisons, snapshot-consistent hourly selection, weighted periods, and DST grouping. Require explicit disposable database provisioning; never call `EnsureDeleted` on a user database.
- Update the existing model-wide one-entity assertion to expect exactly the three statistics entities, while retaining all old model/source-contract assertions. Inspect the new migration and EF bundle for schema-only changes.
- Measure matched-load capture disabled/enabled p95 cycle duration and perform the one-city 24-hour coverage observation. These acceptance checks are planned, not executed during specification/planning.
- During query handoff, ask five representative analysts to interpret the returned definitions; require at least four to distinguish vehicle-hour from trip distance and publication opportunities from audible notes.

## Implementation Sequence

1. Freeze the contract and defaults; record reference examples, supported city zones, and pilot timestamp/cadence evidence.
2. Add two Data entities, explicit mappings, keys/checks, focused immutable store, and schema-only migration; validate concurrent persistence on a disposable database.
3. Implement pure movement eligibility and minute/hour accumulators with deterministic clock fixtures and exact distinct populations.
4. Add worker capture hooks at join/snap/publication/cycle boundaries, failure paths, route refresh, and pruning; preserve old metric/message behavior.
5. Implement the bounded writer/sink adapter and independent host gating; validate overflow, retry loss, shutdown, and durable Complete-hour acceptance.
6. Complete read-only SQL/coverage/period/local-hour recipes against the deployed model and prove exact examples, conflicts, partial boundaries, and DST.
7. Wire nonsecret configuration/Bicep/default checks; build existing server and migration artifacts and prepare schema-first deployment evidence.
8. Apply and verify schema through the existing operator process; deploy disabled capture, enable one city, and run comparison/24-hour coverage/overhead gates.
9. Expand to configured cities and publish the internal query runbook only after pilot evidence passes; record first retained category dates.

## Complexity Tracking

No constitution violations. The separate hour table is required by the exact distinct denominator. The bounded in-process channel and monotone conflict flag meet explicit cadence and trust requirements; no additional service, queue product, identifier table, or generalized event pipeline is needed.
