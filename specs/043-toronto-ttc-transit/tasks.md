---
description: "Task list for Toronto TTC Transit City"
---

# Tasks: Toronto TTC Transit City

**Input**: Design documents from `/specs/043-toronto-ttc-transit/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: NOT requested. This is a config-only city onboarding — no new unit-testable code is
introduced (see plan.md Technical Context). Verification is feed-reachability + a live smoke test
per quickstart.md, captured as explicit verification tasks in each user-story phase.

**Organization**: Tasks are grouped by user story. Because TTC is delivered almost entirely by a
single shared config change, the four mechanical edits live in the Foundational phase (they jointly
enable US1 and US2); each user-story phase is then an independently-runnable verification slice.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included. Root namespace is `ChefKnifeStudios.MartaJazz.*` (repo path says TransitJazz).

## Path Conventions

- Shared: `src/ChefKnifeStudios.MartaJazz.Shared/`
- Worker: `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`
- WebAPI: `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/`
- Client: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the working environment before editing. No project scaffolding needed (existing solution).

- [X] T001 Confirm on branch `043-toronto-ttc-transit` and the solution builds clean today: run `dotnet build` at repo root and note a green baseline (so later regressions are attributable to this feature).
- [X] T002 [P] Verify network egress to both TTC feeds before wiring: `GET https://bustime.ttc.ca/gtfsrt/vehicles` returns 200 protobuf, and the `%20`-encoded static zip URL (see `specs/043-toronto-ttc-transit/contracts/city-config.md`) returns 200 with a valid GTFS zip.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The four mechanical edits that register TTC. These jointly enable User Story 1 and User Story 2 — neither story can be verified until all four land, so they are foundational, not per-story.

**⚠️ CRITICAL**: No user story verification can begin until this phase is complete.

- [X] T003 [P] Add `public const string Ttc = "ttc";` to the `CityNames` class in `src/ChefKnifeStudios.MartaJazz.Shared/CityNames.cs` (alongside `Marta`/`Wmata`/`Mbta`/`Nymta`).
- [X] T004 [P] Add the canonical `ttc` entry to the `Cities:` array in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.json`, exactly per `specs/043-toronto-ttc-transit/contracts/city-config.md` (Name `ttc`; one keyless `GtfsRtUrls` entry; one `%20`-encoded `StaticZipUrls` entry; `EmitsTelemetry: true`; NO `RailRealtime`, `RailRouteIdMap`, `RouteIdNormalization`, or `ApiKeyEnvVar`).
- [X] T005 [P] Add the **identical** `ttc` entry to the `Cities:` array in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.json` (static-zip loader parity — must byte-match T004's entry so shapes and live vehicles agree).
- [X] T006 Add the Toronto picker button + hash handler to `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/CityFab.razor` per `specs/043-toronto-ttc-transit/contracts/city-picker.md`: a `MatListItem`/`MatButton` `Label="Toronto, ON"` `@onclick="HandleTtcClicked"` `Disabled="@(CurrentCity == CityNames.Ttc)"`, plus `async Task HandleTtcClicked()` setting `location.hash='ttc';location.reload()` (mirror `HandleMbtaClicked`).
- [X] T007 Rebuild the solution (`dotnet build`) and confirm it compiles with no new warnings after T003–T006.

**Checkpoint**: TTC is registered end-to-end (Shared constant + Worker feed + WebAPI shapes + picker). User-story verification can now begin.

---

## Phase 3: User Story 1 - See Toronto surface vehicles moving on the map (Priority: P1) 🎯 MVP

**Goal**: Selecting Toronto shows live TTC buses + streetcars moving on real streets, updating each poll cycle.

**Independent Test**: Open the app, pick Toronto from the city FAB, confirm vehicle markers appear and move over a few refresh cycles on real streets — no other story required.

### Implementation / Verification for User Story 1

- [X] T008 [US1] Run the WebAPI and confirm TTC route shapes load: the route-shapes endpoint returns TTC routes (expect ~225 with shapes), and subway lines `1`/`2`/`4` are present as `route_type=1`. (Depends on T005, T007.)
- [X] T009 [US1] Run the Worker + WebAPI + Client, navigate to `#ttc`, and confirm live TTC surface vehicles render and animate along routes over ≥3 poll cycles on real streets (FR-002, SC-001). (Depends on T004, T008.)
- [X] T010 [P] [US1] Confirm the city FAB lists **Toronto** as a fifth entry and that selecting it sets `#ttc`, reloads, switches the map to Toronto, and disables the Toronto button while active (FR-001, FR-003, `contracts/city-picker.md`). (Depends on T006.)

**Checkpoint**: User Story 1 is the MVP — live Toronto vehicles on the map, reachable from the picker.

---

## Phase 4: User Story 2 - Vehicles match their real routes and voice on the soundscape (Priority: P1)

**Goal**: Buses and streetcars match their routes verbatim (no transform), route-less/unknown vehicles are skipped and counted, and matched vehicles voice on the correct treatment.

**Independent Test**: Inspect a Worker cycle's counters for TTC — near-total route matches, normal route-less skips, negligible unknown-route skips — and confirm a matched vehicle voices.

### Implementation / Verification for User Story 2

- [X] T011 [US2] From a live Worker cycle, confirm near-total verbatim route matching for TTC (RT `route_id` == static `route_short_name`, no transform): matched count is the bulk of route-attributed vehicles, `skippedUnknownRoute` reflects only stray internal ids (e.g. `600`), `skippedNoRouteId` reflects the normal ~⅓ deadhead share (FR-004, FR-005, FR-006, SC-002, SC-003). (Depends on T009.)
- [X] T012 [P] [US2] Confirm a matched TTC bus (e.g. route `32`) voices/renders on the **Bus** treatment (FR-007). (Depends on T009.)
- [X] T013 [P] [US2] Confirm a 500-series streetcar (e.g. `504`, `501`) voices/renders on the **Rail** treatment — the accepted as-built `route_type=0`→Rail classification (research R1, FR-007); this is expected, not a defect. (Depends on T009.)
- [X] T014 [P] [US2] Confirm TTC vehicle positions appear in telemetry (`ttc`-tagged rows in the denormalized `telemetry` dataset), consistent with other real-GPS cities (FR-012). (Depends on T009.)

**Checkpoint**: User Stories 1 AND 2 both verified — Toronto vehicles are live, correctly routed, and sonified.

---

## Phase 5: User Story 3 - Toronto subway lines draw without live trains (Priority: P3)

**Goal**: No live subway feed is fetched and none is required; any subway geometry that appears is static-only with no train markers.

**Independent Test**: Confirm the app never attempts a TTC subway/train live fetch and never errors on its absence; no train markers animate on subway lines.

### Implementation / Verification for User Story 3

- [X] T015 [US3] Confirm the Worker makes **no** rail-realtime fetch attempt for `ttc` and logs no error about a missing subway feed — guaranteed by the omitted `RailRealtime` in the `ttc` config (FR-008, SC-006, research R3). (Depends on T004, T007.)
- [X] T016 [P] [US3] Confirm that at `#ttc` no live train markers animate on subway lines `1`/`2`/`4` (there is no feed to drive them); static line geometry drawing is acceptable and needs no further work. (Depends on T008, T009.)

**Checkpoint**: All three user stories verified; the "no live subway" boundary is confirmed safe.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Regression safety and tracked follow-ups.

- [X] T017 Regression check: confirm Atlanta, Boston, New York, and Washington DC each still load and behave exactly as before (additive-only — FR-013, SC-004). (Depends on T007.)
- [X] T018 Run the full `specs/043-toronto-ttc-transit/quickstart.md` verification (all 9 checks) and record results.
- [X] T019 [P] Record the operational follow-ups from research/quickstart as tracked items (NOT implemented here): pin/mirror the CKAN static zip (id can rotate), dedicated streetcar/tram voicing (`TransitMode` wire change), and `CityFab` label localization to `RouteFilterResources.resx` (Principle XII).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories. T003/T004/T005 are parallel; T006 is independent; T007 (rebuild) depends on T003–T006.
- **User Stories (Phase 3–5)**: All depend on Foundational (esp. T007). US1 is the MVP; US2 and US3 depend on the US1 running app (T009) for their live checks.
- **Polish (Phase 6)**: Depends on the user stories being verified.

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational. Delivers the MVP.
- **US2 (P1)**: Depends on Foundational + the running app from US1 (T009) to read live counters and hear voicing. Logically co-critical with US1.
- **US3 (P3)**: Depends on Foundational (T004/T007) for the "no rail fetch" check; T016 also uses the running app (T009).

### Within Each User Story

- Config/registration (Foundational) before verification.
- WebAPI shapes (T008) before live-vehicle checks (T009).

### Parallel Opportunities

- T002 runs parallel to T001.
- T003, T004, T005 edit three different files → fully parallel; T006 is a fourth independent file.
- Within US1: T010 (picker) is independent of T008/T009.
- Within US2: T012, T013, T014 are independent observations once T009 is up.
- T019 runs parallel to other Polish work.

---

## Parallel Example: Foundational Phase

```bash
# The three config/constant edits touch different files — do them together:
Task: "Add Ttc constant in src/ChefKnifeStudios.MartaJazz.Shared/CityNames.cs"          # T003
Task: "Add ttc Cities: entry in TransitDataWorker/appsettings.json"                      # T004
Task: "Add ttc Cities: entry in WebAPI/appsettings.json"                                 # T005
# Then T006 (CityFab.razor), then T007 rebuild.
```

## Parallel Example: User Story 2 Verification

```bash
# Once the app is running (T009), these observations are independent:
Task: "Confirm a TTC bus voices on the Bus treatment"        # T012
Task: "Confirm a 500-series streetcar voices on Rail"        # T013
Task: "Confirm ttc rows appear in telemetry"                 # T014
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (confirm build + feed reachability).
2. Phase 2: Foundational (the four edits + rebuild) — CRITICAL, blocks everything.
3. Phase 3: User Story 1 — live Toronto vehicles on the map.
4. **STOP and VALIDATE**: pick Toronto, watch vehicles move. This is a shippable MVP.

### Incremental Delivery

1. Setup + Foundational → TTC registered.
2. US1 → live vehicles (MVP, demo-able).
3. US2 → verify routing + voicing (buses=Bus, streetcars=Rail).
4. US3 → confirm the no-live-subway boundary.
5. Polish → regression sweep + quickstart + log follow-ups.

### Notes

- This feature is deliberately config-only; the "implementation" tasks in US phases are verification tasks because there is no new logic to write beyond the four Foundational edits.
- Do NOT auto-commit — per repo policy, commits are the user's to make.
- The streetcar=Rail behavior (T013) is an accepted decision, not a bug — see research R1.
