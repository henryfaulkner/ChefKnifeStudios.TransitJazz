# Stats Overlay Bounded Read and Metric Contract v1

This is the implementation contract for `Server.Data/Statistics/StatsOverlayQuery.cs` and WebAPI Stats policy. It specializes the existing [route-hour observation contract](../../057-hourly-route-history/contracts/observed-route-hour-statistics-v1.md); it does not alter capture or the unrestricted internal analyst SQL.

## Window inputs

The service supplies one resolved immutable policy/configuration snapshot and one as-of instant `T` from TimeProvider. The client supplies no date/zone/definition/cadence/SQL/city array. For each validated city c:

```text
H = floorUtcHour(T - 2 minutes)
date_c = localDate(T, configuredZone_c)
M_c = UTC(actual midnight of date_c)
N_c = UTC(actual next local midnight)
today_c = [M_c, max(M_c, min(H,N_c)))
last24 = [H-24 hours,H)
```

Eligible grid consists only of UTC intervals `[h,h+1h)` fully contained in the requested window. Boundary intersections are disclosed and never prorated. Today before the first full interval is pending; current unfinished/future intervals are separate from expected missing evidence. Use actual 23/25-hour boundaries and keep UTC/local date/offset identity. At 14:01Z H=13:00Z; at 14:02Z H=14:00Z. Local date uses T even when H is still yesterday. The two-minute default must be measured against finalizer/writer retention lag.

For city scope only, when today's eligible grid is empty, use the preceding local calendar date for the query's daily slot and return its aggregates in the additive `previousDay` snapshot. The root today fields remain pending. The previous day's grid ends at `min(H, previousLocalDayEndUtc)`, preserving ingestion grace at midnight; last24 is unchanged. This replaces the empty daily query slot rather than adding a read. After the first eligible hour exists, restore the ordinary daily slot regardless of evidence availability. See the [API contract](stats-overlay-api-v1.md#early-morning-city-display).

## Query execution and memory

Use a single parameterized PostgreSQL statement/CTE chain for related result groups. Bound every source read to validated cities and their union of today/last24 intervals. No unrestricted earliest-history scan, client SQL, per-chart/per-route/per-city execution or raw route/hour cube materialized in .NET. PostgreSQL performs cohort grouping, row contribution and summary/prefix arithmetic.

Pipeline:

1. Build/receive expected city/hour grids and bounded city windows.
2. Read the bounded source union once with ordinal key semantics.
3. Discover historical option membership/latest metadata and validate each **unfiltered** cohort.
4. Apply selected membership to eligible rows; preserve absent selected keys as gaps.
5. Derive numerator/denominator/category/hour contributions once.
6. Produce coverage, city/route summaries, options, shares and timeline buckets from that evidence.
7. Return bounded aggregates for one immutable response, after capacity/size validation.

If multiple statements prove clearer, use one short read-only REPEATABLE READ transaction for all groups; independent READ COMMITTED commands are prohibited. All response groups must see the same pre- or post-quarantine state. Use no FOR UPDATE/SHARE, advisory lock or mutation. Dispose connection/reader promptly; transaction-local timeouts do not leak through pooling.

The whole cold-read deadline is five seconds including connection acquisition, commands, projection and response size validation. Pass cancellation to every DB operation, clamp command/server timeouts to remaining budget and perform no unlimited retry/queue. Dedicated four-read admission and bounded cache fills are defined in [API contract](stats-overlay-api-v1.md). Reuse the existing city/hour/route index; add an index only after measured query-plan justification.

## Whole-cohort eligibility

For each city/hour, inspect **every retained row**, even rows outside a filter. Contributing cohort requires:

- Every row Complete, nonconflicting, catalog-stable and valid under existing row invariants.
- Every definition exactly `observed-city-route-hour-statistics-v1` and every cadence equal to resolved configured `RouteHours.GapLimitFor(city)`.
- One capture run; identical positive eligible sample counts and compatible cycle/boundary evidence across rows.
- Intact nonnull route distinct populations and nonnegative finite/numeric counters.
- No unexpected cohort inconsistency; conservative exclusion with safe reason.

Individual route fingerprints may differ. The writer's atomic retention protocol supplies intact membership; no new manifest/repair table. A conflict anywhere excludes the whole hour. Partial/NoData/unsupported/inconsistent rows are diagnostic only. A Complete cohort containing inactive rows contributes valid zeros.

Each missing expected cohort receives an explicit missing status, even if no route keys can be discovered. Current live catalog never supplies historical membership/zero values. Capture currently disabled does not invalidate otherwise eligible retained rows. Publish definition even with no usable rows.

## Historical routes and selection

City selector membership is the retained key union in today or last24, including zero/ineligible/removed routes; no geometry/current-catalog supplementation. Latest retained hour determines label/category, with persisted time deterministic tie handling and range category-change notice. Arithmetic categorization remains per-hour metadata.

Filtered set P includes each selected route's own eligible rows, rather than common/intersecting hours. Missing selected key in an otherwise healthy cohort is unknown membership: retain other selected routes' amounts and report available/expected selected counts. Unfiltered P is exactly each eligible retained cohort, allowing historical population changes across hours. Return unfiltered selector options on a filtered read.

Validate the historical union <=4,096 even if the visitor selected fewer keys; do not promise smaller selection fixes an oversized selector. All output has city bubbles/series and no route list/cube, while cohort/route capacity validation remains explicit. Unknown selected keys stay in normalized selection and get fallback labels/coverage rather than silently becoming unfiltered.

## Arithmetic

For contributing route r/hour h, define:

```text
D_rh = DistanceMetersSum / 1000              (kilometers)
V_rh = DistinctActiveVehicleCount            (route/hour memberships)
S_rh = ActiveVehicleCountSum                 (activity sample numerator)
N_ch = common ValidActiveSampleCount         (city/hour samples)
A_rh = 1 if S_rh > 0 else 0                  (active clock hour)
```

All statistical division is numeric/decimal after summing; rounding is presentation only. A zero divisor yields null. A period with no eligible row has null amounts rather than invented zero.

| Measure | Formula over matching P |
| --- | --- |
| Distance today/last24 | sum(D_rh) |
| Category k distance | sum(D_rh where retained row category=k) |
| Category percentage | 100 × categoryKm / totalKm, null when totalKm=0 |
| Route bubble X | sum_h(D_rh) / sum_h(V_rh) |
| Route bubble Y | sum_h(D_rh) / sum_h(A_rh) |
| Route mean active | sum_h(S_rh) / sum_h(N_ch), per route's own hours |
| City bubble X | sum_rh(D_rh) / sum_rh(V_rh) |
| City bubble Y | sum_rh(D_rh) / count(h with sum_r(S_rh)>0) |
| City mean active | sum_rh(S_rh) / sum_h(N_ch), count samples once/city/hour |
| Category/city cumulative | Prefix of known eligible hourly contributions |

Stationary active vehicles count toward A/V/S even at D=0. A vehicle can count on multiple route-hours after transfer; no city/period uniqueness claim is possible. Ratio inputs use exactly the same contributing rows; no averages of averages or inactive-hour denominator omission. City active hour/sample denominator count once rather than once per route.

## Coverage units

For every window/city return expected completed/full-contained hours, hours with any contribution, fully covered requested hours, exclusions by reason, boundary exclusions, contributing bounds and pending-through instant. Per-route expected/available hours accompany filtered evidence. Selected expectedRouteHours = selected-key count × expectedHours; availableRouteHours = sum individually available route hours. These describe evidence availability, not scheduled service.

Unfiltered entirely missing cohorts cannot establish historical route population; expectedRouteHours is null rather than current-catalog count. All retains per-city coverage plus summed contributing/expected city-hours and contributing/expected city counts. Exclusion reasons can overlap; never sum them to replace the expected denominator. Preserve difference between complete/partial/unavailable/pending and source Partial status: useful partial *windows* sum only Complete contributing rows.

## Known amount versus plotted point

For each category/city bucket compute hourlyKnownKilometers and a cumulative known prefix independently of whether it can draw. No contributing row yields null hourly evidence and no increment. A category with eligible zero evidence yields zero; an entirely unavailable prefix remains null.

For city-mode category stacks, an excluded city/hour or any absent selected route makes every plotCumulativeKilometers null. Keep known amounts from available selected routes in cards/shares/ratios/table and later prefixes. Later fully covered points resume that known subtotal. Persistent qualification explains that it is known observed distance, with no estimated travel through gaps. If final points are gaps, cards/tables still show later known contributions. If all points gap but known amounts exist, show coverage-prevents-plotting rather than no-data.

All city series gap at ineligible/missing city hours and resume their known city subtotal. Hidden lines retain all arithmetic. Category raw cumulative amounts are stacked exactly once by the chart, not pre-stacked by the API. Healthy category sums/stack endpoints/card reconcile before rounding; All city known endpoints sum to its today card including hidden lines. Midnight origin is a projection-only zero reference, not evidence.

## Acceptance fixtures and measurement gates

| Fixture | Required result |
| --- | --- |
| 10-hour route 100 km + 6-hour route 60 km in same 10-hour window | 160 km; route 10/10 and 6/10; 16/20 requested route-hours; six fully covered selection hours |
| Route hours 20/10 km, memberships 4/2, activity 120/60, samples 60/60 | X=5, Y=15, mean=1.5 |
| Two-route city with common 60 samples/hour | City denominator 60/hour, not 120/hour; active hour once |
| Known 100 km, missing hour, then 40 km | Gap followed by 140 known subtotal; no interpolation/reset |
| Two of three selected routes in one healthy hour | Known amounts retained; whole stack gap; available 2/expected 3 |
| Conflict on route outside filter | Entire city/hour excluded |
| Complete inactive vs missing cohort | Numeric zero vs null with explicit coverage |
| DST/repeated local label/fractional midnight | Actual extent/UTC identity/offset and boundary exclusion, no proration |
| Concurrent quarantine during request | Coverage and numerators all pre-change or all post-change |

Disposable PostgreSQL correctness/plans, actual representative retention/lag, worst catalog union, max-scope memory/latency/response cap and combined <=5% history/Stats live-processing budget are required before read exposure. This document records a design and no runtime measurement.
