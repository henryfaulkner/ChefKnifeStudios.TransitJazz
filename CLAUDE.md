<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the most recent
feature plan at specs/014-transit-datasets/plan.md

014-transit-datasets retargets the tools/telemetry-mcp/ MCP bridge (Go,
mcp-go over stdio) off the iris demo dataset and onto the three frozen
parquet datasets from feature 013 (snap/lerp/cycle). The query_telemetry
tool gains a required `dataset` arg (validated against {snap,lerp,cycle}
BEFORE the filter) and an optional `date` arg (strict ^\d{4}-\d{2}-\d{2}$,
default today UTC). The load-bearing allow-list validator is rebuilt around
each dataset's snake_case column contract with two NEW value kinds —
timestamp (compared to date strings only, e.g. observation_utc >
'2026-06-04') and bool (unquoted true/false only). `.` is removed from
identifier chars (tightening; kills dotted-path injection + dot-quoting).
Config swaps TELEMETRY_DATASET_URI → TELEMETRY_STORAGE_URI (e.g.
azure://telemetry); the runner assembles a CONSTANT source template
{StorageURI}/{dataset}/dt={date}/*.parquet — dataset/date/filter are each
validated before assembly so operator input can never redirect the source.
Default timeout raised 10s→30s. Forbidden keyword/char/URL checks and the
delegated telemetry-query-tool (AZURE_STORAGE_CONNECTION_STRING) are
UNCHANGED. All changes are in tools/telemetry-mcp/ only. See
specs/014-transit-datasets/ for plan, research, data-model, contract
(query_telemetry.tool.md accept/reject vectors), and quickstart.

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
