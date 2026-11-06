# Implementation Plan: Metric and Imperial Statistics

**Working Branch**: `062-stats-retrieval-optimization` (retained by user instruction) | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)
**Input**: [Metric and Imperial Statistics design](../../docs/STATS_UNIT_TOGGLE_DESIGN_DOCUMENT.md). Planning only; no production/test implementation. Feature selection remains `specs/062-stats-unit-toggle`, independently of the existing branch name.

## Summary

Add one persisted Metric/Imperial radio group to Stats. Project all displayed distances and distance-based rates from the accepted metric snapshot, using decimal division by exactly `1.609344m` for Imperial. Cards, charts, tooltips and accessible tables consume one converted presentation with resource-backed labels. Reuse presentation replacement to preserve requests, freshness, timers, focus and exploration state.

## Technical Context

**Language/Version**: C#/.NET 10, Razor/Blazor WebAssembly and component CSS.
**Primary Dependencies**: Existing CommunityToolkit.Mvvm, Blazored.LocalStorage 4.5.0, pax.BlazorChartJs/Chart.js, .NET localization/logging. No new package or framework.
**Storage**: Additive hidden `Settings.StatsUnits` under the existing browser settings key, retaining `CurrentVersion = 5`. No server schema change.
**Testing**: Existing desktop net10.0 Client.Shared xUnit 2 project for pure semantics/settings/state; browser acceptance for component, canvas and network behavior. No new component-test framework.
**Target Platform**: Existing Azure Static Web App WASM client, desktop/mobile browsers.
**Project Type**: Presentation feature in existing client Razor component library.
**Performance Goals**: Immediate linear local rebuild over existing bounded evidence; zero additional Stats requests or new placeholders for accepted data; unchanged scheduled-refresh cadence. No invented numeric latency budget.
**Constraints**: Exact conversion, display-only rounding, original metric snapshot retained, English Stats formatting, unchanged API/version/cache/payload, accessible controls and both themes. No branches/switches, commits or commit hooks.
**Scale/Scope**: One visitor preference, two unit systems, current-city/route/All/previous-day views. Map, audio, geometry, worker, SQL, collection and retrieval optimization remain outside scope.

## Constitution Check

Constitution v3.3.2 reviewed before research and after design. Applicable gates pass with the explicitly authorized existing English Stats scope recorded below.

| Principles | Initial / post-design | Design response |
| --- | --- | --- |
| I-II architecture/secrets | Pass / Pass | Existing WASM boundary and dependencies; no secret preference. |
| III/VI pipeline/identity | Pass / Pass | Client-only conversion; metric DTOs and route/city identities unchanged. |
| IV observability | Pass / Pass | Structured ILogger for scoped preference I/O failures; do not treat those failures as Stats data errors. |
| V delivery | Pass / Pass | Existing client build/tests and deployment units. |
| VII-X map/music/selection | Pass / Pass | Preserve scope, selection, fixed controls and map/audio behavior. |
| XI overlays | Pass / Pass | Local refresh; existing open/close and focus lifecycle preserved. |
| XII text/settings | Authorized scope / Authorized scope | Original design explicitly retains English Stats formatting and neutral RouteFilterResources.resx, matching the 059 plan's carried-forward English Stats decision. All new copy remains resource-backed; no second store or culture/language change. General app language behavior is unchanged. |
| XIII themes | Pass / Pass | Existing light/dark palette and ~250ms color transitions; radio/shape cue, focus indicator and 44px targets. |
| User workflow rules | Pass / Pass | Stay on current branch, preserve concurrent work and AGENTS no-commit section; skip before/after plan commit hooks. |

## Project Structure

### Documentation (this feature)

```text
specs/062-stats-unit-toggle/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  contracts/stats-display-units-v1.md
  quickstart.md
  tasks.md                  # subsequent tasks stage, not produced by planning
```

### Source Code (repository root)

```text
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
  Models/Settings.cs
  Models/StatsUnitSystem.cs                           [new]
  Services/StatsDisplayUnits.cs                       [new]
  Services/SettingsService.cs                         [reuse existing API]
  Services/StatsChartText.cs                          [retain formatter]
  Resources/RouteFilterResources.resx
  Components/Stats/StatsOverlayProjection.cs
  Components/Stats/StatsOverlay.razor[.css]
  Components/Stats/StatsOverlayRequestPolicy.cs        [reuse accepted-state seam]
  Components/Charts/{PieChartModel,BubbleChartModel,SeriesChartModel}.cs
  Components/Charts/{PieChart,BubbleChart,SeriesChart}.razor
  Components/Charts/{PieChartConfig,BubbleChartConfig,SeriesChartConfig}.cs
  Components/Charts/{ChartFrame,ChartCanvasHost}.razor  [retain update/instance mechanisms]
  Components/Charts/StatsTimelineVisibilityState.cs    [reuse stable keys]
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/
  StatsDisplayUnitsTests.cs                           [new]
  StatsUnitsPreferenceTests.cs                        [new]
  StatsOverlayProjectionTests.cs / StatsAllProjectionTests.cs
  StatsChartModelTests.cs / StatsAccessibleEvidenceTests.cs / StatsResourcesTests.cs
  StatsOverlaySessionTests.cs / StatsTimelineVisibilityTests.cs
src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/   [existing build/registration]
```

**Structure Decision**: Keep enum with client models, pure helper with client services, conversion in the existing projection and display text in affected models. Shared metric DTOs and server files stay unchanged. No new general preference abstraction, bridge, event bus or migration.

## Phase 0: Research Results

[research.md](research.md) resolves conversion, serializer, lifecycle and rendering decisions using actual repository seams. No unresolved planning questions remain; runtime checks below are implementation obligations, not executed-test claims.

## Phase 1: Design

### Conversion and presentation

The pure `StatsDisplayUnits` helper takes an explicit normalized system; it reads neither storage nor culture. Metric is identity; Imperial uses nullable decimal division by `1.609344m`, followed by double conversion only at chart boundaries. Append an optional units argument to `StatsOverlayProjection.Build`, defaulting Metric. Convert cards, pie distances, bubble X/Y/distance and all timeline plotted/known/hourly/subtotal values directly from metric evidence. Preserve all percentages, vehicle measures, hours, coverage and time coordinates.

Keep `StatsOverlayPresentation.Snapshot` as the exact accepted reference. Existing previous-day local substitution may select chart sources; projected daily distance uses that selected summary while Last24 uses the original Last24 summary. Keep `DailyDistance` metric for coverage consumers; add nullable converted daily/Last24 distances, selected units and matching card/chart text. Cards render projected numbers.

Use the client-only renames in [data-model.md](data-model.md). Models carry resolved distance headings/dataset labels/tooltip templates, with resource-backed Metric defaults for other callers. Update direct metric label lookups and inline unit text in renderers/configuration. In particular, `SeriesChartConfig` currently creates a separate hardcoded `km` tooltip; updating projection evidence alone would miss the canvas tooltip. Table headings/captions must identify the selected unit. Renderers do no conversion or settings access.

### Preference and selector

Add observable `StatsUnits` initialized Metric and `[property: HiddenSetting]`, retaining version 5. Load and normalize on initialization and actual overlay openings, never every city/scope session restart: a failed save must not lose the active choice during the current opening. Scope I/O catches narrowly; initialization currently reads settings for theme, so do not leave an earlier unguarded read able to abort initialization. A failed read selects Metric for that opening. Undefined numeric enum values normalize locally without resetting other preferences or forcing persistence.

At the start of scrollable main, outside all data/loading/error conditions, render a labeled native fieldset/radio pair in a wrapping row. Use stable input names and no units-based component keys. A changed selection updates in-memory units, calls `RefreshPresentation()`, then persists through `SetSettingValue(nameof(Settings.StatsUnits), choice)` against latest settings. Active-option selection returns before any rebuild/save. A write exception logs and leaves the selected presentation active. No cross-tab synchronization.

### Requests and exploration

Every `BuildPresentation(snapshot)` uses current units, including response acceptance after an in-flight request. Unit handlers never call session/request/debounce paths or alter generation, cancellation, retry/error, timers or freshness. `TryReplacePresentation` retains the original snapshot and stale state. Keep chart/frame instances, dataset keys and readiness. Existing `ChartCanvasHost.ApplyLatestInputAsync` already updates options/data without animation and reapplies native dataset visibility; verify it, without adding another bridge.

## Phase 2: Task Handoff

Sequence resources/helper/preference schema, neutral chart models, then complete projection and renderer/configuration conversion before US1 is usable. Wire persistence and local lifecycle/UI behavior with both-theme accessible styling. Existing xUnit proves semantic invariants; browser acceptance proves real handler wiring, focus, open details, Chart.js visibility and zero network requests. Follow [quickstart.md](quickstart.md) and the [client contract](contracts/stats-display-units-v1.md). Task generation follows this stage separately; no implementation occurs here.

## Test Strategy and TDD Handoff

This section applies the repository `util-testing` skill: classification, constructs, test doubles, reliability, review criteria and test types. It enriches this plan directly; it does not regenerate the plan, change the design, implement tests or claim test execution. Implementation MUST use test-driven development for each behavior slice below. A passing pre-existing test is regression evidence, not a newly demonstrated red-to-green cycle.

### Native tooling and meaningful boundaries

Use `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj`: desktop `net10.0`, xUnit 2, `Microsoft.NET.Test.Sdk` and the existing project reference. Follow existing `sealed ...Tests` classes, descriptive behavior names, `[Fact]`/`[Theory]`, arrange/act/assert and direct state assertions. Use the real conversion/projection/model/settings collaborators together when they are pure; add no mocking, browser or component-test package. Reuse the existing fixture builders in projection/All suites, extracting a small composed fixture only if actual duplication warrants it. Do not create test base classes, a second projection implementation or a general units controller for testing.

The lowest useful automated layers are unit tests for conversion/normalization, component semantics for projection/models/accepted-state, and a contract test against the actual configured Blazored serializer. Browser acceptance is required for Razor handler wiring, renderer/configuration consumption, persistence lifecycle, focus, details state, native Chart.js and network/timer behavior. The current tests do not render components and there is no bUnit. A test that manually chooses units then calls `Build` proves projection with an explicit choice; it does not prove `StatsOverlay` uses the latest choice when its HTTP response arrives. Similarly, independently calling `TryReplacePresentation` beside an untouched request gate cannot prove the real unit handler makes no request.

`PieChartConfig` and `SeriesChartConfig` are internal and existing model tests do not exercise their full browser pipeline. Confirm their generated dataset labels and tooltip text in the running browser, including SeriesChartConfig's independently built plotted tooltip and the pie dataset label. Do not add reflection into private component methods, source-file string/regex assertions, mirrored event handlers or new public production APIs solely to claim this coverage. Existing resource-inventory checks may remain regression checks; new unit-label assertions must resolve compiled resources and exercise real presentation values.

### Coverage matrix

Scenario IDs below use `US1.1` for User Story 1, acceptance scenario 1, and similarly for the other stories. All paths are repository-relative. Every automated file below belongs to the Client.Shared test project named above. `B1`–`B7` refer to the explicit browser cases in the following section. Rows distinguish assertions on presentation/table inputs from actual rendered tables.

| Coverage | Lowest meaningful layer and exact test file / browser case | Observable assertions and happy, failure or boundary cases |
| --- | --- | --- |
| FR-005, FR-015; US1.2, US1.5; SC-001, SC-002 | Unit: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsDisplayUnitsTests.cs` (new) | Metric identity; `1.609344m` becomes exactly `1m`; `16.09344m` becomes exactly `10m`; null remains null and zero remains zero in both systems. A fractional value such as `1m` retains the full decimal quotient before formatting. Defined systems normalize unchanged and undefined numeric values (including 999 and -1) normalize Metric. Expected values use literal anchors or independent test-side decimal division; never call the helper to compute its own expected result. |
| FR-005, FR-006, FR-008, FR-015; US1.2, US1.3; SC-001 | Component semantics: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsOverlayProjectionTests.cs` | One representative metric DTO fixture spans both card summaries, category rows/slices, bubble X/Y/distance, timeline plotted/known/hourly/subtotal evidence. Assert all converted numbers at their consumption boundaries and matching table/model inputs. Give each surface a distinct source amount to detect a swapped field. Bubble X/Y are intentionally different from ratios recomputed from distance/hours: expected values scale the supplied ratios directly. Counts, reported-vehicle summaries, percentages/category keys, mean vehicles/radius, hours, samples, coordinates/order, timestamps, coverage and exclusions equal the original evidence. Split tests by behavior instead of one opaque aggregate assertion. |
| FR-005, FR-009, FR-015; US1.4, US1.5; SC-002 | Component semantics: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsOverlayProjectionTests.cs` | Capture initial Metric presentation and original DTO values, then build ten Metric -> Imperial -> Metric cycles from that same DTO. Each Metric result equals the initial values and `Assert.Same(response, presentation.Snapshot)` holds throughout; DTO values remain unchanged. Previous-day fixture uses distinct Today, PreviousDay and Last24 amounts: daily/card/charts use fallback, Last24 retains its own window, headings/coverage remain correct and Snapshot stays original. Include selected-route selection evidence. |
| FR-005, FR-006, FR-008, FR-009, FR-015; US1.3–US1.5; SC-001, SC-002 | Component semantics: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsAllProjectionTests.cs` | Extend existing multi-city/local-date/offset fixture with Imperial assertions and a missing bucket, null plotted gap with non-null known evidence, actual zero, unavailable row and empty series case. Non-null distances convert once; missing stays absent, zero stays zero, local/UTC identities and extent/order remain unchanged. Rebuild from original for round-trip assertions. Current-city, selected-route and previous-day are covered by the companion projection suite; B3 proves actual scope navigation. |
| FR-006, FR-007, FR-008, FR-011; US1.2, US1.3, US3.4; SC-001, SC-005 | Component semantics: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsChartModelTests.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsAccessibleEvidenceTests.cs`; rendered consumption B2/B6 | Unit-neutral properties/table getters carry the same converted values as plotted points, including zero/unavailable rows excluded from canvas. Labels/captions supplied on models identify matching units and Metric defaults work for existing callers. Preserve finite-value/alignment validation, null gaps, omission reasons and bubble radius/relative ordering. Do not test the property rename by name alone; prove its values. Actual open-table retention and configuration output require B6/B2. |
| FR-007, FR-013, FR-015; US1.1–US1.3; SC-001, SC-005 | Unit/compiled-resource contract: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsResourcesTests.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsChartTextTests.cs`; component semantics in `StatsOverlayProjectionTests.cs`; B2 | Resolve real compiled neutral resources through existing DI/localizer and StatsChartText. Assert group/options, miles/mi, full/compact headings, speed mph versus fleet mi/h, timeline heading, table context and tooltip templates. Build both systems with this lookup, assert representative tooltip numbers/text and no contradictory metric-only unit in Imperial unit-bearing fields. Existing metric keys still resolve. Under `es` culture, both systems retain English separators/precision and leave caller cultures unchanged. Rounding happens in the existing formatter, not helper/projection. |
| FR-002, FR-004, FR-013, FR-015; US1.1, US2.1, US2.2, US2.4; SC-004 | Serializer contract + unit: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsUnitsPreferenceTests.cs` (new) | Instantiate ServiceCollection, call the same uncustomized `AddBlazoredLocalStorage()` as WebApp Program.cs, resolve `Blazored.LocalStorage.Serialization.IJsonSerializer`. Deserialize literal legacy version-5 JSON without StatsUnits and with unrelated nondefaults: Metric initializer survives and unrelated properties retain values. Round-trip both defined choices with the real serializer. Deserialize numeric 999, normalize to Metric and preserve all other settings. Check CurrentVersion remains 5 and generated StatsUnits property carries HiddenSetting (metadata is the reflection-driven panel's contract). Browser B4 proves no duplicate control. No assumption of string enums or use of Shared.JsonSettings. |
| FR-003, FR-004, FR-015; US2.3, US2.4; SC-004 | Settings component semantics: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsUnitsPreferenceTests.cs`; overlay behavior B4 | Real SettingsService plus a per-test ISyncLocalStorageService double seeded with version-5 settings. Modify another preference in stored data before SetSettingValue for units; reread proves newest unrelated value survives. Inject GetItem/SetItem failures separately; write exception is observable from the service. This does not prove overlay recovery. The service catches read failures and may seed defaults; test that a subsequent failing default write escapes to the overlay boundary, whose initialization must remain usable in B4. Do not change legacy whole-settings fallback behavior. |
| FR-010, FR-015; US3.2, US3.3; SC-006 | Component semantics: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsOverlaySessionTests.cs`; actual wiring B7 | Accept original snapshot, mark stale, replace with real Imperial projection and assert identical snapshot/reference, changed display values and retained IsStale. Replacement without accepted data or for a different snapshot is rejected; obsolete response generation remains rejected. A newly accepted valid projection uses its explicitly supplied current units. Existing request-gate/retry/refresh-loop tests remain regressions; actual pending/latest-choice, error text, no cancellation/generation/cadence change is B7. |
| FR-011; US3.4; SC-005 | Unit/component semantics: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsTimelineVisibilityTests.cs`; actual native state B6 | Hide a stable series key, reconcile keys from Metric and Imperial projections, and assert it remains hidden, others retain visibility, keys/order survive. Existing removed-key/reset semantics remain unchanged. This proves the visibility model, not ChartCanvasHost/Chart.js application. |
| FR-001, FR-012; US3.1, US3.5; SC-005 | Browser B1/B5 | Selector placement, availability with no snapshot/loading/error, exactly two native choices and accessible group name; narrow-screen wrap and no fixed-control overlap; keyboard/touch, focus, non-color selected cue and measured >=44px targets in both themes. No automated component coverage claimed. |
| FR-002, FR-003, FR-004; US2.1–US2.4; SC-004 | Browser B4 (settings contracts above provide lower-layer support) | Load at initialization/actual opening, persist across reopen/reload/city/routes, active choice immediately visible before save completion, undefined/missing/read-failure fallback, latest other-setting retention, write-failure session continuity and no duplicate general-settings control. Active-option selection causes no storage write or rebuild; B7 covers its network effect. |
| FR-009, FR-010, FR-011, FR-015; US1.4, US3.1–US3.4; SC-003, SC-005, SC-006 | Browser B3/B6/B7 | Actual city/route/All/previous-day surfaces update locally, request count does not rise because of toggle, no loading replacement, pending work and cadence remain intact, stale/error survives and late response uses latest choice. Focus/details/native hidden series survive. Pure tests cannot substitute for these assertions. |
| FR-014, FR-015; SC-003 | Existing transport/contract regressions: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsTransportTests.cs` and `src/ChefKnifeStudios.TransitJazz.Shared.Tests/StatsOverlayContractTests.cs`; browser B7 and scope diff review | Existing metric request/response fixture expectations pass without edits for units. Browser confirms no units parameter/header or duplicate evidence. Review final diff: no Shared DTO, API/version, cache-key/payload, map/audio/SQL/worker changes. Client fixtures are the default regression gate; run Shared contract gate if shared-contract concern arises, keeping its original expected payload. |

FR-015 is the aggregate completion gate over every row. SC-001's all-surface assertion requires both model assertions and B2's rendered/configuration check; no inferred code-coverage percentage is substituted for that requirement. Every US acceptance scenario and SC-001 through SC-006 has a mapped gate.

### Required browser cases and evidence

Execute against the existing local app/API environment with representative populated snapshots. Record environment, fixed/accepted snapshot identity, expected converted values, observations and pass/fail/unverified results in `specs/062-stats-unit-toggle/validation.md` during implementation (proposed evidence file, not created by this planning phase). Do not require production data or claim completion when a necessary fixture/environment is unavailable. Existing [quickstart.md](quickstart.md) supplies the operational walkthrough.

- **B1 — unavailable data:** Check group placement before charts/previous-day notice and availability during initial loading and error without data. Choose Imperial before data exists and confirm accepted data uses it.
- **B2 — every rendered surface:** Use a known comprehensive snapshot with independent expected values. Check both cards, pie magnitudes/dataset metadata, bubble X speed mph and Y fleet mi/h, distance evidence, timeline plotted/known/hourly/subtotal values, actual canvas tooltips and all open table headings/cells. Compare full precision models and final formatted text at existing precision; null/zero/coverage evidence remains distinct. Repeat Metric. This catches remaining renderer metric lookups and SeriesChartConfig's separate tooltip text.
- **B3 — scope/window matrix:** Current city, selected routes, All with missing buckets, and previous-day fallback; verify daily versus Last24 sources, scope qualifications and gaps without new placeholders. Establish scope via its normal request, then attribute only subsequent requests caused by units to the toggle test.
- **B4 — persistence/failure matrix:** Missing/legacy/invalid settings; successful Imperial reopen/reload/city/routes; unrelated preference modified before units save; and controlled read/write storage failures in an isolated local browser profile. A failed write retains chosen units through city/route/All changes during that opening; reopening may restore last persisted choice. Read/default-write failure must not abort initialization. Verify hidden setting is absent from general settings. For active-option no-op, observe storage writes and component/presentation changes using a temporary local debugger/instrumentation if needed; do not add permanent telemetry or a new test framework.
- **B5 — accessibility/themes:** Both existing themes at narrow mobile and desktop widths; accessible tree group name/options, Tab/arrow/Space native radio behavior, actual touch/tap, visible focus retained on selected input, selected shape independent of color, >=44px measured targets, normal wrapping above charts/notice and no fixed-control collisions.
- **B6 — exploration:** Expand every details table and hide timeline series using native legend interaction, then toggle repeatedly. Expanded state, selected radio focus and legend choices survive; actual Chart.js dataset visibility agrees with app state. Confirm labels/tooltips update without remount or loading state. Existing host visibility reapplication should suffice; fix only an observed deficiency.
- **B7 — pending/stale/network/cadence:** Use a controllably delayed local Stats response, choose units while pending and then release it; verify latest choice at acceptance, no cancellation/restart and normal generation ordering. Display stale accepted data with an error and toggle: both indications persist and values update without placeholders. Capture Network before/after repeated and active-option selection: zero additional Stats requests caused by units, no new units parameter/header. Separately observe an already scheduled refresh on its pre-toggle schedule; avoid crossing an ordinary refresh boundary when counting toggle-only requests. If private timer/generation invariants cannot be observed through the browser, use local debugger evidence and record that method; do not infer them solely from a green policy test.

Snapshot populated fixtures locally; isolate browser storage and restore modified values/failure injections afterward. Record delayed-request release/error setup explicitly. Live transit changes must not be mistaken for conversion effects. Wait on visible/observable conditions, not arbitrary sleeps. Missing API history, inaccessible touch hardware or unavailable browser instrumentation is a documented acceptance gap until exercised, not a pass.

### Deterministic fixtures, doubles and cleanup

Use fresh immutable response graphs and state instances per test; never share mutable Settings, visibility dictionaries, service providers or storage among tests. Keep the existing fixed UTC/DateOnly/offset evidence and explicit elapsed-hour coordinates. This feature adds no clock algorithm; use explicit instants with existing refresh policies rather than `UtcNow`, real timers or delays. Preserve deterministic ordinal key comparisons/order and compare semantic sets only where order is unspecified. No randomness, external network, database, filesystem writes or user browser storage in semantic tests.

For settings service tests, use a small nested in-memory sync-storage fake only because latest-read/mutate/write behavior needs it; store serialized JSON using the real resolved serializer so each read returns fresh data, rather than aliasing one mutable Settings object. Add call recording/throw switches only for GetItem/SetItem assertions under test; unrelated interface members can throw NotSupportedException. Serializer tests resolve only IJsonSerializer and require neither IJSRuntime nor a browser. Dispose DI containers and local cancellation resources. Culture tests save both CurrentCulture and CurrentUICulture and restore in `finally` as the existing suites do; do not mutate global DefaultThreadCurrentCulture. Avoid shared static mutable culture/setup. If an actual parallel interaction requires serialization, scope it to that xUnit collection and document why.

Pending completion tests, if a genuine existing production seam supports them, use TaskCompletionSource with RunContinuationsAsynchronously, explicit request-start/release signals and finite WaitAsync guards, never Thread.Sleep/retries. No new mirrored orchestration harness is justified; absent a genuine seam, B7 is the required proof. Timeouts are hang guards chosen for local/CI scheduling, not feature latency assertions. Inject failures deterministically and assert the specific usable-state outcome rather than merely "does not throw."

### Ordered red-green-refactor slices and dependencies

For every slice: write the smallest meaningful failing assertion first, run its focused filter and record the expected failure; implement only enough production behavior to pass; rerun focused tests; refactor with tests green. A compile failure caused by a missing enum/property/renamed constructor is scaffolding, not the behavioral red gate: add the minimal compiling signature/default, then demonstrate the wrong observable value/label/state fails. Do not leave tests failing across task boundaries or invent tests for constants/property spelling alone. Existing behavior tests that already pass are retained as regressions. Renderer/interaction gaps require a recorded failing browser observation before the smallest fix and a passing rerun afterward.

1. **Conversion foundation:** Add StatsDisplayUnitsTests red cases for exact anchors, identity, null/zero, precision and normalization. Introduce minimal enum/helper, green then refactor. This is a dependency for projection/preference slices. Resource-backed label expectations may be added alongside the first consumer, avoiding orphan resource-only scaffolding.
2. **Preference schema/serializer:** Add StatsUnitsPreferenceTests against actual DI serializer, legacy JSON, saved choices and invalid normalization; retain version and other values. Add latest-settings/failing-write service tests with the isolated double. Demonstrate default/round-trip failure before implementing the additive property/helper integration. Existing SettingsService failure propagation may already pass; label that regression. Overlay recovery remains pending B4. Can proceed after foundation independently of chart renames.
3. **Neutral model contracts:** Update existing model/evidence tests and callers together so the rename compiles without changing numeric behavior. Add failing observable tests for new model labels/defaults/value agreement, then implement the smallest model context change and preserve validation/omission tests. This is a dependency for conversion projection and renderer consumption; a green rename alone is a refactor, not a feature TDD claim.
4. **Complete US1 presentation:** Add separate red assertions to existing projection fixture for cards/pie, bubble supplied rates/distance, and timeline values/evidence. Implement and green one surface slice at a time. Then add previous-day/All/null/ten-cycle tests and compiled-resource English/Imperial labels, preserving snapshot identity. Before calling US1 usable, perform B2 red/green for Razor/configuration labels and values, including the independent timeline tooltip and pie metadata. Models and projection must be ready first.
5. **Local refresh and exploration:** Extend accepted-state/visibility tests with real converted projections; preserve stale/reference/key behavior. Wire selector and current-units BuildPresentation with scoped preference I/O. Demonstrate B1/B4/B6/B7 failing behavior before the fix, then passing after: no snapshot choice, delayed acceptance, stale/error, failed-save session continuity, same-option no-op, no extra request/reset and native hidden-series/details/focus. Do not report pure policy tests as proof of wiring. Depends on projection and preference schema.
6. **Accessible selector and release gate:** Complete B3/B5 across scope/window/theme/narrow/touch cases; then run all automated regressions/build and reconcile every coverage row with evidence. Styling changes need meaningful browser verification; no brittle CSS/source-text tests. Feature acceptance remains incomplete until required browser gaps are resolved.

Tasks generated next MUST preserve these test-before-implementation dependencies, pair each meaningful red test with its smallest implementation and green verification, and include explicit browser proof/evidence tasks. Do not flatten this into implementation-first work followed by one final test task.

### Commands and acceptance gates

During implementation, run focused filters from repository root, then the existing Stats regressions after model/projection/lifecycle integration. These are proposed commands; no tests/build have been run in this planning phase.

```powershell
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName~StatsDisplayUnitsTests"
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName~StatsUnitsPreferenceTests"
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName~StatsOverlayProjectionTests|FullyQualifiedName~StatsAllProjectionTests|FullyQualifiedName~StatsChartModelTests|FullyQualifiedName~StatsAccessibleEvidenceTests|FullyQualifiedName~StatsResourcesTests|FullyQualifiedName~StatsChartTextTests"
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName~StatsOverlaySessionTests|FullyQualifiedName~StatsTimelineVisibilityTests"
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj --filter "FullyQualifiedName~Stats"
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
dotnet build src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
```

Use narrower `FullyQualifiedName` filters for each single red assertion before broadening. Final full Client.Shared tests are justified by the common chart-model and Settings changes; no blanket server/database suite is required. If contract concerns arise, run existing `dotnet test src/ChefKnifeStudios.TransitJazz.Shared.Tests/ChefKnifeStudios.TransitJazz.Shared.Tests.csproj --filter "FullyQualifiedName~StatsOverlayContractTests"` without changing its expectations. Client workflows currently publish Release and do not run xUnit; do not claim CI test coverage. The existing release compile gate is `dotnet publish src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj -c Release /p:BlazorEnableCompression=false` with the existing wasm-tools workload; use it when validating release readiness. No CI deployment, probe stamping, branch changes or commits are part of this feature handoff.

Completion gates: focused red/green evidence per new behavior; Stats and full client regressions green; client build/release compilation as applicable; unchanged metric contract diff; B1–B7 recorded with all FR/scenario/SC links satisfied. Record SDK/workload/environment failures distinctly from assertion failures and unresolved browser cases. Review the final tests for behavioral assertions, negative/boundary coverage, independent expected values, isolation and cleanup. No fabricated coverage target, retries hiding flakes, or test/build success claims without execution.

## Complexity Tracking

One enum and one pure helper suffice. No added complexity violation. The neutral-resource English Stats scope is an explicit source-design/current-plan exception to general multilingual presentation, documented above; it does not authorize inline copy or another resource store. No server or generic state infrastructure is justified.
