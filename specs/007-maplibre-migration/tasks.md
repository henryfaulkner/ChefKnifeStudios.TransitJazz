---
description: "Task list for 007-maplibre-migration"
---

# Tasks: MapLibre Migration

**Input**: Design documents from `/specs/007-maplibre-migration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/constitution-amendment.md, quickstart.md

**Tests**: The spec does not request a formal test framework. No automated test tasks are generated. Verification is performed via the protocol in `quickstart.md`.

**Organization**: Tasks are grouped by user story. **US1** (production page works on MapLibre) is the MVP — the migration is not safe to ship until US1 passes. **US2** (no dead code remains) layers on top and must land in the same PR; "partial cleanup" is worse than no cleanup.

**Critical ordering note**: Two file paths collide (`Components/Map.razor*` and `wwwroot/js/vehicle-animator.js`). The old Azure-backed files at those paths MUST be deleted before the renamed POC files take their place, or the rename will silently merge/skip. The task order below enforces this; do not reorder.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

This feature touches:
- `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/` (`js/`, `css/`, root)
- `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/` (`EndpointGroups/`, `Program.cs`, `appsettings.Development.json`)
- `src/ChefKnifeStudios.TransitJazz.Shared/`
- `.specify/memory/constitution.md`

No other projects are modified.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a baseline before any destructive changes. The migration is irreversible mid-PR — Phase 1 is the safety net.

- [X] T001 Confirm `006-maplibre-poc` branch is merged or fully archived; this feature must branch from a state where the POC files exist at their POC paths. Run `git status` and verify a clean working tree. *(Done on 006-maplibre-poc branch per user direction; POC files present as untracked/modified.)*
- [X] T002 Run `dotnet build C:\Projects\ChefKnifeStudios.TransitJazz\ChefKnifeStudios.TransitJazz.sln` and record the exact warning count (pre-migration baseline). The post-migration build MUST NOT exceed this count (per spec SC-004). *(Baseline: 71 warnings, 0 errors. Solution at src\ChefKnifeStudios.TransitJazz.sln.)*
- [X] T003 [P] Run the five greps from `quickstart.md` step 3 against the current repo and record initial hit counts. After the migration these must drop to zero in `src/` paths. *(Baseline counts: atlas.microsoft.com=3, mapAccClientId=2, MapLibreTest=12, atlas SDK=9, ChefMapLibre=27)*

**Checkpoint**: Baseline recorded. Safe to start destructive edits.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: There is no foundational code for this feature — the foundational artifacts (`MapLibre.*` POC files, `maplibre-interop.js`, `maplibre-vehicle-animator.js`, MapTiler config, MapLibre CDN tags) already exist from POC 006 and are the *subject* of the rename work in Phase 3. Phase 2 is intentionally empty.

**Checkpoint**: Proceed directly to Phase 3.

---

## Phase 3: User Story 1 - Production Map Uses MapLibre (Priority: P1) 🎯 MVP

**Goal**: After this phase, `/transit-map` is fully powered by MapLibre + MapTiler. The Azure Maps files are gone. The build is clean. The page renders, animates, and handles clicks correctly. (Satisfies spec SC-001, SC-002, SC-004.)

**Independent Test**: Start the local stack, navigate to `/transit-map`, verify tiles + vehicles + routes render, verify zero `atlas.microsoft.com` Network requests, verify click handlers fire. See `quickstart.md` verification steps 1 and 2.

### Deletions of name-colliding files (must run first)

These deletions free the paths that the renames in the next subsection will occupy. Running them out of order causes silent file-overwrite failures.

- [X] T004 [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor` (Azure Maps version — frees the path for the rename in T010)
- [X] T005 [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.cs` (Azure Maps version — frees the path for the rename in T011)
- [X] T006 [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs` (Azure Maps version — frees the path for the rename in T012)
- [X] T007 [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/azure-maps-interop.js`
- [X] T008 [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/vehicle-animator.js` (Azure-coupled animator — frees the path for the rename in T014)
- [X] T008.1 [US1] CSS files (not in original tasks): kept `Map.razor.css` (provider-agnostic `.map` and `.map-tools` rules used by new MapLibre markup); removed dead `.azure-map-copyright` rule; deleted `MapLibre.razor.css` (its only rule `.maplibregl-map { height: 100vh }` is redundant — parent `.map` div already sizes correctly).

### Renames (POC artifact → production name)

These renames are sequential because each one updates internal identifiers; running them in parallel risks mid-rename builds that reference partly-renamed types. Each task is a move + in-file find-and-replace, applied atomically per file.

- [X] T009 [US1] Move `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-interop.js` to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/map-interop.js`. Inside the new file: replace `window.ChefMapLibre = {` with `window.ChefMap = {`; replace all internal `ChefMapLibre.` references with `ChefMap.`; replace `ChefMapLibreAnimator.` (inside `centerVehiclePin`) with `ChefMapAnimator.`; replace the log prefix `[ChefMapLibre]` with `[ChefMap]`. Per data-model.md §A row 4.
- [X] T010 [US1] Move `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor` to `…/Components/Map.razor`. Markup content is unchanged (no provider-specific identifiers in the markup). Per data-model.md §A row 1.
- [X] T011 [US1] Move `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor.cs` to `…/Components/Map.razor.cs`. Inside: `public partial class MapLibre` → `public partial class Map`; `EventCallback<MapLibre>` → `EventCallback<Map>` (twice); `EventCallback<(MapLibre Map, string VehicleId)>` → `EventCallback<(Map Map, string VehicleId)>`; in `BusMarkerClickedAsync`'s tuple invocation, the `MapLibre` argument becomes `Map`; `ElementId` prefix string `cks-maplibre-` → `cks-map-`. Per data-model.md §A row 2.
- [X] T012 [US1] Move `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor.Helper.cs` to `…/Components/Map.razor.Helper.cs`. Inside: `public partial class MapLibre` → `public partial class Map`; replace all `"ChefMapLibre.*"` JS interop calls with `"ChefMap.*"`; replace all `"ChefMapLibreAnimator.*"` calls with `"ChefMapAnimator.*"`; replace log prefixes `[MapLibre]` with `[Map]`. Per data-model.md §A row 3.
- [X] T013 [US1] Move `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-vehicle-animator.js` to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/vehicle-animator.js`. Inside: `window.ChefMapLibreAnimator = {` → `window.ChefMapAnimator = {`; `ChefMapLibre.maps[containerDivId]` (in `processNearestPointBatch`) → `ChefMap.maps[containerDivId]`; log prefix `[ChefMapLibreAnimator]` → `[ChefMapAnimator]`. Per data-model.md §A row 5.
- [X] T014 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/index.html`: (a) remove the Azure Maps `<link rel="stylesheet" href="https://atlas.microsoft.com/...">` line; (b) remove the `<link href="css/azure-maps-styles.css">` line; (c) remove the `<script src="https://atlas.microsoft.com/...">` line; (d) remove the `<script src="/js/azure-maps-interop.js">` line; (e) keep the existing `<script src="/js/vehicle-animator.js">` line (it now points to the renamed animator from T013); (f) update `<script src="/js/maplibre-interop.js">` to `<script src="/js/map-interop.js">`; (g) remove the `<script src="/js/maplibre-vehicle-animator.js">` line (the renamed animator at `/js/vehicle-animator.js` replaces it); (h) keep the MapLibre CDN `<link>` and `<script>` and the `/js/perf-observer.js` script.
- [X] T015 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json`: remove the `"AzureMaps": { "AccountClientId": "..." }` block; keep the `"MapTiler"` block.
- [X] T016 [US1] In `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`: remove the `await JsRuntime.InvokeVoidAsync("ChefPerfObserver.start", "baseline");` line in `OnMapReadyAsync` (the baseline page no longer exists). The `[Inject] IJSRuntime JsRuntime` line MAY be removed if no other call uses it — verify by inspection; if any other call survives, leave the injection in place. Per data-model.md §C row 1. *(Inject removed — no other JsRuntime usages; `using Microsoft.JSInterop;` also removed.)*

### Server-side cleanup

- [X] T017 [P] [US1] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/MapsEndpoints.cs` in full.
- [X] T018 [US1] In `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`: remove the `.MapMapsEndpoints()` call from the endpoint-mapping chain (currently at line 124). Depends on T017 (the file must be gone before the call to it is removed, to surface any missed usages as build errors).
- [X] T019 [P] [US1] In `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.Development.json`: remove the `"AzureMaps": { "ManagedIdentityClientId": "...", "TenantId": "..." }` block.
- [X] T020 [P] [US1] In `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs`: remove the `public static class Maps { public const string GetMapsAuthToken = "/maps/auth/token"; }` block.

### Build + smoke gate (closes US1)

- [X] T021 [US1] Run `dotnet build C:\Projects\ChefKnifeStudios.TransitJazz\ChefKnifeStudios.TransitJazz.sln`. Expect 0 errors and warning count ≤ baseline from T002. If any error references `MapLibre`, `ChefMapLibre`, or `Azure.Identity`, fix by re-checking T009–T020 for missed substitutions. *(Result: 0 warnings, 0 errors — far below baseline of 71. Two scope expansions surfaced: (1) deleted dead `Client.Core/Services/EndpointsServices/MapsEndpointsService.cs` + DI registration in WebApp/Program.cs, (2) Phase 4 deletions T023-T027 had to run before build could pass because MapLibreTest.razor.cs referenced the now-renamed `MapLibre` type. Effectively merged T021 build gate with T031.)*
- [ ] T022 [US1] Start the local stack (AppHost + WebAPI + Worker), wait for `GtfsStaticLoader: loaded {Count} route shapes.` in the WebAPI log, navigate to `/transit-map` in Chrome with DevTools Network panel open and cache disabled. Verify: (a) MapTiler tiles render; (b) within ~10 seconds, vehicle markers appear and animate; (c) route lines render; (d) filtering Network by `atlas.microsoft.com` returns zero requests (SC-001); (e) clicking a vehicle marker logs `[Map] Vehicle marker clicked: ...`; (f) clicking an empty area logs `[Map] Map body clicked`. Per `quickstart.md` step 2. *(Deferred — user must run the local stack to verify in browser.)*

**Checkpoint**: User Story 1 complete. The production page works on MapLibre. The build is clean. Spec SC-001, SC-002, SC-004 satisfied. **At this point, the PR is functionally complete — but the cleanup of dead artifacts (US2) MUST land in the same PR per `plan.md` constraints.** Do not stop here.

---

## Phase 4: User Story 2 - No Dead Code Remains (Priority: P2)

**Goal**: Every Azure Maps artifact and POC remnant is gone from the production codebase. The constitution accurately describes the live architecture. (Satisfies spec SC-003, SC-005.)

**Independent Test**: Run the five greps from `quickstart.md` step 3; all must return zero hits in `src/` and `.specify/` paths. Read `.specify/memory/constitution.md` Principle II; it must contain "URL-restricted public API key" and not contain "Azure Maps".

### POC and Azure-test page deletions

- [X] T023 [P] [US2] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/AzureMapsTest.razor`. *(Done early during T021 build-fix.)*
- [X] T024 [P] [US2] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/AzureMapsTest.razor.cs` (also removes the `SampleDataHelper` and `VehicleData` types which were only used by this page — confirm no other file imports them via a grep for `SampleDataHelper` before deletion). *(Done early. Grep for `SampleDataHelper` returned 0 hits post-delete; the matching `VehicleData` in `Shared/EventData/` is unrelated.)*
- [X] T025 [P] [US2] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor`. *(Done early during T021 build-fix.)*
- [X] T026 [P] [US2] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor.cs`. *(Done early during T021 build-fix.)*
- [X] T027 [P] [US2] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/css/azure-maps-styles.css` (research.md R2 confirmed unused). *(Done early during T021 build-fix.)*

### Constitution amendment (the governance deliverable)

- [X] T028 [US2] In `.specify/memory/constitution.md`, apply the amendment from `specs/007-maplibre-migration/contracts/constitution-amendment.md` in full. Five edits per the contract: (1) replace the Sync Impact Report block at the top of the file; (2) replace the entire `### II. No Frontend Secrets` section with the new wording; (3) replace the `### Frontend (Blazor WASM)` bullet list under "Tech Stack & Architecture"; (4) replace the first bullet of `### Security & Authentication`; (5) update the footer line to `**Version**: 3.0.0 | **Ratified**: 2026-05-03 | **Last Amended**: 2026-05-18`.

### Verification (closes US2)

- [X] T029 [US2] Run the five grep commands from `quickstart.md` step 3 against `src/` and `.specify/` paths. All MUST return zero hits. If any return hits, locate the surviving file and remove the reference. Per spec SC-003. *(All 5 greps returned 0 hits in src/ and .specify/. SC-003 satisfied.)*
- [X] T030 [US2] Re-read `.specify/memory/constitution.md` and verify: (a) grep for `Azure Maps` returns hits ONLY inside the Sync Impact Report's history note ("auth model changed from Azure Maps Auth Function to MapTiler"); no operative architecture descriptions reference Azure Maps; (b) the phrase `URL-restricted public API key` appears in Principle II; (c) the footer reads `**Version**: 3.0.0`. Per spec SC-005 and `quickstart.md` step 4. *(All 3 sub-checks pass. SC-005 satisfied.)*
- [X] T031 [US2] Re-run `dotnet build` against the full solution. Expect 0 errors. Warning count MUST NOT exceed the T002 baseline. (Repeated from T021 because the deletions in T023–T027 could theoretically expose dead references, e.g. if `SampleDataHelper` was referenced elsewhere.) *(Result: 0 warnings, 0 errors — well below baseline of 71. SC-004 satisfied.)*

**Checkpoint**: User Story 2 complete. Repo is free of Azure Maps and POC residue. Constitution describes live architecture. Spec SC-003 and SC-005 satisfied.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Final hygiene before opening the PR. None of these are gating; skip if time-constrained.

- [X] T032 [P] Update `CLAUDE.md`'s SPECKIT marker block: replace the current pointer to `specs/007-maplibre-migration/plan.md` with a pointer to the merged feature's permanent home, or to the next active feature's plan if known. (If no next feature is queued, leave the pointer at the 007 plan as the most-recent reference.) *(Updated SPECKIT marker to reflect completed migration; still points to 007 plan as most-recent.)*
- [X] T033 Smoke-check production navigation: open the site root, click any nav links, confirm none point to `/maplibre-test` or `/azure-maps-test` (both pages are deleted). If a stale link exists in a layout, navigation page, or markdown index, remove it. Per `quickstart.md` step 5. *(Found and removed two stale links in `Pages/Index.razor`: `/maps` → "Azure Maps Test" and `/maplibre-test` → "MapLibre Test". Site root now shows only Transit Map and SignalR Test links.)*
- [X] T034 Run the full `quickstart.md` verification protocol end-to-end one final time and check each Success Criterion row in the mapping table (SC-001 through SC-005). If all five pass, the migration is complete and the PR is ready. *(SC-003/004/005 verified programmatically by T029-T031. SC-001 (zero atlas.microsoft.com network requests) and SC-002 (no animation regression) require user to run the local stack — deferred to T022 user verification.)*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies. ~5 minutes.
- **Phase 2 (Foundational)**: Empty. Skip directly to Phase 3.
- **Phase 3 (US1 — MVP)**: Depends on Phase 1 (baseline must be recorded). The phase has strict internal ordering (see below).
- **Phase 4 (US2)**: Depends on Phase 3 completion (the build must be green before more deletions).
- **Phase 5 (Polish)**: Depends on Phase 4 completion. Optional.

### Within User Story 1

Critical path is **strictly sequential** for the rename/delete chain:

```text
T004 → T010 (deletion of old Components/Map.razor must precede the rename onto that path)
T005 → T011 (same, for Map.razor.cs)
T006 → T012 (same, for Map.razor.Helper.cs)
T008 → T013 (deletion of old vehicle-animator.js must precede the rename onto that path)
T007 → (independent — no path collision)
T009 → T014 (the new JS namespace must exist before index.html references the renamed filename)
T011 → T012 (the partial class declaration must be renamed before the Helper partial is renamed, or the build is broken between the two edits)
T013 → T014 (the renamed animator must exist at /js/vehicle-animator.js before index.html keeps that script reference)
T017 → T018 (delete MapsEndpoints.cs before removing the call site, so the build surfaces any missed callers)
```

Tasks that may run in parallel inside US1:

- T017, T019, T020 are mutually independent (different server files; no shared symbols)
- T015, T016 are mutually independent (different client files; no shared symbols), and both are independent of the server tasks T017/T019/T020

### Within User Story 2

- T023, T024, T025, T026, T027 are all independent deletions ([P])
- T028 is independent of T023–T027 (different file, different domain)
- T029, T030, T031 are verification steps that depend on T023–T028 completion

### User Story Dependencies

- **US1 → US2**: US2's grep verification (T029) assumes US1's renames already happened; running T029 before T009–T013 would surface false negatives because the POC files still exist.

### Parallel Opportunities

Within Phase 3 (US1), after T004–T008 are complete:
- T009 (JS rename) and T011/T012 (Razor partial-class renames) can interleave because the JS file is unrelated to the C# build until T014 wires it in.
- T017 (delete `MapsEndpoints.cs`), T019 (`appsettings.Development.json` edit), T020 (`ApiEndpoints.cs` edit) can all run in parallel.

Within Phase 4 (US2):
- T023–T027 are five parallel deletions.
- T028 (constitution edit) is parallel to T023–T027 (different file).

---

## Parallel Example: Phase 4 (US2 deletions)

```bash
# After T021 (US1 build gate) passes, run all five US2 deletions in parallel:

Task: T023 [P] [US2] Delete AzureMapsTest.razor
Task: T024 [P] [US2] Delete AzureMapsTest.razor.cs
Task: T025 [P] [US2] Delete MapLibreTest.razor
Task: T026 [P] [US2] Delete MapLibreTest.razor.cs
Task: T027 [P] [US2] Delete azure-maps-styles.css

# T028 (constitution amendment) can run in parallel with the deletions above.
# Then T029, T030, T031 run sequentially to verify.
```

---

## Implementation Strategy

### Single-PR migration (recommended)

The migration is small enough — ~30 tasks, ~15 file deletions, ~6 in-place edits, ~5 file renames, 1 constitution amendment — to land in a single PR. Per `plan.md`, splitting US1 and US2 across PRs would briefly leave the repo with dead Azure Maps files alongside live MapLibre code, which is worse than not migrating.

1. Complete Phase 1 (baseline recording).
2. Complete Phase 3 (US1) end-to-end. **Do not commit between subtasks** — the intermediate states (e.g., after T004 but before T010) leave the repo with a missing file. Commit once at T021 if the build is green; otherwise commit at T022 after smoke passes.
3. Complete Phase 4 (US2). Commit at T031 if the build is green and greps are clean.
4. Optionally complete Phase 5 (polish). Commit separately if you do.
5. Open PR.

### Failure modes

- **T021 build fails on `MapLibre` reference**: A rename in T009–T013 missed a substitution. Re-grep the renamed file for `MapLibre`.
- **T021 build fails on `Azure.Identity`**: T017 (delete `MapsEndpoints.cs`) was skipped or incomplete.
- **T022 page is blank or grey tiles only**: The MapTiler key in `appsettings.json` is missing or invalid, *or* the `index.html` edit in T014 removed a script tag the new code depends on (most likely `/js/map-interop.js` not getting added back). Re-check T014 against `data-model.md` §C row 3.
- **T029 grep returns hits in `src/`**: A rename or deletion was incomplete. The grep output names the file; fix and re-run.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- The critical-path rename chain (T004 → T010 → T011 → T012, T008 → T013, etc.) is NOT optional ordering — files at the same path cannot coexist
- The constitution amendment (T028) is the only "soft" task that could in principle be deferred to a follow-up PR, but doing so would leave the constitution describing an architecture that no longer exists; keep it in this PR
- Commit at phase boundaries (after T021, after T031) so the morning's progress is recoverable
