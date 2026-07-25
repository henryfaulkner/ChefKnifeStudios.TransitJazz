# Feature Specification: Contextual Telemetry Query MCP Bridge

**Feature Branch**: `main`
**Created**: 2026-06-04
**Status**: Draft
**Input**: User description: "C:\Users\hfaul\Downloads\telemetry-mcp-design-document.md" — Integrate a legacy CLI query tool into Claude Code via an MCP bridge over stdio, exposing a single `query_telemetry` tool that runs conversation-derived filters against a static `iris.parquet` dataset, with safe input validation to prevent SQL/command injection.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask a natural-language question about telemetry data (Priority: P1)

A user working in their AI assistant asks a subjective, plain-language question about the telemetry dataset (for example, "How many records have large petals?"). The assistant resolves the implicit meaning into a concrete data filter, the bridge runs that filter against the static dataset, and the assistant returns the matching result to the user — all without the user writing any query syntax themselves.

**Why this priority**: This is the entire reason the feature exists — turning conversational questions into answers from the dataset. Without it there is no product. It is the minimum viable slice: a single working query path delivers immediate value.

**Independent Test**: Can be fully tested by issuing a natural-language data question in a conversation that has the bridge registered, and confirming a correct count/result is returned from the dataset for the implied condition.

**Acceptance Scenarios**:

1. **Given** the bridge is registered and the dataset is available, **When** the user asks a question implying a simple numeric condition (e.g., petals longer than a threshold), **Then** the bridge returns the count/rows matching that condition.
2. **Given** a question implying a comparison on a categorical or string attribute, **When** the user asks it, **Then** the assistant produces a valid filter and the bridge returns matching results.
3. **Given** a question that is already answered earlier in the conversation context, **When** the user refines it ("now only the small ones too"), **Then** the assistant composes the combined condition and the bridge returns the refined result.

---

### User Story 2 - Malicious or malformed filters are safely rejected (Priority: P1)

A filter is produced (whether from an honest-but-wrong interpretation, a malformed conversation, or a prompt-injection attempt embedded in data) that contains content beyond a simple read-only condition — for example, attempts to read other files/paths, combine datasets, chain additional statements, or invoke shell/command behavior. The bridge rejects the request with a clear error instead of executing it, and never exposes data outside the intended static dataset.

**Why this priority**: The source design interpolates assistant-supplied text directly into a query/command string with no validation, which is an injection vulnerability. Because the filter content originates from LLM-extracted text (which can itself be influenced by untrusted conversation/data content), validation is a core correctness and safety requirement, not an enhancement. It ships alongside P1 query functionality.

**Independent Test**: Can be fully tested by submitting a set of known-bad filters (extra statements, file/URL references, comment/escape sequences, command metacharacters, attempts to reference tables other than the intended dataset) and confirming each is rejected with an error and produces no data access beyond the intended dataset.

**Acceptance Scenarios**:

1. **Given** a filter that contains a statement separator or attempts to chain a second statement, **When** it is submitted, **Then** the bridge rejects it with a validation error and does not execute it.
2. **Given** a filter that references any data source, file path, URL, or table other than the single intended static dataset, **When** it is submitted, **Then** the bridge rejects it.
3. **Given** a filter containing comment markers, escape characters, or shell/command metacharacters, **When** it is submitted, **Then** the bridge rejects it.
4. **Given** an empty or missing filter, **When** the tool is invoked, **Then** the bridge returns a clear "missing required filter" error.
5. **Given** a rejected filter, **When** the error is returned, **Then** the message explains why it was rejected without leaking internal paths or credentials.

---

### User Story 3 - Operator installs and registers the bridge (Priority: P2)

An operator (the tool's owner) installs the bridge on their machine and registers it with their AI assistant's local tool runtime so that the `query_telemetry` capability becomes available in conversations. After registration, the assistant lists/uses the tool without further manual steps.

**Why this priority**: The feature is unusable until it is installed and discoverable, but this is a one-time setup concern that depends on the core query path (US1) existing first. It is essential but secondary to the primary value flow.

**Independent Test**: Can be fully tested by following the documented install/registration steps on a clean machine and confirming the assistant discovers and can invoke the `query_telemetry` tool.

**Acceptance Scenarios**:

1. **Given** the bridge binary and the legacy query tool are present, **When** the operator registers the bridge per the documented configuration, **Then** the assistant lists `query_telemetry` as an available tool.
2. **Given** the legacy query tool is missing or not runnable, **When** the bridge is invoked, **Then** it returns a clear, actionable error rather than crashing the assistant session.

---

### Edge Cases

- **Filter is valid syntax but matches no rows** → the bridge returns an empty/zero result cleanly (not an error).
- **Legacy query tool exits non-zero or emits an error** → the bridge surfaces a sanitized error message and does not hang the conversation.
- **Legacy query tool produces no output or extremely large output** → the bridge returns a bounded, readable result and does not stall indefinitely (a response time limit applies).
- **Filter references a column that does not exist in the dataset** → the bridge rejects or returns a clear "unknown field" error rather than undefined behavior.
- **Unicode, very long, or deeply nested filter input** → input is bounded by a maximum length and rejected if it exceeds limits.
- **Concurrent/rapid invocations** → each invocation is handled independently without cross-contaminating results.
- **Bridge process startup failure (e.g., dataset path unreachable)** → the assistant reports the bridge as unavailable rather than silently returning wrong answers.

## Requirements *(mandatory)*

### Functional Requirements

#### Core query capability

- **FR-001**: The bridge MUST expose a single tool named `query_telemetry` to the AI assistant runtime, discoverable via the assistant's standard tool-listing mechanism.
- **FR-002**: The tool MUST accept one required input, `filter`, representing a read-only condition over the static dataset, expressed without any leading query keyword (the condition only).
- **FR-003**: The bridge MUST apply the provided filter to the single, fixed, statically-configured dataset and return the matching result (e.g., a count and/or matching rows) as text.
- **FR-004**: The bridge MUST return results in a form the assistant can present directly to the user in conversation.
- **FR-005**: The bridge MUST operate entirely as a local process with no network listener; it communicates with the assistant over standard input/output.

#### Input validation & safety (injection prevention)

- **FR-006**: The bridge MUST validate the `filter` input against an explicit allow-list of permitted constructs before it is ever incorporated into the executed query, and MUST reject anything outside that allow-list.
- **FR-007**: The bridge MUST reject any filter that attempts to reference a data source, file path, URL, or table other than the single intended static dataset.
- **FR-008**: The bridge MUST reject any filter containing statement separators, additional statements, comment markers, escape sequences, or command/shell metacharacters.
- **FR-009**: The bridge MUST NOT pass user/assistant-derived text to the underlying tool in any way that allows it to alter the structure of the executed query or the command invoked — only the validated condition may vary.
- **FR-010**: The bridge MUST enforce a maximum length and reject filters that exceed it.
- **FR-011**: The bridge MUST reject empty or missing filters with a clear "missing required filter" error.
- **FR-012**: When rejecting input, the bridge MUST return an error explaining the rejection without disclosing internal file paths, credentials, or other sensitive configuration.
- **FR-013**: The dataset target MUST be fixed by configuration on the bridge side and MUST NOT be selectable or overridable through the `filter` input.

#### Operational behavior

- **FR-014**: The bridge MUST surface failures from the underlying query tool (non-zero exit, missing tool, malformed output) as sanitized, actionable errors rather than crashing or hanging the assistant session.
- **FR-015**: The bridge MUST enforce a bounded response time and bounded output size so a single query cannot stall the conversation indefinitely.
- **FR-016**: The bridge MUST run with no more privilege than the invoking user and MUST NOT require elevated permissions.
- **FR-017**: The bridge MUST be registerable in the operator's local assistant tool runtime via a documented configuration entry referencing the bridge executable.
- **FR-018**: The bridge MUST treat the underlying dataset as read-only and MUST NOT perform or permit any operation that modifies the dataset.

#### Underlying-tool reality & secret remediation

- **FR-019**: The validation in FR-006–FR-013 is a **load-bearing security control**, because the underlying query tool executes the supplied SQL through a general-purpose query engine with a live cloud-storage credential loaded — an unvalidated filter could otherwise reach other storage objects, the local filesystem, write operations, or engine extension features, not merely other rows of the intended dataset. The allow-list MUST be enforced on the understanding that the executable itself imposes no such restriction.
- **FR-020**: The cloud-storage connection string that the underlying tool currently embeds in committed source MUST be removed from source control, supplied to the tool via a local environment variable / local configuration instead, and the exposed credential MUST be rotated/revoked. The MCP bridge MUST NOT embed or log this credential.

### Key Entities *(include if feature involves data)*

- **Telemetry Query Request**: A single invocation carrying the required `filter` condition. Its only meaningful attribute is the validated condition string; it is bounded in length and constrained to the allow-listed grammar.
- **Telemetry Dataset**: The single, fixed, read-only data source the bridge is configured to query (a parquet object in cloud storage). Not selectable by the request.
- **Query Result**: The text outcome returned to the assistant — the underlying tool's rendered output (a human-readable table preceded by a status line) for a successful query, or a sanitized error. The bridge relays this text; it does not require a specific numeric shape.
- **Validation Policy**: The allow-list grammar and limits (permitted operators/fields, maximum length, forbidden constructs) that every filter must satisfy before execution.
- **Underlying Query Tool**: The operator-owned local executable (`telemetry-query-tool.exe`) that accepts a full SQL statement as its single argument, runs it against the dataset via an embedded query engine + cloud-storage credential, and prints a rendered table. The bridge constructs the full statement around the **validated predicate** and a **constant** data-source target; the model never supplies the full statement.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can get a correct answer to a plain-language question about the dataset within a single conversational turn, without writing any query syntax, in at least 90% of well-formed questions over the supported attributes.
- **SC-002**: 100% of a curated set of known-malicious or malformed filters (statement chaining, alternate file/URL/table references, comment/escape/metacharacter injection, oversized input) are rejected without any data access beyond the intended dataset.
- **SC-003**: No filter input can cause the bridge to read, return, or modify any data outside the single configured dataset (verified by attempting each known bypass technique).
- **SC-004**: A typical valid query returns a result within 5 seconds under normal local conditions, and no single query can stall the conversation beyond the configured response-time bound.
- **SC-005**: An operator can install and register the bridge and see the tool become available in a conversation by following the documented steps, with no code changes, in under 10 minutes.
- **SC-006**: Underlying-tool failures (missing executable, non-zero exit) are reported as clear errors in 100% of cases, with zero assistant-session crashes or indefinite hangs.
- **SC-007**: The cloud-storage credential no longer appears anywhere in source control, is supplied at runtime via local configuration, and the previously-exposed key has been rotated/revoked (verified by inspecting the tool source and the Azure portal).

## Assumptions

- **Authorized, self-owned tooling**: This is internal developer tooling the operator owns and runs locally against their own data; it is not a multi-tenant or public-facing service.
- **Safe-by-design validation is in scope**: Per the requester's direction, the unsafe direct-interpolation pattern in the source design document is explicitly replaced by allow-list validation; relying solely on the downstream tool being "read-only" is NOT considered sufficient mitigation.
- **Single dataset, single tool**: The initial scope is one fixed dataset and one `query_telemetry` tool. Multiple datasets, dataset selection, write operations, and additional tools are out of scope for this version.
- **Local stdio transport**: The bridge runs as a local child process communicating over standard I/O; no network hosting, ports, or remote access are introduced.
- **The query tool exists and is operator-owned**: The underlying executable (`telemetry-query-tool/` — a local DuckDB-based Go CLI the operator owns) is available on the operator's machine; this feature wraps it rather than reimplementing it. **It is NOT inherently read-only or restricted**: it executes whatever SQL it is given against an embedded engine with a live cloud-storage credential. Read-only/safety is therefore enforced by the *bridge's* validation (FR-019), not assumed of the tool.
- **Wrapper architecture (chosen)**: The MCP server invokes the existing `.exe` as a subprocess rather than re-implementing the DuckDB query logic in-process. (The native/in-process alternative was considered and deferred.)
- **Full-SQL argument**: The underlying tool takes a complete SQL statement as its single command-line argument; the bridge is responsible for assembling that statement safely from the validated predicate + constant data source, never passing model-supplied text as the statement.
- **Assistant performs interpretation**: Converting the user's natural-language question into a candidate filter is done by the AI assistant; the bridge's responsibility begins at receiving that filter and is to validate, execute, and return — never to trust the filter blindly.
- **Result format**: Plain text results are acceptable for the assistant to relay; rich/structured formatting is not required for this version.
- **Supported attributes**: The supported filterable fields are those present in the configured dataset; questions about unsupported fields are expected to fail gracefully with a clear error.
