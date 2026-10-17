# Implementation Plan: Reported Vehicles in Stats

**Branch**: `059-reported-vehicles` | **Date**: 2026-10-13 | **Spec**: [spec.md](spec.md)
**Input**: [reported vehicles design](../../docs/REPORTED_VEHICLES_DESIGN_DOCUMENT.md). Planning only; no implementation or deployment has occurred.

## Summary

Add two secondary metrics beneath existing distance values. Extract identifiable raw vehicles at each response boundary before mapping/synthesis, retain immutable city/hour memberships with independent source coverage, and count the distinct union over the existing completed-hour windows within the existing Stats read. Counts remain city-wide under route selection; All sums city populations. Partial, absent, successful-empty and pending evidence have distinct presentation.

## Technical Context

**Language/Version**: Existing C#/.NET 10, Razor/Blazor WASM, PostgreSQL SQL, CSS.
**Primary Dependencies**: Existing protobuf-net, HttpClient, TimeProvider, Channels, EF Core/Npgsql and xUnit. No new framework or runtime project.
**Storage**: Two new PostgreSQL header/membership tables, atomic immutable cohorts, seven-day retention, ordinal identity comparison; separate from route history.
**Testing**: Existing Worker, WebAPI, Shared and Client.Shared xUnit projects; opt-in disposable PostgreSQL; browser, visitor and matched-load acceptance. Tests explicitly requested by FR-021.
**Target Platform**: Existing Azure WebAPI/Worker host, PostgreSQL and Azure Static Web App WASM.
**Project Type**: Worker capture, relational retention, bounded aggregate reporting, existing Stats cards.
**Performance Goals**: Existing <=5-second whole Stats deadline, prepared read p95 <=250 ms and cold read p95 <=2 seconds under existing matched conditions; combined history-plus-Stats worker p95 overhead <=5% against recorded baseline. No additional allowance.
**Constraints**: Exact cross-hour deduplication, per-response receipt UTC, private identities, independent feed coverage, no storage waits in polling, finite byte/item bounds, single capture owner/city, no count-only backfill, default-disabled rollout.
**Scale/Scope**: Seven current cities; <=32 selected cities and sources/city. Initial proposals: <=100,000 distinct identities/city/hour, <=512 UTF-8 bytes/value, <=64-byte source/domain/kind keys, two open hours/city, queue <=32 batches, seven-day retention. Global byte admission also applies; see capture contract. Bounds require measured release validation.

## Constitution Check

Constitution v3.3.2 checked before research and after design. Design gates pass without exceptions. Runtime measurements remain release prerequisites.

| Principles | Initial / post-design | Response |
| --- | --- | --- |
| I-II architecture/secrets | Pass / Pass | Existing units; raw IDs and credentials never enter public DTOs. |
| III/VI pipeline/route identity | Pass / Pass | Capture precedes both processing passes; RouteJoinKey/music/map outputs retain semantics. Vehicle domain is separate. |
| IV-V observability/delivery | Pass / Pass | Bounded status/reason telemetry, schema-first rollout, existing CI. |
| VII-XI map/music/selection/overlays | Pass / Pass | Extend existing native dialog; no navigation/audio/grid/lifecycle redesign. Active-bus selection behavior remains; new feed metric has explicit city-wide scope. |
| XII presentation | Pass / Pass | Existing neutral RouteFilterResources.resx, English formatting. Carry forward user's feature-058 English-only decision; no language UI or additional resource store. |
| XIII themes | Pass / Pass | Existing light/dark palette, secondary typography, accessible associations. |
| User Git rule | Pass / Pass | Before/after commit hooks skipped; AGENTS no-commit section preserved. |

## Project Structure

### Documentation (this feature)

```text
specs/059-reported-vehicles/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/reported-vehicle-capture-v1.md
  contracts/reported-vehicle-reporting-v1.md
  tasks.md                     # next workflow; not produced by planning
```

### Source Code (repository root)

Existing seams and proposed additions:

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
  Cities/{CityConfig,GtfsRtCity,MartaCity,NymtaCity}.cs
  RailRealtime/RailRealtimeAdapter.cs
  Metrics/CityFetchResult.cs
  Program.cs / Worker.cs
  Statistics/ReportedVehicle{Identity,SourceObservation,SourceCatalog,HistoryOptions}.cs [new]
  Statistics/ReportedVehicle{Capture,Accumulator,CaptureLifecycleService,Sinks}.cs [new]
  Statistics/FinalizedReportedVehicleHourBatch.cs [new]
src/Server/ChefKnifeStudios.TransitJazz.Server.Data/
  Models/CityReportedVehicle{Hour,HourMembership}.cs [new]
  Configurations/CityReportedVehicle{Hour,HourMembership}Configuration.cs [new]
  AppDbContext.cs / ServiceCollectionExtensions.cs
  Migrations/*CreateCityReportedVehicleHours*.cs [generated new migration/snapshot]
  Statistics/CityReportedVehicleHoursStore.cs [new]
  Statistics/StatsOverlayQuery.cs
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
  Statistics/ReportedVehicle{StatisticsWriter,RetentionService,CaptureServiceCollectionExtensions}.cs [new]
  Statistics/{StatsOverlayOptions,StatsOverlayService}.cs
  Program.cs / appsettings.json
src/ChefKnifeStudios.TransitJazz.Shared/Stats/
  StatsOverlayResponse.cs / StatsReportedVehicleSummary.cs [new]
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
  Components/Stats/StatsOverlay.razor[.css]
  Components/Stats/StatsOverlayProjection.cs
  Resources/RouteFilterResources.resx
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicle*.cs [new]
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicle*.cs [new]
src/ChefKnifeStudios.TransitJazz.Shared.Tests/StatsOverlayContractTests.cs
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsOverlayProjectionTests.cs
skills/transitjazz-db/references/{tables,queries,results}.md [schema documentation]
```

**Structure Decision**: Follow route-history ownership: Worker owns capture; WebAPI host owns writer/registration/cleanup; Data owns persistence/SQL; Shared owns DTO; Client.Shared owns presentation. Independent reported collection options allow raw capture while route capture is disabled. No generic event ledger or abstraction over all stores.

## Design Details

### Capture

Configure explicit source keys/domains alongside existing URLs; validate one policy entry per enabled endpoint and carry it through both host registrations, including separate NYC subway/bus configuration. Source key identifies a poll endpoint; domain identifies a vehicle-ID namespace and may be shared across endpoints. Fingerprint stable membership, identity rules and cadence, excluding URLs/secrets.

Timestamp each successfully decoded response immediately with TimeProvider before filtering, not at city-cycle completion. Extract GTFS nondeleted Vehicle records with nonblank descriptor IDs; default excludes undocumented entity/trip fallbacks. Extract original MARTA TRAIN_ID before realtime/Line/coordinates filters. NYC fetch/decode must precede stop-offset readiness so geometry failures do not stop raw reporting; synthesis remains conditional. Preserve immutable per-source observations in CityFetchResult.Combine and submit independently of spatial readiness/publication. Source failures/empty raw responses cannot be inferred from synthesized feed outcome.

### Coverage and lifecycle

Per-source rolling summaries preserve success/failure, first/last receipt, maximum gap and boundary witnesses without retaining poll logs. Require all expected sources, stable source/policy set, healthy successful start/end continuity, no failed polls, no missing IDs and intact populations for Complete. Intact Partial with at least one successful response contributes; no success => NoData excluded. Identity loss, conflict, unsupported policy or clock regression excludes the hour. Successor evidence proves continuity only; its identities belong to its own receipt hour.

Sweep/shutdown independently of polling. Current and preceding grace hour are the only open states/city. Restart creates a new run ID; no unverified merging. Atomic store quarantines contradictory city/hour cohorts. Operational single owner/city is a rollout prerequisite; configuration/ownership conflicts cannot be repaired by unioning populations.

### Storage

Persist header plus complete membership set atomically under a namespaced advisory city/hour lock following CityRouteHourStatisticsStore. Compare canonical semantic header and all sorted membership tuples, not only hashes. Persistence timestamp/conflict marker are excluded from retry payload equality. Different run/payload produces sticky quarantine. Empty successful populations retain a header. Chunk insertion stays within one transaction; bounded writer attempts/drain and nonblocking queue admission preserve polling.

Retention deletes finite batches of expired headers with cascade, outside public reads. Validate retention >=7 days and beyond maximum active Stats horizon. Store no positions, raw feed body or per-vehicle event ledger.

### Reporting and disabled mode

Extend the existing single SQL statement's server-built city/hour grid, independently from route-eligible cohorts. Enabled SQL adds reported CTEs; disabled SQL contains no new-table references so pre-migration hosts still serve distance. CASE alone cannot hide absent relations. Eligible intact Complete/Partial cohorts with successful observations, supported source fingerprint/policy and matching stored membership count contribute.

Per-city/window COUNT DISTINCT over domain/kind/value across eligible hours, then sum city populations for All. Never sum hourly counts. Empty successful cohorts contribute zero. Only aggregate counts and bounded diagnostic rows leave PostgreSQL. Existing policy supplies identical today/last24 hours/cutoff and city-local date. Changed unsupported policies are excluded rather than silently mixing domains. Cache identity includes read setting, source/policy fingerprints and definition. Existing total deadline, cancellation, admission, response bytes and cache bounds remain.

### Presentation and rollout

Two secondary heading/value groups in existing cards, English integers, independent states/coverage, accessible heading association and both themes. All copy resides in existing neutral resx. Partial always displays observed qualification and contributing/expected city-hours; Pending/Unavailable never appear as zero. Route selections show persistent city-wide scope; All discloses sum and per-city windows.

Separate default-disabled ReportedVehicleHistory mode/cities from StatsOverlay.ReportedVehiclesEnabled read setting. Schema first, certified source contracts and single owner, dry run, selected persistence, retained-evidence/performance review, read enablement. Rollback disables reads then collection. No production migration, enablement or backfill occurs in planning.

## Phase 0: Research Results

[research.md](research.md) resolves source extraction, identity, coverage, storage, bounds and snapshot architecture from concrete repository seams. Source-specific external stability claims are explicitly unverified; default descriptor counting and disclosed limits provide implementable behavior pending release certification.

## Phase 1: Design Outputs

[data-model.md](data-model.md), [capture contract](contracts/reported-vehicle-capture-v1.md), [reporting contract](contracts/reported-vehicle-reporting-v1.md) and [quickstart.md](quickstart.md). Update AGENTS SPECKIT marker directly per skill: repository has no update-agent-context script. Preserve all other instructions. Post-design gates pass.

## Phase 2: Task Handoff

Foundational correctness includes raw extraction, source coverage, atomic storage and independent DTOs. US1 delivers correct city counts; US2 adds detailed qualification/presentation and failure fixtures; US3 route/All scope. Never ship US1 with optimistic completeness. Parallel work can cover extraction, mappings and DTO after contracts; queries wait for persistence/DTO; UI waits for summary; benchmarks wait for integration. Include requested independent fixtures, retry/conflict/restart, mobile/theme/accessibility, visitor checks and original performance budgets. During implementation record actual evidence in validation.md.

## Complexity Tracking

No constitution violations. Identity memberships are necessary to reproduce period distinct populations; separate headers are necessary to distinguish empty from incomplete collection. Existing host/store conventions avoid a new telemetry platform.
