# Tasks: City Slug Migration

**Input**: Design documents from `/specs/052-city-slug-migration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Test tasks ARE included — plan.md defines a mandatory 4-tier Testing Strategy, and the
feature's central risk (a constant-only rename compiles clean and breaks at runtime) is only
detectable by test. These are not optional here.

**Organization**: Tasks are grouped by user story. Note the unusual shape of this feature:
Phase 2 (Foundational) carries the highest-risk work (the telemetry split), because US1's slug
change silently corrupts telemetry if it lands first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Existing 11-project .NET solution; no new projects. Roots:

- Shared: `src/ChefKnifeStudios.TransitJazz.Shared/`
- Client: `src/Client/ChefKnifeStudios.TransitJazz.Client.{Core,Shared,WebApp}/`
- Server: `src/Server/ChefKnifeStudios.TransitJazz.Server.{WebAPI,TransitDataWorker}/`
- Tests: the 4 existing `*.Tests` projects

---

## ⚠️ Gate: Read Before Starting

- **FR-023 — do NOT begin before the 051 Phase 3 `batch_wire_bytes` baseline window closes.**
  Verify this first (T001). Migrating during the window destroys the ≥3-day baseline.
- **Ordering is load-bearing.** Phase 2 (telemetry split) MUST be complete and green before
  Phase 3 changes any slug value. Reversing them silently rewrites parquet history (FR-016).
- **Do NOT edit these existing test fixtures.** `TelemetryEventSchemaTests.cs:27,97` and
  `ChannelLoadSheddingTests.cs:35,83` assert `city_name = "MARTA"`. Under the split they stay
  correct. "Fixing" them to `"atlanta"` silently defeats FR-016.

---

## Phase 1: Setup (Preconditions & Baseline)

**Purpose**: Confirm the gate is open and capture the pre-migration state that later tasks pin against.

- [X] T001 Verify the 051 Phase 3 `batch_wire_bytes` baseline window has closed (FR-023) by checking `specs/051-*/` for the baseline end date; if still open, STOP and do not proceed
- [X] T002 Establish a green baseline: run `dotnet build ChefKnifeStudios.TransitJazz.sln` and `dotnet test ChefKnifeStudios.TransitJazz.sln`, recording the passing test count so later regressions are attributable
- [X] T003 [P] Record the pre-migration map origins from `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` `_cityCenter` (7 lat/lon pairs) into a scratch note; these become the expected values pinned by T033 (FR-014)
- [X] T004 [P] Confirm the current production telemetry `city_name` values for all 7 cities so `TelemetryName` is frozen at real values, not guessed — research R1 verified `MARTA` only; query via the telemetry MCP bridge for `SELECT DISTINCT city_name`

**Checkpoint**: Gate open, build green, pre-migration values captured.

---

## Phase 2: Foundational (Telemetry Split — BLOCKS ALL USER STORIES)

**Purpose**: Decouple the telemetry city value from `ITransitCity.Name` **before** any slug moves,
so that changing `Name` cannot touch parquet history.

**⚠️ CRITICAL**: This is the highest-risk phase in the feature. `Worker.cs:103` writes
`city_name = result.CityName`, sourced from `city.Name`. Until `TelemetryName` exists and Worker
reads it, renaming `CityNames.Marta` silently rewrites `city_name` (research R1). No user story
work may begin until T012 is green.

**Values are unchanged in this phase; behaviour is unchanged.** It is a pure refactor whose only
observable effect is that telemetry now reads a distinct property.

### Tests for Foundational (write first — T005–T007 must FAIL before T008–T011)

- [X] T005 [P] Add `telemetry_name_is_agency_valued_not_slug` to a new `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/TelemetryNameSplitTests.cs` — asserts each city's `TelemetryName` equals its frozen agency string and is NOT equal to its `Name` (**the FR-016 guard**)
- [X] T006 [P] Add `telemetry_name_values_are_frozen` to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/TelemetryNameSplitTests.cs` — asserts the exact expected set `MARTA`, `WMATA`, `MBTA`, `NYMTA`, `TTC`, `SEPTA`, `RTD`, so a later "tidy-up" to slugs fails loudly
- [X] T007 [P] Add `per_city_cycle_writes_telemetry_name_as_city_name` to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/TelemetryNameSplitTests.cs` — drives a tick over a fake `ITransitCity` whose `Name` and `TelemetryName` differ, and asserts the emitted `TelemetryEvent.city_name` equals `TelemetryName` (behaviour, not implementation)

### Implementation for Foundational

- [X] T008 Add `string TelemetryName { get; }` to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/ITransitCity.cs`
- [X] T009 [P] Implement `TelemetryName => "MARTA"` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/MartaCity.cs` and `TelemetryName => "NYMTA"` in `.../Cities/NymtaCity.cs` (both currently hardcode `Name => CityNames.*`)
- [X] T010 Add `TelemetryName` to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/CityConfig.cs` and surface it as `TelemetryName => config.TelemetryName` in `.../Cities/GtfsRtCity.cs` — the 5 config-driven cities (wmata, mbta, ttc, septa, rtd) derive `Name` from config, so they need a config-sourced telemetry value; populate the new key in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` for all 7 entries with the frozen uppercase agency values from T004
- [X] T011 Change `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` so `CityTickResult.CityName` is populated from `city.TelemetryName` instead of `city.Name` (`Worker.cs:86,:92`), leaving the `city_name = result.CityName` write at `:103` intact
- [X] T012 Run `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests` and confirm T005–T007 pass AND the pre-existing `TelemetryEventSchemaTests`/`ChannelLoadSheddingTests` `"MARTA"` assertions still pass unmodified

**Checkpoint**: Telemetry is decoupled. Slug values may now move safely. **Do not proceed past this line until T012 is green.**

---

## Phase 3: User Story 1 — A visitor reaches a city and hears it (Priority: P1) 🎯 MVP

**Goal**: All seven cities load at their new city-name slugs, join the correct realtime group, and
receive live vehicles and audio — exactly as they did under agency slugs.

**Independent Test**: Load each of the seven new slugs in turn and confirm vehicles render and
move, and that audio triggers on crossings. Fully independent of US2 and US3.

### Tests for User Story 1 (write first)

- [X] T013 [P] [US1] Create `src/ChefKnifeStudios.TransitJazz.Shared.Tests/CitySlugTests.cs` with `every_city_slug_conforms_to_format_rule` — all 7 `CityNames` values match `^[a-z0-9]+(-[a-z0-9]+)*$` (contract C1)
- [X] T014 [P] [US1] Add `city_slugs_are_unique` and `city_slugs_contain_no_agency_names` to `src/ChefKnifeStudios.TransitJazz.Shared.Tests/CitySlugTests.cs` — the latter asserts no value is `marta`/`wmata`/`mbta`/`nymta`/`ttc`/`septa`/`rtd`, catching a half-finished rename
- [X] T015 [P] [US1] Add `city_slugs_survive_uri_fragment_round_trip` to `src/ChefKnifeStudios.TransitJazz.Shared.Tests/CitySlugTests.cs` — `Uri.EscapeDataString` → unescape → `ToLowerInvariant` is identity, guarding the hyphenated `washington-dc` and `new-york-city`
- [X] T016 [P] [US1] Add `join_hub_method_is_versioned` to `src/ChefKnifeStudios.TransitJazz.Shared.Tests/CitySlugTests.cs` — asserts `HubMethods.JoinCity` has the value `"JoinCityV2"` (contract C2)
- [X] T017 [P] [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/CityConfigParityTests.cs` with `city_config_names_match_across_both_appsettings` — compares the **set** of `Cities[].Name` in both `appsettings.json` files (not whole-file equality; the files legitimately differ elsewhere). **SC-007/FR-006**
- [X] T018 [P] [US1] Add `every_registry_slug_has_a_config_entry` to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/CityConfigParityTests.cs` — no `CityNames` value lacks a config entry and vice versa, catching registry/config drift the parity test alone would miss
- [X] T019 [P] [US1] Add `no_agency_slug_literal_remains_in_client_source` to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/CitySlugLiteralGuardTests.cs` (new file) — scans `CityFab.razor` source for `location.hash='<agency>'`, guarding contract C3
- [X] T020 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ResolveCityTests.cs` — `ResolveCity()` returns the fragment lowercased, returns the default slug for empty/whitespace/malformed input, and resolves `#ATLANTA` and `#Atlanta` both to `atlanta`, using a stubbed `NavigationManager`
- [X] T021 [P] [US1] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/WorkerTransitHubTests.cs` with `join_city_v2_adds_connection_to_group_named_by_slug` — spy on `IGroupManager` to capture the group name and assert it equals the slug passed, verbatim
- [X] T022 [P] [US1] Add `legacy_join_city_method_is_absent` to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/WorkerTransitHubTests.cs` — reflects over `TransitHub` and asserts no `JoinCity` method exists, proving the shim contract C2 forbids was not reintroduced
- [X] T023 [P] [US1] Add `join_city_v2_replays_cached_batch_for_that_city` to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/WorkerTransitHubTests.cs` with a stubbed `ILastBatchCache` — existing replay behaviour preserved under the new name

### Implementation for User Story 1

- [X] T024 [US1] Change the 7 constant VALUES in `src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs` — `Marta="atlanta"`, `Wmata="washington-dc"`, `Mbta="boston"`, `Nymta="new-york-city"`, `Ttc="toronto"`, `Septa="philadelphia"`, `Rtd="denver"`. **Leave the constant IDENTIFIERS unchanged** (`CityNames.Marta` still resolves Atlanta) — the DI branches in both `Program.cs`, `GtfsStaticLoader`'s NYMTA case, `_cityCenter`, and both overlay switch arms reference the constants and follow automatically
- [X] T025 [US1] Rename `HubMethods.JoinCity` value `"JoinCity"` → `"JoinCityV2"` in `src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs` (identifier stays `JoinCity`; only the value gates)
- [X] T026 [US1] Replace the 7 hardcoded `location.hash='<agency>'` literals with the new slugs in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/CityFab.razor:48,55,60,65,70,75,80` — prefer interpolating `CityNames.*` so contract C3's single-source-of-truth holds by construction rather than by convention
- [X] T027 [P] [US1] Update the 7 `Cities[].Name` values in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json:4,14,28,34,59,65,71` to the new slugs, leaving every sibling key (feed URLs, `RailRouteIdMap`, `StaticZipUrls`, and the `TelemetryName` added in T010) untouched
- [X] T028 [P] [US1] Update the 7 `Cities[].Name` values in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json:34,44,71,77,102,109,115` to the new slugs — must match T027's set exactly or the parity test (T017) fails
- [X] T029 [US1] Rename `TransitHub.JoinCity` → `JoinCityV2` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/TransitHub.cs:21`, signature unchanged, and update its log message to name the new method (contract C3/FR-010). **Do NOT keep a `JoinCity` shim** — retaining it recreates the silent failure the gate exists to prevent
- [X] T030 [US1] Verify the two `_hubConnection.InvokeAsync(HubMethods.JoinCity, ...)` call sites at `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/SignalRNotificationService.cs:101,105` now resolve to `"JoinCityV2"` via the constant, and confirm the `:101` reconnect path re-invokes join on reconnect (FR-011, contract C4 — "verify, don't assume")
- [X] T031 [US1] Confirm `ResolveCity()` in `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/NavigationManagerExtensions.cs` needs no edit — its fallback is `CityNames.Marta`, which now returns `atlanta` automatically
- [X] T032 [US1] Confirm the default-fragment navigation at `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs:105` uses `CityNames.Marta` and so emits `#atlanta` with no edit
- [X] T033 [US1] Add `every_city_has_a_map_origin` to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/` — asserts all 7 new slugs are keys in `_cityCenter` and that the coordinates equal the values captured in T003, pinning origins against accidental edits (**FR-014**)
- [X] T034 [US1] Run `dotnet build ChefKnifeStudios.TransitJazz.sln` and `dotnet test ChefKnifeStudios.TransitJazz.sln`; confirm T013–T023 and T033 pass and no pre-existing test regressed against the T002 baseline

**Checkpoint**: All seven cities resolve, join, and stream under their new slugs. US1 is independently testable via local run.

---

## Phase 4: User Story 2 — Each city reads as itself (Priority: P2)

**Goal**: Each city's audio-unlock overlay and info panel resolve that city's own copy, with no
missing strings and no agency token shown as city identity.

**Independent Test**: Visit each new slug and confirm the audio-unlock overlay, the info panel,
and the picker all show that city's correct copy.

**Note**: The two switch expressions already match on `CityNames.*` constants, so their *match arms*
follow T024 automatically. Only the resx **key prefixes** and the switch **result strings** move —
and they must move in lockstep or a string goes missing (FR-012/SC-005).

### Tests for User Story 2 (write first)

- [X] T035 [P] [US2] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/CityCopyResolutionTests.cs` with `every_city_resolves_audio_overlay_copy` — for all 7 slugs, each `*AudioOverlayHeader` and `*AudioOverlayParagraph1-3` key resolves to a non-empty string that is not the key name itself (**SC-005**; catches a half-renamed prefix)
- [X] T036 [P] [US2] Add `every_city_resolves_info_overlay_copy` to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/CityCopyResolutionTests.cs` — same for the 7 `*OverlayParagraph1` keys used by `InfoFab`

### Implementation for User Story 2

- [X] T037 [US2] Re-prefix the 30 agency-prefixed keys in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx` — `Wmata*`→`WashingtonDc*`, `Mbta*`→`Boston*`, `Nymta*`→`NewYorkCity*`, `Ttc*`→`Toronto*`, `Septa*`→`Philadelphia*`, `Rtd*`→`Denver*` (6 cities × 5 keys). Values are unchanged; Atlanta's unprefixed default keys are untouched
- [X] T038 [US2] Update the result strings in the `_prefix` switch at `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/AudioUnlockOverlay.razor:261-268` to the new prefixes (`"WashingtonDcAudioOverlay"`, `"BostonAudioOverlay"`, …), leaving the `CityNames.*` match arms and the `_ => "AudioOverlay"` default intact
- [X] T039 [US2] Update the result strings in the `_prefix` switch at `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/InfoFab.razor:46-53` to the new prefixes (`"WashingtonDcOverlay"`, `"BostonOverlay"`, …), leaving the match arms and `_ => "InfoOverlay"` default intact
- [X] T040 [US2] Confirm the `CityFab.razor` picker labels (`Atlanta, GA`, `Washington DC`, `Boston, MA`, `New York, NY`, `Toronto, ON`, `Philadelphia, PA`, `Denver, CO`) are already city names needing no edit (contract C2), and that the `Disabled` checks on lines 17–35 compare `CityNames.*` constants so they follow T024
- [X] T041 [US2] Run `dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests` and confirm T035–T036 pass — zero missing strings across all 7 cities

**Checkpoint**: US1 and US2 both work independently. Every visitor-facing surface reads as a city.

---

## Phase 5: User Story 3 — Operators can still interpret telemetry (Priority: P3)

**Goal**: Telemetry history stays one continuous series per city across the cutover date, on the
unchanged agency values.

**Independent Test**: Query telemetry spanning the cutover date and confirm each city's history is
continuous, with no gap or duplicate-identity split at the migration boundary.

**Note**: The build work for this story already landed in Phase 2 — it had to, because US1 would
otherwise have corrupted the data. What remains here is verification and documentation, exactly as
spec.md predicted ("a *verification* task rather than a build task").

- [X] T042 [P] [US3] Confirm by inspection that `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` sources `CityTickResult.CityName` from `TelemetryName` and that `TelemetryName` reaches no SignalR group, `?city=` parameter, config `Name` key, or URL (contract C4 MUST NOT)
- [X] T043 [P] [US3] Confirm the pre-existing `city_name = "MARTA"` assertions at `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/TelemetryEventSchemaTests.cs:27,97` and `.../ChannelLoadSheddingTests.cs:35,83` are **unmodified** by this branch — `git diff main -- <those files>` must be empty. A diff here means FR-016 is already broken
- [X] T044 [P] [US3] Confirm `tools/telemetry-mcp/` needs no change — `city_name`'s column name, type, and values are all unchanged, so the allow-list stays valid (FR-019); run its existing `validate_test.go` to prove it
- [X] T045 [US3] Document the intentional slug↔telemetry divergence (FR-018) in `docs/CITY_SLUG_MIGRATION_ASSESSMENT.md` or a sibling doc — a table mapping each city slug to its frozen agency `city_name`, stating the divergence is deliberate per FR-016 so a future reader does not "fix" it

**Checkpoint**: All three user stories independently verified. Ready for cutover.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T046 [P] Record the slug rule (contract C1: full city name, lowercase, hyphen-separated, region suffix only to disambiguate) in `.claude/skills/add-transit-city/SKILL.md`, replacing agency-named examples with city-named ones (FR-002)
- [X] T047 [P] Record the same slug rule in `.claude/skills/discover-transit-city/SKILL.md` so autonomously minted slugs conform (**FR-003/SC-010**); note that `docs/city-compat/*.md` filenames are agency documents, NOT city slugs, and are out of scope (contract C6)
- [X] T048 Run the quickstart Step 1 literal sweep and confirm **zero** matches: `Select-String -Path "src/Client/**/CityFab.razor" -Pattern "hash='(marta|wmata|mbta|nymta|ttc|septa|rtd)'"` and `Select-String -Path "src/Server/**/appsettings.json" -Pattern '"Name":\s*"(marta|wmata|mbta|nymta|ttc|septa|rtd)"'` (FR-007)
- [X] T049 Run the full solution build and test suite one final time; confirm zero regressions against the T002 baseline and that the telemetry fixtures are still green and unmodified

---

## Phase 7: Cutover (Manual — Tier 3, per quickstart.md)

**Purpose**: Deploy across three lanes without any city going silently dark. **Deploy order is
load-bearing** (contract C5) — shipping the client first breaks it for *every* user, not just stale
sessions.

- [ ] T050 Execute quickstart Step 0 — prove telemetry is decoupled: `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests --filter "FullyQualifiedName~Telemetry"`. **If this fails, STOP** — the rename will rewrite parquet history
- [ ] T051 Deploy **server + worker together, atomically** (quickstart Step 2); confirm worker logs show ticks for all 7 cities under new slugs and WebAPI logs show `JoinCityV2 ... joined group '<new-slug>'` with group names exactly matching the worker's publish targets. Loud join failures from old clients during this window are the version gate working (FR-009), not a bug
- [ ] T052 Deploy the **client** (quickstart Step 3); confirm a fresh `#atlanta` load joins and receives vehicles, no unversioned `JoinCity` invocations remain in logs, and a hard-refreshed old session recovers (FR-011)
- [ ] T053 Apply the identical change to the `deploy/marta-jazz` branch and repeat the T052 checks there — the MartaJazz deployment breaks otherwise (contract C5 step 3)
- [ ] T054 Execute quickstart Step 4 per-city verification for **all seven** slugs — `#atlanta`, `#washington-dc`, `#boston`, `#new-york-city`, `#toronto`, `#philadelphia`, `#denver`. Per city: map centered correctly, **vehicles appear and move**, count non-zero and plausible, **a crossing produces audio**, shapes render, both overlays show that city's copy, picker behaves, join logged with expected group name, no console errors. **Do not sample** (FR-022). **"No errors" is not evidence** — a silent group mismatch produces clean logs on both sides; only observed vehicle arrival closes SC-003
- [ ] T055 Execute quickstart Step 5 — confirm new parquet rows still carry agency `city_name`, and that `city_name = 'MARTA' AND observation_utc > '<cutover-minus-2>'` returns an unbroken row count across the boundary with no new distinct value (**SC-006**)
- [ ] T056 Communicate the known accepted consequences from quickstart Step 6 to anyone watching dashboards — old `#wmata`-style links fall through to the default city silently, Umami shows `/marta` and `/atlanta` as two separate paths (not a traffic drop), and telemetry intentionally diverges from the slug

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies. T001 is a hard gate — a `no` answer stops the feature.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS all user stories.** This is stricter than
  the usual template convention: Phase 3's T024 actively corrupts data if Phase 2 has not landed.
- **US1 (Phase 3)**: Depends on Phase 2 complete and green (T012).
- **US2 (Phase 4)**: Depends on Phase 2. Independently testable, but its switch-arm match values
  come from T024, so in practice it follows US1.
- **US3 (Phase 5)**: Depends on Phase 2 (which contains its implementation). Verification-only.
- **Polish (Phase 6)**: After US1–US3.
- **Cutover (Phase 7)**: After Phase 6. Strictly sequential — T051 → T052 → T053 → T054.

### User Story Dependencies

- **US1 (P1)**: The MVP. No dependency on US2 or US3.
- **US2 (P2)**: Logically independent (copy resolution vs. data flow), but shares the `CityNames`
  values changed in T024. Sequence US1 → US2 unless working in parallel with a shared T024.
- **US3 (P3)**: Fully independent — its build work is in Phase 2 and its remaining tasks are reads.

### Within Each User Story

- Tests are written first and MUST fail before the implementation tasks in the same phase.
- Registry value change (T024) before dependent literal fixes (T026–T028).
- Resx keys (T037) before switch result strings (T038–T039) — or between them, but **never leave
  the two out of sync in a commit**.

### Critical Path

```
T001 → T002 → [T005–T007] → T008 → [T009, T010] → T011 → T012
     → T024 → [T026, T027, T028] → T029 → T030 → T034
     → T037 → [T038, T039] → T041
     → T049 → T050 → T051 → T052 → T053 → T054
```

### Parallel Opportunities

- **Phase 1**: T003, T004 in parallel.
- **Phase 2 tests**: T005, T006, T007 in parallel (same new file — coordinate, or write as one commit).
- **Phase 2 impl**: T009 and T010 in parallel (different city classes/config paths).
- **US1 tests**: T013–T023 all parallel — four different test projects, no shared files.
- **US1 impl**: T027 and T028 parallel (two different `appsettings.json`); T031, T032 are
  independent read-only confirmations.
- **US2 tests**: T035, T036 parallel.
- **US3**: T042, T043, T044 all parallel (pure verification, no writes).
- **Polish**: T046, T047 parallel (two different skill files).
- **Phase 7**: none — deploy order is strictly sequential by contract C5.

---

## Parallel Example: User Story 1 Tests

```bash
# Launch all US1 test-writing tasks together — four separate test projects:
Task: "CitySlugTests.cs slug-format/uniqueness/round-trip/hub-version in src/ChefKnifeStudios.TransitJazz.Shared.Tests/"
Task: "CityConfigParityTests.cs appsettings parity + registry coverage in src/Server/...Server.WebAPI.Tests/"
Task: "ResolveCityTests.cs + CitySlugLiteralGuardTests.cs in src/Client/...Client.Shared.Tests/"
Task: "WorkerTransitHubTests.cs JoinCityV2 group/absence/replay in src/Server/...Server.WebAPI.Tests/"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup — confirm the FR-023 gate is open.
2. Phase 2: Foundational — **the telemetry split. Non-negotiable, must be green first.**
3. Phase 3: US1 — the rename itself.
4. **STOP and VALIDATE**: run locally (Aspire host), walk all seven slugs, confirm vehicles arrive.
5. US1 alone is a shippable increment — cities work under new names; only copy prefixes lag.

### Incremental Delivery

1. Setup + Foundational → telemetry safe, nothing user-visible changed.
2. US1 → all 7 cities live under new slugs (MVP).
3. US2 → per-city copy re-keyed.
4. US3 → telemetry continuity verified and documented.
5. Polish → skills record the rule for future cities.
6. Cutover → three lanes in order, then verify all 7.

### Why This Feature Deviates From "Foundational Is Just Plumbing"

Normally Phase 2 is scaffolding. Here it carries the feature's single highest risk. Research R1
found that the property being renamed (`ITransitCity.Name`) is the same property telemetry reads,
so the "do nothing to telemetry" requirement (FR-016) is **positive work**, not a no-op. If a
reviewer sees Phase 2 as optional cleanup and lets T024 land first, parquet history splits at the
cutover date and the 051 Phase 3 baseline is destroyed — irreversibly, since parquet is
append-only and immutable.

---

## Notes

- **[P]** = different files, no dependencies.
- The constant **identifiers** (`CityNames.Marta`) deliberately keep agency names; only **values**
  move. Renaming identifiers is ~55 files of churn and is explicitly out of scope (data-model E2).
- Legacy slug aliasing was **declined**. Old `#wmata` bookmarks fall through to the default city
  silently. This is a known accepted consequence, not a defect to fix mid-implementation.
- Audio is provably unaffected: the tone hash keys on `RouteJoinKey + segmentIndex`, never the city
  slug (Constitution Principle VIII). That is what makes SC-009 verifiable rather than aspirational.
- Do not add retries to any test here. Per plan.md's reliability section these are deterministic by
  construction — no clock, no shared state, no ordering, no network. Intermittency means a real defect.
- Commit after each task or logical group. Never commit T037 without T038–T039 (missing strings).
