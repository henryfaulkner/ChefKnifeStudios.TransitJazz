# Design Document: Vehicles Reported in Stats

**Status:** Proposed; implementation and deployment have not started.  
**Date:** 2026-10-13  
**Related:** [Stats overlay design](STATS_OVERLAY_DESIGN_DOCUMENT.md), [current implementation plan](../specs/058-stats-overlay/plan.md)

## Purpose

Add a unique reported-vehicle count beneath each distance metric in the Stats overlay:

| Existing card | New metric beneath distance |
| --- | --- |
| Distance today | Vehicles reported today |
| Last 24 completed hours | Vehicles reported over last 24 hours |

Count vehicles reported by the configured feeds, including those that cannot be matched to a route or positioned on the map. Count an identity once across the entire reporting window, even when it appears in hundreds of polls, multiple hours, or on different routes.

This document proposes the collection, storage, API, and presentation required to support those counts. It does not claim that a deployed database already contains the necessary evidence.

## Existing implementation and gap

The statistics database retains dashboard samples, category aggregates, and route-hour aggregates. Category and route capture use in-memory vehicle-ID sets, but persist only hourly distinct counts. Those counts cannot recover a distinct population across hours or across routes.

The existing `CityFetchResult.Feed` is also too late to establish every raw reported identity for all cities. For example, `NymtaCity.FetchAndSynthesizeSubwayAsync` drops entities whose position cannot be synthesized. A counter inserted only in `Worker.ProcessSpatialReconciliationAsync` would miss those vehicles.

Relevant integration points:

- `TransitDataWorker/Cities/{GtfsRtCity,NymtaCity,MartaCity}.cs` and `RailRealtime/RailRealtimeAdapter.cs`: extract identities at each source boundary before filtering or synthesis.
- `TransitDataWorker/Metrics/CityFetchResult.cs`: carry identity evidence and source outcomes independently of the map-processing feed.
- `TransitDataWorker/Worker.cs`: submit fetched identity evidence to reporting capture independently of route-index readiness and publication success.
- `Server.Data/Statistics/StatsOverlayQuery.cs` and `WebAPI/Statistics/StatsOverlayService.cs`: union retained identities within the existing reporting windows.
- `Shared/Stats/StatsOverlayResponse.cs` and `Client.Shared/Components/Stats/StatsOverlay.razor`: expose and display the counts.

Paths above are relative to their corresponding projects under `src/Server`, `src`, or `src/Client`.

## Counting contract

**Definition:** Number of distinct identifiable vehicles reported in successful configured source responses received during the contributing completed UTC hours.

This measures observed feed identities, not fleet size, vehicles in service, activity on mapped routes, or physical vehicles guaranteed to have permanent IDs. A stale feed record still reports an identity and contributes once. Non-vehicle entities, deleted entities, and blank identities do not establish a vehicle.

Use `(canonical city, identity namespace, identity kind, identity value)` as the identity. Preserve ordinal spelling and case. Prefer a nonblank vehicle descriptor ID. For a source that lacks descriptor IDs, allow an explicitly documented stable entity/agency train ID fallback. Mark fallback use in diagnostics; a trip ID or changing entity ID must not silently become a stable physical-vehicle ID. Exclude unidentifiable vehicle records and report their count.

Identity namespaces represent the source's vehicle-ID domain, not a URL. Feeds covering overlapping NYC subway line groups must share a namespace so duplicate reports deduplicate. Independent bus and rail ID domains use separate namespaces. Never use credentials, complete endpoint URLs, route keys, or poll sequence numbers as namespaces.

Where an agency's rail adapter constructs identities, extract from the original agency record before mapping/filtering and document that source's identity behavior. Actual source contracts must be checked during implementation; the existing synthesized IDs alone are insufficient evidence of permanent physical identity.

### Windows and selection

- **Today:** use the same city-local date and fully contained completed UTC hours as today's distance. Exclude the unfinished current hour. Before any hour is eligible, show Pending rather than zero.
- **Last 24 hours:** use the same 24 completed UTC hours and ingestion-grace-adjusted cutoff as the adjacent distance metric. This is not a rolling window ending at the current second.
- **City scope:** union identities across that city's contributing hours and all configured feeds.
- **All scope:** sum each city's distinct population, keeping city identity separate. Today follows each city's own local day. This is a sum of city populations, not deduplication of physical vehicles travelling between cities.
- **Route selection:** reported vehicles remain city-wide because unmatched vehicles have no route assignment. Distance continues to follow the existing route filter. Show a persistent small label, "All city feeds; independent of route selection", with the reported metric when routes are selected. Do not imply that these counts describe the selected routes.

Window, All-scope, and route-selection behavior are proposed product decisions. They preserve the distance cards' existing time semantics while making the broader population explicit.

## Capture and coverage

Add a reported-vehicle capture path independent of route geometry, route catalogs, spatial reconciliation, and tone publication. Fetch completion time assigns source evidence to a UTC hour. Include identities from every successful source response even if other sources fail.

Accumulate distinct identities in bounded per-city UTC-hour sets. Track source availability, poll timing, unidentifiable records, and population integrity separately. Successful empty responses provide observed-zero evidence. A processing or publishing failure after a successful fetch does not invalidate raw-feed coverage.

An hour is Complete only when:

1. The configured source membership stays stable throughout the hour.
2. Every expected source has successful, healthy observations with start/end continuity and gaps within the saved cadence policy.
3. Identity capture remains intact; no limits, clock regressions, queue loss, or capture errors have silently truncated the population.

Track timing per source so healthy polling of one source cannot conceal another source's outage. A successor poll may prove the preceding hour's end boundary but its identities belong to the successor hour. Startups, shutdowns, restarts, configuration changes, source outages, and exceeded bounds create Partial hours or missing evidence.

Partial hours with intact identity sets may contribute an explicitly qualified observed count. Hours with identity loss are excluded from the count. NoData, missing, and conflicted evidence never become zeros. Do not infer continuous capture from first/last sightings alone.

## Storage proposal

Use two new PostgreSQL tables, separate from existing immutable route history:

| Table | Key | Retained evidence |
| --- | --- | --- |
| `city_reported_vehicle_hours` | `(city_slug, hour_start_utc)` | Definition, run ID, source-set fingerprint, cadence policy, status, conflict flag, identity integrity, expected/successful/failed poll diagnostics, boundary evidence, unidentifiable count, incomplete reasons, persistence timestamp |
| `city_reported_vehicle_hour_memberships` | `(city_slug, hour_start_utc, identity_namespace, identity_kind, identity_value)` | One membership per distinct identity observed in that hour |

The header additionally retains bounded per-source timing and outcome diagnostics needed to validate coverage. Use EF mappings, aligned-UTC/nonnegative constraints, ordinal identity comparison, a composite foreign key from memberships to header, and an index beginning with `(city_slug, hour_start_utc)` for bounded window reads.

Persist each finalized city/hour header and its membership set atomically. Exact retries are idempotent. Different payloads for an existing immutable city/hour quarantine that hour rather than merge populations or silently replace evidence. Equality must include the complete membership set as well as header fields. A restarted process cannot upgrade a previously partial hour through an unverified union.

Keep raw identity values private to the data layer; the public API returns aggregate counts and coverage only. No raw feed bodies, positions, trips, or per-vehicle event ledger are needed.

### Bounds and retention

Proposed initial limits: 100,000 distinct identities per city/hour, 512 UTF-8 bytes per identity, 32 source domains per city, and a bounded persistence queue of 32 city/hour batches. Validate memory cost and observed agency populations before release. Reject/quarantine oversized evidence explicitly; never truncate a set and present its count as exact.

Retain seven days of completed membership evidence and headers, sufficient for the two current windows with operational margin. Run bounded cleanup through an existing host service, deleting expired headers and cascading memberships. Keep cleanup outside public reads and avoid deleting any requested window. A delayed cleanup increases storage rather than altering counts. Retention and capture settings must be configurable and documented.

Reuse existing asynchronous persistence and shutdown-drain conventions. Feed polling must not wait for PostgreSQL writes. Persistence failures must appear as missing coverage and bounded operational diagnostics. Multiple active worker replicas for one city require a single capture owner; otherwise conflicting cohorts remain quarantined. Do not solve replica ownership by silently merging incomplete batches.

## Query and API

Extend the existing bounded Stats read instead of adding browser requests. Compute vehicle summaries from the same server-built city/hour grid, cutoff, and consistent database snapshot as distance. Reported-feed eligibility is independent of route-hour eligibility.

For each window, union membership keys across eligible intact hours, then count distinct `(city_slug, identity_namespace, identity_kind, identity_value)`. Never sum hourly distinct counts. A vehicle appearing in all 24 hours contributes one. Aggregate in PostgreSQL; do not load identity sets into the API or expose them in the response.

Add `TodayReportedVehicles` and `Last24ReportedVehicles`, each containing:

- Window reference and nullable integer count.
- Coverage state: Complete, Partial, Unavailable, or Pending.
- Expected, contributing, and fully covered city-hour counts.
- Per-city window and coverage details, including missing/excluded hours and source/identity limitations.
- Population scope (`cityFeeds` or `sumOfCityFeeds`) and metric definition version.

Keep reported-vehicle coverage distinct from `StatsCoverage`'s route-hour fields. A complete feed count may coexist with unavailable distance, and the reverse may also occur. An unidentifiable record makes population completeness limited even when fetch cadence is healthy.

Reuse Stats cache admission, deadline, cancellation, and refresh behavior. Migration-first rollout avoids querying absent tables. Keep reported reads disabled until the schema is deployed; disabled reads return Unavailable without failing existing distance panels. Cache identity must account for reported-read configuration and definition/policy changes.

## Presentation

Keep the two existing cards. Add a secondary heading and integer count beneath each distance value, with consistent spacing and a divider if needed:

```text
Distance today                    Last 24 completed hours
983,607.09 kilometers             1,372,213.04 kilometers

Vehicles reported today           Vehicles reported over last 24 hours
12,345                            15,678
```

Numbers above are illustrative only. Use English integer formatting, existing light/dark styling, and responsive stacking. Keep the distance as the primary metric. The accessible text must associate each count with its heading.

Display Complete counts directly. For Partial evidence, label the count "observed; partial coverage" and show contributing/expected hours. Show Unavailable when no usable identity evidence exists and Pending when no completed today-hour is eligible. Display `0` only when usable empty-feed evidence establishes zero for the contributing window. Explain missing-identity limitations alongside coverage rather than presenting an exact fleet total.

Explain in concise visitor-facing copy: "Distinct vehicles reported by transit feeds during completed hours, including vehicles that could not be placed on the map." Show the cutoff/window with existing Stats details. Include the route-selection scope label described above.

## Delivery sequence

1. Verify per-city raw source identity contracts and namespace assignments; record fallback limitations.
2. Add raw identity evidence to source results before synthesis/filtering, with no changes to map/music behavior.
3. Implement bounded capture and per-source coverage, immutable storage mappings/migration, asynchronous writer, and retention cleanup.
4. Extend the existing aggregate query and typed API with independently covered vehicle summaries.
5. Add the two secondary metrics and coverage text to the existing cards.
6. Deploy schema first, enable collection for selected cities, inspect retained evidence, then enable reported reads and presentation. Existing history cannot backfill missing identity memberships.

Do not enable capture or apply a production migration as part of writing this design. Historical rollout and source identity behavior must be verified during implementation.

## Verification and acceptance

- One identity in repeated polls, across hours, and on different routes counts once per reporting window.
- Overlapping feeds sharing a namespace deduplicate; identical IDs in independent bus/rail domains remain separate.
- Raw records excluded by route matching, coordinates, or subway synthesis still contribute when identifiable.
- Missing/unstable identities, deleted/non-vehicle entities, source failures, identity limits, and capture loss receive their defined treatment.
- Empty successful feeds produce observed zero; absent history produces Unavailable; startup/restart gaps produce qualified coverage.
- UTC boundaries, source timestamps versus receipt time, local midnight, DST changes, ingestion grace, and the 24-hour cutoff match the defined windows.
- Partial intact memberships union correctly, conflicts exclude entire hours, retries remain idempotent, and membership/header transactions cannot expose half-written cohorts.
- Route selection does not change reported counts; All sums city populations with each city's local day and independent coverage.
- Database fixtures verify distinct union arithmetic and coverage independently of capture implementation. Worker fixtures verify extraction before filtering. Client/API checks verify null/zero formatting and separate coverage states.
- Query plans and matched worker-load measurements validate the proposed bounds, retention storage, existing Stats deadline, and collection overhead. Browser checks cover long labels, mobile widths, keyboard/screen-reader reading order, and both themes.

Acceptance requires counts backed by retained identities and explicit coverage. Summed route/hour counts, inferred fleet totals, placeholder numbers, or silently truncated sets do not satisfy the feature.
