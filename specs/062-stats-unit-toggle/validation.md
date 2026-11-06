# Statistics Units Validation

Implementation evidence for feature 062. Open browser gates are listed explicitly; model tests do not substitute for the specified browser checks.

## Environment and scope

- Branch: `062-stats-unit-toggle`, rebased onto `origin/main` at `88a38a8`.
- `.specify/feature.json` selects `specs/062-stats-unit-toggle`; `AGENTS.md` points to its plan.
- SDK: .NET `10.0.401`; `wasm-tools` workload `10.0.113` is installed.
- Existing test project: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj`.
- Diff scope: client presentation, chart-model contracts, preference property, resources, client tests, and this feature's validation/task artifacts. No Shared DTO, API, database, map, audio, worker, or feature-061 source changes.
- Local WebApp and API ran against the populated local fixture. Atlanta had unavailable distance cards; All Cities had populated values. No pre-fix browser capture was made before implementation, and there was no isolated browser storage or response-delay instrumentation.
- `plan.md` and `quickstart.md` retain the older branch label `062-stats-retrieval-optimization`; the requested branch and active feature marker are preserved.

## TDD evidence

- `dotnet test ... --filter "FullyQualifiedName~StatsDisplayUnitsTests"` initially produced the intended behavioral red: 3 failures and 5 passes for absent Imperial conversion/invalid enum normalization. After the pure conversion helper was added, the focused suite passed 8/8, including exact `1.609344m` and `16.09344m` anchors, null/zero, and full-precision conversion.
- Configured `AddBlazoredLocalStorage()` serializer and settings-service tests cover legacy version-5 PascalCase JSON, both choices, undefined numeric values, hidden-setting metadata, latest-setting preservation, and separate storage Get/Set failures. The first fixture used camelCase and exposed the configured serializer's case sensitivity; it was corrected to the production PascalCase shape. The focused preference suite passed.
- Projection, All-scope, chart-model, accessible-evidence, resource/text, session, and timeline-visibility regressions cover conversion from original evidence, ten-cycle round trips, missing/null/zero distinctions, unit labels, and stable visibility/session state. The final `FullyQualifiedName~Stats` run excluding the known base-branch failure passed 168/168.
- The full client suite passed 223 tests and failed one existing assertion: `StatsOverlayProjectionTests.CityProjectionPreservesServerSharesRatiosZeroAndKnownAmountsAcrossPlotGap` expects `IsStacked == true`, while the base-branch category projection sets it to `false`. That assertion and projection behavior are unchanged from `origin/main`; stacking was not changed as part of this feature. One broader run also hit a timeout in `StatsSnapshotScheduleTests.IdenticalValidHttpSuccessClearsFailureWithoutReanchoringSnapshot`; that test passed on an isolated rerun. Excluding those two unrelated tests, the full client project passed 222/222.
- `dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj` succeeded. `dotnet publish ... -c Release /p:BlazorEnableCompression=false --no-restore` succeeded. Builds reported existing package advisory and nullable/analyzer warnings; no build errors.

## Browser acceptance

### B1 — Selector placement and no-data/loading/error availability

The native `Units` fieldset is at the start of the scrollable `<main>`, outside the presentation/loading/error branches. In the running app it was visible during loading and with Atlanta's unavailable-distance/no-plotted-values state, and remained usable. The no-data state was checked; an error without data was not induced.

### B2 — Converted values and matching chart/table/tooltip labels

In populated All Cities data, Metric showed `543,179.77 kilometers` today and `700,484.51 kilometers` over 24 hours. Imperial showed `337,516.26 miles` and `435,260.89 miles`; reported-vehicle counts remained 8,604 and 9,413. The cumulative-distance heading changed from `(km)` to `(mi)`. An open timeline table showed unit-bearing per-city, known-distance, and subtotal headings; an open category table caption changed from `observed distance (mi)` back to `(km)` when the unit changed. Actual rendered canvas tooltip interaction and separate dataset/Series tooltip display were not independently exercised in the browser; projection/model tests cover their contexts.

### B3 — City, route, All, and previous-day scopes

Atlanta city scope and All Cities were exercised, including switching units and retaining the choice across that scope change. The local Atlanta distance fixture was unavailable, so the populated numerical comparison used All Cities. Selected-route and previous-day browser fixtures, including their fallback/window labels, remain open; unit-level projection tests cover these data paths.

### B4 — Preference persistence and storage failures

Imperial was selected, the page reloaded, and the Stats overlay reopened; Imperial remained selected and displayed miles. A scope transition also retained the selected unit. The setting is hidden from general settings metadata and no duplicate control was present in the observed settings/overlay tree. Browser fault injection for missing/invalid saved values, read/default-write failures, delayed/failed writes, and no-write behavior for the active option was unavailable; serializer/service tests cover schema and latest-setting/failure propagation, but not the Razor storage boundary.

### B5 — Keyboard, touch, focus, themes, and narrow layout

The accessible tree exposed a `Units` group with Metric/Imperial radios. ArrowLeft from Imperial selected Metric and kept the Metric radio active. The rendered Imperial label measured approximately 111.8 × 44 CSS pixels. The implementation supplies a non-color checked-radio cue, visible focus styling, wrapping layout, light/dark selectors, and reduced-motion handling. Touch hardware, Space/Tab traversal, computed dark-theme contrast, and actual narrow/desktop viewport behavior were not verified in the browser.

### B6 — Open tables and native hidden-series preservation

The open timeline/category data table remained open through a unit change and its caption/column labels updated to the new units. Model-level timeline visibility tests retain stable hidden keys and order across Metric/Imperial projections. Actual Chart.js legend visibility and canvas focus state were not exercised in the browser.

### B7 — Pending/stale/error lifecycle and request cadence

The selector was available during initial loading and toggles reprojected the accepted local presentation. No controlled delayed response/error, request counter, or refresh-timer observation was available; cancellation/order/cadence and zero toggle-request claims remain open for browser instrumentation.

## Requirement and success-criteria reconciliation

| Requirement | Evidence and remaining gate |
|---|---|
| FR-001 | Native fieldset placement and no-data/loading visibility observed; induced error, narrow layout, and fixed-control collision checks remain open. |
| FR-002 | Metric default/schema tests and Imperial reload/reopen observed; route/city navigation and invalid-storage browser cases remain open. |
| FR-003 | Immediate local reprojection and latest-settings property write are implemented/tested; browser write timing and active-option no-op instrumentation remain open. |
| FR-004 | Legacy/undefined values, hidden metadata, unrelated setting preservation, and service read/write failure tests pass; Razor-boundary failure injection remains open. |
| FR-005 | Exact anchors, precision, null/zero, normalization, and ten-cycle original-evidence round trips pass. |
| FR-006 | Card, category, bubble, timeline, table, and tooltip projection/model tests cover units; actual browser canvas tooltip/dataset inspection remains open. |
| FR-007 | Resource and model tests verify full/compact labels, mph versus mi/h, and unit-bearing table captions; browser tooltip confirmation remains open. |
| FR-008 | Projection tests preserve counts, percentages, hours, coordinates, timestamps, coverage, exclusions, ordering, and source DTO identity. |
| FR-009 | Projection tests cover city, selected route, previous-day fallback, All, missing buckets, nulls, and zero; browser route/previous-day navigation remains open. |
| FR-010 | Handler reprojects locally and does not call request/session methods; controlled browser request counts, pending/stale/error, and cadence checks remain open. |
| FR-011 | Session and timeline-visibility regressions pass; open table retention observed; actual native Chart.js hidden-series persistence remains open. |
| FR-012 | Native group, keyboard arrow/focus, and 44px target observed; touch, both-theme viewport behavior, and complete keyboard traversal remain open. |
| FR-013 | Shared English resources and decimal formatting under `es-ES` pass; caller cultures are restored. |
| FR-014 | Diff review found no Shared payload, API, cache, transport, map, audio, SQL, or worker changes. |
| FR-015 | Automated focused checks and client build/release publish ran; the browser lifecycle/network/touch/tooltip/hidden-series gates listed above remain open. |
| SC-001 | Projection/model values and populated cards/tables are consistent; full browser verification of every plotted/tooltip surface remains open. |
| SC-002 | Ten Metric→Imperial→Metric cycles preserve exact Metric values, missing values, snapshot identity, and source evidence. |
| SC-003 | City and All views were observed; selected route/previous day, network counts, placeholders, and refresh cadence remain open. |
| SC-004 | Schema, reload/reopen persistence, service failures, and latest-setting preservation are tested/observed; overlay-boundary failure injection remains open. |
| SC-005 | Keyboard arrow, focus, 44px target, and table-unit labels are observed; touch, hidden canvas series, narrow screens, and both-theme browser checks remain open. |
| SC-006 | Projection/session state regressions pass; pending and stale/error browser transitions remain open. |

## Automated commands

```powershell
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName~Stats"
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName!~CityProjectionPreservesServerSharesRatiosZeroAndKnownAmountsAcrossPlotGap&FullyQualifiedName!~StatsSnapshotScheduleTests.IdenticalValidHttpSuccessClearsFailureWithoutReanchoringSnapshot"
dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
dotnet publish src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj -c Release /p:BlazorEnableCompression=false --no-restore
```
