# Per-Route Prototype Insights — Design Document

**Status:** Proposed; implement after feature 057 code and schema are deployed and retained route history is verified  
**Date:** 2026-10-10  
**Scope:** A route exploration section in the existing Grafana insights prototype  
**Depends on:** [057 specification](../specs/057-hourly-route-history/spec.md), [implementation plan](../specs/057-hourly-route-history/plan.md), and [route-hour query contract](../specs/057-hourly-route-history/contracts/observed-route-hour-statistics-v1.md)

## Recommendation

Add a **Routes behind the rhythm** section to the existing **TransitJazz — The City's Rhythm (Prototype)** dashboard, UID `transitjazz-category-insights-lab`.

Use four graphs to explain which selected routes carry recorded distance, how their distance accumulates, when their movement is concentrated, and how movement relates to musical activity. Place a compact route coverage strip directly beneath the cumulative curves.

The existing category section supplies the city/cohort overview. The new section explores individual resolved routes using feature 057 history. Its route selector affects only the new section; existing city and transit-ensemble controls provide shared context.

This document specifies future dashboard work. It does not establish deployed schema, active capture, retained observations, or query execution. Dashboard implementation begins with the preflight below.

## Questions the section answers

1. Which selected routes account for the most recorded movement?
2. Do routes accumulate distance steadily or during concentrated periods?
3. At which local hours does each route move most?
4. How do routes differ in recorded movement and published tone opportunities?
5. Which gaps or route changes limit those comparisons?

Keep the main view focused on these questions. Processing counts and suppression diagnostics belong in a later optional explanation view rather than additional default charts.

## Layout

Append one route section after the current category charts. Use Grafana's existing 24-column layout:

| Order | Panel | Width | Suggested height |
| --- | --- | ---: | ---: |
| 1 | Routes behind the rhythm: introduction | 24 | 3 |
| 2 | Which selected routes carry the distance? | 8 | 10 |
| 3 | Watch the routes accumulate distance | 16 | 10 |
| 4 | How much of each route can we see? | 24 | 5 |
| 5 | When does each route find its rhythm? | 12 | 10 |
| 6 | Which routes turn movement into musical activity? | 12 | 10 |

On narrow displays, stack the panels in this order. Coverage remains adjacent to the cumulative view. Final heights depend on rendered label and tooltip review.

Introductory copy:

> Follow a few routes behind the city's rhythm. Compare their recorded distance, daily patterns, and musical activity over the same observed hours. Gaps represent unavailable history; distance comes from accepted movement updates.

Show a compact shared context line: selected route count, comparable hours / requested whole hours, contributing bounds, and the named local zone used by the daily profile. Explain unavailable comparisons in this line instead of presenting empty totals as zero.

## Controls and identity

| Control | Behavior |
| --- | --- |
| City | Reuse the existing canonical city selector |
| Transit ensemble | Reuse the category selection to discover route candidates from retained history |
| Routes | Add a multiple-selection variable named `route`; accept one to five resolved route keys, with no All option |
| Time range | Reuse the existing finite range; default remains the last seven days with automatic refresh disabled |
| Local zone | Resolve the selected city's configured IANA zone; display it in the daily-profile title/context without changing the dashboard's UTC time axis |

Discover route choices from retained rows intersecting the range, including removed routes. Use `route_short_name` as a display label when present, and append `route_join_key` where labels collide. Keep the canonical case-sensitive `route_join_key` as the value. Never group or select by raw static aliases, lowercase route keys, or infer historical membership from today's catalog.

On first load, select up to three eligible routes from the most recent retained Complete city/hour cohort in the chosen categories, ordered by display name and then ordinal route key. This is a deterministic starting selection, not a claim that those routes lead the city's movement. If no suitable cohort exists, display **Choose routes with retained history** and preserve any explicit route selection.

On city change, clear route selections that belong to the previous city. After other filter changes, retain explicit route keys and show Missing evidence when appropriate rather than silently substituting different routes. More than five routes produces **Choose up to five routes to compare**; do not silently truncate.

A bar's data link can select its route while preserving city, category, time range, and UTC settings. Use a normal Grafana data link with URL-encoded route values; this is dashboard navigation, not a new application endpoint.

## Panel 1: Which selected routes carry the distance?

**Form:** Horizontal bar chart.  
**X:** Recorded distance, kilometers.  
**Y:** Selected route labels, ordered by descending recorded distance; ordinal key breaks ties.

For each selected route, calculate:

```text
recorded_km = sum(distance_meters_sum over comparable hours) / 1000
selected_route_share = route_recorded_km / sum(selected_routes_recorded_km)
```

The comparison universe is the selected routes. Do not label their shares as the percentage of all city or category travel. Hover shows recorded kilometers, share among selected routes, comparable-hour count, requested whole-hour count, and contributing bounds.

An observed route with zero recorded distance retains a zero bar and label. No comparable hours yields **No comparable hours for these routes**, not zero bars. A zero total makes percentage shares unavailable.

Caption: **Recorded distance during hours shared by the selected routes.**

## Panel 2: Watch the routes accumulate distance

**Form:** Unstacked stepped time-series lines, one per selected route.  
**X:** UTC hour.  
**Y:** Cumulative recorded distance, kilometers.

Sum accepted meters in chronological order within the selected period and compatible route-history segment, then divide by 1,000. Calculate the running sum over eligible comparable rows first; left-join it to the expected hour grid afterward so gaps remain null.

Start at the first comparable hour in the selected period, not at the beginning of all retained history. Each point represents a whole completed hour. A missing hour breaks the line. A subsequent point resumes the accumulated sum of recorded hours only; do not carry values through gaps or infer missing travel. Fresh stationary zero-distance hours remain valid observations.

Use separate lines rather than stacking so users can compare slopes and route crossings directly. Keep the y-axis anchored at zero. Hover shows the UTC hour, that hour's recorded kilometers, accumulated kilometers, and route identity. Add a catalog-change marker where historical evidence proves a fingerprint change.

Caption: **Accumulated recorded movement; missing hours add no estimated distance.**

## Panel 3: When does each route find its rhythm?

**Form:** Grouped bar chart with categorical local-hour labels `00` through `23`.  
**X:** Local hour of day in the city's named IANA zone.  
**Y:** Average recorded kilometers per contributing UTC hour.  
**Series:** Selected routes, using their consistent route colors.

For a route and local-hour bucket:

```text
mean_recorded_km_per_hour = sum(distance_meters_sum) / contributing_utc_hour_count / 1000
```

Group the same comparable UTC-hour population used by the other route charts into local-hour buckets. The current 057 `typical_local_hours` recipe provides publication cadence; add an analogous distance projection from eligible hourly rows. This requires query adaptation, not another stored table.

Preserve the UTC key, local date, hour, and offset in query evidence. Both UTC occurrences of a repeated autumn local hour contribute independently. A skipped spring hour has no fabricated observation. Empty buckets remain unavailable rather than zero.

Hover shows route, local hour, average recorded distance, number of contributing UTC hours, and contributing dates. Label the graph **Observed pattern for the selected dates**; a short capture period is not an established typical service schedule. No weekday/weekend control is required in the first version.

## Panel 4: Which routes turn movement into musical activity?

**Form:** XY bubble chart; one point per selected route and compatible history segment.  
**X:** Kilometers per observed route-vehicle-hour.  
**Y:** Published tone opportunities per complete contributing hour.  
**Bubble size:** Sample-weighted mean observed vehicles.

Use summed numerators and their actual denominators over comparable hours:

```text
x = sum(distance_meters_sum) / sum(distinct_active_vehicle_count) / 1000
y = sum(crossings_published_count) / contributing_utc_hour_count
size = sum(active_vehicle_count_sum) / sum(valid_active_sample_count)
```

Do not average displayed hourly ratios. Omit points with zero population denominators or no accepted movement intervals; explain the omission in the shared context. Known zero published opportunities can plot at zero. Use a common size scale across routes, bounded to prevent one point obscuring the graph.

Hover shows route, observed movement, publication cadence, mean observed vehicles, contributing hours, exact route-vehicle-hour denominator, and accepted movement interval count. Use direct route labels where they fit and otherwise a short route legend.

Caption: **Recorded movement and published musical opportunities; the relationship does not establish causation.**

Movement is observed along-route distance, not trip length or vehicle speed. Route-vehicle-hours are summed exact hourly route populations; transferring vehicles can contribute to several routes. They do not establish city or period vehicle uniqueness. Published opportunities do not measure notes heard by listeners.

## Coverage strip and comparison rules

The strip uses a state timeline with one row per selected route and explicit UTC hour bounds. Show Complete, Partial, NoData, Conflict, and Missing as separate labeled states. Render Missing explicitly as a neutral state from the expected grid; do not depend on a null alone to explain it. A Complete stored hour can still be excluded for an incompatible definition, a partial request boundary, or another comparison restriction; its tooltip must state whether it contributes and why.

Show each route's own coverage, including Complete hours not shared with the other selected routes. This makes the cost of requiring comparable hours visible. Hover includes bounded incomplete reasons, fingerprint/catalog-change evidence, and definition/cadence compatibility. Pair state colors with labels so color is not the only cue.

Metric eligibility follows feature 057:

- Only fully contained Complete, nonconflicting, supported-definition hours contribute.
- Any retained route's quarantine excludes the whole city/hour cohort, using the contract's conservative conflict check before route filtering.
- Route completeness comes from capture continuity, intact populations, and atomic city/hour retention. There is no route-minute table or category-style reconciliation against 60 durable minutes.
- Partial and NoData remain diagnostics; absence remains Missing. Never extrapolate partial hours or prorate request boundaries.
- Every comparative graph uses hours where all explicitly selected routes are eligible under the same definition and cadence policy.
- Separate incompatible cadence policies. If several remain, require narrowing the period for the initial prototype rather than combining policies into one ranking or scatter summary.
- Treat each route's own catalog fingerprint as part of its comparison identity. Fingerprints differ between routes and must not be equated across routes.

### Route changes across the selected period

Fingerprint changes create separate history segments. A comparison segment is an interval in which each selected route retains its own fingerprint and compatible metadata. Do not merge a reused route key across service/geometry changes into one period mean or silently bridge its curve.

For the first prototype, when several compatible segments exist, add a conditional **Route history segment** selector, defaulting to the most recent segment with common contributing hours. Label its actual bounds; keep excluded intervals visible in coverage. Accumulation restarts for the selected segment. This control is absent when there is only one segment.

A fingerprint change visible after a capture gap has an unknown exact change time. Mark the observed transition interval and explain the uncertainty. An entirely missing historical hour has unknown membership; the current catalog cannot prove which routes were present then.

No common contributing hours yields an unavailable comparison with a visible coverage explanation. Users can choose fewer routes or another period; the implementation must not silently relax eligibility.

## Query adapters

Use `public.city_route_hour_statistics`, pinned to `observed-city-route-hour-statistics-v1`, and derive Grafana queries from [route-hour-insights.sql](../specs/057-hourly-route-history/contracts/route-hour-insights.sql).

The existing recipe returns one JSON report with related result sets. Grafana panels need flat projections; reuse its hour grid, city-wide conflict detection, historical identity, compatibility, and evidence CTEs rather than passing the whole JSON document directly to a chart.

Add shared selection/segment and matched-hour CTEs before chart aggregation. The existing `periods` set computes each route using its individually eligible hours; it is not sufficient on its own for comparisons over common hours.

| Projection | Essential output fields |
| --- | --- |
| Distance ranking | route key/label, recorded km, selected share, comparable/requested hours, contributing bounds |
| Cumulative curves | UTC time, route series label, cumulative km, hourly km, segment identity |
| Local-hour distance | local hour label, route series, mean recorded km, contributing UTC-hour count, zone |
| Bubble plot | route/segment label, movement ratio, cadence ratio, mean activity, raw denominators and contributing bounds |
| Coverage | hour start/end, route label, effective coverage, contributes flag, exclusion explanation and provenance |

Keep SQL selectors safely quoted and preserve route-key case. Translate contract inputs to reviewed Grafana PostgreSQL macros and escaped variables using the existing generator's conventions; never concatenate raw URL values into SQL. Pin supported definition and the configured zone server-side/in reviewed configuration. Bound requests and fail clearly rather than silently truncating history.

Start with at most five routes and a maximum 31-day panel range. Longer requests display **Choose a range of 31 days or less for route comparisons**. The 31-day limit is a prototype query budget, not a feature 057 retention policy. No automatic refresh is necessary.

Each panel should obtain its measures and accompanying coverage counts in one statement snapshot. Independent Grafana panels can execute at different moments; label the dashboard as an exploration view rather than a snapshot-consistent report. For reproducible verification, execute the complete underlying report in one read-only snapshot.

Use a shared query-building helper for these projections so coverage predicates cannot drift among panels. Do not add a public endpoint, materialized view, new collector, backfill, or new database schema for this dashboard feature.

## Visual conventions

- Keep the existing dashboard title, UID, UTC range, source selection, and category overview.
- Use a consistent color per selected route across all four graphs. Reserve coverage colors for collection states; do not reuse category colors as route identity when comparing several routes of the same type.
- Keep route labels short; reveal canonical keys and metadata in hover details.
- Include units on axes and avoid fleet-size, ridership, speed, completed-trip, or audible-note claims.
- Do not connect nulls, use legend averages to summarize weighted measures, or invent values for unavailable panels.
- Use standard Grafana bar, time-series, XY, and state-timeline panels. No custom panel plugin is required by this design.

## Post-deployment preflight

Before enabling route targets in the saved prototype:

1. Confirm feature 057 schema and code are deployed, and inspect the actual table/columns/definition through the selected Grafana PostgreSQL source.
2. Verify that source can SELECT the new table using its existing approved configuration. Diagnose access separately from schema or collection; this dashboard task does not change credentials, grants, or capture settings.
3. Confirm that the existing historical collection controls are enabled for the intended cities with persistence active, and distinguish disabled collection, dry run, empty history, and failed retention.
4. Discover retained canonical route keys, names, categories, fingerprints, cadence policies, and actual capture bounds in a finite range.
5. Verify complete contributing route hours and conflict/missing evidence against the deployed 057 query contract. Code or migration presence alone does not prove captured history.
6. Confirm the supported Grafana panel options on the deployed version and reuse the existing source UID only after verification.

If the schema or retained history is unavailable, leave the route section explicitly pending and its observation queries disabled. Existing populated category insights remain available. Do not apply migrations, enable capture, seed data, or reconstruct routes from category history as part of implementing these panels.

## Implementation after preflight

1. Extend `observability/grafana/generate_city_category_insights.py` with reusable route evidence/selection helpers and the five flat graph/coverage projections.
2. Add the route selector, optional segment selector, shared context, and six-panel section to the generated classic dashboard JSON.
3. Validate the generated configuration against the deployed Grafana version. Preserve modern XY field-mapping version markers and use explicit data shapes for categorical bars and coverage states.
4. Execute bounded panel SQL for one route, several routes, and a city with several transit types; compare results with the authoritative report before publishing.
5. Update the existing prototype using fresh saved metadata/resource version and its current folder placement. Preserve the dashboard UID and existing URL selections.
6. Render and inspect the full dashboard and individual panels; correct empty charts, labels, color drift, bubble sizing, time boundaries, and gap handling.
7. Update `observability/grafana/insights/README.md` with verified data availability, query semantics, source paths, and the actual review results.

Deployment of feature 057 is a dependency, not an action authorized by this design document. Leave implementation changes for normal review and do not create commits on the user's behalf.

## Acceptance criteria

- The new section answers the four route questions with visible units, selections, contributing coverage, and historical identity.
- Ranking totals agree with sums of eligible hourly meters over the same common hours. Shares refer only to selected routes and are null when total distance is zero.
- Curves retain gaps, resume from recorded totals only, and restart across selected history segments. Their final recorded totals agree with the ranking for the same selections.
- Local-hour distance uses summed meters divided by contributing UTC hours. Repeated daylight-saving hours contribute independently; skipped/empty buckets are unavailable.
- Bubble ratios use summed numerators/denominators, correct route-vehicle-hour units, common point-size scaling, and disclosed omissions for unavailable movement.
- Coverage exposes Missing, Partial, NoData, Conflict, incompatible definitions, request-boundary exclusions, and complete hours excluded by comparison selection.
- City-wide quarantine is checked before narrowing to selected routes. A conflict in an unselected retained route still excludes that city/hour.
- Removed and case-distinct route keys remain selectable. Labels, category changes, and fingerprints do not silently collapse historical identities.
- Empty ranges, zero-activity routes, no common hours, more than five routes, and requests beyond the prototype range budget have clear states without fabricated zeros or silent truncation.
- At least two deployed cities are visually reviewed when available, including a city with multiple route categories. A missing second city's data is disclosed rather than fabricated.
- Query execution, saved configuration, and rendered charts are verified after deployment. This design document claims none of those checks has passed.

## References

- [Hourly route history specification](../specs/057-hourly-route-history/spec.md)
- [Hourly route history implementation plan](../specs/057-hourly-route-history/plan.md)
- [Hourly route history source design](HOURLY_ROUTE_HISTORY_DESIGN_DOCUMENT.md)
- [Observed route-hour statistics contract](../specs/057-hourly-route-history/contracts/observed-route-hour-statistics-v1.md)
- [Authoritative route-hour SQL](../specs/057-hourly-route-history/contracts/route-hour-insights.sql)
- [Current Grafana prototype](../observability/grafana/insights/README.md)
