# TransitJazz: The City's Rhythm prototype

[Open the dashboard](https://gallantpuffin3113.grafana.net/d/transitjazz-category-insights-lab/?from=now-7d&to=now&timezone=utc&var-city=atlanta&var-category=%24__all).

Dashboard UID: `transitjazz-category-insights-lab`.
The existing prototype was rebuilt on 2026-10-10 around cohort distance and
musical activity. It replaces the former statistics tables and operational
definitions with eleven insight panels, preserving the dashboard UID and URL.

## Current data source

The saved PostgreSQL source is `cg086lcj6z6yob`
(`grafana-postgresql-datasource`). On 2026-10-10, Grafana's health
check returned **Database Connection OK**. A query through that source confirmed
database `transitjazz` and both category statistics tables. Bounded
observation queries returned actual captured history.

Credentials stay in the existing Grafana data-source configuration. This
redesign does not change data sources, grants, capture settings, or migrations.
The source UID is pinned; database queries execute server-side.

## The dashboard story

| Panel | Insight |
| --- | --- |
| How far did the ensemble move? | Accepted along-route kilometers recorded across all selected cohorts in comparable hours |
| When did it cover the most ground? | The verified UTC hour with the largest recorded cohort distance |
| Who carries the movement? | The cohort with the largest share of recorded distance |
| How much of the story can we see? | Matched verified hours / fully contained requested UTC hours |
| City Score: The distance adds up | Stacked cumulative recorded distance by cohort |
| Distance in motion | Stacked distance recorded in each verified hour |
| Who carries each hour's movement? | Each cohort's percentage of that hour's recorded distance |
| Movement & Music | Observed kilometers per vehicle-hour versus tone opportunities; bubble size represents average observed vehicles |
| Share of the recorded journey | Cohort shares of recorded period distance |
| When the music gets dense | Published tone opportunities over time |
| Introduction | City context and the meaning of observed distance and gaps |

The introduction plus four headline cards and six charts make eleven panels.
City and transit ensemble control the category overview. Routes and Route
history segment control the new route section. The supported
definition remains fixed in SQL, rather than exposed as a product control.
All transit types are selected initially. The default range is the last seven
days, UTC, with automatic refresh disabled.

## Distance and coverage rules

Every metric starts with the checked-in `hourly-insights.sql` recipe.
It verifies an intact distinct population, 60 durable complete nonconflicting
backing minutes, identical definition and cadence policy, and equality of all
nine additive totals.

Comparisons use only UTC hours verified for **every selected category under
the same cadence policy**. This makes the total, distance shares, curves, and
scatter observations comparable. Selecting another ensemble changes this set
of hours. Unsupported definitions are never interpreted as v1.

Distance is the sum of accepted absolute changes in snapped along-route meters
for fresh vehicle observations. It is recorded movement, rather than a
reconstruction of every vehicle's full journey. Feed timing, geometry, and
observation eligibility can differ between cohorts.

- Period kilometers sum accepted meters across comparable hours, then divide
  by 1,000. They are cohort totals, not unique vehicles or average trip length.
- Cumulative distance restarts at the selected period's first comparable hour.
  Missing hours plot as null. When observations resume, the curve resumes the
  sum of recorded hours only; missing movement is never filled or interpolated.
- Hourly distance is accepted kilometers recorded during that UTC hour.
  It is not individual vehicle speed.
- Hourly percentages divide cohort distance by the selected cohorts' recorded
  distance in that hour. A zero total has no percentage distribution.
- The donut uses summed recorded distance over the same matched hours.
  Zero-distance types have no slice.
- Scatter movement divides accepted meters by the exact distinct population
  for each hour, then converts to kilometers. Hours with no accepted movement
  intervals or no observed vehicles are omitted.
- Tone opportunities count crossing records from successfully published
  batches. They do not establish listener playback.
- The hour grid retains missing observations. Only fully contained hours
  contribute; partial selection boundaries are omitted.
- Cadence policies form separate series. Headlines and the combined donut
  are unavailable if multiple policies occur, avoiding mixed-policy measures.
- Independent panels do not share a database snapshot.

Source contract:
[observed category statistics v1](../../../specs/056-city-transit-type-insights/contracts/observed-category-statistics-v1.md).

## Regenerate and publish

The existing Python generator remains the source for the classic dashboard JSON:

```powershell
python observability/grafana/generate_city_category_insights.py --datasource-uid cg086lcj6z6yob --route-history-available --default-route 1 --default-route 10 --default-route 104
```

It writes `observability/grafana/dashboards/transitjazz-city-category-insights-prototype.json`
and does not connect or publish. Omitting the source UID or supplying
`--schema-unavailable` keeps observation targets disabled.
Omitting `--route-history-available` keeps just the route section pending,
including its observation targets and selectors. The generator never discovers
or enables collection. Initial route keys must be verified against the latest
Complete cohort before passing `--default-route` (up to three).

Use the current saved dashboard metadata and resource version when wrapping
the classic JSON as `dashboard.grafana.app/v1beta1` for an update.
Retain the target's current folder annotation. Write manifests as UTF-8 without
a BOM for gcx. Do not replace the operational dashboards.

The XY panel marks its modern mapping schema with `pluginVersion: 13.3.0`.
Without a version marker, Grafana applies the legacy XY migration and turns
field matcher objects into invalid field names. The donut uses Rows to fields
so category color overrides match its numeric fields. Bus, rail, and streetcar
colors are consistent across the charts.

## Review performed on 2026-10-10

- Confirmed the active `transitjazz` Grafana context, source health,
  and both deployed category relations.
- Ran every panel query through Grafana's PostgreSQL source over a bounded
  seven-day range with Atlanta bus and rail selections.
- Validated the generated manifest and updated the existing dashboard.
- Retrieved the saved dashboard and confirmed its title and panel replacement.
- Rendered and inspected the complete Atlanta dashboard. Corrected the XY
  migration issue and the donut's category color mapping.
- Rendered and inspected the distance-focused Atlanta and Toronto dashboards,
  including Toronto's streetcar series and distinct bubble colors.
- Kept the user's UTC range and city/category URL selectors.
- No database or application configuration changes and no commits were made.

## Routes behind the rhythm (2026-10-10)

The saved prototype now has **18 panels**: the eleven category panels and
seven route panels. Route projections live in
[`route_insights.py`](../route_insights.py) and are called by the existing
generator. The first evidence CTEs are read directly from the authoritative
feature 057 SQL, retaining its hour grid, historical identities, and city-wide
quarantine before selecting routes.

| Route panel | Measure |
| --- | --- |
| Routes behind the rhythm | Selected count, shared / requested whole hours, contributing UTC bounds, named local zone, omitted bubbles, and unavailable explanations |
| Which selected routes carry the distance? | Accepted kilometers over common hours; share among selected routes only |
| Watch the routes accumulate distance | Unstacked stepped running sums, computed before joining the expected grid; gaps stay null |
| How much of each route can we see? | Each route's own Complete, Partial, NoData, Conflict, and explicit Missing hours |
| When does each route find its rhythm? | Local-hour accepted kilometers / contributing UTC hours, including both occurrences of repeated DST hours |
| Which routes turn movement into musical activity? | Summed meters / summed exact route populations; published opportunities / shared hours; sample-weighted observed vehicles set bubble size |
| Route distance · Per-vehicle movement vs hourly route distance | Average km / observed route-vehicle-hour vs average total route km / observed in-service hour; bubble size is the sample-weighted mean observed vehicles |

The city-scoped canonical route keys retain their case. Choices come from
retained history intersecting the range, including removed routes, with
collision-safe labels. Custom values allow a historical key to remain an
explicit selection when it is absent from discovery. The saved initial Atlanta
keys are `1`, `10`, `104`, ordered by name/key from the latest Complete cohort.
They are starting choices, not movement leaders. Route slot symbols and colors
agree across the four graphs.

All comparisons require fully contained, supported-definition Complete hours
for every selected route under one cadence policy. Partial hours and boundary
fragments are diagnostic only. There is no route-minute reconciliation.
Any retained route conflict quarantines its whole city/hour, even when that
route is unselected. Multiple matched cadence policies require narrowing the
range. Requests over 31 days or with more than five keys produce a context
message and no observation grid; selections are never silently truncated.

History segments compare each route's own fingerprint, category, short name,
and static metadata in canonical route order. A change starts a new segment,
including when observed after a gap. The default uses the most recent segment
with shared hours; earlier segment choices appear when available. Accumulation
restarts within the selected segment, while coverage retains excluded hours.
Catalog-transition annotations show the observed interval and identify unknown
change timing across capture gaps. Missing hours cannot prove route membership.

The route-distance bubble chart averages only complete shared hours with at
least one observed route vehicle. This is an observed-service proxy, not proof
of the agency's scheduled service hours. It excludes complete zero-vehicle
hours while keeping in-service hours with zero movement in the averages.
Bubble size uses the sample-weighted mean observed vehicle count over those
same hours.

Route vehicle-hours sum exact hourly route populations and do not establish
city or period vehicle uniqueness. Published opportunities do not establish
notes heard. Zero movement stays zero; zero totals have null shares and zero
population/accepted-interval denominators omit bubbles. Each panel obtains its
measures and coverage in one SQL statement, but the dashboard panels do not
share one transaction snapshot.

### Verified source and review

The existing source `cg086lcj6z6yob` could SELECT
`transitjazz.public.city_route_hour_statistics` on Grafana
`13.3.0-36917460899`. The transitjazz-db helper first inspected all 38 deployed
columns and both route indexes in a read-only Repeatable Read transaction.
The seven-day discovery range `[2026-10-03T19:00:00Z, 2026-10-10T19:00:00Z)`
contained the supported `observed-city-route-hour-statistics-v1` definition,
30-second cadence, Partial startup rows at 17:00 UTC and Complete rows at
18:00 UTC for all seven retained cities. This is short future capture, not
seven days of route history. Actual retained rows establish persistence;
current application enablement settings were not independently inspected.

All route projections and discovery/segment SQL were executed through the
Grafana source for Atlanta and Toronto, including Toronto bus/rail/streetcar
selections. A separate read-only Repeatable Read comparison of the adapter
and authoritative report over `[2026-10-10T17:00:00Z, 2026-10-10T20:00:00Z)`
returned matching totals:

| City / selected keys | Shared hours | Authoritative km | Adapter km |
| --- | ---: | ---: | ---: |
| Atlanta / 1, 10, 104 | 1 | 89.596420232 | 89.596420232 |
| Toronto / 1, 96, 501 | 1 | 523.296900919 | 523.296900919 |

The three-hour comparison includes Partial startup and Missing hours; only
the Complete hour contributes. Context queries were also executed for empty
selections, absent route keys, six routes, and 32-day ranges. Saved manifests
passed server validation, were updated using fresh resource versions and the
existing folder placement, and were rendered for Atlanta (three routes) and
Toronto (streetcar route 501). Review corrected selector SQL, coverage labels,
local-hour units, context sizing, and consistent route colors. Snapshots are
local temporary review artifacts, not committed images.

### Native selector and tooltip limitations

Classic Grafana variables cannot conditionally hide a selector or run a
city-change reset hook. The segment selector therefore remains visible with
one Latest option when no extra segments exist. After changing cities, review
the route selection: a key existing in both cities can remain selected and
will refer to the newly selected city. This differs from the design's automatic
city-change clearing. Explicit selections otherwise use native Grafana
variable retention, including custom historical keys; there is no custom UI
script or plugin.

The coverage SQL retains contribution reasons, incomplete reasons,
fingerprints, definitions, and cadence metadata. The daily-profile SQL retains
bucket counts and contributing dates. Grafana's matrix transformation renders
the daily chart as one numeric field per route; those extra bucket metadata
columns are available in query results rather than its chart tooltip. Inspect
the panel's query data for full evidence. The context makes unavailable
comparisons and omitted bubbles visible beside the graphs.
