# Design Document: Metric and Imperial Statistics

**Status:** Proposed; design only.  
**Date:** 2026-10-30  
**Related:** [Statistics design](STATS_OVERLAY_DESIGN_DOCUMENT.md), [reported vehicles design](REPORTED_VEHICLES_DESIGN_DOCUMENT.md), [current project plan](../specs/059-reported-vehicles/plan.md)

## Decision

Add a **Units: Metric / Imperial** selector to the statistics overlay. Convert distances and distance-based rates entirely in the Blazor WebAssembly client, using the already accepted API snapshot. The existing API remains metric and its request, response, contract version, cache keys, and payload size remain unchanged.

Use kilometers and kilometers per hour for Metric, miles and miles per hour for Imperial. Default to Metric to preserve the current presentation. Persist the visitor's explicit choice in the existing browser settings, shared across cities and remembered after closing the overlay or reloading the page.

This feature changes measurement units, independently of language, number formatting, city selection, and transit data collection. Continue the statistics feature's existing English formatting. Map distances, route geometry, audio timing, database records, and worker calculations remain outside this feature's scope.

## Existing implementation

Relevant paths below are relative to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/` unless otherwise stated.

| Location | Current behavior and implication |
| --- | --- |
| `src/ChefKnifeStudios.TransitJazz.Shared/Stats/StatsOverlayResponse.cs` | Contains metric distance summaries, category distances, bubble coordinates/rates and evidence, timeline distances, and the previous-day fallback. All inputs for conversion already exist. |
| `Components/Stats/StatsOverlayProjection.cs` | Builds immutable chart models and tooltip evidence. This is the conversion boundary. It retains the original response as `StatsOverlayPresentation.Snapshot`. |
| `Components/Stats/StatsOverlay.razor` | Reads card distances directly from the metric snapshot and supplies chart titles. Cards must instead consume converted presentation values. |
| `Components/Stats/StatsOverlayRequestPolicy.cs` | `StatsOverlayAcceptedSnapshotState.TryReplacePresentation` permits rebuilding the presentation for the same snapshot reference while preserving stale state. Reuse this path. |
| `Components/Charts/{SeriesChart,BubbleChart,PieChart}.razor` | Render accessible tables as well as charts. Some table labels resolve metric resources directly. These labels must follow the selected units. |
| `Components/Charts/PieChartConfig.cs` | Hardcodes the dataset label `Observed distance (km)`. Replace it with resource-backed presentation text. |
| `Services/StatsChartText.cs` | Resolves English resources and formats numbers. Retain its existing formatting precision. |
| `Models/Settings.cs`, `Services/SettingsService.cs` | Persist settings using Blazored.LocalStorage. The settings version check discards mismatched versions, so an additive preference should preserve the current version. |

The chart models currently contain properties explicitly named `Kilometers`. Putting miles into those properties would make the model contract misleading. Rename the affected client chart fields to unit-neutral names as part of implementation; retain every metric field name in shared API DTOs.

## Visitor experience

Place a labeled, two-option native radio group at the top of the scrollable statistics content, above the charts and any previous-day notice. Keep it available while loading or showing an error. Use a normal wrapping row so it does not compete with the existing fixed city/All control or fixed route accordion.

```text
Units   (o) Metric   ( ) Imperial
```

Selecting an option immediately updates all visible statistics, including open data tables and chart tooltips. Selecting the already active option does nothing. With no snapshot available, retain the choice and apply it when a response is accepted.

The selector applies equally to current-city, selected-route, All-cities, and previous-day presentations. City or route changes retain the units. The previous-day distance card and charts convert along with the last-24-completed-hours card; the existing window rules continue to determine which source values appear.

Use native keyboard radio behavior, an accessible group name, visible focus indicators, and at least 44-pixel tap targets. Show the selected state through the radio/shape as well as color. Provide light and dark styles using the existing theme palette and transitions. Keep focus on the selected input during updates; do not remount the overlay or its charts with a units-based component key.

## Conversion contract

Use the exact relationship `1 mile = 1.609344 kilometers`:

```text
Metric:   displayed value = metric value
Imperial: displayed value = metric value / 1.609344
```

Add a small client-only `StatsDisplayUnits` helper and a two-value `StatsUnitSystem` enum. The helper takes the chosen system explicitly; it must not read storage or culture. Use decimal division for decimal DTO values, then convert to double where chart models require it. Keep full precision through projection and chart configuration, and round only in the existing number formatter. Null stays null and zero stays zero.

Always build from the original metric snapshot. Never convert an existing projected model, mutate a response, or convert rounded strings. Repeated toggling therefore cannot accumulate conversion error.

| Source value | Presentation behavior |
| --- | --- |
| `TodayDistance.Kilometers`, `Last24Distance.Kilometers`, `PreviousDay.Distance.Kilometers` | Convert the value selected for each card; label with `kilometers` / `miles`. |
| Category-share `Kilometers` | Convert table distances and pie dataset magnitudes. Retain supplied percentages and category keys; the common scale factor preserves slice proportions. |
| Bubble `DistanceKilometers` | Convert distance evidence in the accessible table. |
| Bubble `X` | Convert the server-computed average vehicle speed; label axis/tooltips `Average vehicle speed (km/h)` / `(mph)`. |
| Bubble `Y` | Convert the server-computed aggregate distance per active clock hour; label `Average distance per hour (km/h)` / `(mi/h)`. This remains a fleet activity rate, not individual vehicle speed. |
| Timeline `PlotCumulativeKilometers` | Convert plotted Y values, including gaps preserved as null. |
| Timeline `CumulativeObservedKilometers`, `HourlyKnownKilometers` | Convert known values, point evidence, tooltip lines, and table cells. |
| Vehicle counts, mean active vehicles, bubble radius, category percentages | Preserve unchanged. |
| Route-vehicle hours, active-clock hours, elapsed-hour coordinates, sample counts, timestamps, coverage and exclusion details | Preserve unchanged. |

The server defines bubble `X` as distance divided by route-vehicle hours and `Y` as distance divided by active-clock hours. Scale its returned ratios directly; do not recompute them from rounded distances or change their denominator semantics. Bubble positions change scale while marker areas and relative ordering remain the same.

Use `km` / `mi` in compact distance headings and axes. For example, the timeline Y axis becomes `Cumulative distance (km)` / `Cumulative distance (mi)`. Cards can retain full unit names. Include units in table headings or captions so numeric evidence remains understandable when read without the canvas.

## Client design

### Preference ownership

Add `StatsUnitSystem StatsUnits = StatsUnitSystem.Metric` to `Models/Settings.cs`, following its observable-property convention and marking the generated property with `HiddenSetting`. The selector lives in statistics; the reflection-driven settings blade should not add another control.

Keep `Settings.CurrentVersion` at its current value, 5, for this additive field. Existing version-5 JSON omits the field and should deserialize to Metric. Verify that behavior using the configured serializer rather than relying on an assumed enum representation. Normalize undefined enum values to Metric without discarding unrelated settings.

Load the choice on initialization and whenever the overlay opens. On change, update the overlay's in-memory units, reproject immediately, then use `ISettingsService.SetSettingValue` to persist only this property against the latest stored settings. A storage write failure must leave the selected units working for the current overlay session; reopening or reloading may fall back to the last persisted choice. Scope any exception handling to preference I/O and follow the existing logging convention. No new storage package, settings API, cross-tab synchronization, or application-wide event bus is required.

### Projection ownership

Extend `StatsOverlayProjection.Build` with an optional `StatsUnitSystem` argument defaulting to Metric. All metric fields enter the conversion helper exactly once. Resolve unit labels and tooltip templates during projection using the existing resource lookup delegate.

Extend `StatsOverlayPresentation` with the selected units, converted daily and last-24 distance values, and the unit-dependent labels needed by cards and charts. Keep `DailyDistance` as canonical evidence for existing coverage consumers, but render card numbers from the new presentation properties. Preserve `Snapshot` as the exact original response reference, including when building the previous-day presentation.

Rename only the affected client chart fields and methods to reflect displayed values:

| Current client name | Proposed name |
| --- | --- |
| `PieChartRow.Kilometers`, `PieChartSlice.Kilometers` | `Distance` |
| `PieChartModel.GetTableKilometers()` | `GetTableDistances()` |
| `BubbleChartRow.DistanceKilometers`, `PlottedBubblePoint.DistanceKilometers` | `Distance` |
| `ChartPointEvidence.HourlyKnownKilometers` | `HourlyKnownDistance` |
| `ChartPointEvidence.KnownCumulativeKilometers` | `KnownCumulativeDistance` |

Keep already neutral names such as `ChartSeries.Values`, `KnownValues`, and `CombinedPlottedSubtotal`. Carry localized distance/dataset labels in the affected chart models, with Metric-compatible defaults for existing callers. Series already carries axis titles; make its table distance headings consistent with that unit context. Renderers consume these presentation labels and values without doing additional conversion or reading settings. Update constructor callers and associated tests with these client-only model changes.

This keeps one converted representation for the canvas, tooltip evidence, and accessible table. Remove direct metric label lookups and inline unit text from the affected rendering paths, including pie dataset metadata and bubble tooltip strings.

### Reprojection and request lifecycle

Use `RefreshPresentation()` and `TryReplacePresentation()` for unit changes, as the overlay already does for theme or route-metadata changes. Pass the current unit system from `BuildPresentation(snapshot)` for every accepted response and every rebuild.

A unit change must not call `StartNewSessionAsync`, `RequestSnapshotAsync`, or the route-selection debounce path. It must not increment request generations, cancel in-flight requests, reset refresh timers, clear errors, or mark stale data fresh. If a request completes after the visitor changes units, build its presentation using the current choice at acceptance time.

Preserve series keys and chart component instances so existing legend visibility and open-table state survive a unit change. Existing chart configuration updates can consume the rebuilt models. Verify that native Chart.js dataset visibility also survives the update; if necessary, reapply the existing `StatsTimelineVisibilityState` after the configuration update rather than resetting it.

### Resources

Put all new visible and assistive text in the existing neutral `Resources/RouteFilterResources.resx`, consistent with the current English statistics feature. Add keys for the group/options, miles, compact distance units, unit-bearing axis headings, table captions, and tooltip templates. Keep existing metric keys for callers that still need them. Unit selection must not alter `CurrentCulture` or introduce another resource store.

## API and performance

No changes to `StatsOverlayRequest`, `StatsOverlayResponse`, endpoint parameters, headers, server caches, SQL, storage, or worker collection are needed. Do not add miles alongside kilometers to a response or send a units parameter. The same accepted response supports both views, including when stale data is visible after an error.

Reprojection adds linear work over the existing bounded chart evidence, with no additional network calls or dependencies. Chart updates continue through the existing host/configuration mechanism. Unit selection must not trigger loading placeholders for already available data.

## Implementation sequence

1. Add resource keys, the unit enum, the conversion helper, and the additive hidden settings preference.
2. Make affected client chart fields unit-neutral and carry labels in their presentation models; update existing callers.
3. Extend projection to convert every distance/rate and generate matching evidence and labels, including fallback data and totals.
4. Add the radio group, preference loading/persistence, and local presentation refresh in the statistics overlay.
5. Update chart renderers/configuration to use the projected labels and remove remaining metric-only presentation assumptions.
6. Complete focused automated checks and browser acceptance below.

## Verification and acceptance

Use the existing Client.Shared xUnit project and statistics fixtures; this design does not require a new test framework. Implement meaningful checks for:

- `1.609344 km -> 1 mi`, `16.09344 km/h -> 10 mph`, Metric identity, null preservation, zero preservation, and rounding only at display time.
- One fixture covering cards, category distances, both bubble rates, bubble evidence, timeline plotted/known/hourly values, and tooltip text. Assert matching converted values across model and table inputs, with unchanged counts, percentages, radii, coverage, and time coordinates.
- Metric -> Imperial -> Metric rebuilding from the same DTO, preserving its values and original snapshot reference. Include the city previous-day fallback and All-cities data with missing buckets.
- Legacy version-5 settings without the new field, saved choice round-trip, undefined enum fallback, preservation of other settings, and a failing preference write.
- Unit changes while a request is in flight and while a stale snapshot/error is visible: current units win when data arrives, stale/error state survives, and the unit action causes no Stats API request or timer reset.

Run the existing statistics projection, chart model, resource, session, and timeline visibility tests after updating client model names, then build the Blazor client. Shared/server DTO and payload fixtures should require no edits.

In browser acceptance, verify keyboard and touch selection, narrow-screen layout, both themes, reload/reopen persistence, route/city/All transitions, previous-day rendering, already-open tables, tooltips, and hidden timeline series. Inspect the network panel while toggling: **zero additional API requests**, with normal scheduled refresh continuing on its existing cadence. Verify each surface labels the units it actually displays.

The feature is complete when a single local choice switches every distance-bearing statistics surface consistently, while the API continues to return its existing metric snapshot unchanged.
