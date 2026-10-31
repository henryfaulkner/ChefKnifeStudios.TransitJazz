# Client Contract: Statistics Display Units v1

This document versions the client presentation contract only. It does not change the Stats API contract/version. References: [spec](../spec.md), [data model](../data-model.md).

## Pure conversion and projection

`StatsUnitSystem` contains Metric (0) and Imperial (1). Unsupported values normalize to Metric. `StatsDisplayUnits` accepts units explicitly and performs nullable decimal conversion:

```text
Metric   = source metric value
Imperial = source metric value / 1.609344m
```

No conversion helper reads storage/culture, mutates DTOs, rounds numbers or accepts already projected values. Chart double materialization occurs after decimal conversion. The same metric snapshot can be rebuilt arbitrarily many times without drift. Existing number precision/English formatting remains final-output behavior.

`StatsOverlayProjection.Build(response, localize, routeShapes?, isDark?, units = Metric)` produces all converted values and resolved labels as one presentation and preserves `ReferenceEquals(presentation.Snapshot, response)`. Previous-day local source selection does not replace that canonical reference. Daily fallback and Last24 cards select their established windows independently.

Every distance-bearing value listed in data-model.md converts exactly once; server-supplied X/Y rates are scaled directly. Counts, percentages, radius/area, hours, sample denominators/numerators, times, coverage and exclusions do not change. Missing evidence and plotted gaps remain missing. Metric defaults retain compatibility for existing callers.

## Unit-bearing surfaces

| Surface | Metric / Imperial |
| --- | --- |
| Distance cards | kilometers / miles |
| Compact distance heading, table context and pie dataset | km / mi |
| Timeline Y axis | Cumulative distance (km) / Cumulative distance (mi) |
| Bubble X axis/tooltip | Average vehicle speed (km/h) / Average vehicle speed (mph) |
| Bubble Y axis/tooltip | Average distance per hour (km/h) / Average distance per hour (mi/h) |

All strings/templates are resource-backed in the existing neutral RouteFilterResources.resx. Keep existing metric keys needed elsewhere. Canvas, tooltip and accessible-table numbers/labels agree; tables identify units without relying on canvas. The fleet activity rate must not be relabeled as individual vehicle speed. Affected client properties have neutral distance names; shared wire fields retain kilometer names.

PieChartConfig consumes projected dataset label. SeriesChartConfig consumes projected unit context/template rather than its existing hardcoded km suffix. Bubble renderer consumes projected distance heading and axes. Renderers/configuration do no further conversion or settings reads.

## Preference persistence

- Add hidden observable `Settings.StatsUnits`, default Metric, under existing settings storage with version 5 unchanged.
- Load at initialization and actual overlay opening; missing/undefined values and failed reads use Metric. Numeric undefined values must not reset other valid settings.
- Persist through `ISettingsService.SetSettingValue(nameof(Settings.StatsUnits), choice)`, whose latest-settings read preserves unrelated values. No cached whole-settings write.
- Apply selected units before attempting persistence. Failed write logs and retains active selection for the current opening, including city/route/scope changes. Next opening/reload may use the last saved value.
- Active-option selection is a no-op including no persistence. No general settings-blade duplicate, new storage API/package/key, cross-tab synchronization or global event bus.
- Existing malformed whole-settings fallback remains service behavior; this feature does not redesign it. Catch preference I/O failures narrowly so Stats remains usable.

## Interaction and lifecycle

The labeled native two-option radio group sits first in scrollable Stats main, above previous-day notice and charts, outside loading/error/data branches. It wraps normally, has an accessible group name, native keyboard behavior, visible focus, non-color selection cues and >=44px targets. Styles use both established palettes/transitions.

A changed choice updates overlay units, calls existing local RefreshPresentation/TryReplacePresentation, then saves. It never starts a session, requests a snapshot, enters route debounce, cancels requests, changes generations, resets timers, clears errors/retry state, marks stale data fresh or introduces loading placeholders. Newly arriving data uses units current at acceptance.

Keep radio/chart/frame instances and dataset keys; do not add a units-based component key. Existing open-table state and hidden-series choices survive. ChartCanvasHost already reapplies native visibility after applying latest datasets/options; use that seam and verify in browser.

## API invariance

No new request parameter/header, changed Stats request/response field, duplicated Imperial evidence, changed contract version, changed server cache identity or payload bytes. No Shared/server/SQL/worker/database/map/audio conversion changes. A unit toggle uses only already accepted evidence, including stale snapshots.

## Required verification

Cover spec FR-001..015 and SC-001..006 using semantic xUnit fixtures plus real browser acceptance. Exact anchors include 1.609344 km -> 1 mi, 16.09344 km/h -> 10 mph, null/zero and display-only rounding. One fixture spans all surfaces and unchanged evidence; round trips preserve original reference and values. Add fallback and All missing-bucket cases, configured-serializer legacy/invalid/round-trip checks, unrelated preference preservation and failed write behavior. Browser proves focus, keyboard/touch, themes/narrow layout, reload/reopen, scopes, open tables, tooltip/legend retention, in-flight and stale/error behavior and zero unit-triggered requests with normal refresh cadence.
