# Quickstart: Statistics Units Implementation Validation

This is an implementation handoff, not evidence of checks already run. This workflow creates planning/tasks documentation only. Do not modify production/tests, commit, create/switch branches or touch concurrent retrieval-optimization artifacts during planning.

## Confirm scope

From repository root, verify `.specify/feature.json` selects `specs/062-stats-unit-toggle` and the branch is still `062-stats-retrieval-optimization`. Read [spec](spec.md), [plan](plan.md), [source design](../../docs/STATS_UNIT_TOGGLE_DESIGN_DOCUMENT.md) and [contract](contracts/stats-display-units-v1.md). Preserve all existing untracked user design documents and other feature directories.

## Implementation sequence

1. Add neutral resource strings before wiring the enum/helper and hidden observable StatsUnits preference. Retain settings version 5.
2. Rename affected client distance fields/methods and add resource-backed display context with Metric-compatible defaults. Update callers/tests; retain Shared DTO names.
3. Convert full-precision metric evidence once in projection, including cards, fallback, pie, bubble rates/distance and timeline evidence. Fix chart renderers/configuration, including independent SeriesChartConfig tooltip suffix and pie dataset label.
4. Wire the radio group first in scrollable main; load on initialization/actual opening, refresh locally, then property-only save. Scope preference catches/logging and retain session choice on failed write.
5. Preserve existing chart instances, keys, accepted-state and request/timer lifecycle. Apply both-theme focus/tap-target/wrapping styles.
6. Complete semantic tests and browser acceptance; record actual evidence in feature validation notes during implementation.

## Focused automated checks

Use existing Client.Shared.Tests xUnit project and real fixtures. Do not introduce bUnit or source-text assertion tests.

- Pure helper: exact mile/speed anchors, Metric identity, null/zero, fractional precision before formatting and invalid-system fallback.
- Comprehensive projection fixture: card values, pie magnitudes/table distances, bubble X/Y/distance evidence, timeline plotted/known/hourly/subtotals and tooltip labels agree. Counts, percentages, radius, hours, time coordinates and coverage/exclusions remain equal.
- Ten Metric -> Imperial -> Metric cycles from the same DTO restore Metric exactly and preserve original snapshot reference/values. Include previous-day daily versus Last24 distinction and All-cities missing buckets/null gaps.
- Actual configured serializer: ServiceCollection -> AddBlazoredLocalStorage() -> resolve Blazored.LocalStorage.Serialization.IJsonSerializer. Deserialize literal version-5 JSON missing StatsUnits with other nondefaults, round-trip both choices, and deserialize undefined numeric 999 followed by normalization. Check HiddenSetting and version 5; do not substitute Shared.JsonSettings options.
- Settings service with an in-memory ISyncLocalStorageService fake: latest other preferences survive SetSettingValue, and failing writes are observable. Overlay-local write-failure behavior needs browser proof unless a minimal existing pure seam can prove it without adding general infrastructure.
- Accepted-state/session tests: presentation replacement retains snapshot/stale state; response built after a changed choice uses current units. Existing request/visibility suites remain regression coverage. Pure policy tests alone do not prove component handler/network wiring.

After test/model changes, run the existing statistics suite and build the client (PowerShell, repo root):

```powershell
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName~Stats"
dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
```

Existing Shared/server DTO and payload fixtures require no edits. Review diff to ensure there are no changes to their metric contract, endpoint parameters, cache keys or API payload. Run relevant existing contract fixtures as regression checks if implementation exposes a concern; do not regenerate them for units.

## Browser acceptance

Use the project's existing local app/API startup environment and representative populated fixtures/data. This client-only feature does not require migrations or new server options. If history data is unavailable locally, record the environment limitation instead of claiming populated-data acceptance.

1. Open Stats with legacy/no preference; Metric is selected. Confirm Units group precedes previous-day notice and charts and remains available with loading/error and no snapshot.
2. Inspect every card, chart axis, plotted tooltip and open data table after selecting Imperial. Check exact conversion under current precision and units in table headings/captions. Bubble X says speed mph; Y says fleet distance mi/h. Counts, percentages, bubble areas and evidence metadata remain unchanged.
3. Exercise current city, selected routes, All and previous-day data. Check daily fallback/Last24 distinction and All missing gaps. Toggle repeatedly and restore Metric.
4. Save Imperial, close/reopen, navigate cities/routes and reload. Confirm preference and unrelated settings survive. With storage writes made to fail in a controlled browser/local test, selection works immediately and survives scope/city changes for the opening; reopening may return to last saved choice. A controlled read failure selects Metric and does not abort overlay initialization.
5. Keyboard Tab/arrow/Space and touch: accessible group/options, native selection behavior, focus retained after changes, visible focus/non-color cue and >=44px targets. Check narrow viewport in both themes without collision with fixed controls.
6. Open tables, hide timeline series through the native legend, then toggle. Tables stay open and hidden series stay hidden both in app state and actual Chart.js canvas. Tooltips and table text update without remount/loading.
7. Delay an existing Stats request, change units, then allow response: latest choice applies and request order/generation remains valid. Show stale data plus an error, toggle, and verify error/stale status remain without placeholders.
8. In Network, record accepted response/request count, toggle both options and select the active option. Attribute zero new Stats requests to units; allow scheduled refresh to occur at its prior cadence. Confirm no cancellation, timer reset or units parameter. Same-option selection also causes no storage write/presentation rebuild when observed through a controlled local test.

Record actual test/build results and browser evidence, including environment/fixtures, any unverified case and observed failures. Completion requires all spec acceptance cases, not just passing projection tests.
