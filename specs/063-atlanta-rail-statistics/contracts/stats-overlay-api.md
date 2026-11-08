# Contract: Stats Overlay v2

## HTTP negotiation

Keep `POST /stats/overlay/query` and existing request JSON exactly:

```json
{"scope":"city","citySlug":"atlanta","routeJoinKeys":[]}
```

Existing city/all scope validation, historical selection, windows, rate/size limits, local-day/DST/completed-hour/grace and hourly refresh remain. Contract negotiation is a request header, not a body/schema change.

| X-TransitJazz-Stats-Contract | Result |
| --- | --- |
| Absent | stats-overlay-v1 semantics |
| `stats-overlay-v1` | Explicit legacy semantics |
| `stats-overlay-v2` | Available-measure semantics and evidence below |
| Empty, multiple values, unsupported value | 400 ProblemDetails, code/title `unsupported_stats_contract`; no query/cache fill |

Use exact ordinal supported values after trimming HTTP whitespace. Response `contractVersion` matches negotiation. Emit `Vary: X-TransitJazz-Stats-Contract` while preserving `Cache-Control: no-store`. Keep existing safe ProblemDetails shape and other status codes. Client adds header per request in StatsEndpointsService/HttpService, never by mutating shared DefaultRequestHeaders. Preserve cancellation, disposal, Retry-After and safe error mapping.

Normalized contract is explicit in service/query/projection policy, complete cache identity, retained selection slot, in-flight coalescing and client snapshot acceptance identity. Existing byte/entry/concurrency limits remain global across variants. Add legacy/v2 interleaving and concurrent same-selection tests. An absent-header cache entry cannot serve opted-in semantics or vice versa.

## Compatibility and admission

The additive storage migration is `20261107184134_AddRouteHourSourceEvidence`. It leaves all v2 columns null for existing v1 rows and does not update or relabel history. Rollout order is additive schema, v2 writer/capture, dual-contract server, then v2 client opt-in. Verify the migration against the guarded disposable PostgreSQL database before enabling writes; a generated migration or skipped integration test is not an execution result.

v1 retains original conservative all-Complete city/hour semantics, response shape/metric definition and formulas. New server may project validated v2 **Complete** cohorts to v1 only where every original eligibility predicate, common sample support and formula can be proven equivalent; never expose Partial measures to v1 or relabel stored evidence. Otherwise exclude with a legacy-safe reason. Test v1 behavior against both legacy and newly captured hours. Previous binaries safely exclude unknown stored versions during rollback; no claim that they read v2.

v2 supports metric definition `observed-city-route-hour-statistics-v2` and disclosed admitted definition versions per measure/window. Healthy v1 Complete cohorts retain existing admission checks. v1 Partial stays excluded with reason `partial_cohort`; no history upgrade. v2 safe Partial may contribute per measure. Unknown version/cadence/formula is excluded with bounded reason. Mixed-run/conflicting cohorts remain excluded as before. Source outage alone is not integrity failure. Unlocalized loss excludes cohort; localized loss excludes only provably affected measures.

Definition transitions are explicit. Combine v1/v2 measures only with unchanged formulas/units/cadence. Distance and unchanged route ratios can combine. Existing v1 city activity uses sum/max support; v2 sum/sum aggregate activity must not mix that formula. Incompatible aggregate activity stays null with `unsupported_definition`; distance and valid route bubbles survive. No default success for null legacy evidence.

Before an incompatible reader rollback, stop v2 writes and retain the additive columns and immutable history. Do not run the migration's destructive `Down`, backfill, or relabel rows to force older definitions to appear supported. Roll the client back to v1 where necessary; unknown versions remain safely excluded by previous binaries.

## Shared response extensions

Extend existing response records rather than duplicate the endpoint. v2 fields below are required in v2 and omitted from v1 JSON (avoid leaking altered semantics through additive defaults). Every unavailable numeric value remains explicitly null under existing serializer conventions.

| Owner / proposed field | Contract |
| --- | --- |
| StatsCoverage.ObservedRouteHours | Number of route/hour rows contributing valid observations for the assessed measure; NoData is excluded |
| StatsCoverage.UnavailableRouteHours | Nullable when historical expectations unknown; otherwise expected minus observed, with exclusions explained |
| StatsCoverage.SourceCoverage | Bounded city/source/window entries with state, contributing/full hours, covered/unavailable UTC-hour periods and reasons |
| StatsCoverage.MeasureAvailability | Bounded distance/activity/publication/derived-measure entries with availability, reasons, compatible definition versions and observed-ratio qualification |
| StatsCoverage.DefinitionVersions | Sorted admitted versions and explicit transition warning; no formula equivalence implied by sharing the list |
| StatsCategoryShare.Coverage | Category-specific coverage/measure availability, independent of city-wide state |
| StatsCategoryShare.DenominatorKind | `availableDistance`; visible/localized chart explanation required |
| StatsBubbleStatistic.MeasureAvailability | Per-axis/size/population reasons; retain omitted table row |
| StatsTimelineBucket.SourceCoverage / reasons | Bounded bucket evidence/limitations, including recovery; no false exact per-poll intervals |

Keep existing ExpectedClosedHours, ContributingHours, FullyCoveredHours, nullable ExpectedRouteHours, AvailableRouteHours, selected-route evidence and first/last contribution fields. AvailableRouteHours retains measure-eligible meaning; ObservedRouteHours makes observation coverage explicit where a derived measure is unavailable. Declare which measure a coverage object assesses. Count each hour once per city/selection: partial contributes one and full zero. All-city totals count city-hours explicitly, never as window-wide clock hours. When membership is known, observed/unavailable partition the same expected set; otherwise expectation/unavailable count stays null and HistoricalMembershipUnknown is true.

SourceCoverage entries use stable SourceKey and CitySlug, not endpoint addresses. Covered/unavailable intervals are merged adjacent UTC-hour intervals within the existing finite window, bounded by requested completed hours × configured sources. Include source names via localized key mapping in the client; never send arbitrary source failure text. A healthy bus-only selection can be Complete while Atlanta-wide coverage is Partial. Removing configuration does not remove historical expected dependencies.

Extend bounded vocabulary with `source_failure`, `source_disabled`, `source_recovered`, `unknown_source_provenance`, `invalid_measure_evidence`, `unaligned_activity_samples`; retain all existing integrity/window reasons. Definition transition can use a bounded warning `definition_transition`. Recovery is source coverage metadata, not a notification. Unknown reasons are rejected/safely bounded by version-aware server/client validation. Existing reported-vehicle response fields/population rules remain independent and city-wide.

## Arithmetic and availability

1. **Distance**: sum accepted kilometers from measure-eligible retained route/hour rows. At least one valid observation supports a number, including valid zero. No accepted observations means null; no scaling to hypothetical city/day totals. Category/route/city totals and timeline use the identical admitted distance set/window.
2. **Population/ratios**: route-vehicle-hours remain sum of intact route/hour distinct populations, never window-wide unique vehicles. Active-clock-hour definitions remain unchanged for the entity being shown. Distance and each denominator use the same eligible row set. A denominator-only exclusion cannot silently keep unmatched distance in the ratio; the separately displayed total may still retain it. Partial-hour ratios are qualified as observed ratios.
3. **Activity**: route mean is sum(valid numerator)/sum(valid samples). Failed polls add neither. City/category v2 mean uses summed valid numerator/denominator only when capture proves coherent aligned support within each hour. Compare accepted-support identity and count, not count alone; no max/min denominator or mean-of-means. Unaligned support returns null and `unaligned_activity_samples`. Incompatible formulas return null `unsupported_definition`.
4. **Bubbles**: validate X/Y/size independently. Missing/invalid axis or size omits the plotted bubble with an accessible table reason, without hiding distance or other valid bubbles/charts. Zero denominator is null, not infinity/NaN; no active vehicles can explain an omitted zero-size point.
5. **Shares**: category kilometers / sum(available category kilometers). Bus may be 100% while rail is unavailable; denominator kind/copy says available distance. Zero denominator yields null percentages. Rail remains in legend/table as unavailable, never zero-filled because missing.
6. **Timeline**: buckets carry observed increments, cumulative observed subtotal and nullable plotted cumulative values. Add only accepted increments. A wholly unavailable bucket has null increment/plot and remains a gap; do not bridge it or reset to zero. Later valid increments may resume the observed subtotal with a separate line segment. Partial intervals stay annotated. Category/total/increment sums reconcile.
7. **All-cities**: sum available city contributions, disclose missing city/source coverage, preserve each city's local day/cutoff. Current catalogs do not manufacture historical route expectations.

Illustrative v2 outcome (not a replacement for the full existing response): TodayDistance.Kilometers=120, Coverage.State=partial, ContributingHours=1, FullyCoveredHours=0; bus share kilometers=120/percent=100 with availableDistance; rail share kilometers=null/percent=null with source_failure. Bus-only historical selection may have complete coverage. Both-failed gives null distance. Proven full empty gives distance=0 and complete coverage, shares/undefined ratios=null.

## Snapshot/fallback rules

Update shared/client validation, recursive freeze, fixtures and contract tests before opt-in. Keep the v1 fixture at `specs/058-stats-overlay/fixtures/stats-overlay-v1.json`; add a separate v2 fixture in this feature's fixtures directory during implementation. Current-selection usable Partial including observed zero outranks previous-day fallback and older Complete snapshots. Previous-day applies only to the defined absence of an eligible current period, with server/client using one rule. Cancellation, wrong selection, obsolete generation and out-of-order responses cannot publish. New evidence changes must produce a new generated snapshot identity.
