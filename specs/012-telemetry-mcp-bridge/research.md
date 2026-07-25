# Phase 0 Research: Contextual Telemetry Query MCP Bridge

## R1. MCP server SDK & correct API surface

**Decision**: Use `github.com/mark3labs/mcp-go` with the current API: `server.NewMCPServer(name, version, ...)`, `mcp.NewTool(name, mcp.WithDescription(...), mcp.WithString("filter", mcp.Required(), mcp.Description(...)))`, `s.AddTool(tool, handler)`, and `server.ServeStdio(s)`. In the handler, read the argument with `request.RequireString("filter")` and return results with `mcp.NewToolResultText(...)` / `mcp.NewToolResultError(...)`.

**Rationale**: This is the maintained, idiomatic stdio MCP server library for Go and matches how Claude Code launches MCP servers (command + args, communicating over stdio). It handles the JSON-RPC framing, tool listing, and graceful shutdown (SIGINT/SIGTERM) for us.

**Correction to source design doc**: The design document's `main.go` is **not buildable as written** and must not be copied verbatim:
- Import paths are placeholders (`"://github.com"`) — the real paths are `github.com/mark3labs/mcp-go/mcp` and `github.com/mark3labs/mcp-go/server`.
- It calls `server.WithLogging()`, `mcp.WithStringProperty`, `mcp.WithRequiredProperties`, and `s.RegisterTool` — **none of these are the current API**. The correct calls are `mcp.WithString(... mcp.Required())` and `s.AddTool(...)`.
- Reading args via `req.Params.Arguments["filter"].(string)` is replaced by `request.RequireString("filter")`.

**Alternatives considered**: The official `modelcontextprotocol/go-sdk` — viable, but `mcp-go` is the more widely-used community SDK with the simplest stdio story for a single-tool bridge. Either works; `mcp-go` chosen for minimal ceremony.

**Sources**:
- [mark3labs/mcp-go README](https://github.com/mark3labs/mcp-go/blob/main/README.md)
- [MCP-Go Getting Started](https://mcp-go.dev/getting-started/)
- [server package – pkg.go.dev](https://pkg.go.dev/github.com/mark3labs/mcp-go/server)

---

## R2. Closing the injection vector (the core problem)

**Decision**: Treat `filter` as **untrusted input** and validate it against a strict **allow-list grammar** *before* it is ever placed into the executed query. Never use `fmt.Sprintf(... WHERE %s ...)` on raw input. Instead:

1. **Tokenize** the filter into a small set of allowed token types only:
   - Identifiers, restricted to a **fixed allow-list of known dataset columns** (`sepal_length`, `sepal_width`, `petal_length`, `petal_width`, `species`).
   - Numeric literals (integer/float, with optional leading `-`).
   - Single-quoted string literals containing only a safe character class (letters, digits, spaces, `-`, `_`), with length bounds — used for `species`.
   - Comparison operators: `<`, `<=`, `>`, `>=`, `=`, `!=`.
   - Boolean connectors: `AND`, `OR` (case-insensitive), and parentheses `(` `)`.
2. **Reject** anything else — including `;`, `--`, `/* */`, `#`, backslashes, `'` outside a well-formed quoted literal, backticks, `$`, `|`, `&`, `>`/`<` used as redirection, the keywords `WHERE/SELECT/FROM/UNION/INSERT/UPDATE/DELETE/DROP/ATTACH/COPY/PRAGMA/azure://`/`http`/`file`/any path-or-URL-looking token, and any identifier not on the column allow-list.
3. **Parse** the validated token stream against a tiny recursive-descent grammar (predicate → comparison (`AND`/`OR` predicate)\* with parens) to guarantee structural validity, then **re-emit a canonical predicate string from our own tokens** (not from the original raw bytes). Only this re-emitted, fully-validated predicate is interpolated into the final query.

This satisfies FR-006 (allow-list before execution), FR-007 (no alternate data source — the `FROM 'azure://parquet/iris.parquet'` target is a **constant in the bridge**, never derived from input, FR-013), FR-008 (separators/comments/escapes/metacharacters rejected), and FR-009 (input cannot change query/command structure — only the predicate varies, and only within the grammar).

**Rationale**: The dataset has a small, fixed schema, so a hand-written allow-list grammar is both sufficient and auditable — far safer than trying to "sanitize" arbitrary SQL. Re-emitting from our own validated tokens (rather than passing the user's bytes through) eliminates clever-encoding bypasses: if it didn't tokenize into allowed tokens, it never reaches output.

**Alternatives considered**:
- *Full SQL parser (e.g., vitess/sqlparser)*: overkill, larger attack surface, and still requires an allow-list pass afterward. Rejected for v1.
- *Blacklist/regex-strip of dangerous chars*: classic anti-pattern; bypassable via encoding and easy to get wrong. Rejected — FR-006 explicitly mandates allow-list, not deny-list.
- *Parameterized query / prepared statement*: ideal in principle, but the legacy `telemetry-query-tool.exe` takes a single query string argument and exposes no parameter binding. Allow-list + canonical re-emission is the closest safe equivalent given that constraint.

---

## R3. Safe process invocation & bounding

**Decision**:
- Invoke the executable with `exec.CommandContext(ctx, toolPath, fullQuery)` where `fullQuery` is the **complete SQL statement** the underlying tool expects as `argv[1]` (it reads `os.Args[1]` and runs it directly via DuckDB `db.Query`). The bridge assembles `fullQuery` itself from the **constant** data-source target + the **validated, re-emitted predicate** (R2); model text is never the statement. Passed as a **single discrete argv element** (Go's `os/exec` does not invoke a shell, so no shell metacharacter interpretation occurs — defense in depth on top of R2).
- The tool prints a leading status line (`Fetching and analyzing telemetry data...`) followed by an ASCII table (`tablewriter`). The bridge relays this stdout text as-is to the model (optionally trimming the status line); it does **not** assume a bare numeric count. This is why the spec's Query Result entity describes "rendered table text," not a fixed shape.
- Resolve `toolPath` and the dataset target from **config/env at startup** (`config.go`), validated once; they are constants for the process lifetime and never influenced by the request (FR-013).
- Wrap each call in a **timeout context** (default ~10 s, bounding SC-004's 5 s target with headroom) so a hung tool cannot stall the conversation (FR-015, edge case).
- **Cap output size** read from the tool (e.g., truncate at a configurable byte limit with a "[truncated]" marker) to satisfy FR-015's bounded-output requirement.
- On non-zero exit or missing executable, return `mcp.NewToolResultError` with a **sanitized** message (no absolute paths/credentials) (FR-012, FR-014).

**Rationale**: `os/exec` argv-passing avoids the shell entirely; the context timeout + output cap turn a flaky/hostile downstream tool into a clean, bounded error rather than a hang. Sanitization keeps internal layout out of model-visible text.

**Alternatives considered**: `cmd.CombinedOutput()` (as in the source doc) mixes stderr into results and offers no timeout — replaced with context-bound execution and explicit stdout/stderr handling.

---

## R4. Logging without corrupting the protocol

**Decision**: Reserve **stdout exclusively for MCP JSON-RPC traffic**; send all diagnostic/structured logs to **stderr** via Go's `log/slog`. Do not enable any SDK option that writes human logs to stdout.

**Rationale**: stdio MCP servers multiplex protocol messages on stdout; any stray stdout write corrupts the stream and breaks the tool in Claude Code. This honors Constitution Principle IV (structured logging) in spirit without an Azure backend, which is out of scope for a local dev tool.

---

## R5. Configuration & registration with Claude Code

**Decision**: Register the built binary in Claude Code's MCP config (e.g., `~/.claude.json` / project `.mcp.json` `mcpServers` entry) with an absolute `command` path and empty `args`. Dataset target + tool path are supplied to the bridge via environment variables in that same config entry (e.g., `TELEMETRY_TOOL_PATH`, `TELEMETRY_DATASET_URI`), so nothing sensitive is hard-coded and the dataset stays fixed per deployment (FR-013, FR-017).

**Rationale**: Matches how Claude Code launches and configures stdio MCP servers; keeps the dataset/tool location operator-controlled and out of source. The exact config file/key is confirmed at quickstart time against the running Claude Code version.

**Open item (low risk)**: Exact config filename/scope (global vs project) is environment-specific and documented in `quickstart.md`; it does not affect the bridge's code.

---

## R6. The underlying tool — read from source, not assumed

**Decision**: Treat `telemetry-query-tool/` as a **known, operator-owned DuckDB CLI**, and size the threat model to what DuckDB+Azure can actually do — confirming the allow-list (R2) is the right control, and that the **wrapper** architecture (chosen) does not weaken it.

**What the source actually does** (`telemetry-query-tool/main.go`, reviewed):
- Opens in-memory DuckDB (`sql.Open("duckdb", "")`).
- Runs bootstrap: `INSTALL azure; LOAD azure; SET azure_storage_connection_string = '<key>';`.
- Executes **`os.Args[1]` verbatim** via `db.Query(query)` — arbitrary SQL, no restriction.
- Renders results as an ASCII table to stdout.

**Threat-model implications**:
- The engine can read **any** Azure object reachable by the credential, read **local files** (`read_csv`/`read_parquet`/`read_text` of arbitrary paths), **write** (`COPY ... TO`), and `INSTALL`/`LOAD` further extensions. So an unvalidated filter is *remote-code-shaped*, not merely "extra rows." This is exactly why FR-019 marks validation as load-bearing and why the data source must be a **constant**, never derived from input.
- Because the bridge passes a single argv element to a non-shell `exec`, and because the SQL is re-assembled from validated tokens, there is no shell-injection and no SQL-structure injection path even though the downstream engine is fully capable.

**Two defects found in the existing tool** (folded into this feature):
1. **Hardcoded live Azure AccountKey** in committed source (line ~24), with a dead `if azureConnString == ""` guard → **FR-020**: move to env var (`AZURE_STORAGE_CONNECTION_STRING`, which the code's own error text already references) and rotate the key.
2. **Does not currently build as committed** — `build_err.txt`: `go-duckdb@v1.8.5: undefined: Conn`. The shipped `.exe` predates/post-dates this, so a build-fix (pin a working `go-duckdb`/DuckDB version) may be needed before the env-var change can be rebuilt and re-tested. Flagged for the tasks phase.

**Decision on architecture**: Per requester direction, **wrap the `.exe`** rather than re-implement DuckDB logic in-process. The wrapper keeps the existing tool authoritative and avoids cgo/DuckDB build complexity in the bridge; the trade-off (a second process + the doc's full-SQL-string surface) is fully mitigated by R2/R3.

**Sources**: `telemetry-query-tool/main.go`, `telemetry-query-tool/go.mod`, `telemetry-query-tool/build_err.txt` (in-repo).
