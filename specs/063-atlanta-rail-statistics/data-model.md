# Data Model: Atlanta Rail Recovery and Available Statistics

## Existing identities and relationships

One city/hour envelope contains ordinally sorted unique route rows under `(CitySlug, RouteJoinKey, HourStartUtc)` and one nonempty CaptureRunId. HourStartUtc is a canonical microsecond UTC hour. RouteJoinKey remains `RouteShapeProperties.JoinKey`, distinct from StaticRouteId. Preserve existing catalog/geometry fingerprints, counters, decimals, collection status, retention and sticky HasConflict. Do not change the advisory-lock namespace when introducing v2: v1/v2 writers must serialize on the same city/hour identity.

City source inventory → explicit route dependency set → route poll validity → route-hour evidence → versioned per-measure query admission → selection/window coverage. Reported-vehicle observations are a separate relationship, not the authority for source health. Category capture receives the same validity decisions but retains its own minute/hour reconciliation and tables.

## SourceObservationEvidence (new bounded capture value)

| Field | Type / rule |
| --- | --- |
| SourceKey | Stable ordinal configured key, nonblank, at most 64 UTF-8 bytes; never URI/vehicle ID |
| Outcome | `success`, `empty`, `failed`, `disabled` |
| ReceiptUtc | UTC receipt/completion time from injected TimeProvider |
| FailureReason | Nullable bounded code; no exception/body/address/key |
| PositionedCoverageValid | Distinguishes valid realtime-position population, schedule-only empty and rejected realtime positions |
| ExpectedSourceSet | Immutable configured inventory, at most 32 source keys/city, independent of reported-history flag |
| RouteDependencies | Explicit canonical route → expected source keys, optional proven substitute groups; unknown mapping is invalid provenance |

CityFetchResult retains combined Outcome/SuccessfulSourceCount/FailedSourceCount for monitoring. New evidence exists independently of ReportedVehicleObservations, including when reported capture is disabled. Disabled expected sources must still be represented. Cancellation is propagated, not converted to an empty success. Other adapters lacking this evidence remain on conservative legacy capture; do not invent v2 provenance.

## Route/source membership

Atlanta static bus routes depend on `marta-bus`; BLUE/GREEN/RED/GOLD depend on `marta-rail`. Validate against the same static route catalog/aliases used for positioning. Do not use category as global source identity. Freeze historical expected dependencies at hour creation; preserve necessary union on source/catalog/config changes and mark the change. Removing a source cannot retroactively improve an hour. Overlap requires every dependency unless an explicit adapter contract establishes complete substitution; existing vehicle representative deduplication still applies.

Retained envelope membership establishes expected routes only for that hour when intact. Missing cohorts cannot be reconstructed from today's catalog or configured source list. ExpectedRouteHours therefore remains nullable. NoData rows count toward known expected membership but not available measurement hours.

## RouteHourSourceSummary (new persisted JSONB)

Column `source_evidence` is nullable JSONB. Null is required for legacy rows; v2 requires a recognized `schemaVersion` and bounded canonical payload. Proposed canonical shape:

```json
{
  "schemaVersion": "route-hour-source-evidence-v1",
  "sourceSetFingerprint": "<64 lowercase SHA-256 hex>",
  "expectedSourceKeys": ["marta-bus"],
  "contributingSourceKeys": ["marta-bus"],
  "membershipKnown": true,
  "sources": [{
    "sourceKey": "marta-bus",
    "successfulPollCount": 350,
    "emptyPollCount": 10,
    "failedPollCount": 0,
    "disabledPollCount": 0,
    "firstSuccessfulReceiptUtc": "2026-11-07T13:00:00Z",
    "lastSuccessfulReceiptUtc": "2026-11-07T13:59:50Z",
    "maxSuccessfulGapSeconds": 10,
    "startBoundaryOk": true,
    "endBoundaryOk": true,
    "positionedCoverageValid": true,
    "recoveredDuringHour": false,
    "reasons": []
  }]
}
```

`successfulPollCount` counts nonempty successes; `emptyPollCount` counts successful empty polls separately. Their sum is successful support. Each poll has exactly one outcome. Boundary successor/predecessor evidence can lie immediately outside the hour under existing rules; in-hour first/last receipt fields stay within the canonical hour. A failed/disabled interval invalidates source continuity despite successful siblings. Recovery marks the hour Partial permanently; later healthy hours can qualify independently.

Fingerprint a versioned, length-prefixed canonical encoding of expected source/dependency policy (including declared substitutions), not URLs or JSON property order. Only route-relevant sources are stored. Source keys/arrays/reasons are unique and ordinally sorted. Validate counts/timestamps/gaps against inventory and hour limits; checked overflow or excess inventory invalidates capture. No per-poll record, timestamp array or vehicle list is retained. Covered/unavailable periods exposed publicly are bounded hour intervals derived from summaries; never claim exact per-poll outage intervals absent evidence.

## Independent SQL scalar evidence

Add nullable columns to the existing entity/finalized row and map through the existing writer/store:

| Column / field | Meaning and rule |
| --- | --- |
| `distance_valid` | True only when retained distance evidence is trustworthy; false is not observed zero |
| `activity_valid` | True only when numerator/denominator and required route population are intact |
| `publication_valid` | Independent crossing-publication evidence validity |
| `distance_reasons`, `activity_reasons`, `publication_reasons` | Nullable for v1; sorted bounded recognized text arrays for v2 |
| `source_set_fingerprint` | Required v2 mirror of JSONB fingerprint, null v1 |
| `activity_support_fingerprint` | Constant-size digest of accepted cycle support, scoped to run/hour; null when unproven |

Use a bounded streaming SHA-256 support accumulator over canonical accepted cycle identities (same city poll identity/completion, run and UTC hour) plus accepted count. No persistent poll list is needed. Equal counts alone do not prove support alignment; compare count and fingerprint among contributing routes within each hour and coherent source/population contract. Unknown/unequal support prevents aggregate activity. Each hour may have a different support fingerprint; validate alignment per hour before summing compatible hour numerators/denominators.

Preserve existing nonnegative counters, valid counts ≤ observed counts, numeric(20,6) distance/gaps and exact integer limits in API. Successful empty adds a valid zero activity sample. Failure adds no numerator/denominator; all-failed has no valid zero evidence. Distance is available zero only with valid observation support, never because stored accumulator default is zero. Accepted intervals remain valid pre/post-outage; baseline reset/reseed prevents bridging the outage.

Validity is distinct from coverage: a Partial route can have valid distance/activity, invalid publication; a sibling-source outage is not measurement corruption. Identity overflow invalidates population-dependent measures; loss of route attribution/index/processing/capture invalidates affected measures. Preserve distance only if loss impact is provably absent. Unlocalizable loss excludes the whole cohort.

## Definition, validation and immutable persistence

New Atlanta evidence uses `observed-city-route-hour-statistics-v2`. v1 definitions/Complete checks remain unchanged; v1 Partial stays excluded. v2 Complete requires healthy boundaries/gaps and every expected dependency, known membership, stable catalog, intact populations, independent measure evidence and no unresolved limitations. Partial permits valid measures. NoData has no eligible measures but retains source/membership evidence.

Additive migration adds nullable columns and version-aware checks without updating existing rows. Keep v1 Complete constraints as a separate preserved branch; add v2 shape/scalar agreement and reason vocabulary constraints. Null legacy evidence never defaults to success. Validate Worker row and Data entity consistently; reject malformed/unknown v2 evidence before persistence and conservatively exclude corrupted retained evidence on read.

All fields participate in FinalizedRouteHourStatisticsBatch.Validate, writer ToEntity, Data Validate, store Normalize/Copy/SamePayload and EF migration tests. Canonical typed JSON comparison must make semantically identical property ordering retry-equal while treating dependency/counter/validity/support changes as payload mismatch. Atomic whole-city/hour insertion and sticky quarantine remain; mismatched retry does not replace stored values. New evidence cannot alter lock scope, retention or ordinal collation.

## Query/result entities

- **CohortIntegrity**: city/hour/run/version/cadence/catalog/loss checks; distinguish structural exclusions from source limitations. Mixed-run and conflict remain cohort exclusions.
- **MeasureContribution**: eligible row/hour set separately for distance, activity/population and publication. v2 may contribute Partial; v1 only healthy Complete under existing checks.
- **CoverageAssessment**: contributing versus fully covered hours, nullable expected route hours, observed/unavailable route hours, bounded exclusions, source key/state/covered hour periods, definition transitions and measure availability. Count each city/hour once; never add category hour counts into city hours.
- **MetricCompatibility**: unchanged formula/unit/cadence identity per measure. Distance and unchanged route ratios may mix v1/v2; incompatible aggregate activity remains null without invalidating distance.
- **Snapshot**: contract-qualified selection/windows, independent nullable measures, per-category evidence, bubbles/omission reasons and timeline gap/Partial evidence. Preserve independently qualified city-wide reported vehicles.

## State transitions

`expected → success/empty` establishes valid support; `expected → failed/disabled` adds a limitation with no sample. `failed → success` marks recovery, reseeds movement and retains Partial for the recovery hour. Startup/shutdown/boundary/gap evidence also prevents Complete. Closed hours freeze once; later source recovery cannot relabel them. Conflict is monotone quarantine. API `pending` means no eligible completed period yet, `unavailable` means no admitted observation, `partial` means observations with coverage limitations, and `complete` means full proven coverage. Null, zero and NoData remain distinct.
