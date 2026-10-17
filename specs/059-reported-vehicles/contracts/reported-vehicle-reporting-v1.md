# Reported Vehicle Reporting Contract v1

Extend existing Stats POST route and request; no new endpoint or browser request. Selection/time policy, authorization/public exposure, rate limits, refresh, stale-request suppression, cancellation and error transport retain existing behavior. Existing distance/charts are unchanged. New fields are aggregate-only and never contain identity values, URLs or credentials.

## Response shape

`StatsOverlayResponse` adds `todayReportedVehicles` and `last24ReportedVehicles` (C# TodayReportedVehicles / Last24ReportedVehicles). Each is StatsReportedVehicleSummary:

```text
window: "today" | "last24"
count: nullable long, explicitly serialized null
state: "complete" | "partial" | "unavailable" | "pending"
populationScope: "cityFeeds" | "sumOfCityFeeds"
definitionVersion: "reported-vehicles-v1"
expectedCityHours, contributingCityHours, fullyCoveredCityHours: nonnegative int
expectedCityCount, contributingCityCount: nonnegative int
cities: bounded per-city coverage entries
limitations: allowlisted codes
```

Per-city entry: citySlug, windowReference (existing StatsCityWindow + summary window), nullable count, state, expected/contributing/fullyCoveredHours, boundaryExcludedHours, firstContributingUtc/lastContributingUtc nullable, exclusions (code + hour count), unidentifiableRecordCount/fallbackRecordCount (records, not distinct vehicles), and source/identity limitations. Source detail may use approved public source keys and reason codes; no raw IDs or URLs. Response size bound remains <=8 MiB; number of diagnostic entries is bounded by configured cities/hours/sources. Validate long count within JavaScript exact integer range. Public nulls never disappear due to JSON defaults.

## SQL eligibility and arithmetic

Use StatsOverlayPolicy's existing finite city/hour grid and isToday/isLast24 flags. Do not recalculate date after ingestion grace, round/prorate partial UTC hours, or substitute rolling-now windows. Use one SQL statement snapshot for distance and reported results. Query input additionally carries trusted per-city expected source fingerprint, cadence policy fingerprint and supported reported definition, independently of route fields. Selection's route keys do not enter reported identity eligibility.

For each requested city/hour retain diagnostic row, including missing header. Contributing hour has:

1. Supported definition, expected source fingerprint and cadence policy; no contradictory/malformed diagnostics.
2. No conflict, identity integrity true and membership rows equal header membership_count.
3. Complete or Partial status and at least one successful source response.

NoData, missing, failed-only, loss, mismatch and conflicts do not contribute. A successful-empty Partial hour contributes observed zero; it is not Unavailable. Unidentifiable records produce Partial, not identity loss; oversized/unrepresentable IDs and dropped identity buffers are loss and excluded. Definition/source-policy changes exclude incompatible hours with explicit reasons instead of mixing ID domains.

For each window independently group memberships of contributing hours by city/domain/kind/exact value and count unique tuples. An identity seen across 24 hours counts once. Left-join count results to contributing-hour summary so successful empty populations yield numeric zero. Null only when no contributing evidence. All sums per-city counts of contributing cities, preserving absent cities as coverage exclusions; identical IDs across cities contribute separately. PostgreSQL performs deduplication; .NET receives only bounded aggregates/diagnostics.

FullyCoveredHours additionally requires Complete plus every expected source's validated healthy boundaries/gaps, no failed observations or missing IDs. Source diagnostics/fingerprint checks are independent of route-hour status. Use checked aggregate counter arithmetic; invalid/scope-too-large numbers use existing safe error handling without exposing records.

## State and denominators

| Condition | Value / state |
| --- | --- |
| No eligible completed today hours across scope | null / pending |
| Read disabled or no contributing evidence | null / unavailable (pending wins only for truly empty today grid) |
| Contributing evidence but any expected hour/city/source incomplete/excluded | distinct observed count including possible 0 / partial |
| Every expected hour fully covered, no boundary exclusion/missing IDs | distinct count including possible 0 / complete |

In All, cities with zero expected today hours are per-city Pending and do not invent contributing hours. Other cities can contribute; aggregate is partial if any city remains Pending or has boundary-excluded today time, even when fullyCovered equals expected for other cities. All Pending only when total expected city-hours is zero. last24 expects 24 city-hours per configured included city. Distance coverage can be unavailable while reported coverage is complete, or vice versa.

Exclusion/reason codes include missing_hour, no_usable_observation, conflict, identity_loss, unsupported_definition, source_policy_mismatch, cadence_policy_mismatch, source_failure, source_missing, start_boundary_missing, end_boundary_missing, cadence_gap, missing_identity, fallback_identity, source_set_changed, clock_regression, storage_unavailable, reported_reads_disabled and boundary_excluded. Distinguish limiting reasons on contributing Partial hours from excluded-hour reasons; never add these overlapping diagnostics into a fabricated coverage denominator. Definition copy says observed identities, not verified physical fleet.

## Compatibility and cache

StatsOverlay.ReportedVehiclesEnabled defaults false. Disabled query SQL has NO references to new relations; output summaries use unavailable/disabled except genuinely pending today. Select enabled/disabled SQL text server-side before execution, not CASE around absent relations. Deployment setting enables reads only after migration and evidence readiness; enabled unexpected missing-schema errors follow existing endpoint error policy and operational diagnostics.

Add read flag, definition and sorted per-city source/cadence fingerprint to existing cache key alongside original scope/selection/windows. Reuse cache/concurrency/whole-request timeout and refresh; no second timeout allowance. Existing clients tolerate additive response fields. New client handles omitted new fields from older server as Unavailable for migration compatibility, never zero; Shared model additions should therefore default to an unavailable summary rather than require new fields in old payloads.

## UI contract

Existing card layout:

```text
Distance today                    Last 24 completed hours
[primary distance + unit]         [primary distance + unit]
Vehicles reported today           Vehicles reported over last 24 hours
[secondary integer/state]         [secondary integer/state]
[independent coverage/scope]      [independent coverage/scope]
```

Exact headings and explanatory sentence are resx-backed: "Distinct vehicles reported by transit feeds during completed hours, including vehicles that could not be placed on the map." English integer N0 formatting with no decimals. Complete shows value; Partial adds "observed; partial coverage" and contributing/expected city-hours; Unavailable/Pending show words. Missing/fallback IDs are disclosed in accessible details. Route selection persistently displays "All city feeds; independent of route selection" by reported metrics; counts do not change. All discloses sum of city populations and per-city local-date/time-zone windows. Keep distance primary, show separate coverage, associate each count with heading via semantic heading/label, responsive at >=320px and 200% zoom, both themes. No chart/navigation/audio changes.
