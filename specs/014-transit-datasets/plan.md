# Implementation Plan: Transit Telemetry Datasets for the Query Bridge

**Branch**: `014-transit-datasets` | **Date**: 2026-06-04 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/014-transit-datasets/spec.md`

## Summary

Retarget the `tools/telemetry-mcp/` MCP bridge from the single `iris.parquet` demo
dataset onto the three frozen transit datasets published by the feature-013 logging
sidecar (`snap`, `lerp`, `cycle`). The `query_telemetry` tool gains a required
`dataset` argument and an optional `date` argument; the load-bearing allow-list
validator is rebuilt around each dataset's snake_case column contract (with new
`timestamp` and `bool` value kinds); the runner assembles the read source from a
fixed, bridge-controlled glob template (`{storage}/{dataset}/dt={date}/*.parquet`)
where dataset and date are validated independently before assembly. Configuration
replaces `TELEMETRY_DATASET_URI` with `TELEMETRY_STORAGE_URI`. No change to the
underlying `telemetry-query-tool` binary or the MCP transport.

## Technical Context

**Language/Version**: Go 1.x (module `telemetry-mcp`, `tools/telemetry-mcp/go.mod`)  
**Primary Dependencies**: `github.com/mark3labs/mcp-go` (stdio MCP server); standard
library only for config/validate/query  
**Storage**: Read-only over Azure Blob via the delegated `telemetry-query-tool`
(DuckDB + Azure extension); this bridge holds no storage credentials itself  
**Testing**: `go test` — table-driven unit tests in `internal/validate` and
`internal/query` (existing pattern), plus a compiled stub query tool under
`testdata/stub-query-tool/`  
**Target Platform**: Local developer machine (Windows-first), invoked by Claude Code
over stdio as an MCP server  
**Project Type**: Single-module CLI / MCP server tool (standalone; not part of the
.NET solution or its deployment)  
**Performance Goals**: Interactive operator latency; default query timeout raised
from 10s to 30s because parquet-over-Azure is slower than the local iris demo  
**Constraints**: No data-source redirection from operator input; `dataset`, `date`,
and `filter` each validated before query assembly; existing forbidden-keyword/char
protections retained unchanged; column contract must match feature-013
`contracts/parquet-schemas.md` exactly (frozen)  
**Scale/Scope**: Three datasets, ~15–18 columns each; single-day partition per query;
filter capped at 256 chars. ~4 source files + 2 test files touched.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The TransitJazz constitution governs the .NET application (frontend/WebAPI/Worker,
Azure deployment, GTFS pipeline). This feature modifies a **standalone local
developer tool** under `tools/`, outside the deployed application. Most principles are
therefore not applicable; the relevant gates:

| Principle | Applicability | Status |
|-----------|---------------|--------|
| I. Decoupled Cloud Architecture | N/A — not a deployable unit | ✅ Pass (no change to the three units) |
| II. No Frontend Secrets | Indirectly relevant — credential handling | ✅ Pass — bridge holds no storage key; the delegated tool reads `AZURE_STORAGE_CONNECTION_STRING` from env (feature-012 FR-020 remediation, unchanged here) |
| III. Two-Pass Pipeline | N/A — read-only telemetry consumer | ✅ Pass |
| IV. OpenTelemetry Observability | Relevant in spirit — this tool *queries* the telemetry the Worker emits | ✅ Pass — strengthens observability by making feature-013 data queryable |
| V. CI/CD Pipeline | N/A — local tool, not a build artifact | ✅ Pass |
| VI. GTFS ID Mapping | Indirect — `route_id` columns carry route short names per feature-013 contract | ✅ Pass — names consumed as-is from the frozen contract |

**Security gate (carried from feature 012)**: The allow-list grammar is the only
control that lets this tool be exposed to an LLM. Replacing the column set must not
weaken it. The plan *tightens* the grammar (removes `.` from identifier chars,
eliminating dotted-path injection surface) and keeps all forbidden-keyword/char and
data-source checks. The data source remains a constant template, never operator
input. **No violations.**

**Result**: PASS — no entries in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/014-transit-datasets/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── query_telemetry.tool.md   # Updated tool contract + accept/reject vectors
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
tools/telemetry-mcp/                      # Standalone Go module (the only code touched)
├── main.go                               # MODIFY: tool def gains dataset+date args; updated description; handler passes 3 args
├── internal/
│   ├── config/
│   │   └── config.go                     # MODIFY: drop DatasetURI/TELEMETRY_DATASET_URI; add StorageURI/TELEMETRY_STORAGE_URI; default timeout 30s
│   ├── validate/
│   │   ├── validate.go                   # MODIFY: per-dataset column maps; timestamp+bool kinds; Filter(dataset, input); ValidateDataset; ValidateDate; remove '.' from isIdentifierChar; remove dot-quoting
│   │   └── validate_test.go              # MODIFY: replace iris matrix with transit accept/reject matrix
│   └── query/
│       ├── runner.go                     # MODIFY: Run(ctx, cfg, dataset, date, filter); build {StorageURI}/{dataset}/dt={date}/*.parquet
│       └── runner_test.go                # MODIFY: 3-arg call; assert glob construction per dataset
├── testdata/stub-query-tool/main.go      # MODIFY: accept transit-style queries (telemetry/snap|lerp|cycle), not iris
├── DESIGN-transit-datasets.md            # (source design doc — unchanged)
└── README.md                             # MODIFY: env var + tool argument docs, migration note
```

**Structure Decision**: Single existing Go module, modified in place. No new packages
or files are required — the change is a wholesale replacement of the dataset/column
contract plus two new validated arguments threaded through the existing
config → validate → query layering. This preserves the feature-012 architecture
(tokenize → parse → re-emit canonical predicate; constant data source) and keeps the
security review surface small and reviewable.

## Complexity Tracking

> No constitution violations. Section intentionally empty.
