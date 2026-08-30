# Azure Centralized Structured Logging Design

**Status:** Proposed  
**Date:** 2026-08-30  
**Scope:** TransitDataWorker application and platform logs, their Azure persistence, and a read-only AI investigation skill  
**Supersedes after cutover:** the Parquet telemetry sidecar, Blob-backed telemetry query API, and Parquet-specific AI/query tooling  
**Retirement completed:** legacy Parquet retirement was carried out in feature 055 (specs/055-remove-parquet-sidecar).

## Decision

TransitJazz will replace the worker's custom Parquet telemetry path with sparse, structured JSON logs written through `ILogger` to standard output. Azure Container Apps will collect those streams and route them through Azure Monitor diagnostic settings to the project's existing Log Analytics workspace.

The target storage policy is:

| Stream | Azure table | Table plan | Retention |
|---|---|---|---|
| Worker stdout/stderr | `ContainerAppConsoleLogs` | Basic | 30 days |
| Container Apps platform events | `ContainerAppSystemLogs` | Analytics | 30 days |

Thirty days is intentionally longer than the required week. Basic tables have a fixed 30-day interactive query window, while lowering an Analytics table below the included retention period does not reduce ingestion cost. `ContainerAppSystemLogs` remains Analytics because that table does not support the Basic plan. These capabilities are documented in the [Container Apps console-log schema](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/containerappconsolelogs), [Basic and Auxiliary query documentation](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/basic-logs-query), and [Azure Monitor retention documentation](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/data-retention-configure).

Grafana remains the source for continuous numeric signals, trends, and alerts. Centralized logs become the source for discrete events and explanations. The worker will not emit a paid log row for every metric sample or successful polling cycle.

A new repository skill, proposed as `$transitjazz-logs`, will provide read-only KQL investigation over the workspace. It will follow the same boundaries and interaction model as the existing `$grafana` skill: hidden short-lived authentication, a diagnostic-first failure path, human output by default, JSON when requested, and no administrative or write operations.

## Why this decision

The current Parquet path is a small telemetry platform embedded in the application. The worker creates `PerCityCycle` and `FullCycle` records, buffers them through a hosted sidecar, serializes Snappy-compressed Parquet part files, writes them to Blob Storage, and maintains separate Web API, CLI, MCP, schema-validation, and AI-skill layers to read them. Most persisted fields now duplicate worker metrics already exposed to Grafana.

The Azure logging foundation already exists. The Bicep deployment creates a pay-as-you-go Log Analytics workspace and connects the Container Apps environment directly to it. The change therefore simplifies an existing route rather than introducing a second observability service or an application-side exporter.

There is also a near-term platform reason to change the route. The current Container Apps `log-analytics` destination uses the legacy HTTP Data Collector API and writes `_CL` custom tables. Microsoft has placed that API on a deprecation path, with support ending September 14, 2026, and recommends changing Container Apps environments to the Azure Monitor destination. See [Change Azure Container Apps logging from Log Analytics to Azure Monitor](https://learn.microsoft.com/en-us/azure/container-apps/migrate-logs-azure-monitor).

Azure Container Apps can route console and system logs through Azure Monitor to Log Analytics, Storage, or Event Hubs. This design uses Log Analytics only; a second Blob archive would preserve the duplication that this change is intended to remove. See [Container Apps logging options](https://learn.microsoft.com/en-us/azure/container-apps/log-options).

## Goals

- Keep at least seven days of persistent, centrally queryable logs.
- Make failures and unexpected states, including missing tones, explainable with KQL.
- Reuse the existing Azure Container Apps and Log Analytics infrastructure.
- Minimize ingestion volume and operational components.
- Preserve Grafana for metrics and alerts.
- Give AI agents a safe read-only path from a Grafana symptom to supporting logs.
- Remove the worker's buffering, Parquet serialization, Blob persistence, and bespoke telemetry query stack after a proven cutover.
- Keep credentials and tokens out of prompts, command output, source files, and logs.

## Non-goals

- Replacing Grafana, Prometheus, dashboards, or metric alerting.
- Sending every successful cycle or every numeric measurement to logs.
- Adding Application Insights, OpenTelemetry collectors, Loki, Event Hubs, a dedicated Log Analytics cluster, or a second archive in the first version.
- Creating a general Azure administration skill.
- Letting the AI skill create saved queries, alerts, workbooks, diagnostic settings, role assignments, tables, or Azure resources.
- Deleting historical Parquet blobs during the migration. Any later deletion requires a separate, explicit retention decision.

## Alternatives considered

| Option | Decision | Reason |
|---|---|---|
| Keep Parquet and improve the query bridge | Reject | Blob storage is inexpensive, but the application still owns buffering, persistence, schemas, an API, two query tools, and a specialized AI skill. It also duplicates metrics rather than providing normal operational logs. |
| Application Insights | Reject for this scope | It is valuable for distributed tracing and application performance monitoring, but TransitJazz currently needs worker events and explanations. Adding its SDK and telemetry model would be more machinery than stdout-to-Log-Analytics. |
| Azure Monitor + Log Analytics Analytics plan | Fallback only | It offers the broadest KQL support, but sparse worker events do not initially justify the higher ingestion rate. |
| Azure Monitor + Log Analytics Auxiliary plan | Reject initially | It has the lowest ingestion rate but a slower, more constrained query experience. After source-side volume reduction, its savings are unlikely to justify a worse interactive AI workflow. |
| Azure Monitor + Log Analytics Basic plan | **Choose** | It is the lowest-cost plan that retains a practical interactive investigation experience for the proposed single-table KQL recipes. |
| Azure Storage archive only | Reject | It persists cheaply but is not an interactive centralized log search solution; querying it would recreate the custom tooling being removed. |
| Azure Event Hubs, Loki, or another log platform | Reject | They add another service and operational path when the required Azure workspace already exists. |

## Cost model

The recurring Azure cost is intentionally limited to:

- Basic-plan ingestion for sparse `ContainerAppConsoleLogs` rows;
- data scanned by bounded Basic queries; and
- Analytics-plan ingestion for the low-volume platform-generated `ContainerAppSystemLogs` stream.

There is no separate application telemetry host, collector, event stream, archive, dedicated cluster, commitment tier, or paid retention beyond the included/fixed 30-day window. After cutover, the telemetry storage account may be removed if its audit finds no other consumer, eliminating that path's ongoing storage and transaction cost.

The document does not assert a dollar estimate without the workspace's real ingestion baseline and Azure region pricing. Phase 0 measures current console volume, projects the post-filter volume, and records the resulting estimate before rollout. This prevents a speculative price from becoming an architecture assumption.

## Target architecture

```text
TransitDataWorker
  ILogger + structured state
             |
             v
  one-line JSON on stdout/stderr
             |
             v
Azure Container Apps environment
  appLogsConfiguration.destination = azure-monitor
             |
             v
Azure Monitor diagnostic settings
  +-- ContainerAppConsoleLogs --> existing Log Analytics workspace
  |                              Basic plan, 30 days
  +-- ContainerAppSystemLogs  --> existing Log Analytics workspace
                                 Analytics plan, 30 days

Grafana metrics -----------------> trends, panels, and alerts
                                      |
                                      | city + time window
                                      v
$transitjazz-logs --> read-only Azure query interface --> KQL evidence
```

There is no application-to-workspace SDK, connection string, local credential file, or in-process upload buffer. Azure Container Apps owns collection and delivery. Logs can take several minutes to become queryable, so investigations and acceptance tests must account for ingestion delay.

## Major design decisions

### 1. Use the existing Log Analytics workspace

The project already deploys a `PerGB2018` Log Analytics workspace and wires it to the Container Apps environment. Reusing it avoids another billable service, SDK, secret, exporter, and operational surface.

The direct `log-analytics` Container Apps destination will change to `azure-monitor`. A diagnostic setting on the managed environment will route the two log categories to the existing workspace. This produces the standard `ContainerAppConsoleLogs` and `ContainerAppSystemLogs` tables and lets the console table use the Basic plan. The existing direct destination's `_CL` tables are not the target contract.

Historical `_CL` rows remain queryable for their existing retention period; the routing change does not copy them into the standard tables. The cutover therefore updates all new queries to the standard names and treats the old tables as read-only history.

### 2. Choose Basic for application logs, not Auxiliary or Analytics

`ContainerAppConsoleLogs` supports Analytics, Basic, and Auxiliary plans. Basic is the lowest-cost plan selected for this design because it preserves an interactive investigation workflow while charging a reduced ingestion rate. It supports table-scoped filtering, projection, scalar operations, and aggregations, which cover the proposed logging skill.

Auxiliary has a still lower ingestion rate, but it is optimized for infrequently queried verbose data, has a slower query experience, and would add little value after the worker's high-frequency success messages are removed. Analytics provides a richer KQL surface but charges the higher ingestion rate for data that does not need cross-table joins during normal investigations. Azure Monitor's current cost model charges Basic and Auxiliary queries by data scanned, so every skill query must remain time- and table-bounded. See [Azure Monitor Logs cost calculations and options](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/cost-logs).

Basic's constraints are accepted:

- queries start with exactly one table;
- cross-table `join`, `search`, `find`, and `externaldata` patterns are unavailable;
- only two Basic/Auxiliary queries may run concurrently per user;
- the query tool must use a Basic-compatible search operation; and
- results are intended for investigation, not a replacement analytical warehouse.

If the official Azure MCP query operation cannot query a Basic table during the implementation proof, the preferred fallback is a thin read-only wrapper over Azure Monitor's `/search` API using the caller's Azure identity. The fallback is to keep `ContainerAppConsoleLogs` on Analytics, not to restore Parquet or introduce a second datastore. This compatibility gate must be resolved before disabling Parquet writes.

### 3. Retain 30 days and guarantee at least seven

The target declares 30-day retention for both tables. Basic's interactive query window is fixed at 30 days. Analytics ingestion includes roughly the first month of retention, so attempting to configure seven days would not create a meaningful saving. Thirty days also gives enough overlap for delayed incident investigation while remaining a short operational window rather than an indefinite archive.

The release gate is phrased as a service guarantee: seven consecutive days of expected logs must remain queryable before Parquet is disabled. Configuration alone is not proof.

### 4. Emit native structured JSON through `ILogger`

The worker will use the built-in .NET JSON console formatter with single-line output, UTC timestamps, and scopes where useful. Structured message-template arguments remain structured state; code must not pre-serialize a JSON string into the message field. The supported formatter and configuration options are described in [.NET console log formatting](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/console-log-formatter).

This choice keeps the application on standard .NET logging and lets Container Apps capture stdout/stderr without Azure-specific logging code. Azure Monitor does not dynamically promote JSON members into columns on this route, so KQL explicitly parses the `Log` value. A contract test will lock the actual JSON shape received in `Log`, including how formatter state and exceptions are represented, before production queries depend on it.

### 5. Metrics describe behavior; logs explain discrete events

The existing `PerCityCycle` and `FullCycle` rows are not copied one-for-one into Log Analytics. That would exchange inexpensive compressed objects for paid log ingestion while retaining duplicated data.

The boundary is:

| Signal | Home |
|---|---|
| Counts, gauges, durations, freshness, memory, cache sizes, suppression totals, wire bytes, heartbeat | Grafana metrics |
| Startup/shutdown, failures, state changes, partial inputs, anomalous zero-tone cycles, publish failures, exception context | Structured logs |
| Dashboard editing and alerts | Grafana |
| Root-cause narrative and correlated events | Log Analytics |

Normal successful cycles produce metrics only. Routine reconciliation summaries that currently log at `Information` must move to `Debug`, be removed, or become a bounded anomaly event. Repeated failures must be coalesced or rate-limited by stable key so an upstream outage cannot create a log storm; the first occurrence, material state change, periodic reminder, and recovery are retained.

### 6. Use a small, versioned event contract

Every queryable application event uses stable property names. Optional fields appear only when they add evidence; the event name and version define their meaning.

| Property | Purpose |
|---|---|
| `EventName` | Stable event discriminator, not free-form message text |
| `EventVersion` | Positive integer schema version |
| `EventId` | Unique ID for this emitted event |
| `CycleId` | Correlates all events from one full worker tick |
| `City` | Canonical city slug when city-scoped |
| `Outcome` | Small bounded value such as `Succeeded`, `Partial`, or `Failed` |
| `ReasonCode` | Stable machine-queryable explanation |
| `DurationMs` | Operation duration when relevant |
| `DeploymentRevision` | Container Apps revision, obtained from platform context where practical |
| `ExceptionType` | Exception type only; the formatter carries exception details |

An anomalous city-cycle event may additionally carry `TonesEmitted`, `VehiclesProcessed`, `FeedFreshnessSeconds`, `CrossingsEmitted`, the four crossing-suppression counts, `PublishAttempted`, `PublishSucceeded`, and `BatchWireBytes`. These are diagnostic context on an exceptional row, not a second metrics stream.

Initial event names are:

- `WorkerStarted` and `WorkerStopped`;
- `CityInputFailed`, `CityInputPartial`, and `CityInputEmpty`;
- `RouteIndexUnavailable`;
- `CityCycleAnomaly`;
- `PublishFailed` and `PublishRecovered`; and
- `WorkerCycleFailed` and `WorkerCycleRecovered`.

Initial missing-tone reason codes are:

- `NO_VEHICLES`;
- `STALE_FEED`;
- `DUPLICATE_FEED`;
- `ROUTE_INDEX_UNAVAILABLE`;
- `NO_CROSSINGS`;
- `ALL_CROSSINGS_SUPPRESSED`;
- `INPUT_FAILED`; and
- `PUBLISH_FAILED`.

Human-readable messages remain useful, but automation and the AI skill filter on the stable properties rather than parsing prose.

### 7. Make cost control a source concern

The least expensive ingested byte is the one not emitted. Cost controls are therefore ordered as follows:

1. Do not log normal metric samples or full-cycle records.
2. Keep production's default level at `Information`, with noisy categories or per-cycle messages at `Warning`/`Debug` as appropriate.
3. Coalesce repeated identical failures and emit recovery transitions.
4. Keep payloads small and fields bounded; never log feed bodies or transit entity arrays.
5. Keep every AI query table-scoped, time-bounded, projected to needed columns, and row-limited.
6. Measure ingested GB and query scan volume after deployment and review it after the first week.

No commitment tier or dedicated cluster is justified at TransitJazz's expected volume. A daily ingestion cap is not part of the initial design because reaching it silently stops useful collection; consider one only after a measured baseline and with an alert below the cap.

### 8. Treat redaction as part of the schema

Logs must never contain access tokens, API keys, authorization or cookie headers, connection strings, credential-file contents, full request/response bodies, or URLs containing query-string secrets. The existing rail fetch message that may include an `apiKey` in `RequestUri` must be replaced with a safe endpoint identity before central collection is enabled.

Use canonical city, feed type, HTTP status, exception type, and stable reason codes instead of raw URLs or headers. Tests must reject known secret-shaped property names and verify that configured feed credentials cannot appear in emitted JSON.

### 9. Build a read-only `$transitjazz-logs` skill

The skill is a repository-specific investigation front end, not a logging administrator. Its description should trigger for requests to inspect TransitJazz logs, run KQL, investigate worker failures or missing tones, follow a Grafana symptom into logs, or diagnose log access.

Its preferred interface is the official Azure MCP Server's Azure Monitor operations for listing workspaces/tables and querying logs with KQL. Those operations are documented as read-only in the [Azure MCP Server Azure Monitor tools](https://learn.microsoft.com/en-sg/azure/developer/azure-mcp-server/tools/azure-monitor), and the corresponding command surface is shown in the [Azure MCP command reference](https://github.com/Azure/azure-mcp/blob/main/docs/azmcp-commands.md). The implementation must still prove Basic-table support as described in decision 2.

If the preferred operation lacks Basic support, the skill may call a project-owned, read-only query helper that sends `POST /v1/workspaces/{workspaceId}/search` through `az rest`. Azure CLI acquires the short-lived token. The helper accepts only a workspace, time range, table-scoped KQL, and result limit; it has no generic Azure mutation capability.

### 10. Keep KQL as the primary log query language

Users may provide KQL directly and choose common time ranges and output formats. The skill preserves supplied KQL exactly unless the user asks for refinement. A refinement is shown or described so it is auditable.

Queries must begin with `ContainerAppConsoleLogs` or `ContainerAppSystemLogs`, use the narrowest practical time range, and project only useful columns. The skill refuses or narrows unbounded Basic-table scans. It reports the effective workspace, table, UTC range, KQL, and result limit with the result.

Human-readable tables are the default. JSON is available for scripts, automation, and deeper analysis. `--show-kql` behavior is always available so a natural-language investigation can be reproduced manually.

### 11. Accept links and stable identifiers as investigation entry points

The logging skill accepts:

- a copied Azure Logs/Log Analytics link, preserving workspace, time range, and query when present;
- an explicit workspace alias or resource ID;
- a `CycleId`, `EventId`, city, deployment revision, event name, or reason code;
- a UTC or local time range, with the resolved UTC range reported; or
- a Grafana dashboard/panel link and symptom description.

Explicit user values override context encoded in a link. A copied Azure link is preferred to a bare workspace ID because it can preserve investigative context. When a Grafana link is supplied, `$transitjazz-logs` composes with `$grafana` to obtain the effective panel range, variables, city, and relevant metric; it does not reimplement dashboard parsing.

### 12. Support event-level investigation

Without an event selection, the skill summarizes relevant events by name, reason code, city, and time bucket before retrieving detail. With a stable identifier, it narrows immediately. `CycleId` is the primary correlation key within logs; city plus a bounded timestamp is the bridge from Grafana metrics to the corresponding cycle.

The preferred workflow is:

1. Identify the Grafana panel, city, symptom, and effective time window.
2. Query anomaly/failure events for the same city and bounded window.
3. Select an event and retrieve other rows with the same `CycleId` and deployment revision.
4. Inspect its reason code and diagnostic context.
5. Refine the KQL while preserving the original query as context.

The skill must distinguish an absence of matching logs from proof that an event did not happen. Ingestion delay, retention, rate limiting, log level, and query filters are possible explanations.

### 13. Make authentication invisible and least-privileged

The user or agent identity authenticates through the configured Azure tooling and receives short-lived credentials. The skill never asks the user to paste a bearer token, client secret, workspace shared key, or credential-file content. Tokens and authorization headers are never printed, even in diagnostic output.

The principal receives `Log Analytics Reader` scoped to the TransitJazz workspace, which provides the query permission described in [Azure Monitor's log-query access guidance](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/get-started-queries). It does not receive Contributor, Monitoring Contributor, or rights to change diagnostic settings, retention, table plans, alerts, role assignments, or saved queries.

### 14. Make `doctor` the first failure path

When an operation fails, the skill runs a read-only `doctor` workflow before speculating. It reports one failing layer and a secret-free next action.

`doctor` checks, in order:

1. the Azure MCP/query interface is installed and registered;
2. an Azure identity is available without displaying its tokens;
3. the expected subscription and workspace resolve;
4. the caller has workspace query permission;
5. the target tables exist and their plans are compatible with the query path;
6. the latest ingestion timestamp is recent enough; and
7. a minimal, time-bounded query succeeds.

This distinguishes local integration problems, authentication, RBAC, Azure connectivity, missing diagnostic settings, ingestion delay, an empty time range, and an unsupported Basic query path. The skill does not repeatedly retry a persistent permission or configuration error.

### 15. Separate access from administration

Azure Portal and infrastructure-as-code remain the places for diagnostic settings, table plans, retention, budgets, alerts, workbooks, and RBAC. The skill runs queries and presents evidence only. Even when the caller has broader Azure rights, the skill may use only workspace/table discovery, query, and diagnostic operations classified as read-only.

### 16. Cut over with evidence, then retire the Parquet path

Central logging and Parquet run in parallel for at least seven consecutive days. During this window, representative failures and a deliberately emitted safe test event are verified in Log Analytics. Parquet writes are disabled only after the retention, query, redaction, and AI-skill acceptance criteria pass.

Historical blobs are left intact and read-only. The removal phase deletes code and infrastructure references, not stored history. A separate future decision may set a Blob lifecycle or delete the storage account after its contents and other consumers are reviewed.

## Structured logging examples

The following C# illustrates the intended shape; exact event IDs and logging helpers are implementation details:

```csharp
logger.LogWarning(
    new EventId(2101, "CityCycleAnomaly"),
    "EventName={EventName} EventVersion={EventVersion} EventId={EventId} " +
    "CycleId={CycleId} City={City} Outcome={Outcome} ReasonCode={ReasonCode} " +
    "TonesEmitted={TonesEmitted} VehiclesProcessed={VehiclesProcessed} " +
    "CrossingsEmitted={CrossingsEmitted} PublishAttempted={PublishAttempted} " +
    "PublishSucceeded={PublishSucceeded}",
    "CityCycleAnomaly",
    1,
    eventId,
    cycleId,
    city.Name,
    "Partial",
    reasonCode,
    0,
    vehicleCount,
    crossingCount,
    publishAttempted,
    publishSucceeded);
```

The implementation should normally wrap this in a source-generated `LoggerMessage` method instead of repeating a long template at call sites. The contract is the set of structured properties, not this API spelling.

After validating the actual JSON formatter shape, a representative missing-tone query will resemble:

```kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-08-29T00:00:00Z) .. datetime(2026-08-30T00:00:00Z))
| extend Entry = parse_json(Log)
| extend State = todynamic(Entry.State)
| where tostring(State.EventName) == "CityCycleAnomaly"
| where tostring(State.City) == "atlanta"
| project
    TimeGenerated,
    RevisionName,
    CycleId = tostring(State.CycleId),
    ReasonCode = tostring(State.ReasonCode),
    VehiclesProcessed = toint(State.VehiclesProcessed),
    CrossingsEmitted = toint(State.CrossingsEmitted),
    PublishSucceeded = tobool(State.PublishSucceeded)
| order by TimeGenerated desc
| take 100
```

The final query must follow the JSON shape observed in Azure; this example must not be copied into the skill until the ingestion contract test passes.

## Proposed `$transitjazz-logs` skill contract

The skill should mirror the organization of `.agents/skills/grafana/SKILL.md`:

1. **Find the available interface.** Prefer the registered Azure Monitor read-only tool; use the project query helper only for Basic compatibility. Inspect only relevant help when exact arguments are unknown.
2. **Preserve the read-only boundary.** Allow workspace/table discovery, KQL queries, event retrieval, and `doctor`; prohibit every mutation.
3. **Run KQL.** Preserve user KQL, resolve range/output, bound scans, and expose effective query context.
4. **Investigate events.** Accept links and identifiers, summarize before expanding, and correlate by `CycleId`.
5. **Connect Grafana to logs.** Reuse the Grafana skill's panel context, then query the matching city and time range.
6. **Authenticate safely.** Use short-lived Entra authentication and workspace-scoped `Log Analytics Reader`; never reveal credentials.
7. **Diagnose first.** Run `doctor` on failures and report the failing layer.
8. **Present the investigation.** Lead with the finding, separate evidence from interpretation, show reproducible KQL, and default to a concise table.

The skill's implementation should bundle:

- `SKILL.md` with the read-only workflow and safety boundary;
- a compact reference for the structured event schema and reason codes;
- reviewed KQL recipes for missing tones, input failures, publish failures, exceptions, recoveries, revision changes, and ingestion freshness; and
- a `doctor` script or exact read-only diagnostic workflow.

It must not bundle Azure credentials, workspace keys, access tokens, or an unrestricted arbitrary REST helper.

## Infrastructure changes

Implementation will update Bicep to:

1. retain the existing Log Analytics workspace on `PerGB2018` pay-as-you-go;
2. change the Container Apps environment log destination from `log-analytics` to `azure-monitor`;
3. remove the workspace customer ID/shared-key flow from the environment module;
4. add an environment diagnostic setting for `ContainerAppConsoleLogs` and `ContainerAppSystemLogs` targeting the existing workspace;
5. configure `ContainerAppConsoleLogs` as Basic with 30-day retention;
6. keep `ContainerAppSystemLogs` as Analytics with 30-day retention;
7. add a workspace-scoped `Log Analytics Reader` assignment for the intended human/agent principal through the normal deployment process; and
8. retain the telemetry storage module during dual-run, then remove it only after cutover and a storage-use audit.

Generated ARM JSON must be regenerated through the repository's normal Bicep build process; it is not edited by hand.

## Application changes

Implementation will:

1. configure the built-in JSON console formatter;
2. define versioned event names, reason codes, and correlation IDs;
3. emit structured anomaly, failure, transition, and recovery events;
4. demote or remove routine high-frequency `Information` messages;
5. redact unsafe request information, especially URLs containing API keys;
6. add JSON-shape, redaction, rate-limiting, and event-contract tests;
7. keep Grafana metric instrumentation unchanged except for removal of Parquet-sidecar self-health metrics; and
8. remove Parquet event posting only after the dual-run gate passes.

## Components retired after cutover

The implementation plan must verify references before deleting, but the expected retirement set is:

- worker-side `ParquetLoggingService`, `LogEventWorker`, `ILoggingService`, server-side `IEventNotificationService`, `TelemetryEvent`, and `LoggingOptions`;
- `Logging:Telemetry` configuration and Blob identity/access used only by the sidecar;
- Parquet.Net and Blob client dependencies used only by this path;
- Parquet-sidecar buffer, drop, and persist-failure metrics and tests;
- Web API `TelemetryEndpoints` and shared telemetry paging/event DTOs if they have no non-Parquet consumer;
- client telemetry endpoint service and UI dependencies if present;
- `tools/telemetry-query-tool` and `tools/telemetry-mcp`;
- the Parquet-oriented `mj-data-explorer` skill, replaced by `$transitjazz-logs`;
- the telemetry Blob container module, role assignments, and outputs after confirming no other consumer; and
- Parquet-specific documentation, schema validators, tests, and feature-plan statements.

The feature 053 plan and contracts currently state that the Parquet path remains unchanged. Implementation must update those artifacts to record this superseding decision; the design document alone does not silently rewrite an accepted plan.

## Migration and rollout

### Phase 0: measure and prove the query route

- Measure current `ContainerAppConsoleLogs_CL`/console ingestion volume and identify the noisiest categories and messages.
- Confirm the existing workspace name, region, and actual retention.
- Prove a `ContainerAppConsoleLogs` Basic-table query through Azure MCP or the `/search` helper.
- Estimate monthly ingestion after high-frequency messages are removed. Do not choose a commitment tier.

### Phase 1: define and emit the contract

- Add JSON console formatting and safe structured events.
- Add `CycleId` propagation and missing-tone reason classification.
- Demote routine success-cycle messages.
- Add redaction and log-volume tests.
- Keep Parquet enabled.

### Phase 2: route and retain centrally

- Deploy the Azure Monitor destination and diagnostic settings.
- Apply table plans and 30-day retention.
- Start a unique timestamped console canary before the routing change, apply the diagnostic setting immediately after switching the destination, and validate at least two fresh post-change markers.
- Allow for the documented diagnostic-setting activation window of up to 90 minutes before declaring the route broken; use log streaming and Grafana metrics during that window.
- Validate console and system ingestion after the documented delay.
- Check that secrets and raw payloads are absent.

### Phase 3: build the read-only skill

- Create `$transitjazz-logs` using the contract above.
- Configure only the read-only Azure query interface.
- Validate `doctor`, table output, JSON output, direct KQL, link context, and Grafana-to-log correlation.

### Phase 4: seven-day dual-run

- Keep Parquet and Log Analytics active for at least seven consecutive days.
- Verify a known test event on day one remains queryable after day seven.
- Compare representative anomaly/failure evidence, including a zero-tone condition.
- Review ingested GB, scan volume, and query latency.

### Phase 5: disable, then remove Parquet

- Disable new Parquet writes after all release gates pass.
- Observe one normal release cycle with centralized logs as the only log path.
- Remove the retired code, dependencies, API, tools, skill, and deployment resources in a reviewed change.
- Preserve existing Blob data until a separately approved archival/deletion decision.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Existing `Information` messages create avoidable cost | Audit message frequency before routing; demote routine cycle logs; coalesce failures |
| Basic KQL limitations block an investigation | Design queries as single-table operations; validate recipes; fall back to Analytics only if required |
| Azure MCP uses the standard query endpoint and cannot query Basic logs | Make Basic compatibility a phase-0 gate; use a narrow `/search` helper if needed |
| JSON formatter shape differs from assumed KQL | Capture an actual ingested row and lock it with contract tests before publishing recipes |
| Missing tones have no explanatory branch | Require a stable reason code and diagnostic counts whenever the worker detects an anomalous zero-tone cycle |
| Repeated outage floods logs | Coalesce by city/event/reason, log transitions and periodic reminders, and emit recovery |
| Secrets enter centralized logs | Remove raw authenticated URLs, use property allowlists, and add redaction tests before routing |
| Container/App logs arrive late | Treat several-minute delay as normal; `doctor` checks ingestion freshness before declaring absence |
| Disabling Parquet loses investigative coverage | Seven-day dual-run and explicit parity scenarios gate cutover |
| Storage removal deletes historical evidence | Do not delete blobs in this migration; require a separate decision |

## Acceptance criteria

- A safe structured test event is queryable in `ContainerAppConsoleLogs` with all required fields and no secret-bearing values.
- The same test event remains queryable at least seven days later; both target tables declare 30-day retention.
- `ContainerAppConsoleLogs` uses Basic and `ContainerAppSystemLogs` uses Analytics, unless the documented Basic compatibility gate forces the explicit Analytics fallback.
- A normal successful city cycle does not emit a paid `Information` row solely to duplicate metrics.
- An anomalous zero-tone cycle emits one bounded `CityCycleAnomaly` event with city, cycle ID, reason code, relevant counts, and publish outcome.
- Repeated identical failures are rate-limited or coalesced, and recovery is logged.
- Grafana remains able to show the numeric symptom without depending on logs.
- `$transitjazz-logs` can take the Grafana city/time context, find the corresponding log event, show its KQL, and return table or JSON output.
- The skill principal can list/query the workspace but cannot mutate Azure resources or observability configuration.
- `doctor` distinguishes missing integration, authentication failure, RBAC denial, connectivity failure, missing table/diagnostic setting, ingestion delay, and an empty query result.
- Parquet writes remain enabled until all seven-day dual-run gates pass.
- Historical blobs are not deleted as part of the cutover.

## Consequences

The result is a smaller and more conventional observability system: metrics answer "what changed," centralized structured logs answer "why," and the AI workflow connects the two. TransitJazz pays for a deliberately sparse stream in an existing workspace rather than maintaining a custom telemetry data lake and its query stack.

The tradeoff is that the old dense per-cycle Parquet history no longer exists for new data. That is intentional. Questions requiring continuous numeric history belong in Grafana; only meaningful events and their immediate context belong in Log Analytics. Basic-table KQL is also deliberately constrained, so the logging schema and query recipes must make single-table investigation sufficient.

## References

- [Azure Container Apps logging options](https://learn.microsoft.com/en-us/azure/container-apps/log-options)
- [Change Azure Container Apps logging from Log Analytics to Azure Monitor](https://learn.microsoft.com/en-us/azure/container-apps/migrate-logs-azure-monitor)
- [ContainerAppConsoleLogs table reference](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/containerappconsolelogs)
- [ContainerAppSystemLogs table reference](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/containerappsystemlogs)
- [Query Basic and Auxiliary tables in Azure Monitor Logs](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/basic-logs-query)
- [Configure data retention and archive policies in Azure Monitor Logs](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/data-retention-configure)
- [Azure Monitor Logs cost calculations and options](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/cost-logs)
- [Azure Monitor Logs API request format](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/api/request-format)
- [.NET console log formatting](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/console-log-formatter)
- [Azure MCP Server Azure Monitor tools](https://learn.microsoft.com/en-sg/azure/developer/azure-mcp-server/tools/azure-monitor)
- [Azure MCP Server command reference](https://github.com/Azure/azure-mcp/blob/main/docs/azmcp-commands.md)
- [Get started with Azure Monitor log queries](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/get-started-queries)
