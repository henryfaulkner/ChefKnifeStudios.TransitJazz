---
description: "Task list for feature: Add Boston (MBTA) as a Transit City"
---

# Tasks: Add Boston (MBTA) as a Transit City

**Input**: Design documents from `/specs/032-mbta-boston-transit/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/mbta-city-config.md, quickstart.md
**Branch**: `031-multi-city-transit` (no branch switch, per user request)

**Tests**: None requested. This feature is config + two source lines; validation is the manual end-to-end pass in `quickstart.md`. No automated test tasks are fabricated.

**Organization**: Tasks grouped by user story. US1 (view Boston) is the MVP; US2 (pick Boston from the picker) is a thin add. A final Polish phase reconciles the constitution with the codebase's actual join key (per user request).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 (Polish tasks carry no story label)

## Path Conventions

Web app — real paths from plan.md. Worker: `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`. WebAPI: `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/`. Shared: `src/ChefKnifeStudios.MartaJazz.Shared/`. Client RCL: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`.

---

## Phase 1: Setup

**Purpose**: Nothing to scaffold — this feature consumes the merged feature-031 multi-city machinery (`ITransitCity`, `GtfsRtCity`, `CityConfig`, `Cities:` config arrays, per-city SignalR groups). No new projects, files, or dependencies.

- [X] T001 Confirm the 031 multi-city machinery is present before starting: `GtfsRtCity` auto-registration for non-`marta` cities exists at `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Program.cs:39-42`, and `GtfsStaticLoader.LoadCityEntries()` iterates `Cities:` at `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs:59-86`. (Read-only sanity check; no edits.)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: One stable identifier the rest of the app keys on. Blocks the `CityFab` picker entry (US2) and keeps US1 config consistent.

**⚠️ CRITICAL**: T002 must land before T007 (CityFab references `CityNames.Mbta`).

- [X] T002 Add `public const string Mbta = "mbta";` to `CityNames` in `src/ChefKnifeStudios.MartaJazz.Shared/CityNames.cs` (next to `Marta`/`Wmata`).

**Checkpoint**: The stable city key exists; user-story config and UI can reference it.

---

## Phase 3: User Story 1 - View Boston's live transit (Priority: P1) 🎯 MVP

**Goal**: Boston's vehicles (all modes, incl. Red/Orange/Blue heavy rail) flow through the worker → hub → client, and Boston's route shapes load — visible at `…/#mbta` with zero MARTA/WMATA bleed.

**Independent Test**: Open the app at `…/#mbta`; confirm Boston vehicles render on Boston routes (incl. live heavy-rail trains), audio + route pills are Boston's, and a second tab on `…/#marta` shows no Boston vehicles.

**Note**: The four config edits are the same JSON object (contracts/mbta-city-config.md). They are in four different files, so they are parallelizable, but they are tiny — apply together.

- [X] T003 [P] [US1] Append the MBTA entry (Name `mbta`, single `GtfsRtUrls`, single `StaticZipUrls`, `EmitsTelemetry:false`, **no** `ApiKeyEnvVar`/`RailRealtime`/`RailRouteIdMap`) to the `Cities` array in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.json`. Use the exact object from `contracts/mbta-city-config.md`.
- [X] T004 [P] [US1] Append the same MBTA entry to the `Cities` array in `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.Development.json`.
- [X] T005 [P] [US1] Append the same MBTA entry to the `Cities` array in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.json`.
- [X] T006 [P] [US1] Append the same MBTA entry to the `Cities` array in `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.Development.json`.

**Checkpoint**: Run worker + WebAPI. Worker logs MBTA vehicles published each cycle (~300); WebAPI logs `city mbta loaded N route shapes` (~hundreds). US1 is functional via direct `#mbta` URL — no picker entry yet.

---

## Phase 4: User Story 2 - Select Boston from the city picker (Priority: P2)

**Goal**: "Boston, MA" appears in the `CityFab` menu next to Atlanta/DC; selecting it navigates to `#mbta` and reloads; the active city's item is disabled.

**Independent Test**: Open the city picker FAB; confirm "Boston, MA" is listed; select it and confirm the app reloads scoped to Boston; confirm the currently-viewed city's item is disabled.

**Depends on**: T002 (`CityNames.Mbta`).

- [X] T007 [US2] Add a "Boston, MA" `MatListItem` (mirroring the WMATA item, `Disabled="@(CurrentCity == CityNames.Mbta)"`) and a `HandleMbtaClicked` handler (mirroring `HandleWmataClicked`, setting `location.hash='mbta'`) in `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/CityFab.razor`.

**Checkpoint**: All three cities selectable from the picker; US1 + US2 both work.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Reconcile the constitution with the codebase's actual join key (per user request: "change the constitution to the codebase reality"). Feature 031 moved the static↔RT route index from `route_short_name` to `route_id`, keyed `{city}:{routeId}` — but Principles III and VI still describe `route_short_name`. MBTA's 100% alignment *depends on* the `route_id` keying, so the constitution should now match reality.

- [X] T008 Amend Principle VI ("GTFS ID Mapping") in `.specify/memory/constitution.md:72-73`: change the join-key mandate from "MUST use `route_short_name` … falling back to `route_id`" to "MUST use the GTFS static `route_id` as the join key (matching the GTFS-RT `Trip.RouteId`), scoped per city as `{city}:{routeId}`; `route_short_name` is carried as display metadata only." Keep the MARTA example but note that the route index keys on `route_id`.
- [X] T009 Amend Principle III V2 Pass in `.specify/memory/constitution.md:62`: change "(keyed by `route_short_name` from GTFS static data, matching the GTFS-RT `Trip.RouteId`)" to "(keyed by `route_id` from GTFS static data, matching the GTFS-RT `Trip.RouteId`, scoped per city)". (Same file as T008 — sequential, not [P].)
- [X] T010 Bump the constitution version and Sync Impact Report in `.specify/memory/constitution.md` header: 3.2.0 → 3.2.1 (PATCH — clarification reconciling documented join key with the as-built `route_id`/`{city}:{routeId}` keying delivered by feature 031; no principle removed or redefined). Update **Last Amended** date. (Same file — after T008/T009.)
- [ ] T011 [P] Run the full `quickstart.md` verification pass end-to-end (worker logs, WebAPI shape load, `#mbta` view, picker selection, MARTA/WMATA isolation, heavy-rail-with-no-remap, no-secret, no-regression). This is the feature's acceptance gate in lieu of automated tests.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)**: read-only check; no dependency.
- **Foundational (T002)**: blocks T007 (CityFab uses `CityNames.Mbta`). Does **not** block US1 config (T003–T006).
- **US1 (T003–T006)**: independent of US2; delivers the MVP (Boston viewable by URL).
- **US2 (T007)**: depends on T002. Independent of US1's config, but only *useful* once US1 makes Boston actually load.
- **Polish (T008–T011)**: T008→T009→T010 are sequential (same file). T011 depends on T002–T007 being applied.

### User Story Dependencies

- **US1 (P1)**: needs only T002-independent config. The MVP.
- **US2 (P2)**: needs T002. Integrates with US1 (the picker is pointless if the city doesn't load) but is independently testable (the item appears + navigates regardless).

### Parallel Opportunities

- T003, T004, T005, T006 are all `[P]` — four different files, identical edit. Apply in one pass.
- T008–T010 are NOT parallel (all edit `constitution.md`).
- T011 runs last.

---

## Parallel Example: User Story 1

```bash
# The four config edits touch four different files — apply together:
Task: "Append MBTA entry to TransitDataWorker/appsettings.json"
Task: "Append MBTA entry to TransitDataWorker/appsettings.Development.json"
Task: "Append MBTA entry to WebAPI/appsettings.json"
Task: "Append MBTA entry to WebAPI/appsettings.Development.json"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001 (sanity check) → T002 (`CityNames.Mbta`) → T003–T006 (config).
2. **STOP and VALIDATE**: open `…/#mbta`, confirm Boston vehicles + shapes, confirm MARTA/WMATA unaffected.
3. Deploy/demo — Boston is live (reachable by URL).

### Incremental Delivery

1. US1 → Boston viewable by URL (MVP).
2. US2 → Boston selectable from the picker.
3. Polish → constitution reconciled with the `route_id` reality + full quickstart pass.

---

## Notes

- This is a deliberately tiny feature: 4 config edits + 2 source lines + a constitution PATCH. No new files, no new code paths, no secret.
- The constitution amendment (T008–T010) fixes pre-existing drift introduced by feature 031, surfaced while planning MBTA — it is included here at the user's request, not because MBTA caused it.
- Commit after US1 (MVP), after US2, and after the constitution amendment as three logical groups.
- No automated tests requested; T011 (quickstart) is the acceptance gate.
