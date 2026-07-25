---
description: "Task list for Contextual Telemetry Query MCP Bridge"
---

# Tasks: Contextual Telemetry Query MCP Bridge

**Input**: Design documents from `/specs/012-telemetry-mcp-bridge/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/query_telemetry.tool.md, quickstart.md

**Tests**: INCLUDED. The spec makes validation a load-bearing security control (FR-019) with explicit rejection requirements (US2, SC-002/SC-003) and the quickstart requires `go test ./...` + a fuzz target. Test tasks for the validator and the exec runner are therefore mandatory, not optional.

**Organization**: Tasks are grouped by user story. The new bridge lives in its own Go module at `tools/telemetry-mcp/`. The existing wrapped tool lives at `telemetry-query-tool/` (modified only by the secret-remediation phase).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in each task.

## Path Conventions

- New bridge module (single Go project): `tools/telemetry-mcp/`
- Existing wrapped tool (pre-existing, modified by Phase 2): `telemetry-query-tool/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Initialize the standalone Go module for the bridge.

- [X] T001 Create the bridge module directory tree `tools/telemetry-mcp/` with subdirs `internal/validate/`, `internal/query/`, `internal/config/`, and `testdata/stub-query-tool/` per plan.md Project Structure
- [X] T002 Initialize the Go module in `tools/telemetry-mcp/go.mod` (`module telemetry-mcp`, Go 1.23+) and add the dependency `github.com/mark3labs/mcp-go`, then run `go mod tidy` to produce `tools/telemetry-mcp/go.sum`
- [X] T003 [P] Add `tools/telemetry-mcp/.gitignore` (ignore built binaries `telemetry-mcp`, `telemetry-mcp.exe`, and `testdata/stub-query-tool/*.exe`) and a placeholder `tools/telemetry-mcp/README.md` pointing at `specs/012-telemetry-mcp-bridge/quickstart.md`
- [X] T004 [P] Verify the toolchain builds an empty `tools/telemetry-mcp/main.go` (package main with empty `func main()`) via `go build ./...` to confirm the module compiles before real work begins

---

## Phase 2: Secret Remediation & Underlying-Tool Readiness (Blocking Prerequisite)

**Purpose**: FR-020 / SC-007 — remove the committed live Azure credential, source it from the environment, and make the wrapped tool buildable/runnable. This BLOCKS safe end-to-end testing of every user story (the bridge cannot be validated against a tool that won't build or that ships a hardcoded key).

**⚠️ CRITICAL**: No user-story validation/integration is trustworthy until this phase is complete.

- [ ] T005 Rotate/revoke the exposed Azure storage AccountKey in the Azure portal (Storage account `randomstoragehenry` → Access keys → Rotate), and record the new connection string in a local secret store (NOT in any repo file)
- [ ] T006 Modify `telemetry-query-tool/main.go` to read the connection string from `os.Getenv("AZURE_STORAGE_CONNECTION_STRING")` instead of the hardcoded literal on line ~24; keep the existing `if azureConnString == ""` guard as a real check (fail with the existing stderr message + non-zero exit when unset)
- [ ] T007 Fix the build break recorded in `telemetry-query-tool/build_err.txt` (`go-duckdb@v1.8.5: undefined: Conn`) by pinning a working `github.com/marcboeker/go-duckdb` + DuckDB version in `telemetry-query-tool/go.mod`, then `go mod tidy`
- [ ] T008 Rebuild `telemetry-query-tool/telemetry-query-tool.exe` from the remediated source and smoke-test it once with `AZURE_STORAGE_CONNECTION_STRING` set: run a trivial `select count(*) ... from '<dataset>'` and confirm a table prints
- [ ] T009 Verify no credential remains in source: `git grep -i accountkey` and `git grep -i "AccountKey="` return zero matches across the repo (satisfies SC-007)

**Checkpoint**: The wrapped tool builds, runs with an env-supplied (rotated) key, and the repo is clean of the secret.

---

## Phase 3: Foundational (Blocking Prerequisites for the bridge)

**Purpose**: Core bridge scaffolding every user story depends on: startup config (fixed dataset + tool path + limits), stderr logging, and the MCP server bootstrap that registers `query_telemetry`.

**⚠️ CRITICAL**: No user story phase can be completed until this phase is done.

- [ ] T010 [P] Implement startup configuration in `tools/telemetry-mcp/internal/config/config.go`: resolve and validate `TELEMETRY_TOOL_PATH`, `TELEMETRY_DATASET_URI`, optional `TELEMETRY_TIMEOUT_SECONDS` (default 10) and `TELEMETRY_MAX_OUTPUT_BYTES` (default 65536) once at startup into an immutable `Config` struct; fail fast with a clear stderr error if required vars are missing (FR-013, FR-017)
- [ ] T011 [P] Configure structured logging to **stderr only** via `log/slog` in `tools/telemetry-mcp/internal/config/` (or a small `logging.go`), ensuring stdout is never written to outside MCP protocol traffic (R4, FR-014)
- [ ] T012 Implement the MCP server bootstrap in `tools/telemetry-mcp/main.go`: load `Config` (T010), build the server with `server.NewMCPServer("Telemetry-Bridge","1.0.0", ...)`, define the `query_telemetry` tool with `mcp.NewTool(... mcp.WithString("filter", mcp.Required(), mcp.Description(...)))` matching `contracts/query_telemetry.tool.md`, register via `s.AddTool(tool, handler)`, and start with `server.ServeStdio(s)` (R1) — handler wired to a stub for now
- [ ] T013 Build the test stub executable in `tools/telemetry-mcp/testdata/stub-query-tool/main.go`: a tiny Go program that echoes its received argv[1] (and optionally a canned table) to stdout, supports an env flag to exit non-zero / hang / emit oversized output, used by runner integration tests

**Checkpoint**: `go build ./...` succeeds; the server starts, lists `query_telemetry`, and the stub tool is available for tests.

---

## Phase 4: User Story 2 - Malicious or malformed filters are safely rejected (Priority: P1) 🎯 MVP (security core)

**Goal**: A strict allow-list validator that turns an untrusted `filter` into a validated, canonically re-emitted predicate — or rejects it before anything executes. This is the load-bearing control (FR-019) and is implemented FIRST because US1's success path depends on it.

**Independent Test**: Run the validator unit/fuzz tests; every Reject vector in `contracts/query_telemetry.tool.md` is rejected and every Accept vector yields a canonical predicate — with no process execution involved.

### Tests for User Story 2 (write first, ensure they FAIL) ⚠️

- [ ] T014 [P] [US2] Table-driven unit tests for the validator in `tools/telemetry-mcp/internal/validate/validate_test.go` asserting ALL Accept vectors pass (return canonical predicate) and ALL Reject vectors fail (statement chaining `;`, comments `--`/`/* */`/`#`, `UNION`/keywords, alternate `azure://`/`http`/`file`/path data sources, non-allow-listed columns e.g. `password`, shell metacharacters `$(...)`/`|`/`&`, `1=1` literal-in-column-position, oversized >256 chars, empty/missing) — per `contracts/query_telemetry.tool.md` and `data-model.md` ValidationPolicy
- [ ] T015 [P] [US2] Fuzz target `FuzzValidate` in `tools/telemetry-mcp/internal/validate/validate_test.go` asserting the invariant: any input that validates re-emits a canonical predicate containing ONLY allow-listed columns/operators/connectors/literals and never a forbidden token (defense against encoding bypasses)

### Implementation for User Story 2

- [ ] T016 [US2] Implement the tokenizer in `tools/telemetry-mcp/internal/validate/validate.go`: produce only allow-listed token types (allow-listed column identifiers, numeric literals `-?\d+(\.\d+)?`, bounded single-quoted strings `'[A-Za-z0-9 _-]{0,64}'`, comparison ops `< <= > >= = !=`, `AND`/`OR`, parens, whitespace); reject any other byte/token (FR-006, FR-008)
- [ ] T017 [US2] Implement length + presence pre-checks in `validate.go`: trim, reject empty (FR-011) and >256 chars (FR-010) before tokenizing
- [ ] T018 [US2] Implement the recursive-descent grammar (`predicate→orExpr→andExpr→comparison`, with parens and column/op/literal rules incl. type match: numeric cols→number, `species`→quoted string) in `validate.go`, enforcing column-on-left / literal-on-right so `1=1` is rejected (data-model.md grammar)
- [ ] T019 [US2] Implement **canonical re-emission** in `validate.go`: build the output predicate string from parsed tokens (never from raw input bytes), returning `(canonicalPredicate string, err error)` (FR-009, R2)
- [ ] T020 [US2] Return sanitized validation errors from the validator (stable, user-facing reason strings with no internal paths/credentials) per `contracts/query_telemetry.tool.md` error rows (FR-012)

**Checkpoint**: `go test ./internal/validate/...` is green (incl. fuzz); the validator independently enforces SC-002/SC-003 with zero execution.

---

## Phase 5: User Story 1 - Ask a natural-language question about telemetry data (Priority: P1) 🎯 MVP (primary value)

**Goal**: End-to-end happy path — a validated filter is assembled into a full SQL statement around the constant data source, executed via the wrapped `.exe` under time/output bounds, and the rendered result is returned to the model.

**Independent Test**: With the bridge registered, asking a natural-language question (e.g., "how many records have petal length over 5?") returns the tool's rendered count table; quickstart tests 1–4 pass.

### Tests for User Story 1 (write first, ensure they FAIL) ⚠️

- [ ] T021 [P] [US1] Integration tests for the query runner in `tools/telemetry-mcp/internal/query/runner_test.go` using the stub executable (T013): assert (a) a validated predicate produces the expected full statement passed as a single argv element with the constant data source, (b) timeout aborts a hung stub within the bound, (c) oversized stub output is truncated to the cap, (d) non-zero/missing stub yields a sanitized error (FR-014/FR-015, C-1/C-5)
- [ ] T022 [P] [US1] Handler-level test in `tools/telemetry-mcp/internal/query/` (or `main_test.go`) verifying the `query_telemetry` handler returns `NewToolResultText` on stub success and `NewToolResultError` on a rejected filter without invoking the stub (C-2/C-3)

### Implementation for User Story 1

- [ ] T023 [US1] Implement statement assembly in `tools/telemetry-mcp/internal/query/runner.go`: build `select count(*) as count from '<config.DatasetURI const>' where <canonicalPredicate>` using ONLY the validated predicate + the constant dataset URI (FR-007/FR-013, C-1)
- [ ] T024 [US1] Implement bounded execution in `runner.go`: `exec.CommandContext(ctx, config.ToolPath, fullStatement)` with the configured timeout, capped stdout reading, and `AZURE_STORAGE_CONNECTION_STRING` passed through to the child env; classify outcomes (success / timeout / non-zero / missing tool / oversized) (FR-015, R3)
- [ ] T025 [US1] Implement result shaping in `runner.go`: return the stub/tool stdout (rendered table), optionally stripping the leading `Fetching…` status line, as the success text (data-model.md QueryResult)
- [ ] T026 [US1] Implement sanitized execution-error mapping in `runner.go`: convert tool/exec failures to user-facing messages with no absolute paths, dataset URI, or credential (FR-012, C-4)
- [ ] T027 [US1] Wire the real handler in `tools/telemetry-mcp/main.go`: read `filter` via `request.RequireString("filter")`, call validator (Phase 4) → on error return `NewToolResultError`; on success call runner (T023–T026) → return `NewToolResultText`/`NewToolResultError` (replaces the T012 stub handler)

**Checkpoint**: `go test ./...` green; manual quickstart tests 1–4 pass against the real tool. **This + Phase 4 is the MVP.**

---

## Phase 6: User Story 3 - Operator installs and registers the bridge (Priority: P2)

**Goal**: A documented, reproducible install/registration path so the operator gets `query_telemetry` available in Claude Code, with graceful behavior when the wrapped tool is absent.

**Independent Test**: Following the quickstart on a clean setup makes `query_telemetry` appear in Claude Code; removing the `.exe` yields a sanitized error rather than a crash (quickstart tests 1, 8).

- [ ] T028 [P] [US3] Finalize `tools/telemetry-mcp/README.md` with build (`go build -o telemetry-mcp.exe .`), env vars, and the Claude Code `mcpServers` registration snippet (incl. `AZURE_STORAGE_CONNECTION_STRING`) mirroring `specs/012-telemetry-mcp-bridge/quickstart.md`
- [ ] T029 [US3] Add a startup self-check in `tools/telemetry-mcp/internal/config/config.go` (or `main.go`): if `TELEMETRY_TOOL_PATH` does not exist/stat fails at startup, log a clear stderr warning (do not crash the server) so missing-tool surfaces as a clean tool-error at call time (FR-014, SC-006)
- [ ] T030 [US3] Add an integration/e2e check (scripted or documented in README) that registers the built binary, lists tools, and exercises the missing-`.exe` path returning a sanitized error (quickstart tests 1 & 8)

**Checkpoint**: All three user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final verification and hardening across stories.

- [ ] T031 [P] Run the full `specs/012-telemetry-mcp-bridge/quickstart.md` (all 9 manual tests, incl. #9 `git grep accountkey`) and record pass/fail
- [ ] T032 [P] Run `go vet ./...` and `gofmt -l` over `tools/telemetry-mcp/`; fix findings
- [ ] T033 Re-run `go test ./...` including `go test -run Fuzz -fuzz=Fuzz ./internal/validate` for a bounded duration; confirm no new corpus crashers
- [ ] T034 Security pass: re-confirm C-1…C-6 invariants from `contracts/query_telemetry.tool.md` hold against the assembled code (constant data source, no second statement, missing-filter never executes, sanitized errors, bounded time/output, read-only)
- [ ] T035 [P] Update `CLAUDE.md` SPECKIT block only if implementation revealed deviations from the plan (e.g., final dataset URI form or projection syntax)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Secret Remediation (Phase 2)**: Independent of the Go module; SHOULD complete before end-to-end testing of US1/US3. T005→T006→T007→T008→T009 are sequential (same files / build chain).
- **Foundational (Phase 3)**: Depends on Phase 1. Blocks Phases 4–6.
- **US2 (Phase 4)**: Depends on Phase 3. **Implemented before US1** because US1's handler calls the validator.
- **US1 (Phase 5)**: Depends on Phase 3 + Phase 4 (validator) and, for trustworthy end-to-end runs, Phase 2.
- **US3 (Phase 6)**: Depends on Phase 3; meaningful e2e depends on US1 + Phase 2.
- **Polish (Phase 7)**: Depends on all desired stories complete.

### User Story Dependencies

- **US2 (P1, validation)**: Self-contained; no execution. Foundation of the security guarantee.
- **US1 (P1, query)**: Depends on US2's validator (cross-story dependency is intentional here — the wrapper's safety is inseparable from its happy path).
- **US3 (P2, install)**: Depends on a built bridge (US1) to be meaningful.

### Within Each Story

- Tests written first and FAIL → implementation.
- Tokenizer → grammar → re-emission (US2). Statement assembly → execution → result/error shaping → handler wiring (US1).

### Parallel Opportunities

- Phase 1: T003, T004 in parallel after T001/T002.
- Phase 2 can run fully in parallel with Phases 1 & 3 (different files/module).
- Phase 3: T010 and T011 in parallel; T012/T013 after.
- Phase 4: T014, T015 (tests) in parallel; implementation T016→T019 sequential (same file `validate.go`).
- Phase 5: T021, T022 (tests) in parallel; implementation T023→T026 sequential (same file `runner.go`), T027 last.
- Phase 7: T031, T032, T035 in parallel.

---

## Parallel Example: User Story 2 (validator)

```bash
# Write both test files first (they share no code):
Task: "Table-driven validator tests in tools/telemetry-mcp/internal/validate/validate_test.go"
Task: "Fuzz target FuzzValidate in tools/telemetry-mcp/internal/validate/validate_test.go"
# (then implement T016→T019 sequentially in validate.go)
```

---

## Implementation Strategy

### MVP First (Security core + primary value)

1. Phase 1 Setup → Phase 3 Foundational.
2. Phase 2 Secret Remediation (in parallel — unblocks safe e2e).
3. Phase 4 (US2 validator) → Phase 5 (US1 query path).
4. **STOP and VALIDATE**: quickstart tests 1–7 pass (happy path + rejections).
5. This is a demoable MVP: safe natural-language querying.

### Incremental Delivery

1. Setup + Foundational + Remediation → foundation ready & secret closed.
2. US2 validator → unit/fuzz green (SC-002/SC-003).
3. US1 query path → e2e green (SC-001/SC-004) → MVP demo.
4. US3 install/docs → operator onboarding (SC-005/SC-006).
5. Polish → full quickstart + security invariant sign-off.

---

## Notes

- [P] = different files, no dependencies. Most US2/US1 implementation tasks touch a single file each (`validate.go` / `runner.go`) so they are intentionally NOT [P].
- US2 is sequenced before US1 despite both being P1: the wrapper's happy path literally calls the validator.
- The Azure credential is the *wrapped tool's* concern — passed through the child process env (T024); the bridge never embeds or logs it.
- Commit after each task or logical group. Working on `main` per operator request.
