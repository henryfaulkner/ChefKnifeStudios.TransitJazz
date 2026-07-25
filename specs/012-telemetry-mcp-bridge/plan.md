# Implementation Plan: Contextual Telemetry Query MCP Bridge

**Branch**: `main` | **Date**: 2026-06-04 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/012-telemetry-mcp-bridge/spec.md`

## Summary

Build a small, standalone Go MCP server (`telemetry-mcp`) that runs locally over stdio and exposes a single `query_telemetry` tool to Claude Code. The tool accepts a `filter` (a read-only condition over a fixed `iris.parquet` dataset) that Claude derives from conversation, **validates that filter against a strict allow-list grammar before it ever reaches the underlying executable**, then invokes the existing **`telemetry-query-tool.exe`** (the operator-owned Go CLI already in this repo at `telemetry-query-tool/`) to run the query and returns the rendered result as text.

**Chosen architecture: wrapper around the existing `.exe`** (the native/in-process alternative was considered and deferred). The bridge shells out via `exec.CommandContext`, passing one fully-assembled SQL statement as the single argv element.

**Reality of the underlying tool (now read from its source, not assumed):** `telemetry-query-tool/main.go` opens an in-memory **DuckDB**, `INSTALL`/`LOAD`s the **Azure** extension, sets an Azure storage connection string, then runs **whatever SQL string is passed as `argv[1]`** via `db.Query(...)` and prints a status line plus an ASCII table (via `tablewriter`). It is therefore **not** an inherently read-only black box — it will execute arbitrary DuckDB SQL with a live cloud credential loaded. This makes the allow-list validation **load-bearing** (FR-019): without it, a crafted filter could reach other storage objects, the local filesystem, write operations (`COPY ... TO`), or extension installs — far beyond "other rows of iris.parquet."

The central technical problem is thus **closing the injection vector** the source design doc left open: the filter is untrusted input and must be parsed/validated, never string-interpolated blindly. The design replaces the doc's `fmt.Sprintf(... WHERE %s ...)` with a tokenize→parse→re-emit approach so only a validated predicate over known columns can be executed, with no ability to redirect the data source, chain statements, inject comments/escapes, or reach the shell.

**Companion remediation (FR-020):** `telemetry-query-tool/main.go` currently hardcodes a **live Azure storage AccountKey in committed source** (and its `if azureConnString == ""` guard is dead code). This feature includes moving that connection string to a local environment variable and rotating the exposed key.

## Technical Context

**Language/Version**: Go 1.23+ (single static binary, cross-compilable to Windows)
**Primary Dependencies**: `github.com/mark3labs/mcp-go` (MCP server SDK over stdio); Go stdlib `os/exec`, `context`, `regexp`/hand-written tokenizer for filter validation. No third-party SQL parser required for the v1 allow-list grammar.
**Storage**: None owned by the bridge. The dataset (`iris.parquet`) lives in Azure Blob Storage and is queried exclusively through the existing `telemetry-query-tool.exe` (in-memory DuckDB + Azure extension). The bridge holds no state between calls. The Azure credential belongs to the underlying tool, supplied via its own local env var (FR-020) — the bridge neither holds nor logs it.
**Testing**: Go `testing` (table-driven unit tests for the validator + a fuzz target for the filter parser; integration test using a stub query executable).
**Target Platform**: Local developer machine (Windows primary, per the `.exe` target; binary also builds for macOS/Linux for dev). Runs as a stdio child process of Claude Code — no network listener.
**Project Type**: Standalone CLI / MCP server tool (single Go module), independent of the TransitJazz .NET solution.
**Performance Goals**: Typical valid query returns within 5 s under normal local conditions (SC-004); validation overhead is sub-millisecond.
**Constraints**: No network hosting/ports; user-level privileges only (FR-016); bounded response time and output size (FR-015); dataset target fixed in bridge config and not overridable via input (FR-013); error messages must not leak internal paths/credentials (FR-012).
**Scale/Scope**: Single dataset, single tool, single user, low call volume (interactive). ~300–500 LOC of Go plus tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The TransitJazz Constitution (v3.0.0) governs the transit application's .NET/Azure architecture and real-time data pipeline. This feature is a **standalone, local developer tool** that does not touch the Blazor frontend, WebAPI, Worker, SignalR pipeline, GTFS data, or any Azure deployment artifact. Most principles are therefore **not applicable by scope**. The relevant evaluations:

| Principle | Applicability & Status |
|-----------|------------------------|
| I. Decoupled Cloud Architecture | **N/A** — this is a local stdio tool, not a deployed cloud unit; it adds no new hosted service. ✅ |
| II. No Frontend Secrets | **Engaged — and a fix is in scope.** No frontend involved, and the bridge itself holds no secrets. However, the *existing* `telemetry-query-tool/main.go` hardcodes a live Azure AccountKey in committed source, which violates the spirit of this principle (secrets must never be committed). This plan includes FR-020: relocate the connection string to a local env var and rotate the key. The bridge never embeds or logs the credential, and error messages are sanitized (FR-012). ✅ (with remediation task) |
| III. Two-Pass Real-Time Pipeline | **N/A** — no GTFS-RT processing. ✅ |
| IV. OpenTelemetry Observability | **N/A to .NET stack**, but honored in spirit: the bridge uses structured logging to stderr (MCP requires stdout be reserved for protocol traffic). No Azure Log Analytics integration is required for a local dev tool. ✅ |
| V. Azure DevOps CI/CD | **N/A** — the tool is not part of the WASM-or-Docker deployment artifacts; it builds locally via `go build`. No pipeline change required. ✅ |
| VI. GTFS ID Mapping | **N/A** — no GTFS data. ✅ |

**Technology Enforcement note**: The constitution's "no unauthorized technology substitutions" clause governs the *TransitJazz application* tech stack. Introducing Go for a separate, non-deployed developer utility does not substitute any app technology and does not require a constitution amendment. (The repo already contains an archived Go POC under `POC/BusDataPoc`, so Go is not foreign to the repository.)

**Gate result: PASS** — no violations, no Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/012-telemetry-mcp-bridge/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── query_telemetry.tool.md   # MCP tool contract (input schema, result shape, errors)
├── checklists/
│   └── requirements.md  # Spec quality checklist (already created by /speckit.specify)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

The bridge lives in its own self-contained Go module, kept out of the .NET solution tree so it neither participates in the .NET build nor in the deployment artifacts. It sits beside the **existing** `telemetry-query-tool/` it wraps.

```text
telemetry-query-tool/          # EXISTING (not created here) — operator-owned DuckDB+Azure CLI
├── main.go                    # MODIFIED by FR-020: read conn string from env, not hardcoded
├── telemetry-query-tool.exe   # the wrapped binary
└── ...                        # go.mod, libduckdb/, duckdb.dll

tools/telemetry-mcp/           # NEW — this feature
├── go.mod                     # Module: telemetry-mcp
├── go.sum
├── main.go                    # MCP server bootstrap (stdio), tool registration, wiring
├── internal/
│   ├── validate/
│   │   ├── validate.go        # Allow-list filter grammar: tokenizer + validator + rebuilder
│   │   └── validate_test.go   # Table-driven tests + fuzz target (good/malicious filters)
│   ├── query/
│   │   ├── runner.go          # exec wrapper: builds final query from VALIDATED predicate,
│   │   │                      #   invokes telemetry-query-tool.exe, bounds time/output
│   │   └── runner_test.go     # Integration test against a stub executable
│   └── config/
│       └── config.go          # Resolves dataset target + tool path from env/config (fixed at startup)
├── testdata/
│   └── stub-query-tool/       # Tiny Go program built as a fake telemetry-query-tool for tests
└── README.md                  # Install + Claude Code registration steps (mirrors quickstart.md)
```

**Structure Decision**: A **single standalone Go module under `tools/telemetry-mcp/`**, isolated from the .NET solution. This keeps the injection-safety logic (`internal/validate`) unit- and fuzz-testable in isolation, separates the untrusted-input boundary (validate) from execution (query), and ensures the tool never enters the constitution-governed WASM/Docker deployment pipeline. The legacy `telemetry-query-tool.exe` is treated as an external dependency located via config, not vendored into source control.

## Complexity Tracking

> No Constitution Check violations. No entries required.
