# Tasks: NYC MTA Bus Support

**Input**: Design documents from `specs/041-nymta-bus-support/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: INCLUDED — the spec explicitly requires verifiable route normalization (US2 Independent Test + SC-006), so `RouteIdNormalizer` unit tests are in scope. No other test types requested.

**Organization**: Grouped by user story. US1 (buses on map) + US2 (route matching) are co-critical P1; US3 (second operator) is P2. The MVP is US1+US2 together, because a bus map without route matching is not viable.

**On-disk namespace note**: solution folders are `ChefKnifeStudios.MartaJazz.*` (not `TransitJazz`). Paths below are exact.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 / FOUND (foundational) / POLISH

---

## Phase 1: Setup

No project scaffolding needed — all target projects exist. Nothing to do here.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared constant + config field every story depends on. Small, must land first.

- [X] **T001** [FOUND] Add `public const string NymtaBus = "nymta-bus";` to `src/ChefKnifeStudios.MartaJazz.Shared/CityNames.cs`.
- [X] **T002** [FOUND] Add `public string[] RouteIdNormalization { get; set; } = [];` to `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/CityConfig.cs`. (Default `[]` ⇒ inert for all existing cities.)

**Checkpoint**: Constant + config field exist; stories can proceed.

---

## Phase 3: User Story 2 — Buses match their real routes (Priority: P1) 🎯

> Sequenced **before** US1 implementation because US1's "buses render correctly" depends on route IDs matching. This is the one genuinely new piece of logic.

**Goal**: `Trip.RouteId` values from the obanyc feed are normalized so ≥98% match the static registry (SC-002).

**Independent Test**: Run `RouteIdNormalizerTests` — `Q06→Q6`, `M15+→M15-SBS`, `bx3→BX3`, unknown-step no-op, empty-steps passthrough all pass.

### Tests for User Story 2 (write FIRST, ensure they FAIL before T004) ⚠️

- [X] **T003** [P] [US2] Create `RouteIdNormalizerTests` in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/RouteIdNormalizerTests.cs` — xUnit `[Theory]`/`[InlineData]` covering all 12 accept vectors + invariants from `contracts/route-id-normalizer.md`. (Will not compile until T004 exists — that is the intended red state.)

### Implementation for User Story 2

- [X] **T004** [US2] Create `RouteIdNormalizer` static class in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/RouteIdNormalizer.cs`: `Apply(string, IReadOnlyList<string>)` folding named steps `uppercase` / `plusToSbs` / `stripLeadingZeros` (regex `^([A-Z]+)0*(\d.*)$`), default arm = no-op passthrough (never throws). Per `data-model.md` §2 and the contract.
- [X] **T005** [US2] Wire normalization into the RT pipeline: in `src/Server/.../Cities/GtfsRtCity.cs`, add `void ApplyRouteIdNormalization(FeedMessage feed)` (early-return when `config.RouteIdNormalization is not { Length: > 0 }`; else rewrite each `entity.Vehicle?.Trip?.RouteId` via `RouteIdNormalizer.Apply`) and call it right after the existing `ApplyRailRouteIdMap(merged);` at line ~37.
- [X] **T006** [US2] Verify T003 now passes (`dotnet test ...TransitDataWorker.Tests --filter RouteIdNormalizerTests`).

**Checkpoint**: Normalization is correct and unit-proven in isolation. No feed needed.

---

## Phase 4: User Story 1 — See NYC buses moving on the map (Priority: P1) 🎯 MVP

**Goal**: Selecting "New York Buses" shows live buses moving, correctly route-matched (relies on US2).

**Independent Test**: Pick New York Buses → markers appear and move over several ticks on real streets.

### Implementation for User Story 1

- [X] **T007** [US1] Resolve the credential mechanism (research R4): verify whether the Worker's config layering expands `${NYMTA_BUS_API_KEY}` inside a `GtfsRtUrls` string. If yes → use the pre-templated URL (below). If no → add `public string ApiKeyQueryParam { get; set; } = "api_key";` to `CityConfig.cs` and change `GtfsRtCity.FetchFeedAsync`'s `?api_key=` to use `config.ApiKeyQueryParam`; set `"key"` for `nymta-bus`. **Ensure no live key is committed either way.**
- [X] **T008** [US1] Add the `nymta-bus` entry to `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.json` `Cities:` array per `contracts/city-config.md` (RT URL, 6 static zips, `RouteIdNormalization`, `EmitsTelemetry: true`, credential per T007). Mirror into `appsettings.Development.json` if that file carries a `Cities:` block.
- [X] **T009** [US1] Add the identical `nymta-bus` entry to `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.json` `Cities:` array (WebAPI's `GtfsStaticLoader` needs `StaticZipUrls`; RT/normalization fields harmlessly present). Mirror Development variant if present.
- [X] **T010** [P] [US1] Add resx key `CityNymtaBus` (value e.g. "New York Buses") to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Resources/RouteFilterResources.resx` (Principle XII — no inline copy for the new label).
- [X] **T011** [US1] Add a "New York Buses" button to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/CityFab.razor`: a `MatButton` bound to a new `HandleNymtaBusClicked` handler doing `location.hash='nymta-bus';location.reload()`, `Disabled="@(CurrentCity == CityNames.NymtaBus)"`, `Label` from `IStringLocalizer<RouteFilterResources>["CityNymtaBus"]`. (Inject `IStringLocalizer` if not already; existing inline labels left as-is per research R5.)

**Checkpoint**: End-to-end MVP — NYC buses render and move, route-matched, telemetry on.

---

## Phase 5: User Story 3 — Both NYC operators appear (Priority: P2)

**Goal**: MTA Bus Company-only routes (QM/BXM express, Q06-series) also resolve and render.

**Independent Test**: At least one Bus Co-only route shows buses on the map, alongside NYCT routes.

> No new code — US3 is satisfied by the 6-zip `StaticZipUrls` list (Bus Co zip included) delivered in T008/T009. This phase is verification only.

- [X] **T012** [US3] Verify the MTA Bus Company zip is present in both `Cities:` `nymta-bus` `StaticZipUrls` blocks (T008/T009) and that its routes load: run the WebAPI, confirm `GtfsStaticLoader` merges all 6 zips (per-zip failures log-and-continue, FR-010), and that a Bus Co-only route resolves in the all-shapes endpoint for `nymta-bus`.

**Checkpoint**: Both operators covered.

---

## Phase 6: Polish & Cross-Cutting

- [X] **T013** [POLISH] Regression guard: run the full `...TransitDataWorker.Tests` suite; confirm all pre-existing tests still green (proves normalization is inert for `marta`/`wmata`/`mbta`/`nymta` — SC-004).
- [X] **T014** [POLISH] Run `quickstart.md` steps 3–6 end-to-end (config load, static merge, buses on map, telemetry). Confirm `skippedUnknownRoute` is a small fraction of vehicles (SC-002 ≥98%).
- [ ] **T015** [P] [POLISH] (Optional) Convert the four legacy inline `CityFab` labels to resx keys to fully close Principle XII debt — out of required scope; do only if tidying.

---

## Dependencies & Execution Order

- **T001, T002 (Foundational)** block everything.
- **US2 (T003→T004→T005→T006)** before US1 rendering is meaningful (route matching underpins "buses render correctly"). T003 before T004 (TDD red→green).
- **US1 (T007→T008→T009, T010, T011)**: T007 gates T008 (credential shape). T010/T011: add resx key before binding it. T010 is [P] with T008/T009 (different files).
- **US3 (T012)**: verification of T008/T009 config; no code.
- **Polish (T013–T015)**: after the stories you intend to ship.

### Parallel opportunities

- T003 (test file) is [P] — independent file, written before T004.
- T010 (resx) is [P] with the appsettings edits (T008/T009).
- T015 is [P] optional cleanup.

---

## Implementation Strategy

**MVP = US2 + US1** (T001–T011): normalization proven, then buses on the map. Stop and validate via quickstart §5. US3 (T012) is config-verification only and typically already satisfied by the MVP config. Ship after T014 passes.

## Notes

- The single "confirm before shipping" item is **T007** (credential query-param mechanism) — resolve it before wiring config.
- Buses will not appear until `NYMTA_BUS_API_KEY` is obtained and set in the Worker environment (operational prerequisite, not a code task).
- No `Program.cs` change (existing `else` arm), no new SignalR event, no schema/migration, no new deployable.
