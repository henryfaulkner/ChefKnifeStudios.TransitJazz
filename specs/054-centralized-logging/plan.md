# Implementation Plan: Centralized Structured Logging

**Branch**: `main` | **Date**: 2026-08-30 | **Spec**: [spec.md](spec.md)  
**Input**: `docs/AZURE_CENTRALIZED_LOGGING_DESIGN_DOCUMENT.md`

## Summary

Replace the worker's new Parquet telemetry writes with sparse, versioned, single-line JSON events emitted through `ILogger`. Azure Container Apps will collect stdout/stderr and route it through Azure Monitor diagnostic settings to the existing Log Analytics workspace. `ContainerAppConsoleLogs` is the 30-day Basic store for application events; `ContainerAppSystemLogs` remains the 30-day Analytics store for platform events. Grafana remains authoritative for numeric metrics, dashboards, and alerts.

The delivery is staged: establish and test the event/redaction contract; prove Basic query compatibility and routing; run both paths for seven consecutive days; then disable new Parquet writes. Only after one normal centralized-logs-only release and an audited consumer check may a follow-up remove legacy code, tools, APIs, skills, Blob-only resources, and superseded documentation. Historical blobs are not deleted by this feature.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: built-in `Microsoft.Extensions.Logging` JSON console formatter; existing OpenTelemetry metrics remain unchanged; Azure Monitor diagnostic settings and Log Analytics through Bicep; a registered Azure Monitor read-only interface is preferred for investigation, with a narrow Azure Monitor Search API helper only if the Basic-table proof requires it  
**Storage**: existing Log Analytics workspace: `ContainerAppConsoleLogs` (Basic, 30 days) and `ContainerAppSystemLogs` (Analytics, 30 days); existing Blob/Parquet telemetry continues during the seven-day dual run and remains historical evidence afterward  
**Testing**: xUnit / `dotnet test`; in-process log-capture contract tests; Bicep build, validate, and what-if; documented Azure acceptance and dual-run evidence; skill-sync verification  
**Target Platform**: .NET 10 Linux process co-hosted in an Azure Container App, plus local console/test execution  
**Project Type**: background worker hosted by the ASP.NET Core Web API, Azure infrastructure, and repository agent skill  
**Performance Goals**: normal successful city ticks add no paid informational event; bounded event emission does not disrupt the ten-second cycle; Grafana city/time context reaches reproducible log evidence within five minutes  
**Constraints**: JSON is UTC and one line; event fields, city slugs, names, outcomes, and reasons are closed/bounded; never log credentials, secret-bearing URLs, headers, payloads, entity arrays, or arbitrary exception text; Basic queries are table-scoped, finite UTC ranges, projected, and limited to 1-100 rows; all investigation is read-only  
**Scale/Scope**: seven configured cities, one co-hosted replica, one ten-second tick; eleven v1 event names, eight missing-tone reason codes, seven consecutive dual-run days before Parquet retirement

## Constitution Check

### Initial Gate — Pass with carried production prerequisites

| Constitution area | Result | Plan response |
|---|---|---|
| I. Decoupled cloud architecture | Carried prerequisite | The worker is currently co-hosted in the public Web API Container App. This plan neither expands nor self-authorizes that topology; the feature 053 topology/artifact amendment and production gates remain prerequisites. |
| II. No frontend secrets | Pass | No client credential is added. Investigation uses short-lived Entra identity; the skill never accepts or displays credentials. |
| III. Two-pass pipeline | Pass | Logging observes fetch, reconciliation, crossing, and publish outcomes without changing payloads, route identity, or SignalR publication. |
| IV. OpenTelemetry observability | Pass | Structured .NET logs reach the constitution's Azure Log Analytics target. Existing Grafana OpenTelemetry metrics remain the separate numeric signal. |
| V. GitHub Actions artifacts | Carried prerequisite | Bicep/ARM outputs are regenerated through the established deployment workflow. This plan creates no new deployment artifact or exception. |
| VI. GTFS ID mapping | Pass | The contract carries only canonical city identity, never a new route-identity representation. |
| Governance | Conditional release gate | Azure routing, table plans/retention, workspace reader scope, Basic compatibility, redaction, baseline/cost, dual-run evidence, and removal audit must all be recorded before production cutover. |

**Initial decision**: Planning and local/controlled dual-run implementation may proceed. Production routing, production enablement, and Parquet retirement remain blocked until the carried feature 053 prerequisites and all feature 054 release gates are complete.

### Post-Design Gate — Pass with the same release gates

The design uses the existing Azure backend, keeps metrics separate from sparse events, bounds event state and query scans, and supplies no log-writing or administration workflow. The carried topology/artifact prerequisites plus Basic-query proof, routing/RBAC, redaction, cost, and seven-day evidence remain mandatory before production cutover.

## Project Structure

### Documentation (this feature)

```text
specs/054-centralized-logging/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── structured-log-event-v1.md
│   ├── investigation-skill.md
│   ├── azure-log-routing.md
│   └── migration-gates.md
└── tasks.md                         # generated later by /speckit-tasks
```

### Source Code (repository root)

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── Worker.cs                         # CycleId propagation and event decisions
├── Cities/                           # safe fetch/publish failure context; no raw secret URLs
├── Logging/
│   ├── StructuredLogEvent.cs          # v1 envelope, taxonomy, and bounded context
│   ├── StructuredEventEmitter.cs      # safe `ILogger` emission and correlation
│   ├── StructuredEventPolicy.cs       # injected-clock coalescing and recovery state
│   └── StructuredLogRedactor.cs       # allow-list validation and safe endpoint identity
│   └── [legacy Parquet files]        # retained through dual run; removed only after gates
└── Metrics/                           # Grafana instrumentation remains authoritative

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
├── Program.cs                         # JSON console setup and co-hosted worker DI
└── EndpointGroups/TelemetryEndpoints.cs # legacy read route retained until post-cutover cleanup

src/Server/*Tests/
├── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
│   ├── StructuredLogEventTests.cs      # event schema and missing-tone taxonomy
│   ├── StructuredEventPolicyTests.cs   # coalescing/recovery with a fake clock
│   └── StructuredLoggingRedactionTests.cs # formatter-safe source values and volume
└── ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/
    └── LoggingHostTests.cs             # JSON-console host contract tests

bicep/
├── main.bicep
├── main.json                          # regenerated, never edited by hand
└── modules/
    ├── containerAppsEnvironment.bicep # Azure Monitor destination; no workspace shared key
    ├── logAnalyticsDiagnosticSettings.bicep # managed-environment log routing
    ├── logAnalyticsTablePolicies.bicep # table plan and retention
    └── workspaceRoleAssignment.bicep  # workspace-scoped Log Analytics Reader

skills/
├── transitjazz-logs/{SKILL.md,references/,.skill-sync/}
├── _skill-sync/catalog.json
└── mj-data-explorer/                  # stays available throughout dual run

tools/
├── sync-skills.ps1
└── [optional constrained Azure Logs query helper]
```

**Structure Decision**: The worker owns the event facts; the Web API owns console configuration because it is the deployed host; Bicep owns Azure routing/RBAC. `skills/transitjazz-logs/` is the canonical skill source and is synchronized into generated agent folders. Legacy Parquet components remain until the explicit post-cutover removal task.

## Implementation Sequence

1. **Establish release controls.** Record the carried feature 053 prerequisites, intended workspace reader principal, current `_CL` console baseline/noisy categories, table retention, and projected sparse-event/query volume. Prove the preferred Azure interface can query a Basic console table; only test the constrained Search API fallback if it cannot. Do not select a commitment tier.
2. **Freeze the v1 event contract.** Add event IDs, one `CycleId` per outer tick, canonical city, bounded outcomes/reasons, safe optional counters, deployment revision, exception type, and source-side allow-list validation. Maintain an in-process `(city?, event, reason)` state to emit initial condition, material transition, configurable 15-minute reminder, and recovery; never emit normal-cycle/metric duplicates.
3. **Instrument the worker's actual outcome seams.** Refactor cycle results to retain route-index versus fetch failure, publish attempt/success, source freshness/duplicate state, and zero-tone classification. Use those facts for all eleven event types and exactly one missing-tone reason. Replace secret-unsafe fetch/exception logging with safe endpoint identity, HTTP status, exception type, and reason code; demote routine high-frequency messages to Debug or remove them.
4. **Configure host logging and protect metrics.** Configure UTC single-line JSON console output in the co-hosted Web API and supported standalone worker host. Set production category filters to preserve meaningful events and suppress cycle noise. Leave Grafana metric reporters, dashboards, and alerts unchanged during the dual run; add no application-to-workspace SDK, exporter, collector, listener, or secret.
5. **Prove application behavior.** Test each event type/reason, required fields/version, zero-tone uniqueness, normal-cycle log volume, ten identical failures followed by recovery, redaction of credential-bearing inputs, formatter shape, CycleId correlation, and unaffected Grafana metrics. Keep Parquet tests and sidecar behavior during this phase.
6. **Route with Azure Monitor.** Change the managed environment from `log-analytics` to `azure-monitor`; add environment diagnostics for exactly the console/system categories; configure Basic console and 30-day retention; grant only workspace-scoped `Log Analytics Reader` to the named principal. Keep telemetry storage/RBAC/configuration active for dual run. Regenerate `main.json`, Bicep build/validate/what-if, and capture a pre-change canary plus two fresh post-change markers. Allow up to 90 minutes for activation and several minutes for ingestion.
7. **Build the read-only skill.** Create and catalog `transitjazz-logs`, then run `tools/sync-skills.ps1`. Prefer Azure Monitor read-only tooling and compose with `$grafana` for panel context. Preserve valid KQL, enforce Basic table/range/projection/limit constraints, show effective context, default to table output, support JSON on request, and refuse every Azure/observability mutation. A fallback helper, if needed, has one read-only Search API method/path, approved workspace/table allow-list, finite range, and no arbitrary headers/URLs/commands.
8. **Validate investigation and routing.** Capture an actual Azure row before final KQL recipes. Test event/cycle/city lookup, explicit context precedence, table versus JSON output, all `doctor` first-failure cases, Basic incompatibility, secret-free errors, and mutation refusal. Prove the Grafana city/time/panel-to-event workflow in under five minutes while the independent metric remains visible.
9. **Complete seven-day dual-run evidence.** Retain Parquet and central logs for seven consecutive days. Query the day-one safe event on day seven; compare input failure, publish failure, and zero-tone evidence across both paths; record routing, plan/retention, redaction, skill, cost/scan, and FR-024 evidence. Block Parquet disablement on any missing gate.
10. **Cut over and retire in a reviewed follow-up.** After every gate passes, disable new Parquet writes and observe one normal centralized-logs-only release. Audit Blob resources, APIs, UI, tools, skills, dependencies, configuration, and docs for other consumers. Only then remove confirmed Parquet-only components and update feature 053's superseded “unchanged” statements; preserve historical blobs pending a separate archival/deletion approval.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Temporary dual observability paths | Seven days of retention, redaction, query, and correlation evidence must exist before legacy retirement. | Immediate removal could erase comparative evidence if routing or Basic queries fail. |
| Constrained Search API helper (conditional) | An installed interface may not support Basic queries; the helper preserves the required read path. | An unrestricted REST/CLI wrapper risks mutation and credential exposure; restoring Parquet/adding a datastore conflicts with the design. |
| In-process coalescing state | It preserves first occurrence, transitions, reminders, and recovery without a paid log storm. | Logging every failure tick duplicates metrics and creates cost/noise; distributed state is unjustified at one replica. |
