# Contract: Available Statistics presentation

Use existing StatsOverlay, StatsOverlayProjection, ChartFrame and chart/table models. No global warning, live-music/delivery change or new notification path. All labels, source names, reasons, status, empty explanations and ARIA text use RouteFilterResources and language variants. Maintain existing units, palette/theme behavior, keyboard interaction and mobile layout.

| Source/window condition | Visible value/charts | Persistent explanation |
| --- | --- | --- |
| Both healthy with proven full continuity | Available bus/rail measures | Complete only after all coverage conditions pass |
| Bus healthy, rail failed | Bus distance/timeline and valid bus bubbles | Partial beside card and affected headings; rail unavailable |
| Rail healthy, bus failed | Rail distance/timeline and valid rail bubbles | Partial; bus unavailable |
| Both failed throughout period | Null distance, unavailable plots/table explanations | No observations available for this period |
| Valid empty with full coverage | Observed zero distance | Complete zero; undefined shares/ratios stay unavailable |
| Intermittent outage/recovery | Retain valid observations and gaps | Affected source/hour Partial; subsequent full healthy hours may be Complete |
| One invalid ratio/axis/size | Distance/unrelated plots remain | Omitted bubble stays in accessible table with reason |

Distance cards and ChartFrame.CoverageDescription must show the qualification without requiring a tooltip, toast or table expansion. Source keys map to resource-backed names; details/data tables show covered UTC-hour periods and limitations in the displayed local time convention, preserving offset/DST distinctions. Do not promise precise outage timestamps beyond retained hour evidence. Chart descriptions distinguish observed distance/ratios from full-system estimates.

Pie/category projection uses category coverage, not whole-city coverage for every row. Keep unavailable rail in legend/table while bus is 100% of available distance; visibly explain that denominator. Null/zero rows already survive PieChartModel accessible rows; preserve that behavior. Zero denominator has null percentages, never equal slices.

SeriesChartConfig already has SpanGaps=false. Preserve nullable plot values and known subtotal fields; annotate Partial points, leave wholly unavailable buckets null and do not recompute missing zeros in projection/JS. Resume later observed cumulative totals as a separate segment. Table increments reconcile with the same admitted rows/window as the cards/shares.

BubbleChartModel already separates plotted points from all table rows. Show only valid X/Y/positive-size points, retain omitted rows and localized reasons/source coverage, and leave unrelated charts usable. Clarify route/hour population sums versus unique fleet count. Reported-vehicle cards remain independently city-wide with their current qualified population.

One fallback decision controls projection, daily value, titles and reported-vehicle fallback headings. Current usable Partial/observed zero wins; yesterday is used only when the current eligible-period rule allows fallback. Keep snapshot/store generation, current city/route selection and cancellation checks; newer Partial must replace old Complete without stale route leakage.

Release browser evidence covers bus-only, rail-only and whole-city selections; all-cities missing coverage; source recovery; accessible expanded tables; unavailable legend/category; omitted bubble reasons; keyboard controls; metric/imperial; light/dark and narrow mobile layout. Required outage and recovery views use controlled fixtures/stubs, never production disabling. Record revision/environment/UTC windows and actual visible numbers; source screenshots without assertions are insufficient.
