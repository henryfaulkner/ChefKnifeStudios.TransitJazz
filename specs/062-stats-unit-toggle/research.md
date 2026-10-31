# Research: Metric and Imperial Statistics

**Date**: 2026-10-30. Authority: [source design](../../docs/STATS_UNIT_TOGGLE_DESIGN_DOCUMENT.md), [spec](spec.md), constitution v3.3.2 and repository inspection. No production/test implementation or external technology choice is required.

## Conversion and source integrity

**Decision**: Append optional units to `StatsOverlayProjection.Build`; introduce `Models/StatsUnitSystem.cs` and pure `Services/StatsDisplayUnits.cs`. Convert nullable decimal values with exact `1.609344m` before double chart materialization and retain DTO values/references.

**Evidence/rationale**: Projection currently converts decimals through `ToDouble` for pie distances, bubble X/Y/distance and timeline plotted/known/hourly evidence. It retains `snapshot = response` before a previous-day local copy; cards bypass projection and read metric DTO fields. This seam can supply every surface consistently, including converted cards and previous-day data. Scale returned bubble ratios directly; preserve distinct route-vehicle-hour and active-clock-hour denominators.

**Alternatives considered**: Server units parameters/duplicated payload violate the contract; renderer conversion diverges from tables/evidence; converting projected or rounded values compounds error.

## Chart model names and resource-backed copy

**Decision**: Rename client-only distance fields/methods to neutral names and carry resolved unit-bearing headings/dataset labels/tooltip templates, with resource-backed Metric defaults for existing callers. Retain Shared metric names and current English formatting precision.

**Evidence/rationale**: Pie rows/slices expose Kilometers and GetTableKilometers; bubble rows/points expose DistanceKilometers; ChartPointEvidence names hourly/cumulative kilometers. Pie table uses metric resources and inline caption text; PieChartConfig hardcodes Observed distance (km). Bubble table reads StatsKilometers and projection tooltip embeds km/hr/vehicles. SeriesChartConfig independently builds canvas tooltip lines ending in km; Series table separately renders evidence and captions. Every affected path needs projected context, including that independent canvas tooltip. StatsChartText already pins English lookup and formatting; preserve it.

**Alternatives considered**: A general unit engine or second resource store is unnecessary. Miles stored under Kilometers creates a misleading client contract. Do not broaden into unrelated copy cleanup.

## Additive persistence and actual serializer

**Decision**: Add hidden observable StatsUnits defaulting Metric, retain version 5 and normalize unsupported numeric enum values locally. Persist only explicit changed choices through existing SetSettingValue after local refresh. Use narrowly scoped logging/catches for preference I/O.

**Evidence/rationale**: Settings uses ObservableObject and BackfillTexture demonstrates generated-property HiddenSetting; SettingsBlade filters hidden properties. GetSettings accepts matching version, catches read/deserialization errors and otherwise seeds defaults (whose write can throw). SetSettingValue rereads latest Settings, mutates one reflected property and saves. Version bump discards unrelated old preferences. The overlay's initial settings read also supplies theme and must be guarded against storage failure.

WebApp Program.cs registers AddBlazoredLocalStorage() without customization. Client.Shared pins 4.5.0. Read-only research inspected installed package README and serializer interface: v4+ uses default System.Text.Json options; IJsonSerializer exposes Serialize<T>/Deserialize<T>. Defined enum values are numeric; missing fields retain initializers; representable undefined numeric values deserialize but need application normalization. These are configuration findings, not executed feature test results.

**Implementation validation**: In existing xUnit, create ServiceCollection, invoke production AddBlazoredLocalStorage(), resolve Blazored.LocalStorage.Serialization.IJsonSerializer and deserialize literal legacy version-5 JSON missing StatsUnits with other nondefault values. Round-trip both systems and deserialize numeric 999. Test normalization and other-preference preservation separately. Resolving only the serializer requires no browser/JS runtime. Do not use Shared.JsonSettings.DefaultOptions or assumed string enums. Malformed/unparseable whole settings follow existing service fallback policy; do not redesign general storage error behavior.

**Alternatives considered**: Saving cached full Settings risks overwriting unrelated recent changes. No new key/package/settings API/event bus is needed.

## Reprojection and open lifecycle

**Decision**: Load on initialization and true overlay opening; retain in-memory choice through city/route/scope sessions. Pass current units from every BuildPresentation. Same-option selection returns before refresh/save; changed selection only refreshes locally then saves.

**Evidence/rationale**: OnParametersSet distinguishes open transitions, but location changes also call StartNewSessionAsync. Loading on every session restart could revert a failed-save active choice. RefreshPresentation calls TryReplacePresentation, whose same-snapshot guard and record replacement retain stale state. Response acceptance builds presentation after arrival. Generation, request admission, cancellation, retry and refresh loop are separate state paths that units must never touch.

**Alternatives considered**: New session/requery violates zero-request and retained-state requirements; keys based on units remount components.

## Chart state and accessible controls

**Decision**: Preserve chart/frame instances, stable dataset identities and readiness; use existing LatestConfig update. Place native labeled two-option radios before all conditional scrollable main content, with wrapping, focus indicators, 44px targets and both themes.

**Evidence/rationale**: ChartFrame owns _tableExpanded and closes it on Loading. SeriesChart reconciles visibility by series key and supplies LatestConfig for normal model changes. ChartCanvasHost.ApplyLatestInputAsync assigns latest options, smooth-replaces data with no animation, then already calls ApplyDatasetVisibilityAsync. Positive/null evidence remains positive/null under either unit, so units need no readiness transition. Native keyboard semantics avoid custom controls; real-browser acceptance must confirm native Chart.js behavior.

**Alternatives considered**: No new bridge, custom keyboard state machine, chart state store or fixed toolbar.

## Test boundaries and governance

**Decision**: Extend existing semantic xUnit fixtures and use browser acceptance for actual handler/component/network effects. Preserve explicitly authorized English Stats scope in neutral resources.

**Evidence/rationale**: Client.Shared.Tests is desktop net10.0/xUnit 2 for pure logic, with no bUnit. Existing projection, All, accessible evidence, resources, session/request and visibility suites provide fixtures. Those tests cannot alone prove focus/details/native legend/network wiring. Current 059 plan carries forward English Stats neutral-resource scope; source design explicitly repeats it.

**Alternatives considered**: Source-text assertions or mirrored tests are weak behavioral evidence; adding a new test framework is unnecessary. Shared/server DTO and payload fixtures require no edits.

All planning unknowns are resolved. Serializer runtime, browser behavior and build/test execution remain implementation validation obligations. Optional before/after plan commit hooks are skipped under the user rule; existing branch and concurrent retrieval-optimization artifacts are preserved.
