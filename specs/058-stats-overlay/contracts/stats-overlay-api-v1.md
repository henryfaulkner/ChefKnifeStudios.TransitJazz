# Stats Overlay Public API v1

**Path constant**: `ApiEndpoints.Stats.Query`  
**Method/path**: `POST /stats/overlay/query`  
**Contract version**: `stats-overlay-v1`  
**Metric definition**: `observed-city-route-hour-statistics-v1`  
**Access**: Anonymous aggregate read when `StatsOverlay.Enabled` is true; no mutation or arbitrary query interface. HTTPS follows the existing API deployment. POST success uses `Cache-Control: no-store`; bounded reuse is internal to the API.

## Request

Content type is `application/json`. Exactly one current scope is requested; all dates/windows/definitions/zones/metric expressions remain server-owned.

```json
{"scope":"city","citySlug":"atlanta","routeJoinKeys":[]}
```

```json
{"scope":"city","citySlug":"atlanta","routeJoinKeys":["10","20"]}
```

```json
{"scope":"all","citySlug":null,"routeJoinKeys":[]}
```

| Field | Type | Required behavior |
| --- | --- | --- |
| scope | string | Required exact `city` or `all`; no enum integer/case-insensitive scope |
| citySlug | string/null | Configured canonical public slug for city; null for All |
| routeJoinKeys | string array | Required; empty city array means whole city; All rejects nonempty array |

Route keys are nonblank ordinal strings, <=512 UTF-8 bytes each; preserve spelling/case and do not trim/normalize identity. Deduplicate and ordinal-sort before query/response; enforce <=4,096 distinct selected keys and <=4 MiB body before SQL. Unknown historical keys with valid shape remain in normalized selection, with unavailable reason `unknown_route`; they never silently widen scope. Identity `atlanta` is canonical, while `marta` is not a public city alias.

Reject unknown request fields that attempt dates, city arrays, zones, versions, metrics or paging with safe `invalid_request`; finite DTO validation must not turn unsupported parameters into effective queries. Configuration resolves All membership, with <=32 cities and <=32 categories. Invalid city config/zone or absent schema is service unavailability rather than an arbitrary client identity.

## Successful response

All properties listed below are binding. Nullable evidence properties are explicitly present with JSON null when unavailable/undefined; the existing omit-null global serializer must be overridden only for Stats DTOs. UTC instants use ISO-8601 with Z, date uses `YYYY-MM-DD`, offsets use `±HH:MM`, metrics/counts use JSON numbers, arrays are finite and stable. No numeric NaN/infinity/string encoding. Counts that can exceed JavaScript exact integers must be checked before converting to chart inputs; reject unsupported numeric capacity rather than round a denominator silently.

| Response property | Type/contract |
| --- | --- |
| contractVersion | `"stats-overlay-v1"` |
| metricDefinitionVersion | `"observed-city-route-hour-statistics-v1"`, including wholly unavailable responses |
| generatedAtUtc | Actual cold snapshot as-of; remains unchanged on cached delivery |
| cutoffUtc | `floorUtcHour(generatedAtUtc - configured IngestionGraceMinutes)` |
| refreshIntervalMinutes | Configured integer divisor of 60; present on every valid response from a new server |
| refreshWindowStartUtc | UTC start of the fixed refresh window containing the original generation time |
| nextRefreshAtUtc | Effective UTC expiry: earlier of the next fixed refresh boundary and the earliest future root-city local-day end |
| selection | Validated normalized request, retaining unknown selected keys |
| cities | One CityWindow for city, every configured public city for All, including unavailable cities |
| routeOptions | Unfiltered retained option union for city; empty in All |
| todayDistance | DistanceSummary for `today` |
| last24Distance | DistanceSummary for `last24CompletedHours` |
| categoryShares | Today's CategoryShare list, including known zero/unknown category identity |
| bubbles | Route or city BubbleStatistic list, including omitted/unavailable table entries |
| timeline | Category stacked or city unstacked Timeline |
| warnings | Unique bounded stable code list; localized copy is client-owned |
| previousDay | Optional city-only PreviousDaySnapshot while today has no eligible completed hours; absent/null otherwise |

Refresh metadata is additive. Old clients ignore these fields. A new client distinguishes all-three-absent legacy responses from explicit null or partial schedule metadata. All-absent legacy responses use a 60-second freshness and hard-expiry limit from `generatedAtUtc`; the client does not infer hourly freshness. Any supplied null/partial set, invalid interval, unusable or non-UTC timestamp, or invalid ordering is `invalid_response` and cannot replace previously accepted evidence.

### Early-morning city display

Before the first eligible completed hour of the city's current local date, `previousDay` supplies the preceding local calendar day's dashboard. It contains `city` (CityWindow), `distance` (DistanceSummary), `reportedVehicles` (StatsReportedVehicleSummary), `categoryShares`, `bubbles`, and `timeline`. The nested daily fields/window references named `today` describe that nested city's `localDate`, which is yesterday. The root today fields and root city windows retain their actual-today meaning; the rolling 24-hour fields retain their original cutoff and bounds.

The server builds yesterday with the same cutoff, ingestion grace, route selection, coverage rules, and single bounded SQL read. Its day may have 23 or 25 hours. During the midnight ingestion grace, yesterday's final hour may still be pending. Once today's first hour is eligible, `previousDay` disappears even if current-day evidence is missing, so outages remain visible. All-city scope never supplies this fallback. Existing refresh/cache identity includes the actual local date and cutoff, so the transition does not reuse yesterday's snapshot.

### CityWindow

Fields: `citySlug`, `displayNameKey`, `timeZone`, `localDate`, `localDayStartUtc`, `localDayEndUtc`, `requestedTodayStartUtc`, `requestedTodayEndUtc`, nullable `eligibleTodayStartUtc`/`eligibleTodayEndUtc`, `last24StartUtc`/`last24EndUtc`, `todayCoverage`, `last24Coverage`.

Each city date is resolved from generatedAtUtc in its configured IANA zone. Today's request is clamped empty when cutoff precedes midnight; eligible bounds are null for an empty full-hour grid. Last24 ends at the same UTC cutoff for every city. All can have differing dates/extents and must say “Sum of each city's local day.” It cannot imply normalized performance rankings or omit missing cities.

### Coverage

Fields: `state` (`complete`/`partial`/`unavailable`/`pending`), `expectedClosedHours`, `contributingHours`, `fullyCoveredHours`, nullable `expectedRouteHours`, `availableRouteHours`, `selectedRouteCoverage`, `exclusions`, `boundaryExcludedHours`, nullable `firstContributingUtc`/`lastContributingUtc`, `pendingThroughUtc`, `historicalMembershipUnknown`.

Selected-route coverage entries contain `citySlug`, `routeJoinKey`, `expectedHours`, `availableHours`, `reasons`. Exclusions contain safe `reason` and `hours`; hours count each excluded city/hour once per reason, not each row. Multiple diagnostic reason counts may overlap and are not added as a disjoint denominator.

ExpectedClosedHours counts the fully contained expected completed grid. ContributingHours means any selected/whole-cohort eligible amount; fullyCoveredHours requires the complete requested scope in that hour. Selected expected route-hours = selected-key count × expectedClosedHours; available route-hours sums independently available keys/hours. Missing unfiltered cohort population means expectedRouteHours null. Boundaries/current pending/future hours never masquerade as missing expected completed capture.

Complete means all requested completed evidence is fully available without an unresolved boundary exclusion; partial means some eligible evidence exists with a limitation. Unavailable means completed evidence is expected but none contributes; pending means no completed eligible today hour is yet requestable. Last24 is evaluated independently. No available historical row or zero divisor yields a null metric; known healthy inactivity yields numeric zero.

### DistanceSummary and CategoryShare

DistanceSummary fields: `window`, nullable `kilometers`, `coverage`, `contributingCityHours`, `expectedCityHours`, `contributingCityCount`, `expectedCityCount`. In city mode the city counts are 0/1 as appropriate; All sums each city's window subtotal and counts without flattening away per-city coverage. A partial amount is a known observed subtotal.

CategoryShare fields: `categoryKey`, nullable `kilometers`, nullable `percent`, `coverageReference` (`today`). Category distance and shares describe known today evidence; zero total makes all percentages null. Historical row categories drive arithmetic, including `unknown`; latest option metadata does not rewrite earlier category amounts. Legend visibility never changes shares/amounts.

### RouteOption

Fields: `citySlug`, `routeJoinKey`, `label`, `categoryKey`, `metadataTimestampUtc`, `categoryChanged`, nullable `availabilityReason`.

Membership is the retained key union across the city's today/last24 reporting range, including inactive and ineligible rows. Metadata selects the latest retained hour deterministically; categoryChanged records transitions in that range. No live-catalog geometry/readiness is needed. Unknown selected keys remain in `selection` and coverage rather than becoming invented retained options. City route options/bubbles are bounded at 4,096; selector union is checked even on filtered requests.

### BubbleStatistic

Fields: `entityKey`, `entityKind` (`route`/`city`), `citySlug`, nullable `routeJoinKey`, `label`, nullable `categoryKey`, nullable `distanceKilometers`, nullable `routeVehicleHours`, nullable `activeClockHours`, nullable `activitySampleNumerator`, nullable `sampleDenominator`, nullable `x`, nullable `y`, nullable `meanActiveVehicles`, `coverage`, nullable `omissionReason`.

Entity keys encode city+route without ambiguous concatenation and never parse localized labels. All numerators/denominators use matching eligible evidence. X is km per observed route-vehicle-hour; Y is km per observed active clock hour; mean is activity samples divided by eligible samples. City samples/active hours count once per city/hour. Undefined coordinate or nonpositive mean omits the visual point, with the table entry/reason retained. An active stationary route has valid zero X/Y and positive mean.

### Timeline

Fields: `seriesKind` (`category`/`city`), `stacked`, `elapsedHourExtent`, `series`.

TimelineSeries fields: `key`, nullable `categoryKey`, nullable `citySlug`, `windowReference`, `buckets`. City mode category series share that city's window; All each series references its city's window.

TimelineBucket fields: `startUtc`, `endUtc`, `localDate`, `localEndLabel`, `utcOffset`, `elapsedHours`, nullable `hourlyKnownKilometers`, nullable `cumulativeObservedKilometers`, independently nullable `plotCumulativeKilometers`, nullable `availableSelectedRoutes`/`expectedSelectedRoutes`, `coverageState`, nullable `reason`.

Order intervals by UTC and numeric elapsed X. Include the expected full local-day extent with future buckets marked unavailable/future; no future measurements. Local end offset disambiguates repeated clock time. Known contributions in a gapped bucket remain in cumulative arithmetic; plot value is null for every category at an excluded city/hour/missing selected key. A later fully covered point resumes the known prefix. A wholly unavailable prefix stays null; numeric zero requires eligible zero evidence. A presentation-only midnight origin is not an observed interval.

## Safe vocabulary and HTTP failures

Data/coverage reasons include `missing_cohort`, `partial_cohort`, `no_data`, `conflict`, `unsupported_definition`, `unsupported_cadence`, `inconsistent_cohort`, `unknown_route`, `route_not_retained`, `history_disabled`, `not_retained`, `boundary_excluded`, `pending`, `future`, `zero_denominator`, `no_active_vehicles`. Warnings include `partial_coverage`, `city_days_differ`, `catalog_changed`, `boundary_excluded`, `historical_membership_unknown`, `different_route_periods`. Bound arrays by the finite vocabulary/city/route scopes; never return raw exceptions/incomplete arbitrary diagnostics.

| HTTP | Stable problem code | Meaning/recovery |
| --- | --- | --- |
| 200 | None | Structurally complete evidence response, including partial/pending/wholly unavailable history |
| 400 | invalid_request / invalid_scope / invalid_city / invalid_route_key | Malformed scope/identity/shape; no SQL |
| 413 | request_too_large | Body or selected-key cap exceeded; no SQL |
| 422 | scope_too_large / response_too_large | Historical union/city/category/numeric/response bound; never truncated success |
| 429 | rate_limited | Trusted-client token budget; Retry-After |
| 503 | stats_disabled / dependency_unavailable / read_timeout / busy | Read exposure off, DB/schema dependency, five-second deadline or cold-admission saturation; safe retry hint |

ProblemDetails has stable `code`, safe fixed title/status and optional trace correlation; it contains no SQL, credentials or exception message. Client displays localized resource text, not arbitrary server detail. Numeric/date Retry-After is parsed to a delay; a longer service delay overrides local backoff. Initial delay is 60 seconds, increases to five minutes under continued failure and resets after success. An active cooldown also governs selection changes/retry/visibility restoration.

## Bounded service behavior

Schedule-aware cache: successful All, city-baseline and exact filtered selections share the same bounded cache and in-flight identity. Canonical identity includes scope/city, the ordinal-sorted distinct case-sensitive route-key array, cutoff and refresh window, interval/grace, contract/metric versions, configured city/date/zone/cadence policy, and reported-vehicle enablement/source/cadence fingerprints. All remains one independent consistent database read, never concatenated city caches. Reordered/duplicate routes join only when their normalized exact selection and full policy identity match; other routes, case variants, cities and scopes remain isolated.

Retain at most 33 entries/64 MiB estimated total, with a filtered subset of at most 8 entries/16 MiB by default. Filtered entries may displace filtered LRU entries but never baseline evidence. Baseline admission evicts filtered entries before the oldest baseline; a full set of 33 baselines can serve a filtered result uncached. Remove expired entries before admission. Estimate from the single bounded serialization using UTF-8 bytes ×8 plus fixed response, canonical key, selection-slot and entry overhead; this is an admission estimate, not actual heap measurement. `CacheSeconds` is retired from active configuration and policy.

Entries expire at their original `nextRefreshAtUtc` or corresponding monotonic lifetime, whichever comes first. A clock regression flushes retained entries. Never serve expired fallback or cache a fill that crossed its original boundary. The five-second shared-fill deadline spans query, projection and response-size validation; host shutdown cancels shared work, while an individual caller cancellation only stops that caller's wait. Allow at most four concurrent cold fills per instance, with immediate `busy` when no matching fill can join and no queue. Exact fill promises are published under a short lock; query, projection, serialization and waits run outside that lock. A matching waiter joins even when all read slots are occupied.

Endpoint limiter: ten-token burst, one token/second refill (60/minute sustained), no queue, trusted IP provenance. Requests remain anonymous bounded POSTs with `Cache-Control: no-store`; successful responses must fit the 8 MiB uncompressed response cap before publication. Compression uses the existing API middleware. Cache estimates do not stand in for separate measurements of transient query/projection/serialization memory or supported replica-count load.

No vehicle/trip/listener/raw-feed/capture-run identifiers, credentials, unrestricted SQL or EF/Pax types appear in the response. Read exposure and launchers are independent from capture, dry run and city settings. A missing city's retained history is data availability; unavailable database/schema is service failure.

## Required contract evidence

Independent fixture arithmetic, explicit null-vs-zero serialization under current JsonSettings, unknown/case-distinct key selection, full cohort quarantine outside filter, retained-union metadata, DST/boundary/cutoff windows, full-snapshot consistency, endpoint/cap/retry/cache limits and no sensitive fields. Real disposable PostgreSQL and response/capacity checks are implementation gates, not results of this document.
