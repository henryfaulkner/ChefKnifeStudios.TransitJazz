---
description: "Task list for feature implementation: Neighborhood Routes"
---

# Tasks: Neighborhood Routes

**Input**: Design documents from `specs/018-neighborhood-routes/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Branch**: All work on the current branch `017-map-style-toggle` (per user instruction — do NOT create or switch branches; FR-019). Stage selectively.

**Tests**: No automated test tasks. The spec did not request TDD and research decision D12 chose manual quickstart verification over a test harness (offline, infrequently-run batch tool; output is a committed, human-inspectable artifact). Verification = `quickstart.md`.

**Organization**: Tasks are grouped by user story. Note: US1 (lean) and US2 (full) are both emitted by a single run of `generate.py`; the shared loaders/join live in Foundational so each story stays independently testable on top of it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files / independent sections, no dependency on an incomplete task)
- **[Story]**: US1, US2, US3 (user-story phases only)
- All paths are repo-relative.

## Path Conventions

Standalone Python tool under `tools/neighborhood-routes/`; Claude skill edits under `.claude/skills/`. No `src/` (.NET) changes.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the tool directory and pin dependencies.

- [X] T001 Create directory `tools/neighborhood-routes/` (new sibling of `tools/telemetry-mcp/`).
- [X] T002 [P] Create `tools/neighborhood-routes/requirements.txt` pinning `shapely>=2.0` and `requests>=2.28` (research D1).
- [X] T003 [P] Create `tools/neighborhood-routes/generate.py` skeleton: top-of-file comment stating it is run **manually** and the re-generation triggers (GTFS shape change / GeoJSON update) per design doc §8; a `main()` + `if __name__ == "__main__"` guard; module docstring summarizing inputs/outputs.

**Checkpoint**: `pip install -r requirements.txt` succeeds; `python generate.py --help` runs (once argparse lands in T004).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: CLI surface, both data loaders, and the spatial join — everything US1 and US2 build on. All in `tools/neighborhood-routes/generate.py`.

**⚠️ CRITICAL**: No user story (lean/full output) can be produced until this phase is complete.

- [X] T004 Implement the CLI contract in `generate.py` per `contracts/cli.md`: `argparse` with `--geojson` (default `~/Downloads/Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson`, expand `~`), `--api` (default the dev container-apps base URL, strip trailing slash), `--out-dir` (default the script's own directory). Wire into `main()`.
- [X] T005 [P] Implement `load_neighborhoods(geojson_path)` in `generate.py`: read the GeoJSON; for each feature build an in-memory Neighborhood (data-model.md) — `object_id` (`OBJECTID_1`), `name` (`NAME`), `npu`, `sq_miles` (`SQMILES`), `population`, `median_household_income` (`householdi`), commute fields (`commute__1`→car_alone, `commute__3`→transit, `commute__5`→wfh per research D7), `all_properties` (verbatim copy of `properties`), and `geometry` via `shapely.geometry.shape()` (handles Polygon + MultiPolygon, research D2). Skip/abort cleanly on a feature missing `OBJECTID_1` or `NAME`; preserve missing numerics as `None` (never 0, FR-011/D5).
- [X] T006 [P] Implement `fetch_routes(api_base)` in `generate.py` per `contracts/route-shapes-input.md`: `GET {api}/gtfs/routes/shapes` with timeout ≥30s; parse the JSON array; for each feature build a Route (`route_id`=`routeId`, `route_short_name`=`routeShortName`, `linestring` from `geometry.coordinates`); skip features with unusable geometry (count them). On network error / non-2xx / unparseable body / **empty array**, raise a clear error (consumed by T007's fail-loud path — FR-014). Add a defensive check + clear message if the top level is not a bare array (D8 verify-at-runtime).
- [X] T007 Implement fail-loud orchestration in `main()`: call loaders inside try/except; on any fatal error print a clear message to **stderr** and `sys.exit(1)` **before any file is written** (FR-014, SC-004); only proceed to build/write outputs after both loaders succeed.
- [X] T008 Implement `spatial_join(neighborhoods, routes)` in `generate.py`: for each neighborhood, `matched = [r for r in routes if neighborhood.geometry.intersects(r.linestring)]` (research D2/D3, raw WGS84, no reprojection). Attach matches to each neighborhood in memory. This single result feeds both US1 and US2.

**Checkpoint**: Loaders + join run end-to-end in memory; a forced bad `--api` exits non-zero with no files written. Ready for output-building stories.

---

## Phase 3: User Story 1 - Pre-compute the neighborhood-to-route mapping (Priority: P1) 🎯 MVP

**Goal**: Emit the committed **lean** file mapping every neighborhood to its intersecting routes + high-signal demographics, plus the run summary.

**Independent Test**: Run the tool against a real GeoJSON + live API; confirm `neighborhood_routes.json` contains one entry per neighborhood (sorted by name), with `routes` (empty `[]` when none), rounded demographics, and that stdout prints the summary (totals, 0-route names, unique routes). (quickstart steps 1–7, 9–10)

### Implementation for User Story 1

- [X] T009 [US1] Implement `build_lean_entry(neighborhood)` in `generate.py` per `contracts/lean-output.schema.md` + data-model.md: emit `objectId`(int), `name`, `npu`, `sqMiles`, `population` (round to int, null-guarded), `medianHouseholdIncome` (round to int, null-guarded), `transitCommutePercent`/`carAlonePercent`/`workFromHomePercent` (round to 1 dp, null-guarded — FR-007/D6), and `routes` as `[{routeId, routeShortName}, …]` (`[]` if none — FR-006). Missing numerics → `null` (FR-011).
- [X] T010 [US1] Implement `write_lean(out_dir, neighborhoods)` in `generate.py`: build the top-level object `{ generatedAt (UTC ISO-8601), sourceGeoJson (basename of --geojson), neighborhoods: [...] }` with the array **sorted by `name` ascending** (FR-006/FR-010); write `neighborhood_routes.json` with `json.dump(..., indent=2, ensure_ascii=False)`. No geometry anywhere (FR-009).
- [X] T011 [US1] Implement the run summary to stdout in `main()` per `contracts/cli.md` + FR-013: total neighborhoods processed; count with ≥1 route; count + comma-separated **names** of 0-route neighborhoods; total unique routes matched across all neighborhoods; (recommended) skipped-feature counts from T005/T006.
- [X] T012 [US1] Run the tool against the real GeoJSON + live API and **commit** the generated `tools/neighborhood-routes/neighborhood_routes.json` (FR-015). Verify quickstart steps 2–7, 9–10 and the D7 commute-field spot-check (step 11) on 2–3 known neighborhoods (e.g. Ridgewood Heights, Vine City); correct the D7 mapping in T009 if the spot-check disagrees.

**Checkpoint**: Lean file exists, committed, and answers "which routes serve neighborhood X" / "which neighborhoods does route Y pass through" without re-running (SC-005). MVP complete.

---

## Phase 4: User Story 2 - Deep-dive demographic reference (Priority: P2)

**Goal**: Emit the committed **full** demographic dump keyed by `str(objectId)`, round-trippable from any lean entry.

**Independent Test**: After a run, confirm `neighborhood_routes_full.json` is a dict keyed by stringified objectId, each value holding all source properties verbatim (no rename/round), no geometry; and that a lean `objectId` resolves via `full["neighborhoods"][str(objectId)]`. (quickstart step 8)

### Implementation for User Story 2

- [X] T013 [US2] Implement `write_full(out_dir, neighborhoods)` in `generate.py` per `contracts/full-output.schema.md` + data-model.md: build `{ generatedAt, sourceGeoJson, neighborhoods: { str(object_id): all_properties, … } }` from each neighborhood's verbatim `all_properties` (no rename, no rounding — FR-008), excluding geometry/coordinates (FR-009); write `neighborhood_routes_full.json` with `json.dump(..., ensure_ascii=False)`. Call from `main()` in the same successful run as the lean write (after the join, before exit).
- [X] T014 [US2] Run the tool and **commit** `tools/neighborhood-routes/neighborhood_routes_full.json` (FR-015). Verify quickstart step 8: pick a lean `objectId`, confirm `full["neighborhoods"][str(objectId)]` exists and its `OBJECTID_1` matches (SC-003/FR-012); confirm no geometry present (FR-009).

**Checkpoint**: Both files emitted by one run; lean↔full round-trip holds.

---

## Phase 5: User Story 3 - Assistant skills consume the dataset (Priority: P3)

**Goal**: Wire the two existing Claude skills to read the committed lean file (and the full file on explicit request only).

**Independent Test**: Ask `mj-data-explorer` "which routes serve Vine City?" → it reads `neighborhood_routes.json` and answers without re-running the tool, and consults the full file only when explicitly asked for one neighborhood's demographics. Invoke `create-neighborhood-blurb` with a neighborhood name/objectId → it uses the lean fields as input. (quickstart step 6)

### Implementation for User Story 3

- [X] T015 [P] [US3] Create `.claude/skills/mj-data-explorer/references/neighborhood-routes-context.md` (research D10) describing: the lean file location/shape and when to read it (routes↔neighborhood Q&A, transit-dependency rankings, blurb demographic context); that the full file is consulted **only** per-`objectId` on explicit analyst request and **never loaded speculatively** (FR-016); the lean↔full lookup `full["neighborhoods"][str(objectId)]`.
- [X] T016 [US3] Update `.claude/skills/mj-data-explorer/SKILL.md`: add a routing-table row for neighborhood-level questions pointing to `references/neighborhood-routes-context.md`, and add a matching entry under "Files in this skill" (follow the existing reference-doc pattern — FR-016).
- [X] T017 [P] [US3] Update `.claude/skills/create-neighborhood-blurb/SKILL.md` per research D11 (additive, do NOT replace the existing sonic voice): add a step to accept a neighborhood name or `objectId`, read `tools/neighborhood-routes/neighborhood_routes.json`, find the matching lean entry, and use its signals (routes & shortNames, `transitCommutePercent`, `workFromHomePercent`, `medianHouseholdIncome`, `npu`, `population`+`sqMiles` density) as **structured input** informing the prose. Preserve the skill's existing target voice, 2–3 sentence limit, and the rule that sonic claims match assigned voices. Do NOT emit a new C# artifact (FR-017/FR-018).

**Checkpoint**: Both skills answer neighborhood questions / draft blurbs from the committed files; full dump never auto-loaded.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification and hygiene.

- [X] T018 Run the full `quickstart.md` end-to-end including the API-failure check (step 5: bad `--api` → non-zero exit, no files written/overwritten — SC-004) and confirm all 11 verification rows pass.
- [X] T019 [P] Confirm no `src/` (.NET) files changed and that only `tools/neighborhood-routes/**` + the two skill files are staged for this feature (FR-019 — stage selectively away from the unrelated 017 map-toggle changes on this branch).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS US1 and US2** (they need the loaders + join).
- **User Story 1 (Phase 3, P1)**: Depends on Foundational. The MVP.
- **User Story 2 (Phase 4, P2)**: Depends on Foundational. `write_full` (T013) is independent of the lean code, but T014 commits output from the same run as T012 — sequence US2 right after US1 in practice.
- **User Story 3 (Phase 5, P3)**: Depends on US1 (needs the committed lean file to be meaningful) and ideally US2 (so the full-file guidance in T015 references a real file).
- **Polish (Phase 6)**: After all desired stories.

### User Story Dependencies

- **US1 (P1)**: After Foundational. No dependency on other stories.
- **US2 (P2)**: After Foundational. Shares the run/orchestration with US1 but its output file is independently testable.
- **US3 (P3)**: After US1 (and US2) — consumes their committed artifacts.

### Within Each Story

- Build helpers before the writer; writer before the commit/verify task.
- US1: T009 → T010 → T011 → T012. US2: T013 → T014. US3: (T015, T017 parallel) → T016.

### Parallel Opportunities

- Setup: T002 and T003 in parallel (different files).
- Foundational: T005 and T006 in parallel (independent functions; both edit `generate.py` — coordinate or land sequentially if working in one file).
- US3: T015 and T017 in parallel (different skill files); T016 depends on T015.
- Polish: T019 in parallel with T018's manual review.

---

## Parallel Example: Foundational Phase

```bash
# Independent loader functions (different concerns in generate.py):
Task: "Implement load_neighborhoods(geojson_path) in tools/neighborhood-routes/generate.py (T005)"
Task: "Implement fetch_routes(api_base) in tools/neighborhood-routes/generate.py (T006)"
```

## Parallel Example: User Story 3

```bash
# Different files, no shared edits:
Task: "Create .claude/skills/mj-data-explorer/references/neighborhood-routes-context.md (T015)"
Task: "Update .claude/skills/create-neighborhood-blurb/SKILL.md (T017)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL — blocks output) → 3. Phase 3 US1 → **STOP & VALIDATE** the lean file via quickstart → the tool already answers the core neighborhood↔route questions. Demo-ready.

### Incremental Delivery

1. Setup + Foundational → loaders + join ready.
2. US1 → commit lean file → **MVP** (quickstart 1–7,9–10).
3. US2 → commit full dump → deep-dive round-trip (quickstart 8).
4. US3 → wire both skills (quickstart 6).
5. Polish → full quickstart + failure path + stage-hygiene check.

---

## Notes

- [P] = different files / independent; tasks editing the same `generate.py` section are sequential even when conceptually parallel.
- Output JSON files (`neighborhood_routes.json`, `neighborhood_routes_full.json`) are **committed artifacts** (FR-015) — they are produced by running the tool against the live API, not hand-written.
- Stage selectively: this branch also carries unrelated 017 map-toggle changes (FR-019) — keep this feature's commits to `tools/neighborhood-routes/**` and the two skill files.
- Commute-field decode (D7) is inferred — the T012 spot-check is load-bearing; correct T009 if it disagrees.
- create-neighborhood-blurb update is **additive** (D11) — do not overwrite the existing feature-011 sonic voice. If the user wants a separate demographic blurb artifact, raise it before implementing T017.
