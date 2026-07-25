# Phase 1 Data Model: Contextual Telemetry Query MCP Bridge

The bridge is stateless between calls. The "data model" is the in-flight request/response shapes and the validation policy — not persisted storage.

## Entities

### TelemetryQueryRequest
The single tool invocation crossing the trust boundary.

| Field | Type | Constraints | Source |
|-------|------|-------------|--------|
| `filter` | string | **Required.** Non-empty after trim. Max length **256** chars (FR-010). Must satisfy the Validation Policy grammar (FR-006). Must NOT contain a leading `WHERE`/`SELECT`/etc. (condition only, FR-002). | Claude (untrusted) |

Validation order: presence (FR-011) → length bound (FR-010) → tokenize+grammar (FR-006/008/009) → column allow-list (FR-007). First failure short-circuits with a sanitized error.

### TelemetryDataset (fixed, not a request field)
The single read-only data source. **Configured at bridge startup; never selectable via input** (FR-013, FR-007).

| Field | Type | Notes |
|-------|------|-------|
| `datasetUri` | string (const per process) | e.g. `azure://parquet/iris.parquet`. Comes from `TELEMETRY_DATASET_URI` config. Interpolated into the query as a **constant**, never from `filter`. |
| `columns` | fixed set | `sepal_length`, `sepal_width`, `petal_length`, `petal_width` (numeric); `species` (string). The identifier allow-list. |
| readOnly | invariant | The bridge issues only read queries; no write/DDL tokens are permitted (FR-018). |

### QueryResult
What the handler returns to Claude.

| Variant | Shape | When |
|---------|-------|------|
| Success | `NewToolResultText(stdout)` — the tool's rendered ASCII table (optionally minus its `Fetching…` status line), bounded/truncated | tool exit 0 |
| Validation error | `NewToolResultError("<reason>")` — sanitized | filter fails any validation step |
| Execution error | `NewToolResultError("<sanitized reason>")` | non-zero exit, missing tool, timeout, oversized output |

Error text MUST NOT include absolute paths, the dataset URI, or any credential (FR-012).

### ValidationPolicy
The allow-list grammar all filters must satisfy (FR-006).

**Allowed tokens**
- Column identifiers: only the fixed `columns` set above.
- Numeric literals: `-?\d+(\.\d+)?`.
- String literals: `'[A-Za-z0-9 _-]{0,64}'` (single-quoted, bounded charset) — for `species`.
- Comparison operators: `<  <=  >  >=  =  !=`.
- Logical connectors: `AND`, `OR` (case-insensitive).
- Grouping: `(` `)`.
- Whitespace (separators only).

**Explicitly rejected** (non-exhaustive; anything not in the allow-list is rejected by construction)
- Statement separators / chaining: `;`
- Comments: `--`, `/* */`, `#`
- Escapes/quote-breaking: stray `'`, backslash `\`
- Shell/command metacharacters: `| & $ \` > < ` redirection, `$(...)`
- Reserved/structural keywords: `WHERE SELECT FROM UNION JOIN INSERT UPDATE DELETE DROP ATTACH COPY PRAGMA`
- Data-source/path/URL tokens: anything resembling `azure://`, `http://`, `https://`, `file:`, drive paths, `..`
- Any identifier not in the column allow-list (e.g. `password`, other table/file names)

**Grammar** (recursive descent over validated tokens):
```
predicate   := orExpr
orExpr      := andExpr ( OR andExpr )*
andExpr     := comparison ( AND comparison )*
comparison  := "(" predicate ")"
             | column op literal
column      := one of the allow-listed identifiers
op          := < | <= | > | >= | = | !=
literal     := number | quotedString   (type must match the column: numeric cols→number, species→quotedString)
```

**Output of validation**: a **canonical predicate string re-emitted from the parsed tokens** (not the raw input). Only this canonical string is interpolated into the final **full SQL statement** the underlying tool expects as `argv[1]`, e.g.:
`select count(*) as count from '<datasetUri-const>' where <canonical-predicate>`
The data-source segment (`from '<datasetUri-const>'`) is a **constant** assembled by the bridge; only `<canonical-predicate>` varies. (Note: the source doc's `select * as count` is invalid SQL; corrected here to a real count projection. The underlying tool runs DuckDB, so standard DuckDB SQL applies; exact projection finalized against DuckDB during implementation.)

## State & lifecycle
- **No persistence.** Each `query_telemetry` call is independent (edge case: concurrent calls don't share mutable state — the bridge holds only immutable config).
- Config (`datasetUri`, `toolPath`, limits) is resolved **once at startup** and is immutable for the process lifetime.
