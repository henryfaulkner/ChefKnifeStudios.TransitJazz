# Research: Centralized Structured Logging

**Feature**: [Centralized Structured Logging](spec.md)  
**Date**: 2026-08-30

## Decisions

### 1. Central-log delivery uses Container Apps Azure Monitor routing

**Decision**: Change the managed environment's `appLogsConfiguration.destination` from `log-analytics` to `azure-monitor`; add an environment-scoped diagnostic setting that routes exactly `ContainerAppConsoleLogs` and `ContainerAppSystemLogs` to the existing Log Analytics workspace.

**Rationale**: The legacy `log-analytics` route requires customer ID and shared key and produces legacy custom tables. The Azure Monitor destination removes that key flow and routes to resource-specific tables only when the diagnostic setting is present. These log categories belong to the managed environment, not an app-level diagnostic setting.

**Alternatives considered**:

- Keep the legacy `log-analytics` destination and `_CL` tables: rejected because the HTTP Data Collector route is on the documented retirement path and keeps a shared-key configuration flow.
- Route `allLogs`: rejected because it admits future categories and cost without an explicit product decision.
- Send logs through an application-side Azure SDK/exporter: rejected because Container Apps already collects stdout/stderr and the design prohibits a second client, secret, buffer, or delivery path.

**Sources**: [Managed environment Bicep schema](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2025-01-01/managedenvironments), [Container Apps logging options](https://learn.microsoft.com/en-us/azure/container-apps/log-options), [Container Apps logging migration](https://learn.microsoft.com/en-us/azure/container-apps/migrate-logs-azure-monitor).

### 2. Use Basic for application console rows and Analytics for platform rows

**Decision**: Configure `ContainerAppConsoleLogs` for Basic and a 30-day total retention window. Keep `ContainerAppSystemLogs` on Analytics with 30-day interactive and total retention. Retain the documented fallback to Analytics for console rows only if a real Basic-query proof fails.

**Rationale**: Console rows are sparse, operator-facing events after high-frequency logs are suppressed; Basic is the selected lower-cost tier and has a fixed 30-day interactive window. System logs do not support Basic. Both tables are resource-specific; JSON remains in their `Log` value and must be parsed explicitly.

**Alternatives considered**:

- Analytics for all logs: retained only as the explicit application-log fallback because it costs more at expected sparse volume.
- Auxiliary: rejected because it offers a more constrained and slower interactive investigation workflow without a justified benefit after source filtering.
- A second archive: rejected because it preserves the duplicate data path this feature removes.

**Implementation note**: Basic `retentionInDays` is read-only. Configure `totalRetentionInDays: 30`; set both interactive and total retention to 30 on the Analytics system table. Resource-specific tables materialize after routing, so apply the table plan after the tables exist; table-plan changes are limited to once each week.

**Sources**: [Console table schema](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/containerappconsolelogs), [System table schema](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/containerappsystemlogs), [Retention configuration](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/data-retention-configure), [Log table plans](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/logs-table-plans), [Tables Bicep schema](https://learn.microsoft.com/en-us/azure/templates/microsoft.operationalinsights/2025-07-01/workspaces/tables).

### 3. The logging contract is sparse, structured, and versioned

**Decision**: Emit one-line UTC JSON through the built-in .NET logging formatter. A worker-domain event logger owns event name/version, event/cycle identifiers, city, outcome, reason code, bounded context, coalescing, and recovery. It writes directly through `ILogger`, not a channel, buffer, or persistence service.

**Rationale**: Existing Parquet records recreate metrics every 10-second tick. Continuous counts, timings, memory, caches, and liveness already belong in Grafana. Structured events make discrete failures and anomalies explainable without buying a duplicate metrics stream.

**Alternatives considered**:

- Copy `PerCityCycle` and `FullCycle` rows into logs: rejected because it creates paid high-frequency duplication.
- Preserve the custom sidecar but change serialization: rejected because it retains buffering, persistence, schemas, Blob access, API/UI, query tools, and bespoke skill layers.
- Format pre-serialized JSON into message text: rejected because structured state must remain queryable and redaction/testable.

**Repository findings**: `Worker.cs` currently posts one per-city and one full-cycle `TelemetryEvent` through `LogEventWorker`. It also logs a reconciliation summary at Information every tick. The new event logger must generate one `CycleId` per worker tick, preserve it only as log correlation, and never add it to Grafana labels.

### 4. Redaction and volume control are source-side responsibilities

**Decision**: Replace raw feed URL and request URI logging with bounded endpoint identities; set explicit production levels/categories; demote normal reconciliation and routine SignalR messages; coalesce repeated `(city, event, reason)` failures and log initial, transition, reminder, and recovery evidence.

**Rationale**: Collection cannot safely redact already emitted secret-bearing text, and source suppression is the only way to avoid unnecessary ingestion cost. Current concrete risks include `RailRealtimeAdapter` logging `RequestUri`, configured-city failures logging raw URLs, routine worker reconciliation, sidecar health, routine SignalR traffic, and an existing Web API default `Debug` level.

**Alternatives considered**:

- Rely on final formatter settings or Azure ingestion transformations: rejected because secrets and high-volume messages have already been emitted by then.
- Log every failure occurrence: rejected because an upstream outage becomes a log storm and obscures the first useful cause.

### 5. Basic-table investigation is a bounded read-only contract

**Decision**: The `transitjazz-logs` skill accepts only one approved source table, a bounded UTC range, useful projection, and a `take` of 1–100 rows. It preserves conforming user KQL and reports effective workspace/table/range/KQL/limit. It defaults to a modest range and never silently broadens a request.

**Rationale**: Basic queries are billed by scanned data and allow exactly one source table. They support filtering, projection, scalar operations, `parse_json`, `extend`, and `summarize`, but not joins, `find`, KQL `search`, `externaldata`, user-defined functions, or cross-resource/service queries.

**Alternatives considered**:

- Let arbitrary KQL pass through: rejected because it permits unbounded/billable scans and unsupported Basic features.
- Translate direct user KQL silently: rejected because it makes an operator's investigation non-reproducible.

**Basic compatibility gate**: Prefer a registered read-only Azure Monitor tool. If it cannot query Basic, validate a project-owned helper against the Azure Logs REST `POST /v1/workspaces/{workspaceId}/search?timespan=...` operation using the caller's short-lived identity. The helper is a constant read-only endpoint/method with allow-listed workspaces/tables and no arbitrary flags, URLs, headers, or Azure commands. If both paths fail, record the proof and use the explicit console-to-Analytics fallback—not Parquet or a new datastore.

**Sources**: [Basic and Auxiliary query restrictions](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/basic-logs-query), [Azure Logs request format](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/api/request-format).

### 6. The new skill follows repository source and safety conventions

**Decision**: Create `skills/transitjazz-logs/`, register it in `skills/_skill-sync/catalog.json`, and render generated tool copies through `tools/sync-skills.ps1`. Do not author the skill in generated `.agents` directories. The skill composes with `$grafana` for panel context and replaces—not mutates—the Parquet-specific `mj-data-explorer` skill after cutover.

**Rationale**: `$grafana` already establishes the repository's investigation UX: discover available read-only capability, use hidden short-lived authentication, diagnose first, show reproducible context, and avoid administration. `mj-data-explorer` is wired to the legacy Parquet MCP bridge and cannot be safely repurposed as a generic Azure logging skill.

**Alternatives considered**:

- Modify `mj-data-explorer` in place: rejected because its contract, references, and MCP dependencies are Parquet-specific and confusing during dual-run.
- Depend unconditionally on Azure MCP: rejected because no Azure Monitor MCP tool is currently registered in this runtime; its presence is an explicit `doctor` check.

### 7. Reader access is scoped and read-only by design

**Decision**: Assign the specification-required `Log Analytics Reader` role to the intended human/agent principal at the workspace scope, using a supplied principal object ID. The skill permits discovery, query/event retrieval, and diagnostics only; it refuses table plans, retention, diagnostic settings, role assignments, alerts, workbooks, saved queries, resource changes, and credential handling.

**Rationale**: The role allows workspace query permission without workspace shared-key read, and workspace scope avoids broader subscription grants. Table and routing changes remain deployment responsibilities outside the skill.

**Caveat**: `Log Analytics Reader` is broader than the bare-minimum `Log Analytics Data Reader` surface. The specification names it, so the plan preserves it; document the scope and revisit a lower-privilege role only by an explicit follow-up decision after Basic proof.

**Sources**: [Log Analytics Reader role](https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/monitor#log-analytics-reader), [Manage access to Log Analytics](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/manage-access).

### 8. Cutover requires a canary, seven-day dual-run, and audited removal

**Decision**: Before destination change, emit a unique safe console canary. Apply the diagnostic setting immediately with the Azure Monitor destination, then confirm at least two fresh post-change markers after allowing up to 90 minutes for activation. Retain central logs and Parquet for at least seven consecutive days and require a day-one retention check, representative zero-tone/input/publish evidence, redaction proof, query/skill proof, and cost review before disabling new Parquet writes.

**Rationale**: New Azure Monitor routing does not migrate legacy `_CL` data, platform-event backfill is not guaranteed, and logs can be delayed. The seven-day window proves the new system is useful before the old system is altered. All historical blobs remain intact pending a future approval.

**Alternatives considered**:

- One-step sidecar replacement: rejected because a routing or access issue could erase the only investigative evidence.
- Delete old data/resources during migration: rejected because the specification reserves archival/deletion for a separate decision and consumer audit.

## Resolved Unknowns

All planning unknowns are resolved. The only runtime-dependent facts—the actual JSON shape received in the `Log` column and whether the preferred Azure interface can query Basic—are explicit pre-cutover proof gates, not unresolved design choices. The design defines safe fallback behavior for either outcome.
