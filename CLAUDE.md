<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the most recent
feature plan at specs/013-logging-sidecar-service/plan.md

013-logging-sidecar-service adds an in-process logging sidecar to the
TransitDataWorker (NOT a new deployable): data-processing code posts marker
event-args (Snap/Lerp/Cycle) onto an in-process IEventNotificationService
(server copy of Client.Core's notification pattern); a hosted LogEventWorker
drains a bounded Channel (DropWrite load-shedding, never blocks the hot path)
and a StructuredLoggingService builds parquet IN-PROCESS with Parquet.Net and
uploads one immutable part-file per dataset to Azure Blob via
DefaultAzureCredential (managed identity — NO committed account key, per
the feature-012 FR-020 security gate). Layout: telemetry/{snap|lerp|cycle}/
dt=YYYY-MM-DD/part-<utcts>.parquet, flushed every 5 min + best-effort on
shutdown, read downstream by the telemetry-query-tool (DuckDB). Column names
are a frozen snake_case contract the feature-012 allow-list consumes. All new
files under TransitDataWorker/Logging/. See specs/013-logging-sidecar-service/
for plan, research, data-model, contracts (parquet schemas + blob layout),
and quickstart.

012-telemetry-mcp-bridge is a standalone, local developer tool (NOT part
of the TransitJazz .NET app or its WASM/Docker deployment): a small Go
MCP server (github.com/mark3labs/mcp-go) that runs over stdio and exposes
a single query_telemetry tool to Claude Code. Architecture: WRAP the
existing telemetry-query-tool/ in this repo (an operator-owned Go CLI that
runs arbitrary DuckDB SQL with the Azure extension + a live storage
credential against iris.parquet in Azure Blob) via exec, passing one
fully-assembled SQL statement as argv[1]. Because that underlying tool is
NOT read-only (it'll run any DuckDB SQL — read other blobs, local files,
COPY..TO, INSTALL extensions), the feature's core is SECURITY: the source
design doc interpolated LLM text straight into the SQL string (injection
hole); this plan replaces that with a load-bearing allow-list grammar
(tokenize → parse → re-emit a canonical predicate over a fixed column
set) + a constant data-source, so input can never change the data source,
chain statements, inject comments/escapes, or reach the shell. ALSO in
scope (FR-020): telemetry-query-tool/main.go hardcodes a live Azure
AccountKey in committed source — move to env var + rotate. The new bridge
lives at tools/telemetry-mcp/ (own Go module). See
specs/012-telemetry-mcp-bridge/ for plan, research (correct mcp-go API —
the doc's main.go is NOT buildable; DuckDB threat model; the tool also
currently fails to build per build_err.txt), data-model (ValidationPolicy
grammar), contracts/query_telemetry.tool.md (accept/reject vectors), and a
9-test quickstart incl. secret remediation.
<!-- SPECKIT END -->
