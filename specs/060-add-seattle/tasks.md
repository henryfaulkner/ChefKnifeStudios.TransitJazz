# Tasks: Add Seattle Transit

**Input**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `testing.md`, `quickstart.md`, `validation.md`, both `contracts/` files and `checklists/requirements.md` in `specs/060-add-seattle/`.

**Scope**: One `seattle` city using only direct KCM and Sound Transit sources. These are future implementation, test, local acceptance and human release tasks; every checkbox remains open. Do not create or switch branches, run commit hooks, commit, push, deploy or migrate while executing this task-generation workflow. The human release owner controls publication. Keep Seattle reported-source policy arrays absent until an endpoint-wide identity namespace and cadence/gap policy is justified; preserve the actual global flag values.

**Tests**: Requested by FR-020 and planned in `testing.md`. Use existing xUnit projects, synthetic protobuf/ZIP fixtures, fixed clocks, isolated handlers/configuration and observable outcomes. Test tasks precede the behavior they validate within each implementation phase. Do not add a framework, a migration or a broad end-to-end harness.

## Format: `[ID] [P?] [Story] Description`

- **[P]** means safe to work concurrently after the stated prerequisites, in distinct files without relying on another open task.
- **[USn]** maps to the four stories in `spec.md`. Setup, foundation and cross-cutting tasks have no story label.
- Paths are relative to the repository root. A named new file is intentional; otherwise extend the named existing file.

## Phase 1: Setup

**Purpose**: Prepare deterministic, secret-free verification without changing runtime behavior.

- [X] T001 Create compact synthetic Seattle GTFS-RT and direct-static ZIP fixture builders with a fixed `TimeProvider`, per-test request-capturing handlers and sentinel ST key in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Cities/SeattleTestFixtures.cs`; avoid saved live vehicle IDs, ambient environment mutation and network calls.
- [X] T002 Document the exact local client/backend destinations, which host owns capture, existing city indices and the effective indexed override keys to inspect (all results still pending) in `specs/060-add-seattle/validation.md` before local acceptance; do not copy secret values.

## Phase 2: Foundational city identity

**Purpose**: Establish the single city slug shared by subsequent source, host and visitor work.

- [X] T003 Add an isolated `seattle` slug/catalog assertion to `src/ChefKnifeStudios.TransitJazz.Shared.Tests/CitySlugTests.cs`, retaining existing city identities.
- [X] T004 Add `CityNames.Seattle` to `src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs`; preserve existing city names and order.

**Checkpoint**: The shared identity is ready. No Seattle source is registered or visitor entry exposed yet.

## Phase 3: User Story 2 - Trust the combined agency view (Priority: P1)

**Goal**: Build a source-isolated Seattle adapter that preserves raw per-source coverage and selects one safe display/history/audio representation for verified overlaps.

**Independent test**: Synthetic KCM/ST responses and two direct static ZIPs demonstrate request isolation, route joins, raw capture, partial failures, overlap decisions and one Worker contribution without a deployed Seattle city.

### Tests for User Story 2

- [X] T005 [P] [US2] Add request-boundary cases to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Cities/SeattleCityTests.cs`: only ST agency 40 receives exactly one `key`; KCM receives none; missing key yields a truthful ST failure; redirect to another host, non-2xx, thrown request, logs and public output never expose or forward the sentinel or authenticated URI.
- [X] T006 [P] [US2] Add direct-static request and join cases to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/GtfsStaticLoaderTests.cs`: both ZIPs are keyless; `100451→556`, `102767→547`, `100479→1 Line`, `2LINE→2 Line` have shapes; unmatched and ambiguous routes have none; duplicate display keys/raw aliases are rejected in either ZIP order while unrelated routes survive.
- [X] T007 [US2] Add source-outcome cases to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Cities/SeattleCityTests.cs` for success, successful-empty, malformed data, HTTP failure, timeout, stale position and caller cancellation, asserting independent receipt UTC/counts and healthy-source publication with fixed time and no sleeps.
- [X] T008 [US2] Add overlap-table cases to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Cities/SeattleCityTests.cs`: 556/547 pairs exactly at and just beyond 120 seconds or 5 km, missing time/position/geometry/freshness, same descriptor on another route, unrelated equal text, KCM preference, ST-only fallback and bounded withholding diagnostics. Reuse T001 fixtures, and assert source-qualified independent display identities.
- [X] T009 [P] [US2] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicleSourceExtractionTests.cs` and `ReportedVehicleCoverageTests.cs` with synthetic valid-policy capture for raw 512, suppressed overlap, route-less/position-less/stale records, deleted and nonvehicle entities, missing descriptors, failed versus empty responses and exact per-source receipt/coverage before display filters; no Seattle policy certification is implied.
- [X] T010 [US2] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/RouteHourWorkerIntegrationTests.cs` with a recording publisher/sink over two fixed cycles: one overlap yields one published vehicle, crossing/audio and route-hour movement even when source precedence changes; unrelated equal-text vehicles remain distinct.
- [X] T011 [P] [US2] Add Seattle route-index collision/alias and 512 exclusion cases to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/RouteIndexStartupTests.cs`, including a synthetic direct ZIP that later gains 512 geometry; assert no 512 map/history/audio publication and no arbitrary first-match alias.
- [X] T012 [P] [US2] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicleConfigurationTests.cs` for absent Seattle policy arrays under both actual global capture modes: existing enabled cities still start, Seattle is unsupported, and configured global values remain intact.

### Implementation for User Story 2

- [X] T013 [US2] Add only Seattle-needed per-source access/configuration fields in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/CityConfig.cs`; represent KCM and ST independently, use `SOUND_TRANSIT_OBA_API_KEY` only for ST, and never set a combined city-wide key.
- [X] T014 [US2] Implement focused source fetching/decoding in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/SeattleCity.cs` using existing GTFS-RT, `HttpClient`, cancellation and bounded request conventions; pin the direct endpoints from the source contract, block cross-host credential redirects, and emit safe source-specific outcomes.
- [X] T015 [US2] Preserve each decoded response's original receipt UTC, integrity, nondeleted Vehicle descriptor IDs and success/failure/empty outcome before any display filter in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/SeattleCity.cs`; combine original `CityFetchResult.ReportedVehicleObservations` without changing `ReportedVehicleCatalogBuilder` or inventing fallback IDs.
- [X] T016 [US2] Validate Seattle's two direct-static ZIPs in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`: reject/withhold affected duplicate display keys and conflicting raw-ID aliases independent of source order, keep unrelated valid routes, and never load a consolidated ZIP or invent geometry.
- [X] T017 [US2] Validate the matching direct-static `route_id` aliases against `RouteJoinKey` before Worker processing in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`; ensure known joins resolve, affected ambiguities are withheld with bounded diagnostics, and route 512 is explicitly ineligible for map/history/audio even if later geometry exists.
- [X] T018 [US2] Add deterministic processed-feed selection in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/SeattleCity.cs`: corroborate same-descriptor/same-shared-route 556/547 pairs within inclusive 120-second and 5-km bounds, prefer eligible KCM, use ST only when no possible same-pair KCM counterpart exists, withhold pairs with missing or contradictory comparison evidence, and source-qualify independent display identities after raw capture.
- [X] T019 [US2] Feed the single selected Seattle result into both Worker passes in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`, preserving existing freshness, source counts, route-hour movement and audio/crossing semantics so an overlap contributes once and a healthy source survives partner failure.

**Checkpoint**: Source/authentication, raw capture, route identity and overlap fixtures pass. Reported Seattle collection remains unsupported until its separate identity-domain gate is met.

## Phase 4: User Story 1 - Hear Seattle's live transit (Priority: P1)

**Goal**: Register one combined Seattle city in both hosts and expose one direct-linkable map/audio experience with supported routes from both agencies.

**Independent test**: Both effective registries and the client resolve one `seattle`; sanitized source results have representative shapes, and later local service-hour/browser/listening acceptance verifies actual motion and sound.

### Tests for User Story 1

- [X] T020 [P] [US1] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/CityConfigParityTests.cs` to bind both host JSON files plus representative indexed overrides and assert one Seattle registration, matching source order/endpoints/time zone/adapter, unchanged old indices, visible override collisions and no consolidated URL.
- [X] T021 [P] [US1] Extend `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ResolveCityTests.cs` and `CityMapOriginTests.cs` for exactly one Seattle picker/hash identity and a transit-dense starting origin; keep existing-city resolution and switching behavior.
- [X] T022 [P] [US1] Add selected-feed integration assertions to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/RouteIndexStartupTests.cs` for KCM and ST representative routes with geometry and zero 512 display/history/audio contribution.

### Implementation for User Story 1

- [X] T023 [US1] Add one Seattle `Cities` entry after existing indices to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` with `America/Los_Angeles`, KCM/ST direct realtime and two keyless direct static URLs; leave `ReportedVehicleSources` absent and preserve all global flags and unrelated city entries.
- [X] T024 [US1] Add the identical Seattle identity, ordered sources, static URLs, time zone and absent reported policy to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`; preserve existing city indices, global flags and unrelated city values.
- [X] T025 [US1] Register `SeattleCity` and its source-specific HTTP access in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Program.cs` without changing Marta's index-zero rail binding or enabling another capture owner; include Seattle in intended observability filters and configured processing limits.
- [X] T026 [US1] Register the same `SeattleCity` behavior and source-specific HTTP access in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`; include Seattle in intended observability filters/limits and verify effective overrides at startup rather than relying only on JSON text parity.
- [X] T027 [US1] Add one Seattle city picker/hash entry in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/CityFab.razor` and a Seattle origin in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`; keep existing route selection, reconnect and audio-unlock flow.

**Checkpoint**: Code and isolated tests make Seattle selectable in a local build. This is not release-ready until the gates in US3 and US4 pass; the picker must not be exposed publicly before backend acceptance.

## Phase 5: User Story 3 - Understand Seattle coverage and statistics (Priority: P2)

**Goal**: Show accurate authored coverage copy and Seattle/All history evidence with local-day, category, route and reported-population scope intact.

**Independent test**: Resource lookup and fixed completed-hour/DST fixtures show Seattle once, correct states and truthful copy without live feeds or database mutation.

### Tests for User Story 3

- [X] T028 [P] [US3] Extend `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/CityCopyResolutionTests.cs` and `StatsResourcesTests.cs` to resolve `StatsCitySeattle`, Seattle unlock header plus exactly three paragraphs and InfoFab copy through real resources, including KCM/ST attribution, how to listen, route 512 omission and no unverified Sounder/weekday promise.
- [X] T029 [P] [US3] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/StatsOverlayWindowTests.cs` and `StatsOverlayProjectionTests.cs` with fixed instants across Los Angeles midnight/DST and known completed-hour rows: Seattle appears once in All, selected-route distance/category scope persists, and pending/partial/unavailable differ from verified zero.
- [X] T030 [US3] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleCoverageQueryTests.cs` and `StatsOverlayProjectionTests.cs` for Seattle policy absent with read enabled: Seattle remains in the hour/city denominator as unavailable, All is partial/qualified, and an existing supported city's reported count stays city-wide under route filtering.
- [X] T031 [P] [US3] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/StatsOverlayOptionsTests.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsAllProjectionTests.cs` for one Seattle display key and All membership without fabricated backfill; use isolated configuration and rows.

### Implementation for User Story 3

- [X] T032 [US3] Add Seattle's Stats display key and intended history city selectors in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` and matching standalone selectors in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json` only after checking each host's ownership and configured flags; preserve their actual global route-hour/category/reported/read values and no Seattle reported policy arrays.
- [X] T033 [US3] Ensure `StatsOverlayOptions` and existing reported catalog/projection semantics give Seattle's `America/Los_Angeles` local dates, one All membership and unavailable/partial reported evidence while policy arrays are absent in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatsOverlayOptions.cs`; change policy code only if the tests expose a real gap.
- [X] T034 [US3] Add `StatsCitySeattle` and authored Seattle audio header, three listening paragraphs and InfoFab companion text to `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`, crediting both direct sources and explaining route 512's omission and observed coverage without overstating modes; acknowledge streetcar-as-rail/water-taxi-as-bus where category guidance appears.
- [X] T035 [US3] Wire Seattle resource keys into `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/AudioUnlockOverlay.razor` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/InfoFab.razor`, retaining the existing unlock and information layout and resource conventions.

**Checkpoint**: Seattle visitor and Stats surfaces have fixture-backed scope and copy. Genuine service-hour and completed-hour evidence is still required for acceptance.

## Phase 6: User Story 4 - Release with evidence and recovery (Priority: P2)

**Goal**: Establish local acceptance and a reviewable release record; the human owner controls publication and can restore known healthy versions.

**Independent test**: `validation.md` contains measured results or explicit pending blockers for every gate; no release claim is made until running backend/client versions and public behavior are observed.

### Local implementation verification

- [X] T036 [US4] Run `dotnet build src/ChefKnifeStudios.TransitJazz.sln` and affected Worker, WebAPI, Shared and Client.Shared xUnit projects identified in `specs/060-add-seattle/testing.md`; record commands, UTC times, environment, result/evidence and unresolved failures in `specs/060-add-seattle/validation.md`.
- [ ] T037 [US4] With confirmed local client/backend URLs and exactly one capture owner, inspect effective host JSON plus indexed environment overrides, startup, Seattle catalog/known shapes, source status and several representative service-hour KCM/ST processing and publication updates; record route IDs and safe evidence in `specs/060-add-seattle/validation.md`.
- [ ] T038 [US4] Verify local `/#seattle` link, one picker entry, city switching/return, filters/reconnect, Stats states, desktop/mobile and both themes, plus an existing city; confirm plausible route-following motion, zero 512 display and audible output after unlock by human listening, recording dated results in `specs/060-add-seattle/validation.md`.

### Release prerequisites and human-controlled publication

- [ ] T039 [US4] Obtain the permitted ST agency-40 request rate for actual cycle interval, endpoint count, retries, all environments, active instances and overlapping revisions, and record owner/evidence against the 10-second demand estimate in `specs/060-add-seattle/validation.md`; leave release blocked until confirmed.
- [ ] T040 [US4] Verify intended `SOUND_TRANSIT_OBA_API_KEY` owner, renewal, runtime secret-reference binding and access mechanism without recording a value or treating key-carrying HTTP success alone as validation; record keyless KCM/static access outcomes in `specs/060-add-seattle/validation.md`.
- [ ] T041 [US4] Check both direct-static ZIPs' `calendar.txt` and `calendar_dates.txt` against the intended service period, and collect representative service-hour/weekday samples across multiple updates for each claimed mode/route; validate overlap thresholds and copy limits in `specs/060-add-seattle/validation.md` before release.
- [X] T042 [US4] Record the explicit unsupported Seattle reported-coverage disposition in `specs/060-add-seattle/validation.md`, verifying safe startup with policy arrays absent and truthful Seattle/All unavailable/partial Stats under the configured global flags. Keep namespace and cadence/gap certification pending as a gate only for enabling Seattle reported counts; any route-subset workaround requires separate scope review.
- [ ] T043 [US4] Record actual production backend/client targets and workflow triggers, intended SHA/artifacts, effective override/capture-owner checks, prior healthy versions, rollback owner and backend-first/client-second sequence in `specs/060-add-seattle/validation.md`; inspect pending schema/history feature gates and use existing runbooks only if applicable, without assuming a migration.
- [ ] T044 [US4] Present the exact reviewed diff and open gates in `specs/060-add-seattle/validation.md` to the human release owner before any publication; the owner performs commits and controls push/deployment, with the separate MartaJazz site outside this feature.
- [ ] T045 [US4] After human-controlled backend publication, verify actual serving backend SHA/revision/workflow, readiness, Seattle catalog/geometry, multiple successful service-hour processing/publication updates, source health and existing-city behavior; record UTC times/evidence in `specs/060-add-seattle/validation.md` before exposing the client picker.
- [ ] T046 [US4] After human-controlled client publication, verify actual serving client SHA/revision/workflow and `https://transitjazz.com/#seattle` for visible motion, audio after unlock, filters/switching, Stats and existing-city health; require genuine completed-hour evidence before historical acceptance and record limitations in `specs/060-add-seattle/validation.md`.
- [ ] T047 [US4] If readiness, authentication, publication or existing-city checks fail, stop Seattle exposure and have the release owner restore the recorded healthy backend/client versions without destructive data rollback; record the action and recovery evidence in `specs/060-add-seattle/validation.md`.

**Checkpoint**: A workflow success alone never closes US4. Production acceptance requires observed running versions and all release blockers resolved.

## Phase 7: Polish and cross-cutting review

**Purpose**: Complete coverage and documentation checks after the story work, before the human release decision.

- [X] T048 Review `specs/060-add-seattle/validation.md` against FR-001–FR-025, SC-001–SC-007 and every row of `specs/060-add-seattle/testing.md`; keep unperformed checks pending, cite safe evidence and remove any credential, authenticated URI or raw vehicle identity from recorded material.
- [X] T049 Recheck the Seattle diff and tracked configuration against `docs/ADDING_A_TRANSIT_CITY.md`, `specs/060-add-seattle/contracts/seattle-source-and-identity-v1.md` and `seattle-visitor-and-release-v1.md` for one slug, direct feeds only, absent unsupported reported policies, existing-city parity and no unreviewed schema or site changes.

## Dependencies and execution order

- **Setup → foundation**: T001 supports the isolated US2 tests; T002 records local prerequisites. T003 must fail before T004 adds the slug.
- **US2 before US1**: T005–T012 specify the safe source behavior, then T013–T019 implement it. Source authentication, raw capture, alias validation and overlap selection must pass before either host registers or publicly exposes Seattle.
- **US1 before US3**: T020–T022 specify registry/visitor behavior, then T023–T027 register one city. US3 adds copy and Stats policy to that city. US3's fixture tests T028–T031 can be authored once foundation and the US1 identity are stable, but its integration checkpoint depends on US1.
- **US4 after implementation**: T036–T038 are local checks after US2/US1/US3. T039–T041 and T043 are release prerequisites; T042 closes with a verified unsupported-coverage disposition while identity certification remains open for future Seattle reported counts. T045 requires the owner's backend publication; T046 requires backend acceptance and the owner's client publication. T047 is the failure branch. T048–T049 close coverage review; neither authorizes publication.
- **Reported-policy gate**: Four paired Sunday IDs do not certify the fleet namespace. Seattle map/audio launch may proceed with both reported policy arrays absent once unsupported coverage and Seattle/All Stats qualification are verified. Seattle reported counts remain blocked until endpoint-wide namespace and cadence/gap evidence justify policies; do not flip global flags to mask this.

### Safe parallel examples

| After prerequisite | Independent work |
| --- | --- |
| T001/T004 complete | T005 source-auth tests and T006 static-loader tests in separate test files; T009 raw-capture tests and T011 route-index tests in separate files. T007/T008 share `SeattleCityTests.cs` with T005 and are sequential there. |
| US2 checkpoint | T020 host-parity tests and T021 client resolver/origin tests; T023 standalone JSON and T024 WebAPI JSON once test expectations are fixed. |
| US1 checkpoint | T028 client resource tests, T029 Stats window tests, T030 coverage tests and T031 options/client projection tests where files do not overlap; coordinate the shared `StatsOverlayProjectionTests.cs` edits sequentially. |
| Local implementation verified | T039 quota confirmation, T040 intended access and T041 calendars/service-hour coverage can be gathered independently. T042 can document unsupported Seattle reporting while future identity research continues. |

## Implementation strategy

1. Prove source isolation, raw capture, route joins and conservative overlap behavior with US2 fixtures. A healthy ST-only vehicle is usable only when no possible same-descriptor/same-shared-route KCM counterpart exists; missing comparable KCM evidence withholds the pair.
2. Register and expose one local Seattle experience under US1, then add truthful Stats and visitor copy under US3. Run isolated tests at each checkpoint; local selectability is not a release claim.
3. Complete US4 local and release evidence. The owner reviews the exact changes, resolves quota/access/calendar and effective configuration gates, and verifies explicit unsupported Seattle reported coverage if its policy remains absent. The owner then controls backend-first publication. Verify running versions and public behavior before marking production acceptance.

No task is marked complete by this generated plan.
