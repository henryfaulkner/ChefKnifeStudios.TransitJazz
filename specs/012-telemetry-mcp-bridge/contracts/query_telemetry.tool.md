# Tool Contract: `query_telemetry`

The single MCP tool exposed by the `telemetry-mcp` bridge over stdio. This is the contract Claude Code sees via tool listing and the boundary stakeholders validate.

## Tool definition

- **Name**: `query_telemetry`
- **Description** (sent to the model): "Queries the static iris.parquet telemetry dataset using a read-only filter condition extracted from the conversation. Provide ONLY the condition (e.g., `petal_length > 5.0` or `species = 'setosa'`). Do NOT include the word WHERE, any other SQL keywords, file/URL/table names, semicolons, or comments. Allowed fields: sepal_length, sepal_width, petal_length, petal_width, species."

## Input schema

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string",
      "description": "Read-only condition over the dataset, condition only (no WHERE). Allowed fields: sepal_length, sepal_width, petal_length, petal_width, species. Operators: < <= > >= = != AND OR and parentheses. Max 256 chars."
    }
  },
  "required": ["filter"]
}
```

## Behavior

1. Receive `filter` (untrusted).
2. Validate against the allow-list grammar (see `data-model.md` → ValidationPolicy). On failure → **error result** (no execution).
3. On success, build the **full SQL statement** `select count(*) as count from '<fixed datasetUri>' where <canonical-predicate>` using the **re-emitted canonical predicate** and the **constant** dataset URI. (DuckDB SQL — the underlying tool runs DuckDB.)
4. Run the tool with that full statement as a single argv element, under a timeout, with bounded output.
5. Return the tool's stdout (a rendered ASCII table, optionally minus its `Fetching…` status line) as a **text result**, or a **sanitized error result** on failure.

## Result shapes

| Outcome | MCP result | Example content |
|---------|-----------|-----------------|
| Success | text | rendered table, e.g. `┌───────┐│ count ││ 34    │└───────┘` |
| Empty match | text (not an error) | table with `count` = `0` |
| Missing/empty filter | error | `Missing required string parameter: filter` |
| Invalid filter (grammar/column/length/forbidden token) | error | `Rejected filter: only conditions over [sepal_length, sepal_width, petal_length, petal_width, species] using < <= > >= = != AND OR are allowed.` |
| Tool failure (non-zero/missing/timeout/oversized) | error | `Telemetry tool error: query failed.` (sanitized — no paths/credentials) |

## Invariants (testable)

- **C-1**: No input value can change the data source, table, or file queried — `from '<datasetUri>'` is constant (FR-007/FR-013).
- **C-2**: No input value can introduce a second statement, comment, escape, or shell metacharacter into what executes (FR-008/FR-009).
- **C-3**: A missing/empty `filter` always yields the missing-parameter error, never an execution (FR-011).
- **C-4**: Every error result is free of absolute paths, the dataset URI, and credentials (FR-012).
- **C-5**: No call executes longer than the configured timeout or returns more than the configured max output (FR-015).
- **C-6**: The bridge performs only read queries; no write/DDL token is ever permitted or emitted (FR-018).

## Conformance test vectors

**Accept** (validate → execute):
- `petal_length > 5.0`
- `sepal_width < 3.0 AND petal_length >= 1.5`
- `species = 'setosa'`
- `(petal_width > 1.0 OR sepal_length <= 4.5) AND species != 'virginica'`

**Reject** (validation error, never executed):
- `1=1; DROP TABLE x` — statement chaining
- `petal_length > 5 -- comment` — comment marker
- `species = 'a' UNION SELECT * FROM users` — keyword/second source
- `filename = 'azure://other/secret.parquet'` — alternate data source + non-allowed column
- `petal_length > 5 OR 1=1` — `1` is not an allow-listed column on the left of a comparison (literal-vs-column position enforced by grammar)
- `password = 'x'` — column not in allow-list
- `petal_length > $(whoami)` — shell metacharacters
- `'' ; cat /etc/passwd` — separator + path
- a 300-character filter — exceeds max length
- `` (empty) — missing required parameter
