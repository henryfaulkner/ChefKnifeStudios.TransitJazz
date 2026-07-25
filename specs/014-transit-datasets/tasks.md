---
description: "Task list for feature 014-transit-datasets implementation"
---

# Tasks: Transit Telemetry Datasets for the Query Bridge

**Input**: Design documents from `/specs/014-transit-datasets/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/query_telemetry.tool.md, quickstart.md

**Tests**: Included. The design doc, contract, and quickstart explicitly require an
accept/reject test matrix, runner glob assertions, and a config/migration check, so
test tasks are part of each story.

**Organization**: Tasks are grouped by user story. Note this is a **brownfield
single-module** change â€” US1 (query by dataset) and US3 (reject unsafe queries) both
operate on the same validator and share P1 priority; their tasks are sequenced so the
validator is built once with both the accept and reject behavior, then verified by
separate test phases that can be run/owned independently.

**Module under change**: `tools/telemetry-mcp/` (Go). All paths below are relative to
the repository root.

## Path Conventions

Single Go module: `tools/telemetry-mcp/` with `internal/{config,validate,query}/`,
`main.go`, and `testdata/stub-query-tool/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the module builds and tests run before changing the contract.

- [X] T001 Confirm baseline builds and tests run: from `tools/telemetry-mcp/` run `go build ./...` and `go test ./...`, and build the stub via `go build -o testdata/stub-query-tool/stub-query-tool.exe ./testdata/stub-query-tool` (record current pass/fail as the starting point)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Replace the iris contract with the transit dataset/column tables and the
new validation entry points that every user story depends on. No story can proceed
until the validator exposes per-dataset column maps and the dataset/date validators.

**âš ï¸ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 In `tools/telemetry-mcp/internal/validate/validate.go`, remove the iris `allowedColumns`/`numericColumns`/`stringColumns` maps and add the three per-dataset column maps `snapColumns`, `lerpColumns`, `cycleColumns` populated exactly per `specs/014-transit-datasets/data-model.md` (frozen contract from feature 013)
- [X] T003 In `tools/telemetry-mcp/internal/validate/validate.go`, add value-kind classification maps per dataset for the four kinds â€” `numericColumns`, `stringColumns`, `timestampColumns`, `boolColumns` â€” assigning every column to exactly one kind per data-model.md (DOUBLE/INT32/INT64 + nullable variants all â†’ numeric)
- [X] T004 In `tools/telemetry-mcp/internal/validate/validate.go`, remove `.` from `isIdentifierChar` and delete the dot-quoting branch in `parseComparison` so canonical output is the bare snake_case identifier (research R6)
- [X] T005 In `tools/telemetry-mcp/internal/validate/validate.go`, add `ValidateDataset(dataset string) error` that rejects any value not in the literal set `{snap,lerp,cycle}` (research R1)
- [X] T006 In `tools/telemetry-mcp/internal/validate/validate.go`, add `ValidateDate(date string) (string, error)` enforcing strict regex `^\d{4}-\d{2}-\d{2}$` plus a real-calendar-date check via `time.Parse`, returning the validated date unchanged (research R3)
- [X] T007 In `tools/telemetry-mcp/internal/config/config.go`, remove `DatasetURI`/`TELEMETRY_DATASET_URI`, add `StorageURI`/`TELEMETRY_STORAGE_URI` (required, clear error naming the new var when absent), and raise the default `TELEMETRY_TIMEOUT_SECONDS` from 10 to 30 (research R2)

**Checkpoint**: Validator exposes per-dataset maps + `ValidateDataset`/`ValidateDate`; config uses `StorageURI`. User-story phases can now proceed.

---

## Phase 3: User Story 1 - Query a real transit dataset by name (Priority: P1) ðŸŽ¯ MVP

**Goal**: An operator can name one of `snap`/`lerp`/`cycle` and supply a valid filter
over that dataset's columns and receive matching rows from that dataset only.

**Independent Test**: Issue a valid filter against each dataset and confirm matching
rows are returned from that dataset (accept vectors A1â€“A7 in the contract).

### Tests for User Story 1

> Write these FIRST and confirm they FAIL before implementing T011â€“T013.

- [X] T008 [P] [US1] In `tools/telemetry-mcp/internal/validate/validate_test.go`, replace the iris accept matrix with transit accept cases A1â€“A7 from `contracts/query_telemetry.tool.md` (valid snap/lerp/cycle filters, bool `true`, timestamp-vs-date, grouped predicate), calling the new `Filter(dataset, input)` signature
- [X] T009 [P] [US1] In `tools/telemetry-mcp/internal/validate/validate_test.go`, replace `TestCanonicalForm` iris expectations with transit canonical forms (bare identifiers, e.g. `snap_distance_km > 0.5`; `is_stale = false`; `observation_utc > '2026-06-04'`) per the contract's Canonical form table
- [X] T010 [P] [US1] In `tools/telemetry-mcp/internal/query/runner_test.go`, update to call `Run(ctx, cfg, dataset, date, filter)` and assert the assembled source glob equals `{StorageURI}/{dataset}/dt={date}/*.parquet` for each of snap/lerp/cycle

### Implementation for User Story 1

- [X] T011 [US1] In `tools/telemetry-mcp/internal/validate/validate.go`, change `Filter` to `Filter(dataset, input string) (string, error)`, select the dataset's column + kind maps, and thread them through `tokenize`/`parser` so column lookups and the numeric/string type-check use the selected dataset (depends on T002, T003, T011 signature)
- [X] T012 [US1] In `tools/telemetry-mcp/internal/query/runner.go`, change `Run` to `Run(ctx, cfg, dataset, date, validatedFilter string)` and build `sourceGlob := fmt.Sprintf("%s/%s/dt=%s/*.parquet", cfg.StorageURI, dataset, date)` then `SELECT * FROM '<glob>' WHERE <filter>` (research R7; depends on T007)
- [X] T013 [US1] In `tools/telemetry-mcp/main.go`, add the required `dataset` string argument to the `query_telemetry` tool, update the handler to call `ValidateDataset` â†’ (US2 will add date) â†’ `Filter(dataset, filter)` â†’ `query.Run(...)`, and update the tool description to name the three datasets with transit examples per `contracts/query_telemetry.tool.md` (depends on T005, T011, T012)
- [X] T014 [US1] In `tools/telemetry-mcp/testdata/stub-query-tool/main.go`, replace the iris guard with one that accepts queries referencing `telemetry/` and any of `snap`/`lerp`/`cycle` and emits a transit-shaped mock table (research R9)
- [X] T015 [US1] Run `go test ./...` from `tools/telemetry-mcp/` and confirm the US1 accept/canonical/glob tests (T008â€“T010) now pass

**Checkpoint**: Operator can query each dataset by name with a valid filter; MVP functional.

---

## Phase 4: User Story 2 - Scope a query to a specific day (Priority: P2)

**Goal**: An operator may supply a `date` (YYYY-MM-DD) to scope to one day; omitting it
defaults to today UTC.

**Independent Test**: Issue the same filter with an explicit past date and with no
date; confirm the explicit date targets that partition and the omitted date targets
today (quickstart scenarios 3, plus date vectors).

### Tests for User Story 2

> Write these FIRST and confirm they FAIL before implementing T018.

- [X] T016 [P] [US2] In `tools/telemetry-mcp/internal/validate/validate_test.go`, add `ValidateDate` cases from the contract's Date argument vectors: accept `2026-06-04`; reject `2026-6-4`, `2026-13-40`, `../secret`, `2026-06-04/*.parquet`
- [X] T017 [P] [US2] In `tools/telemetry-mcp/internal/query/runner_test.go`, add an assertion that an explicit date and a defaulted (today UTC) date each produce the correct `dt=` segment in the glob

### Implementation for User Story 2

- [X] T018 [US2] In `tools/telemetry-mcp/main.go`, add the optional `date` string argument; in the handler, default to `time.Now().UTC().Format("2006-01-02")` when absent, call `ValidateDate(date)` before `Filter`, and pass the validated date into `query.Run` (depends on T006, T013)
- [X] T019 [US2] In `tools/telemetry-mcp/main.go`, update the tool description to document the optional `date` argument and its default per the contract (depends on T018)
- [X] T020 [US2] Run `go test ./...` and confirm T016â€“T017 pass

**Checkpoint**: Day scoping works; omitted date targets today; US1 still passes.

---

## Phase 5: User Story 3 - Unsafe/unsupported queries rejected before any data access (Priority: P1)

**Goal**: Unknown dataset, unknown/cross-dataset column, wrong-kind literal, dotted
identifier, demo (iris) column, malformed date, and existing forbidden
keyword/char/URL patterns are each rejected with a reason, before any data access.

**Independent Test**: Issue each reject vector and confirm rejection with a reason and
no query assembly (contract reject vectors R1â€“R14 + date rejects + security
spot-checks).

### Tests for User Story 3

> Write these FIRST and confirm they FAIL (or already correctly reject) before T026.

- [X] T021 [P] [US3] In `tools/telemetry-mcp/internal/validate/validate_test.go`, add dataset-routing reject test R1 (`dataset=other`) asserting `ValidateDataset` rejects before any filter parsing
- [X] T022 [P] [US3] In `tools/telemetry-mcp/internal/validate/validate_test.go`, add unknown/cross-dataset column rejects R2 (`sepal.length`), R3 (`petal.length`), R4 (`snap.outcome`), R5 (`buses_stale` on `snap`) â€” each expecting "unknown column"
- [X] T023 [P] [US3] In `tools/telemetry-mcp/internal/validate/validate_test.go`, add value-kind rejects R6 (`is_stale = 1`), R7 (`is_stale = 'true'`), R8 (`observation_utc > 1234567`), R9 (`observation_utc > '2026-06-04T12:00:00'` â†’ forbidden `:`), R13 (`raw_lat = 'abc'`), R14 (`vehicle_id = 123`)
- [X] T024 [P] [US3] In `tools/telemetry-mcp/internal/validate/validate_test.go`, add retained-guard rejects R10 (`;`), R11 (`SELECT`), R12 (`--`) against transit columns, confirming the unchanged forbidden keyword/char/comment checks still fire (research R8)

### Implementation for User Story 3

- [X] T025 [US3] In `tools/telemetry-mcp/internal/validate/validate.go`, implement the `bool` token type: tokenize bare `true`/`false` (case-insensitive) as a `bool` token, and in `parseComparison` accept a `bool` literal only for `boolColumns` while rejecting `bool` for other kinds and rejecting numeric/string literals for `boolColumns` (research R4; depends on T003)
- [X] T026 [US3] In `tools/telemetry-mcp/internal/validate/validate.go`, enforce kind-matching for `timestampColumns` (require a `string` token, reject `number`) and ensure unknown identifiers and dotted identifiers surface as "unknown column" (depends on T004, T011, T025)
- [X] T027 [US3] In `tools/telemetry-mcp/main.go`, confirm the handler validation order is `ValidateDataset` â†’ `ValidateDate` â†’ `Filter(dataset, ...)`, returning each error to the caller before `query.Run` is ever reached (research R7; depends on T013, T018)
- [X] T028 [US3] Run `go test ./...` and confirm all US3 reject tests (T021â€“T024) pass with no data access path executed

**Checkpoint**: All reject vectors enforced before assembly; security model preserved and re-verified against the new column set.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Config/migration verification, docs, and end-to-end quickstart.

- [X] T029 [P] In `tools/telemetry-mcp/internal/config/config.go` add/confirm a test (or `main.go` startup check) that an unset `TELEMETRY_STORAGE_URI` yields a startup error naming `TELEMETRY_STORAGE_URI`, and that a legacy-only `TELEMETRY_DATASET_URI` is ignored (FR-015 / SC-005; contract Config vectors)
- [X] T030 [P] Update `tools/telemetry-mcp/README.md`: new env vars (`TELEMETRY_STORAGE_URI`, 30s default timeout), the `dataset`/`date`/`filter` arguments, the date-granularity timestamp boundary (no full ISO), and the migration note from `TELEMETRY_DATASET_URI`
- [X] T031 Remove the now-stale `tools/telemetry-mcp/DESIGN-transit-datasets.md` reference debt: verify no source/comment still references iris columns or `DatasetURI` (grep `iris`, `sepal`, `petal`, `variety`, `DatasetURI` across `tools/telemetry-mcp/`)
- [X] T032 Execute `specs/014-transit-datasets/quickstart.md` end to end: build, full `go test ./...`, the 9 validation scenarios, and the 5 security spot-checks; record results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational. The MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational; its handler change (T018) depends on US1's handler wiring (T013).
- **User Story 3 (Phase 5)**: Depends on Foundational; T025/T026 extend the validator built in US1 (T011); T027 depends on US1 (T013) and US2 (T018) handler wiring.
- **Polish (Phase 6)**: Depends on all desired stories complete.

### Why US1/US3 are sequenced (not fully parallel)

Both share P1 and edit the same validator file. US1 builds the accept path + per-dataset
plumbing (T011); US3 layers the bool token and kind-rejection rules on top (T025â€“T026)
and locks the handler validation order (T027). The reject **tests** (T021â€“T024) are
independent and [P], but the reject **implementation** depends on US1's validator.

### Within Each User Story

- Tests written and failing before implementation.
- Validator (`validate.go`) changes before handler (`main.go`) wiring.
- `config.go` / `runner.go` plumbing before the handler that calls them.
- Run `go test ./...` at the end of each story to confirm independence.

### Parallel Opportunities

- Foundational: T002â†”T003 touch the same file (sequence them); T007 (config) is [P] vs the validate-file tasks.
- US1 tests T008/T009 (validate_test.go) conflict with each other on the same file â€” run sequentially; T010 (runner_test.go) is [P] against them.
- US3 reject tests T021â€“T024 all edit `validate_test.go` â€” marked [P] for authoring independence but commit sequentially to avoid same-file conflicts.
- Polish T029 (config) and T030 (README) are [P]; T031/T032 run last.

---

## Parallel Example: User Story 1

```bash
# Test authoring across different files can proceed together:
Task: "T010 update runner_test.go glob assertions"   # internal/query/runner_test.go
# while a second worker authors:
Task: "T008 + T009 transit accept + canonical matrices"  # internal/validate/validate_test.go (same file â€” keep serial within)
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup â†’ 2. Phase 2 Foundational â†’ 3. Phase 3 US1 â†’ **STOP & VALIDATE**:
   run the accept/canonical/glob tests and one live query per dataset. This is the
   demoable MVP: operators can query the three real datasets.

### Incremental Delivery

1. Setup + Foundational â†’ contract swapped, validators in place.
2. US1 â†’ query by dataset â†’ demo (MVP).
3. US2 â†’ day scoping â†’ demo.
4. US3 â†’ full reject matrix re-verified â†’ security sign-off.
5. Polish â†’ migration check, README, quickstart.

### Security note

US3 is P1 alongside US1 precisely because retargeting the column set replaces the
entire allow-list â€” the rejection behavior is a security control and must be
re-verified, not assumed. Do not consider the feature done until Phase 5 + T031/T032
pass.

---

## Notes

- [P] = different files, no incomplete-task dependency. Several validate-file tasks are
  logically parallel but share one file â€” author in parallel, commit serially.
- [Story] labels map tasks to spec.md user stories for traceability.
- The delegated `telemetry-query-tool` binary, the MCP transport/tool name, and the
  forbidden keyword/char/URL lists are **unchanged** â€” do not modify them.
- Timestamp filters are **date-granularity only**; full ISO timestamps (`:`/`T`) are a
  deliberate non-goal (research R5).
- Commit after each task or logical group.

