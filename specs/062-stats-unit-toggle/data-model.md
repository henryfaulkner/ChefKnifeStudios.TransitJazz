# Data Model: Statistics Display Units

This is a client presentation/settings model. There are no new API DTOs, database tables, migrations or collected records.

## Units preference

| Element | Contract |
| --- | --- |
| `Models.StatsUnitSystem` | Two values: `Metric = 0`, `Imperial = 1`; explicit stable values for persistence. |
| `Settings.StatsUnits` | Observable property initialized Metric, generated property marked HiddenSetting. |
| Settings version/key | Existing key and `CurrentVersion = 5`; omit-field legacy settings remain valid. |
| Validation | Missing or undefined enum value resolves to Metric without resetting unrelated settings. |
| Ownership | One browser visitor preference across cities/route scopes, independent of culture/language/theme. |

`StatsDisplayUnits` normalizes unsupported input and provides nullable decimal distance/rate conversion with explicit system. Metric returns input; Imperial divides by `1.609344m`. It has no storage/culture dependency. Decimal output is converted to double only where existing chart models require it; never round here.

## Accepted snapshot

Existing `StatsOverlayResponse` remains immutable canonical metric evidence. All kilometer-named DTO fields, bubble ratios, API version, window metadata and coverage remain unchanged. `StatsOverlayAcceptedSnapshotState` associates this exact snapshot reference with a replaceable presentation and independent stale flag. Same-snapshot reprojection changes only presentation.

## Statistics presentation

Extend existing `StatsOverlayPresentation` with selected units, nullable converted daily/Last24 distances and resource-resolved card/chart labels. Proposed descriptive property names may be adjusted to project conventions without changing semantics:

| Property/value | Semantics |
| --- | --- |
| `Snapshot` | Exact original accepted metric DTO reference, including previous-day views. |
| `UnitSystem` | Normalized system used for every value/label in this presentation. |
| `DailyDisplayDistance` | Converted TodayDistance or selected PreviousDay.Distance. |
| `Last24DisplayDistance` | Converted original Last24Distance, unaffected by fallback choice. |
| `DailyDistance` | Existing metric summary retained solely as canonical scope/coverage evidence. |
| Distance/card labels | Full `kilometers`/`miles`; compact `km`/`mi` for appropriate headings. |
| Bubble axis labels | Speed `km/h`/`mph`; fleet activity `km/h`/`mi/h`. |
| Chart models | Converted values and matching unit-bearing text; counts and other evidence unchanged. |

All presentation text comes from RouteFilterResources.resx via the existing lookup delegate. Carry text on affected models so renderers/config builders neither infer units from culture nor read settings. Retain resource-backed Metric-compatible defaults for non-Stats constructors/callers.

## Client-only chart changes

| Existing | Unit-neutral name |
| --- | --- |
| `PieChartRow.Kilometers`, `PieChartSlice.Kilometers` | `Distance` |
| `PieChartModel.GetTableKilometers()` | `GetTableDistances()` |
| `BubbleChartRow.DistanceKilometers`, `PlottedBubblePoint.DistanceKilometers` | `Distance` |
| `ChartPointEvidence.HourlyKnownKilometers` | `HourlyKnownDistance` |
| `ChartPointEvidence.KnownCumulativeKilometers` | `KnownCumulativeDistance` |

Keep already neutral `ChartSeries.Values`, `KnownValues`, `CombinedPlottedSubtotal`, bubble `X`/`Y` and all stable keys. Update constructor call sites/tests, including named arguments. Do not rename Shared DTO fields.

Affected model text includes distance table heading/caption, pie dataset label, timeline distance context and plotted-value tooltip template. Bubble axis titles may remain existing component parameters supplied from the presentation; its model carries distance-evidence/dataset text. Series already has YAxisTitle; use matching distance context for hourly/known/subtotal table evidence and canvas tooltips.

## Conversion mapping and invariants

| Metric input | Projected output |
| --- | --- |
| Today/PreviousDay/Last24 summary Kilometers | Selected card values. |
| Category Kilometers | Pie slice magnitude and table distance. |
| Bubble X/Y/DistanceKilometers | Converted supplied ratios and distance evidence. |
| Timeline PlotCumulativeKilometers | Plotted values, preserving null gaps. |
| Timeline CumulativeObservedKilometers/HourlyKnownKilometers | Known values, point evidence and table/tooltip values. |
| Combined plotted subtotal | Derived only from converted source evidence, scaled exactly once. |

Do not change percentages/category keys, counts/mean vehicles/radius, route-vehicle hours, active-clock hours, sample counts, X time coordinates, timestamps, coverage/exclusion data or series order. Null remains null, zero remains zero. Validation remains finite/nonnegative where existing chart contracts require it. Table and canvas consume the same values; text formatting is the final step.

## State transitions

1. Initialization/open: read saved choice and normalize; read failure selects Metric for that opening. Subscribe/open lifecycle retains existing behavior.
2. Different choice: update in-memory units -> same-snapshot reproject (if available) -> attempt property-only save. No snapshot: retain choice for the next response.
3. Same choice: no rebuild, save, request or lifecycle mutation.
4. Save failure: retain choice for current opening and log; subsequent city/route/scope changes retain it. Next actual opening/reload may recover last persisted choice.
5. Accepted response: build from that response and the then-current units before normal acceptance. Existing generation/scope guards remain authoritative.
6. Close: existing teardown only. Reopening rereads preference. No cross-tab transition or event bus.
