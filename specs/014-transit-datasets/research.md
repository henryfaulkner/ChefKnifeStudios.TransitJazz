# Phase 0 Research: Transit Telemetry Datasets for the Query Bridge

All decisions below resolve the Technical Context. There were no open
`NEEDS CLARIFICATION` markers in the spec; this file records the design decisions
that the source design doc (`tools/telemetry-mcp/DESIGN-transit-datasets.md`) and the
existing feature-012 code together imply, plus two subtle issues discovered while
reading the current validator.

---

## R1. Dataset selection model

**Decision**: Add a required `dataset` tool argument validated against the literal set
`{"snap","lerp","cycle"}` before any filter parsing. Maintain three separate column
allow-list maps (`snapColumns`, `lerpColumns`, `cycleColumns`) and select one based on
the validated dataset.

**Rationale**: The three datasets have different schemas, so a single global column
map would let a caller reference a `cycle` column while querying `snap` (and vice
versa) — a correctness hole and a contract-drift risk. Validating the dataset name
first means an unknown dataset is rejected before its value can ever reach SQL.

**Alternatives considered**:
- *Infer dataset from columns referenced* — rejected: ambiguous for shared columns
  (`cycle_id`, `vehicle_id`), and infers intent the caller should state explicitly.
- *One union view across datasets* — rejected: schemas differ; would require the
  underlying tool to do schema reconciliation and breaks the one-file-per-dataset
  contract.

## R2. Configuration: storage base URI vs per-dataset URIs

**Decision**: Replace `DatasetURI` / `TELEMETRY_DATASET_URI` with `StorageURI` /
`TELEMETRY_STORAGE_URI` (e.g. `azure://telemetry`). The runner derives the per-query
source as `{StorageURI}/{dataset}/dt={date}/*.parquet`.

**Rationale**: Mirrors the feature-013 sidecar's `LoggingOptions` (one container base +
dataset/date path segments). One env var stays in sync with the blob layout; three
separate dataset URIs would triplicate config and could drift from the layout
contract.

**Alternatives considered**:
- *Three env vars (`TELEMETRY_SNAP_URI`, …)* — rejected: more config surface, easy to
  set inconsistently, no benefit since the path pattern is fixed by feature 013.

**Migration**: A set `TELEMETRY_DATASET_URI` is now ignored; if `TELEMETRY_STORAGE_URI`
is absent, `config.Load()` returns a clear startup error naming the new variable
(satisfies FR-015 / SC-005).

## R3. Date scoping and validation

**Decision**: Add an optional `date` tool argument. Validate against the strict regex
`^\d{4}-\d{2}-\d{2}$` (`ValidateDate`). If omitted, default to `time.Now().UTC()`
formatted as `2006-01-02`. The validated date is interpolated only into the fixed glob
template, never taken from the filter string.

**Rationale**: Datasets are hive-partitioned by `dt=YYYY-MM-DD`. Scoping to one
partition is faster and quieter than scanning all days. The strict regex guarantees
the value cannot carry path separators, globs, or SQL — it is structurally incapable
of redirecting the data source (FR-009, FR-011, FR-012, SC-006).

**Alternatives considered**:
- *Accept a free date expression / range* — rejected: multi-day ranges and
  `hive_partitioning=true` are explicit non-goals (spec Assumptions; design Open
  Questions 2 & 3). Keep to a single validated partition.
- *Validate date by `time.Parse`* — viable, but the regex is stricter (rejects
  `2026-6-4`) and matches the design's stated `^\d{4}-\d{2}-\d{2}$`. Use the regex;
  optionally also `time.Parse` to reject impossible dates like `2026-13-40`.
  **Chosen**: regex + `time.Parse` sanity check for real calendar dates.

## R4. New value kinds: timestamp and bool

**Decision**: Add two value-kind categories to the validator's type-check pass:
- `timestampColumns` — compared against a **string literal only** (e.g.
  `observation_utc > '2026-06-04'`); the underlying engine casts the string. A numeric
  literal against a timestamp column is rejected.
- `boolColumns` — compared against the **unquoted literals `true` or `false` only**.
  A numeric (`is_stale = 1`) or string (`is_stale = 'true'`) literal is rejected.

**Rationale**: The frozen contract introduces `TIMESTAMP` and `BOOLEAN` columns that
the iris grammar never had. Without dedicated kinds, a timestamp column would fall
under "string" and a bool under neither, producing confusing errors or accepting
nonsense. Explicit kinds give precise rejection messages (FR-005, FR-006).

**Implementation notes**:
- `true`/`false` are currently tokenized as identifiers and would fail the
  `allowedColumns` lookup. Add a `bool` token type: the tokenizer recognizes the
  bare words `true`/`false` (case-insensitive) and emits a `bool` token. The parser
  accepts a `bool` literal only when the column is in `boolColumns`, and rejects a
  `bool` literal for any other column kind.
- Timestamp comparison reuses the existing `string` token. The parser requires a
  `string` token (not `number`) for `timestampColumns`.

**Alternatives considered**:
- *Treat bool as a string `'true'`* — rejected: the design explicitly wants unquoted
  `true`/`false` and rejects `is_stale = 1`; quoting would diverge from the contract
  and DuckDB-idiomatic boolean predicates.

## R5. String-literal character set vs full ISO timestamps (discovered issue)

**Finding**: The existing `isStringCharValid` allows only `[A-Za-z0-9 _-]`. A
**date-only** literal `'2026-06-04'` passes (dash allowed). A **full ISO timestamp**
`'2026-06-04T12:00:00'` does **not** — `:` is rejected.

**Decision**: Keep the string-literal char set **unchanged**. Timestamp comparisons
are date-granularity only (`observation_utc > '2026-06-04'`), matching every example
in the design doc and contract. Full-timestamp-literal comparisons are an explicit
**non-goal** for this feature.

**Rationale**: Widening the string char set to admit `:` and `T` enlarges the
injection surface of the load-bearing validator for a capability nobody asked for.
Date-granularity timestamp filtering covers the stated operator scenarios. If
sub-day precision is needed later, it is a separate, security-reviewed change.

**Action**: Document this boundary in the tool description and contract so callers
phrase timestamp filters as dates, and add a reject-vector test for a `:`-bearing
string literal.

## R6. Identifier grammar tightening (remove `.`)

**Decision**: Remove `.` from `isIdentifierChar` and delete the dot-quoting branch in
`parseComparison` (the `"petal.length"` → `"petal.length"` path). Transit columns are
`snake_case` `[a-z_][a-z0-9_]*` with no dots.

**Rationale**: A pure tightening. It eliminates dotted-path identifiers entirely, so
`snap.outcome` or `petal.length` are rejected as unknown columns rather than parsed as
struct-field access. Canonical output becomes a bare identifier (no quoting needed),
simplifying the canonical-form tests (FR-007, FR-008).

**Consequence for tests**: The existing `TestCanonicalForm` expectation
`"petal.length" > 5.0` is removed; canonical form for transit columns is the bare
name, e.g. `snap_distance_km > 0.5`.

## R7. Validation ordering (security invariant)

**Decision**: The handler validates in this fixed order, short-circuiting on first
failure, before any query assembly:
1. `ValidateDataset(dataset)` → must be in `{snap,lerp,cycle}`.
2. `ValidateDate(date)` → strict `YYYY-MM-DD` (or default to today UTC).
3. `Filter(dataset, filter)` → allow-list grammar over the selected column map.

Only after all three pass does `query.Run` build the constant-template glob and
delegate. None of the three inputs is concatenated into SQL before its own validation.

**Rationale**: Encodes FR-011/FR-012/SC-006 as an explicit, testable sequence. Each
input has an independent gate; the data source is assembled from validated parts of a
fixed template, so no input can redirect it.

## R8. Forbidden keyword/char and data-source checks — unchanged

**Decision**: Leave `forbiddenKeywords`, `forbiddenChars`, the comment-marker checks,
and the URL/path checks (`azure://`, `http://`, `file:`, `..`) exactly as they are.

**Rationale**: They are dataset-agnostic and remain correct for the transit columns.
The design's "What does NOT change" section is explicit on this. Retaining them
verbatim keeps the security review focused on the column-map swap and the two new
arguments (FR-013).

## R9. Stub query tool & runner test

**Decision**: Update `testdata/stub-query-tool/main.go` to accept the new query shape
— it currently fails unless the SQL contains `iris`/`petal`/`sepal`. Change the guard
to accept queries referencing `telemetry/` and any of `snap`/`lerp`/`cycle`, and emit a
transit-shaped mock table. Update `runner_test.go` to call
`Run(ctx, cfg, dataset, date, filter)` and assert the constructed glob for each
dataset.

**Rationale**: Keeps the offline integration test meaningful without a live Azure
credential, and adds direct coverage that the source glob is built correctly per
dataset/date (the core of FR-012).

---

## Summary of decisions

| # | Decision | Drives |
|---|----------|--------|
| R1 | Required `dataset` arg + 3 column maps, validated first | FR-001..004 |
| R2 | `TELEMETRY_STORAGE_URI` replaces `TELEMETRY_DATASET_URI`; clear migration error | FR-015, SC-005 |
| R3 | Optional `date` arg, strict `YYYY-MM-DD`, default today UTC | FR-009, FR-010 |
| R4 | New `timestamp` (string-literal) and `bool` (`true`/`false`) kinds | FR-005, FR-006 |
| R5 | Keep string char set; date-granularity timestamps only (non-goal: full ISO) | FR-006 boundary |
| R6 | Remove `.` from identifiers; drop dot-quoting | FR-007, FR-008 |
| R7 | Fixed validation order before assembly | FR-011, FR-012, SC-006 |
| R8 | Forbidden keyword/char/URL checks unchanged | FR-013 |
| R9 | Update stub tool + runner test to transit shape & glob assertion | testability |
