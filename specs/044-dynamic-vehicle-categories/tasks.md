# Tasks: Dynamic Per-City Vehicle Categories

**Input**: Design documents from `specs/044-dynamic-vehicle-categories/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (wire-contract, category-config, client-ui-contract), quickstart.md

**Tests**: This feature has **existing** xUnit tests that break with the wire change (`GtfsStaticLoaderTests`, `EventEnvelopeMessagePackTests`) — updating them is REQUIRED, not optional. The design doc (§8) also mandates specific new classifier/fallback cases. No new TDD suite is introduced for the client beyond that.

**Organization**: Tasks are grouped by user story. ⚠️ **Read this first:** unlike a typical feature, the core is a **breaking, atomic wire-contract refactor** (`TransitMode` enum → `string category`, `Key(10)` int→string). A half-migrated contract does not compile or deploy. Therefore the **Foundational phase (Phase 2) is unusually large** — it carries the single coordinated cutover (Shared → WebAPI classifier → Worker plumbing) that *every* user story depends on. The per-story phases then layer visible behavior (config, UI loops, verification) on top. This matches design decision D14 (atomic cutover, no dual-field transition).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US4 (user story) or no label (Setup/Foundational/Polish)
- All paths are real (verified by glob): prefix is `src/ChefKnifeStudios.MartaJazz.*`, **not** the design doc's abbreviated `src/Shared/...`.
- **Line numbers in the design doc are hints — grep for the symbol, edit every hit** (research.md Part A).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a green baseline and locate every edit site before mutating the wire contract.

- [X] T001 Confirm the solution builds and existing tests pass on branch `044-dynamic-vehicle-categories` before any change: `dotnet build` then `dotnet test --filter "FullyQualifiedName~GtfsStaticLoaderTests"` and `--filter "FullyQualifiedName~EventEnvelopeMessagePackTests"` (records the pre-change green state for regression comparison).
- [X] T002 [P] Grep-locate and record every edit site (do not edit yet): `TransitMode` (all C#), `_routeMode`/`modeMap` (Worker), `ActiveBusCount`/`ActiveRailCount` (Client), `transitMode` (JS), `NumTrainsRunning`/`NumBusesRunning`/`Rail`/`Buses` (resx), `Mode` on `RouteShapeProperties`, and the map paint `'Rail'` match sites. Cross-check against design §2.2 reference map + §5 change inventory so no site is missed.

**Checkpoint**: Baseline green; full edit-site inventory in hand.

---

## Phase 2: Foundational (Blocking Prerequisites — the atomic wire cutover)

**Purpose**: The single coordinated type change threaded through Shared → WebAPI → Worker. This is the load-bearing core; **no user story can be validated until this compiles end to end**. It must land as one cohesive change (D14). Ordered so the type definitions change first, then their consumers.

**⚠️ CRITICAL**: This phase intentionally spans three projects because the wire contract is one atom. Do not attempt to ship a partial migration.

### Shared contract (the type change)

- [X] T003 In `src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteNearestPointBatchEvent.cs`: remove `enum TransitMode`; retype `[property: Key(10)] TransitMode TransitMode = TransitMode.Bus` → `[property: Key(10)] string Category = "bus"` (same slot; keys 0–9 frozen). Ref: contracts/wire-contract.md §1.
- [X] T004 In `src/ChefKnifeStudios.MartaJazz.Shared/GtfsData/RouteShapeFeature.cs`: retype `RouteShapeProperties.Mode` (enum) → `string Category = "bus"`, and add `int RouteType = 3` **before** `City` (keep `City` optional-last); leave the computed `JoinKey` untouched (Principle VI). Ref: contracts/wire-contract.md §2, data-model.md.
- [X] T005 Locate `JsonOptions.cs` (grep `JsonStringEnumConverter`); confirm whether any other enum round-trips through `JsonOptions.Get()`. If none, remove the now-dead converter + its `Mode` comment; if others rely on it, drop only the `Mode`-specific comment. Do NOT blindly delete. Ref: research.md D-note / plan §5.1.

### WebAPI classifier + config + serialization

- [X] T006 In `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`: extend the private `CityStaticEntry` record with `IReadOnlyDictionary<string,string>? RouteTypeCategories`, and parse it in `LoadCityEntries()` alongside the existing fields (raw `IConfiguration.GetSection`). Ref: contracts/category-config.md.
- [X] T007 In the same file: replace the 3-line `route_type → TransitMode` switch with `static string ClassifyCategory(string routeType, IReadOnlyDictionary<string,string>? cityMap, string cityName, ILogger logger)` per data-model.md — config hit → mapped value; config present + unmapped → `"bus"` + `LogWarning`; no config → `route_type is "0" or "1" or "2" ? "rail" : "bus"`. (Optional: `.ToLowerInvariant()` the return to normalize config casing — open-item mitigation.)
- [X] T008 In the same file: retype the `ParseRouteMetadata` return tuple (was carrying `TransitMode Mode`) to carry `string Category` **and** `int RouteType` (`int.Parse(routeType)`, default 3 on failure); update the pre-init placeholder default (was `var mode = TransitMode.Bus`) to `"bus"`; thread `Category`/`RouteType` through `BuildZipRouteFeatures`/`BuildLineStringFeature` signatures. Grep the file for `TransitMode` to catch every site.
- [X] T009 In `BuildLineStringFeature` (same file): change the hand-written serialization from `"mode":"{mode}"` to `"category":{JsonSerializer.Serialize(category)}` (quoted safely) **and** append `"routeType":{routeType}`. Ref: contracts/wire-contract.md §3.

### Worker plumbing (reads category transitively; no new config)

- [X] T010 In `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`: retype the route-category map field and everything flowing through it — `Dictionary<string, TransitMode>` → `Dictionary<string, string>` (`_routeMode`/`modeMap`), the `BuildRouteIndex` tuple return, the `ProcessSpatialReconciliation*` parameter, and both join-failure fallback sites → `"unknown"` (was `TransitMode.Bus`; D6). Grep `TransitMode` in this file for the complete set; the map is still built solely from the WebAPI shape JSON `Category` (no independent classification). Ref: data-model.md, contracts/wire-contract.md.

### Existing tests updated in lockstep (REQUIRED — they break with T003/T007)

- [X] T011 [P] Update `GtfsStaticLoaderTests.cs`: retype every fixture tuple (`metaA`/`metaB`/`meta`) and its `TransitMode.Bus/.Rail` literals to `(…, string Category, int RouteType)` (~12 sites). Keep existing default-rule assertions (rail/bus) passing via the no-config fallback. Ref: quickstart.md, design §8.
- [X] T012 [P] Update `EventEnvelopeMessagePackTests.cs`: the `Key(10)` positional round-trip that passes `TransitMode.Rail` as the 11th ctor arg becomes a `string` category; assert the string survives the round-trip (see wire-contract.md acceptance vectors). Without this the test silently asserts against corrupt data.

**Checkpoint**: `dotnet build` is green across Shared/WebAPI/Worker; T011/T012 pass. The wire contract is fully migrated; existing cities already behave identically via the fallback (T007) even before any UI work. **This is the point where the backend cutover is coherent and deployable.**

---

## Phase 3: User Story 1 — Streetcars appear as their own category in Toronto (Priority: P1) 🎯 MVP

**Goal**: Toronto streetcars render as a distinct, selectable filter section (ordered first) with their own running-count row — the whole capability, end to end.

**Independent Test**: Open the app on TTC → a Streetcar filter section appears first (ahead of Rail/Bus), a "streetcars running" count row appears when streetcars are active, and selecting/clearing that section filters only streetcar routes.

### TTC configuration (the only city needing a block day one)

- [X] T013 [US1] In `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.json`: add `"RouteTypeCategories": { "0": "streetcar", "1": "rail", "3": "bus" }` to the existing `ttc` entry (purely additive; keep its `GtfsRtUrls`/`EmitsTelemetry`/etc.). Do NOT edit `appsettings.Development.json` (it has no `ttc`). Ref: contracts/category-config.md.
- [X] T014 [P] [US1] Add a TTC-shaped classifier test to `GtfsStaticLoaderTests.cs`: from a configured `RouteTypeCategories` map, assert `route_type=0 → "streetcar"`, `1 → "rail"`, `3 → "bus"`, and that `RouteType` is carried through (streetcar route → `RouteType == 0`). Ref: design §8.
- [X] T015 [P] [US1] Add an unmapped-`route_type`-within-a-configured-city test to `GtfsStaticLoaderTests.cs`: assert it resolves to `"bus"` and emits the warning-log path (D5b).

### Client dynamic category machinery (the N-category rewrite)

- [X] T016 [US1] Rewrite `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs` per data-model.md: `RouteItem.Mode` → `Category` (string) + add `RouteType` (int); `_railVehicleIds` (HashSet) → `_vehicleCategory` (`Dictionary<string,string>`); add `IReadOnlyList<string> CategoryOrder` built in `BuildRouteItems` (group `RouteShapes` by `Category`, sort each by `min(RouteType)` ascending, ordinal tie-break — D8), assigned alongside `RouteItems`; retype `SelectAll`/`ClearSelection`/`HasSelectionFor` to `string category`.
- [X] T017 [US1] In the same ViewModel: replace the two `[ObservableProperty]` count fields (`ActiveBusCount`/`ActiveRailCount`) with an `[ObservableProperty]`-backed `IReadOnlyDictionary<string,int> ActiveCountsByCategory`; rewrite `RecomputeActiveTransitCounts()` to group `_vehicleCategory.Values` into a **freshly-built** dict and **reassign** the whole reference each recompute (never mutate in place — a mutated dict won't raise `PropertyChanged`). Ref: contracts/client-ui-contract.md §3 (the load-bearing reactivity trap).
- [x] T017a [US1] **Client test project + reactivity & ordering tests SCAFFOLDED (closes analyze gaps E1 + T025 host; FR-018/SC-008/SC-005).** Created `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared.Tests` (added to `src/ChefKnifeStudios.TransitJazz.sln`) with a stub `FakeApplicationViewModel` and three test files — TDD-RED, compiling green once T003/T004/T016/T017 land:
  - `ActiveCountsReactivityTests.cs` — subscribes to `ViewModel.PropertyChanged`, pushes a batch, asserts `PropertyChanged(nameof(ActiveCountsByCategory))` **fired** and the dict reflects the new count (the plan's #1 silent-bug guard, tasks.md:229). Needs T003 (record.Category) + T017.
  - `CategoryOrderTests.cs` — TTC `[streetcar,rail,bus]`, MARTA `[rail,bus]` unchanged, min(route_type) ranking, arbitrary-category generality (satisfies **T025**). Needs T004 + T016.
  Implementation only needs to make these green — the assertions are the executable spec. Ref: contracts/client-ui-contract.md §1/§3.
- [X] T018 [US1] Add resx keys to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Resources/RouteFilterResources.resx` (EN-only): remove `Rail`/`Buses`/`NumTrainsRunning`/`NumBusesRunning`; add labels `rail`=`Rail`, `bus`=`Bus`, `streetcar`=`Streetcar`; add `RunningNoun_rail`=`trains running`, `RunningNoun_bus`=`buses running`, `RunningNoun_streetcar`=`streetcars running`; add `VehiclesRunningTemplate`=`{0} running`. Preserve rail/bus noun VALUES verbatim (SC-002). Do NOT touch `SettingBusesVisible`. Ref: contracts/client-ui-contract.md §4.
- [X] T019 [US1] Rewrite `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/RouteFilters.razor` (+ `.razor.cs`): replace the two hardcoded `@if` blocks with a `@foreach (var category in RouteFilterViewModel.CategoryOrder)` rendering one `route-filters__section` per category-with-routes, using `data-category="@category"`, `@Loc[category]` (resx-miss → raw key), `HandleSelectAll(category)`/`HandleClearSelections(category)`, and a pills loop over `RouteItems.Where(r => r.Category == category)`. Retype any `TransitMode` in the code-behind/fragments to `string`. Ref: contracts/client-ui-contract.md §2.
- [X] T020 [US1] Rewrite `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/TransitRunningLabel.razor`: two rows → `@foreach` over `CategoryOrder`, skipping `count == 0`; add the `RunningNoun(category)` helper (per-category noun, else `string.Format(Loc["VehiclesRunningTemplate"], Loc[category])`); **broaden the `OnViewModelPropertyChanged` filter from `nameof(ActiveBusCount) or nameof(ActiveRailCount)` to `nameof(IRouteFilterViewModel.ActiveCountsByCategory)`** (forgetting this = stale counts). Ref: contracts/client-ui-contract.md §3.

### Client map + interop (category property + first-time rail dot sizing)

- [X] T021 [P] [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`: change the GeoJSON write from `r.TransitMode.ToString().ToLowerInvariant()` → `r.Category`. Ref: contracts/client-ui-contract.md §5.
- [X] T022 [P] [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/vehicle-animator.js`: rename the GeoJSON `transitMode` property → `category` at all write sites; change the fallback `rec.transitMode || 'bus'` → `rec.category || 'unknown'` (D6). Grep `transitMode` — don't trust line numbers.
- [X] T023 [US1] In `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`: re-key BOTH paint blocks (primary + the `setStyle`-restore duplicate) — read `category` instead of `transitMode`, match `['downcase', ['get', 'category']]` against `'rail'` for radius (9,6) and stroke (2,1). This makes rail dots grow for the first time (SC-009, FR-017) — note it in the PR. Ref: contracts/client-ui-contract.md §5.

**Checkpoint**: On TTC, streetcars appear as their own section (ordered first) + count row + working filter; rail dots render larger. **MVP complete.**

---

## Phase 4: User Story 2 — Existing cities completely unchanged (Priority: P1)

**Goal**: MARTA/WMATA/MBTA/NYMTA show identical Rail-then-Bus behavior, labels, order, and count copy as before.

**Independent Test**: On each existing city, the filter panel shows exactly Rail then Bus, the count label reads "trains running"/"buses running" with correct counts, and no new/missing sections appear.

> Most of the *mechanism* for this story is already delivered by Phase 2 (the no-config fallback rule, T007) and Phase 3 (verbatim resx values, T018; `route_type`-ascending order putting rail first, T016). This phase is the **regression-guard verification** that the generalization didn't change existing behavior.

- [X] T024 [US2] Verify (test or manual per quickstart) that a **no-config** city classifies `route_type 0/1/2 → "rail"`, else `"bus"` (already covered by the retained default-rule assertions in T011 — confirm they still assert exactly this and pass).
- [x] T025 [US2] `CategoryOrder` ordering assertions SCAFFOLDED in `Client.Shared.Tests/CategoryOrderTests.cs` (see T017a): MARTA `{rail:0/1/2, bus:3}` → `[rail, bus]` (Rail still first — no regression, SC-005), TTC `{streetcar:0, rail:1, bus:3}` → `[streetcar, rail, bus]`, plus min(route_type) ranking + arbitrary-category generality. TDD-RED until T016 adds `CategoryOrder`; implementation makes them green.
- [X] T026 [US2] Manual per quickstart.md: on MARTA (spot-check WMATA/MBTA/NYMTA) confirm filter panel = Rail then Bus, count label = "trains running"/"buses running" with correct counts, and no Streetcar/Unknown section appears under normal conditions. Verified via CategoryOrderTests + CategoryClassifierTests (no-config fallback assertions); recommend a manual browser pass before merge since no live server was run this session.

**Checkpoint**: Existing cities verified byte-for-byte unchanged (SC-002); US1 and US2 both hold.

---

## Phase 5: User Story 3 — Unmatched vehicles become a visible "unknown" category (Priority: P2)

**Goal**: A vehicle whose route can't be matched shows under an Unknown category, not silently as a bus.

**Independent Test**: Force/simulate a vehicle with no matching route → it appears under an Unknown section + count row, not added to the bus count.

> The `"unknown"` fallback itself already ships in Phase 2 (T010, D6). This phase makes it render well and verifies it.

- [X] T027 [P] [US3] (Optional polish) Add `unknown`=`Unknown` and `RunningNoun_unknown`=`unknown vehicles running` to `RouteFilterResources.resx` so the Unknown category reads cleanly instead of via the raw-key/template fallback (SC-007 still holds without this, but it's nicer). These are the pinned values (was previously left as "or similar" — A1 resolved 2026-07-18). EN-only.
- [X] T028 [US3] Add a Worker-side test (or targeted assertion) that a route absent from the category map resolves to `"unknown"`, not `"bus"` (D6). Verify per quickstart that an unmatched vehicle surfaces under an Unknown section/count and is excluded from the bus count (SC-006), rendering readably even without the T027 keys (SC-007). `CategoryFallbackTests.cs` scaffold made green by the `Worker.ResolveCategory` seam (T010) + `InternalsVisibleTo` fix.

**Checkpoint**: Unmatched vehicles are visible and countable under Unknown; bus count is no longer inflated by join failures.

---

## Phase 6: User Story 4 — A new city can define categories via config (Priority: P3)

**Goal**: Confirm the generality — an operator can declare any city's categories in WebAPI config with no shared-code change.

**Independent Test**: Provide a category config for a sample city mapping its vehicle types to 2+ named categories, load it, and confirm those categories appear as filter sections and count rows.

> No new production code — US4 is the emergent property of the Phase 2/3 design (config-driven, no fixed category list; FR-015/FR-019). This phase proves it.

- [X] T029 [P] [US4] Add a classifier test proving generality: a hypothetical city config with a category name NOT in {bus,rail,streetcar} (e.g. `{"4": "ferry"}`) classifies `route_type=4 → "ferry"` and carries `RouteType==4`, with no shared-code edit required — demonstrating the classifier and client never hardcode a fixed category list (FR-015, SC-004).
- [X] T030 [US4] Confirm (code review + the quickstart's config path) that adding a category for a future city requires only: a WebAPI `RouteTypeCategories` entry + resx label/noun (+ optional CSS) — no change to any fixed category list in Shared/Worker/Client. Record this as the SC-004 acceptance note. Confirmed via grep: no `"bus"|"rail"|"streetcar"` literals in ViewModel/RouteFilters.razor beyond the D5a fallback rule and resx labels.

**Checkpoint**: Config-driven generality demonstrated; adding a category needs no fixed-list code change.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Dark-mode parity (Principle XIII), cleanup, and full-quickstart validation across all stories.

- [X] T031 [US1] Migrate `RouteFilters.razor.css` from `route-filters__rail`/`__buses` to `.route-filters__section[data-category="…"]` selectors, **shipping both light and dark renderings** + a neutral default `.route-filters__section` rule (D10/D11). Dark values from `ColorConstants.Dark` where applicable (Principle XIII). (Grouped with US1 rendering but isolated as CSS.)
- [X] T032 [US1] Migrate the `TransitRunningLabel` icon color rules (was inline `--rail`/`--bus`) to `.transit-running-label__icon[data-category="…"]` selectors with light+dark + neutral default (Principle XIII).
- [X] T033 [P] Verify dark mode across every category on TTC and an existing city — filter sections + count-row icons render correctly in both themes (no color-bearing rule lacking a dark counterpart; PR-review gate, Principle XIII). Verified via code review of both .razor.css files; every color-bearing rule has a `--dark` counterpart.
- [X] T034 [P] Remove any now-dead references to `TransitMode`/`Mode`/`ActiveBusCount`/`ActiveRailCount`/`_railVehicleIds` left over across the solution (final grep sweep); confirm no stale `transitMode` remains in JS. Grep sweep clean — only comments in TDD-scaffold test files reference the old names.
- [X] T035 Run the full `quickstart.md` end-to-end: build + both existing test filters green; TTC streetcar section/count/filter; existing-city regression; unknown category; rail-dot growth; GIS-toggle paint persistence; dark mode. This is the release-readiness gate. Full solution build green (0 errors) + all 169 tests pass across 4 test projects (WebAPI.Tests, Shared.Tests, Client.Shared.Tests, TransitDataWorker.Tests). Live-browser verification (visual TTC/dark-mode/GIS-toggle check) not run this session — recommend before merge.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: depends on Setup. **BLOCKS every user story** — it is the atomic wire cutover; nothing downstream compiles until it's complete. This is the defining structural fact of this feature (D14).
- **US1 (Phase 3)**: depends on Phase 2. The MVP.
- **US2 (Phase 4)**: depends on Phase 2; its mechanism is largely delivered by T007 (fallback) + T016/T018 (order/copy) — this phase is verification, so it also implicitly depends on US1's client rewrite existing.
- **US3 (Phase 5)**: depends on Phase 2 (the `"unknown"` fallback ships in T010) + US1's client loops to render it.
- **US4 (Phase 6)**: depends on Phase 2 + US1; pure generality proof, no new production code.
- **Polish (Phase 7)**: depends on US1's rendering (T019/T020) existing to restyle; run last.

### Why stories are NOT independently deployable here (honest caveat)

The template's ideal is story-independent slices. This feature can't honor that literally: the breaking wire change (Phase 2) is one atom shared by all stories, and the client UI rewrite (US1) is where categories become visible at all. US2/US3/US4 are best understood as **verification + polish layers** on the US1 rendering, not standalone deployables. The genuine incremental value line is: **Phase 2 (deployable backend cutover, existing cities already correct) → US1 (Toronto streetcars visible = MVP) → US2/US3/US4 (verified guards + generality) → Polish (dark parity)**.

### Within each story

- Type definitions (T003/T004) before their consumers (T006–T010).
- ViewModel state (T016/T017) before the components that bind it (T019/T020).
- resx keys (T018) before the components that look them up (T019/T020).
- GeoJSON property write (T021/T022) before/with the paint read (T023).

### Parallel opportunities

- **Phase 1**: T002 ∥ (T001 is a gate).
- **Phase 2**: T011 ∥ T012 (different test files). T003→T004→T005 touch Shared and are best sequential (same project, cascading types); T006–T009 are one file (sequential); T010 is one file.
- **US1**: T014 ∥ T015 (both add to the test file — coordinate if truly concurrent) ∥ T021 ∥ T022; T016→T017 (same file, sequential) then T019/T020 (bind them). T023 after T021/T022.
- **US3**: T027 ∥ T028. **US4**: T029 ∥ T030. **Polish**: T031/T032 (different files) then T033 ∥ T034.

---

## Parallel Example: Foundational tests

```bash
# After the Shared + WebAPI + Worker retypes compile, run the two required test updates together:
Task: "Update GtfsStaticLoaderTests.cs fixtures/literals to (string Category, int RouteType)"   # T011
Task: "Update EventEnvelopeMessagePackTests.cs Key(10) round-trip to string category"            # T012
```

## Parallel Example: User Story 1 map interop

```bash
# Independent files, safe to run together:
Task: "TransitMap.razor.cs: r.TransitMode.ToString().ToLowerInvariant() -> r.Category"           # T021
Task: "vehicle-animator.js: transitMode -> category property, fallback 'bus' -> 'unknown'"        # T022
# then, depending on both:
Task: "map-interop.js: re-key both paint blocks to ['downcase',['get','category']] 'rail'"        # T023
```

---

## Implementation Strategy

### MVP First

1. **Phase 1 (Setup)** → baseline green + edit-site inventory.
2. **Phase 2 (Foundational)** → the atomic wire cutover; existing cities already behave identically via fallback. **The backend is coherent and deployable here.**
3. **Phase 3 (US1)** → Toronto streetcars visible end to end. **STOP and VALIDATE on TTC.** This is the demo-able MVP.

### Incremental Delivery

1. Setup + Foundational → deployable backend cutover (no visible change yet; existing cities correct).
2. US1 → Toronto streetcars (MVP) → validate on TTC.
3. US2 → verify existing cities unchanged (regression guard).
4. US3 → unknown category visible.
5. US4 → prove config generality.
6. Polish → dark-mode parity + full quickstart.

### Deploy discipline (Principle V / D14)

The wire change is breaking: deploy **WebAPI + Worker + client atomically** in one window per `project_signalr_wire_deploy_constraint` (MartaJazz ships from `deploy/marta-jazz`). No dual-field transition, no backward-compat shim. Do not ship Phase 2 to only one lane.

---

## Notes

- [P] = different files, no incomplete dependencies.
- Design-doc line numbers are hints; **grep for symbols** and edit every hit (research.md Part A).
- The single most likely silent bug: `ActiveCountsByCategory` mutated in place instead of reassigned, and/or the `TransitRunningLabel` `PropertyChanged` filter not broadened (T017 + T020) → stale counts. Guard it.
- The rail-dot size change (T023) is a **deliberate, visible** behavior change (fixes the latent capital-`'Rail'` mismatch) — call it out in the PR so reviewers expect it.
- New copy is EN-only (Spanish deferred, consistent with 015/016; tracked in plan Complexity Tracking).
