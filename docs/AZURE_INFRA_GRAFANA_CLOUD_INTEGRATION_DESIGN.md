# Azure Infrastructure Integration with Grafana Cloud

**Status:** Proposed  
**Date:** 2026-08-30  
**Scope:** TransitJazz Azure Container Apps infrastructure, Azure Monitor, Log Analytics, and the existing Grafana Cloud observability stack

## Executive decision

Use Grafana Cloud as the unified operational dashboard and alerting platform while keeping Azure Monitor and Log Analytics as the system of record for Azure infrastructure metrics and logs.

The Azure Container Apps Grafana view shown in the Azure portal is an Azure-hosted Azure Monitor dashboard experience. It is not a second Grafana Cloud workspace that must be merged. Azure provides prebuilt Container Apps dashboards that can be customized and exported as JSON for reuse in another Grafana instance. The recommended design is therefore:

- Keep the existing TransitJazz application metrics flow into Grafana Cloud.
- Add one Azure Monitor data source to Grafana Cloud for Azure metrics and Log Analytics queries.
- Import or recreate the useful Azure Container Apps panels in Grafana Cloud.
- Combine application panels and Azure infrastructure panels in shared dashboards.
- Keep the Azure portal dashboards as an administrative and emergency fallback view.
- Centralize cross-platform alerting in Grafana Cloud after the data source and alert behavior are proven.

This design does not copy Azure metrics or logs into Grafana Cloud. Grafana Cloud queries Azure Monitor on demand.

## Goals

- Provide one place to investigate application and Azure infrastructure health.
- Correlate TransitJazz metrics, Container Apps revisions, replicas, restarts, and structured logs.
- Reuse the existing Azure Monitor and Log Analytics investment.
- Avoid a new collector, log shipper, application SDK, or inbound endpoint for Azure infrastructure data.
- Preserve Azure-native access for deep platform administration and recovery.
- Use least-privilege Entra authentication from Grafana Cloud.
- Keep dashboards, queries, alert rules, and runbooks understandable and versionable.

## Non-goals

- Replacing Azure Monitor or Log Analytics as the Azure data plane.
- Moving application metrics away from the existing Grafana Cloud OTLP destination.
- Creating Azure Managed Grafana unless a later requirement justifies another Grafana service.
- Adding Application Insights, Loki, Prometheus, or an OpenTelemetry Collector solely to bridge Azure infrastructure data.
- Storing Azure credentials in dashboard JSON, source control, or application configuration.
- Removing the Azure portal dashboard.

## Current state and terminology

There are two related but different Grafana experiences:

| Experience | Role | Recommendation |
|---|---|---|
| Azure Monitor dashboards with Grafana in the Azure portal | Microsoft-hosted visualization of Azure Monitor data, including prebuilt Container Apps dashboards | Retain as Azure-native fallback/admin view |
| Grafana Cloud | Full operational Grafana platform with the existing TransitJazz application metrics | Make this the unified day-to-day view |

The Azure Container Apps dashboard can show application and environment signals such as CPU, memory, request rates, replica counts, revisions, and restarts. Its panels can be exported as JSON and imported into Grafana Cloud, subject to datasource and resource-ID remapping.

## Target architecture

```text
                         Existing application telemetry
TransitDataWorker ---------------------------------------> Grafana Cloud
  OpenTelemetry metrics                                      Prometheus-compatible metrics
                                                             Dashboards and alerts

Azure Container Apps environment
  platform metrics ---------------------> Azure Monitor Metrics
  console/system logs ------------------> Log Analytics workspace
                                                   ^
                                                   |
                                  Azure Monitor data source
                                                   |
                                                   v
                                           Grafana Cloud dashboards
                                      application + infrastructure + logs

Azure portal dashboards -------------------------------> fallback/admin view
```

The Azure Monitor data source is a query path, not an ingestion path:

1. Azure Container Apps emits platform metrics and routes logs through Azure Monitor.
2. Azure Monitor and Log Analytics retain the Azure-side data according to Azure policy.
3. Grafana Cloud authenticates to Azure using an Entra application identity.
4. Grafana panels execute Azure Monitor Metrics or Log Analytics queries.
5. Grafana Cloud combines those results with the existing application metrics in shared dashboards.

No application ingress, Container Apps sidecar, or application instrumentation change is required for this integration.

## Data-source design

### Existing application metrics datasource

Retain the existing Grafana Cloud Prometheus-compatible datasource for TransitJazz application metrics. It remains the home for:

- worker heartbeat and liveness metrics;
- transit input freshness;
- cycle duration and error counters;
- tones, crossings, and other application outcomes;
- publish health;
- application-level alerts.

No Azure infrastructure metrics should be pushed through this application metrics path. Keeping the sources separate preserves ownership, avoids duplicate telemetry, and makes query failures easier to diagnose.

### New Azure Monitor datasource

Create a Grafana Cloud datasource named, for example, `azure-monitor-transitjazz` with these capabilities enabled:

- Azure Monitor Metrics for Container Apps environment and app resource metrics.
- Azure Monitor Logs for the existing Log Analytics workspace.
- Azure annotations for deployments or other important Azure events, where useful.

Grafana's Azure Monitor datasource is built into Grafana and supports both Azure metrics and logs. Grafana Cloud cannot use Azure Managed Identity or Workload Identity directly, so the datasource must use Entra App Registration authentication.

If the application console-log table uses the Basic Logs plan, enable the datasource's Basic Logs support. Complex or large Log Analytics queries may need a datasource timeout of approximately 300 seconds instead of the default 30 seconds; queries should still be bounded so a long timeout is an exception rather than normal behavior.

### Source ownership

| Signal | Primary source | Grafana Cloud use |
|---|---|---|
| Application behavior and business outcomes | Existing Grafana Cloud metrics | Panels and alert rules |
| Container CPU, memory, network, replicas, revisions | Azure Monitor Metrics | Infrastructure panels and alerts |
| Container restarts and platform events | Azure Monitor Metrics and system logs | Availability and deployment diagnosis |
| Structured worker failures and recovery events | Log Analytics console logs | Incident correlation and evidence |
| Azure administrative/deployment events | Azure Monitor annotations or activity data | Timeline context |

## Authentication and authorization

### Recommended identity

Create a dedicated Entra App Registration for Grafana Cloud's read-only Azure Monitor access. Prefer a client certificate if the organization's secret-management policy supports it; otherwise use a short-lived/rotated client secret.

The credential is entered only into Grafana Cloud's secure datasource fields. It must not appear in:

- this design document;
- dashboard JSON;
- provisioning files;
- GitHub Actions logs;
- Container Apps settings;
- screenshots or incident messages.

Grafana Cloud is an external service, so Azure Managed Identity is not the correct authentication mechanism for this connection.

### Proposed RBAC scope

Start with the smallest scope that permits datasource discovery and querying:

| Scope | Role | Purpose |
|---|---|---|
| Target Container Apps resource group, or individual resources if supported cleanly | `Reader` | Enumerate resources and read Azure Monitor metric metadata |
| Target Log Analytics workspace | `Log Analytics Reader` | Query workspace tables and log data |

Grafana's configuration guidance commonly describes `Reader` access at subscription/resource scope. The implementation must test whether resource-group scope is sufficient for the selected Grafana Cloud datasource and dashboard queries. Broaden to subscription-level `Reader` only if enumeration requires it and the security owner approves it.

The identity must not receive `Contributor`, `Owner`, `Monitoring Contributor`, `Log Analytics Contributor`, or permissions to edit diagnostic settings, alerts, workspaces, or dashboards.

### Authorization decision gate

Before creating production dashboards, prove all of the following with the dedicated identity:

1. The datasource can discover the target subscription or resource group.
2. Azure Monitor Metrics returns data for the Container Apps environment and app.
3. Log Analytics returns a bounded query result from the intended workspace.
4. No write operation is possible with the assigned roles.
5. The credential can be rotated without editing dashboards.

Record the tenant ID, subscription ID, resource group, workspace ID, app registration ID, and role scopes in the deployment runbook. Do not record the credential value.

## Dashboard organization

Use Grafana Cloud folders and dashboards that reflect how an incident is investigated.

### Recommended dashboards

| Dashboard | Datasources | Purpose |
|---|---|---|
| `TransitJazz / Executive Overview` | Grafana Cloud + Azure Monitor | One-screen service health and top infrastructure signals |
| `TransitJazz / Application` | Grafana Cloud | Existing application metrics, freshness, work, quality, and liveness |
| `TransitJazz / Azure Platform` | Azure Monitor | Container Apps CPU, memory, network, replicas, revisions, requests, and restarts |
| `TransitJazz / Incident Correlation` | Grafana Cloud + Azure Monitor Logs | Metrics beside structured worker events and platform evidence |

### Standard variables

Use variables only where they reduce repeated dashboard editing:

- environment;
- resource group;
- Container App;
- revision;
- city;
- time range.

The default dashboard time zone should be UTC. Display the selected revision and time window prominently so a user does not compare an application metric from one revision with logs from another unintentionally.

### Azure platform panels

Start with the metrics already represented in the Azure Container Apps dashboards, then validate each metric ID in the Azure Monitor query editor rather than assuming a name is available in every environment. Candidate panels include:

- CPU usage / `UsageNanoCores`;
- memory working set / `WorkingSetBytes`;
- received bytes / `RxBytes`;
- transmitted bytes / `TxBytes`;
- request rate and response health where exposed for the app;
- replica count and revision distribution;
- restart count or restart events;
- revision-level availability.

Use Azure dimensions such as replica and revision when the investigation requires them. Default overview panels should aggregate by app or revision and avoid rendering one series per ephemeral replica unless a variable explicitly requests that detail.

### Cross-source correlation

A useful incident path is:

```text
Grafana Cloud alert or metric anomaly
        |
        +--> identify UTC window, app, revision, and city
        |
        +--> inspect Azure CPU/memory/replicas/restarts
        |
        +--> query structured console events in Log Analytics
        |
        +--> follow Azure portal link for platform remediation
```

Use these correlation fields whenever available:

- Container Apps revision;
- `City`;
- `CycleId`;
- `EventId`;
- `ReasonCode`;
- `DeploymentRevision`.

Grafana panels use one datasource per query, so the combined dashboard is a collection of coordinated panels rather than a single query joining Grafana Cloud and Azure data. The correlation is performed through shared labels, variables, timestamps, and panel links.

## Log Analytics query design

The application structured-log design targets standard Container Apps console and system tables. Older or differently configured environments may expose custom tables such as `ContainerAppConsoleLogs_CL` and `ContainerAppSystemLogs_CL`, while the Azure Monitor destination may expose resource tables such as `ContainerAppConsoleLogs` and `ContainerAppSystemLogs`.

The actual table name and column shape must be verified from a redacted production row before KQL is made a dashboard contract. Do not assume that a field called `Log` or a particular suffix exists across every historical route.

The following is a template, not a production query until that verification is complete:

```kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-08-30T12:00:00Z) .. datetime(2026-08-30T12:15:00Z))
| extend Event = parse_json(Log)
| project TimeGenerated,
          EventName = tostring(Event.EventName),
          EventId = tostring(Event.EventId),
          CycleId = tostring(Event.CycleId),
          City = tostring(Event.City),
          ReasonCode = tostring(Event.ReasonCode),
          DeploymentRevision = tostring(Event.DeploymentRevision)
| where City == 'atlanta'
| take 100
```

Query rules:

- Always use a finite UTC time range.
- Start from exactly one intended table.
- Project only needed columns.
- Limit result rows for dashboard tables.
- Avoid broad `search`, cross-table joins, and expensive exploratory queries on Basic Logs.
- Use a dashboard variable or explicit resource filter for the target app.
- Keep the query compatible with the selected Log Analytics table plan.

## Dashboard migration from Azure

The first Azure platform dashboard should be imported from the Azure portal where possible:

1. Open the Container App or Container Apps environment in the Azure portal.
2. Open **Monitoring > Dashboards with Grafana**.
3. Customize the dashboard as needed.
4. Use **Export > JSON**.
5. Import the JSON into the Grafana Cloud stack.
6. Map the imported panels to the `azure-monitor-transitjazz` datasource.
7. Replace Azure portal-specific resource references, datasource UIDs, variables, and subscription/resource IDs.
8. Add the existing TransitJazz application panels and links to the related Azure resources.
9. Save the resulting Grafana Cloud dashboard with a stable UID.

Imported JSON is a starting point, not the final source of truth. Review every panel for:

- correct datasource UID;
- correct subscription, resource group, app, and workspace;
- correct aggregation and dimensions;
- correct time zone;
- acceptable query cost and timeout;
- meaningful empty/no-data behavior;
- links to the current resource and runbook.

If dashboard-as-code is adopted, commit sanitized dashboard JSON and provisioning metadata to the repository. Never commit the app registration secret or certificate private key.

## Alerting ownership

Grafana Cloud should own the unified operational alerting experience once Azure datasource access is validated. This permits one contact-point policy and avoids forcing operators to watch two alerting systems for the same service.

### Application alerts

Keep application alerts on the existing Grafana Cloud metrics, including:

- worker heartbeat stopped;
- stale or missing transit input;
- cycle failures;
- publish failures;
- abnormal processing duration;
- missing tones or other application-defined anomalies.

### Azure infrastructure alerts

Add Azure Monitor-backed Grafana rules for signals such as:

- sustained high CPU;
- high memory working set;
- unexpected replica or revision changes;
- repeated container restarts;
- unhealthy or unavailable revision;
- abnormal network or request behavior.

The exact thresholds should be based on observed baseline data rather than copied blindly from the portal dashboard.

### Duplicate-alert policy

Do not page twice for the same condition. During migration:

1. Keep existing Azure alerts and Grafana alerts visible but route the new Grafana rules to a test contact point.
2. Trigger a controlled canary condition.
3. Confirm the alert identity, labels, notification, recovery, and deduplication behavior.
4. Choose one paging owner for each condition.
5. Retain Azure alerts only as an explicit fallback where required by the platform or compliance policy.

Azure portal-hosted dashboards are useful for visualization but are not the desired home for Grafana-managed cross-platform alerting. Grafana Cloud should become the alerting home for the unified service view.

## Operational workflows

### Normal investigation

1. Start at `TransitJazz / Executive Overview`.
2. Identify whether the symptom is application-only, Azure-only, or correlated.
3. Open the application or Azure Platform dashboard with the same UTC range.
4. Filter by app, revision, city, or replica.
5. Open the Incident Correlation dashboard and inspect structured events.
6. Follow the panel link to Azure when remediation requires revision, replica, networking, or deployment operations.

### Datasource failure

If panels show no data:

- check Grafana Cloud datasource **Save & Test**;
- verify tenant, subscription, resource group, and workspace identifiers;
- verify App Registration expiration and role assignments;
- run a small Azure metrics query;
- run a bounded Log Analytics query;
- check whether the table is Basic and whether Basic Logs support is enabled;
- check Azure ingestion delay and the datasource timeout;
- use the Azure portal dashboard to determine whether the problem is Grafana access or Azure data availability.

### Azure fallback

The Azure portal remains the fallback for:

- changing Container Apps revisions or secrets;
- inspecting deployment state;
- editing diagnostic settings;
- checking Azure-native activity and resource health;
- investigating Grafana authentication or datasource failures.

Grafana Cloud is intentionally read-only with respect to Azure infrastructure.

## Security and cost controls

### Security

- Use a dedicated read-only Entra identity.
- Scope roles to the required resource group/resources and workspace.
- Store secrets only in Grafana Cloud secure datasource fields.
- Rotate credentials and record expiry ownership.
- Do not place tokens, URLs with secrets, headers, request bodies, or connection strings in dashboards or logs.
- Use separate identities for production and non-production where practical.
- Review exported dashboard JSON before committing it.

### Cost

Grafana Cloud queries Azure data but does not create a second copy of that data. Azure-side Log Analytics ingestion, retention, and query-scan charges remain applicable.

Control cost by:

- using finite dashboard time ranges;
- projecting only necessary columns;
- limiting log rows;
- avoiding refresh intervals shorter than the operational need;
- avoiding cross-table and unrestricted queries on Basic Logs;
- keeping high-cardinality replica detail behind an explicit variable;
- monitoring query latency and Azure workspace ingestion after rollout.

The Azure Monitor datasource should be configured for a practical timeout, but a timeout increase must not become an excuse for unbounded KQL.

## Rollout plan

### Phase 0: inventory and approval

- Record the Azure tenant, subscription, resource group, Container Apps app/environment, and Log Analytics workspace IDs.
- Confirm the current Grafana Cloud stack and datasource ownership.
- Confirm the Azure table names, plans, retention, and actual log schema from a redacted sample.
- Obtain approval for the App Registration and least-privilege RBAC.
- Confirm the Grafana Cloud plan supports the needed Azure datasource and alerting features.

### Phase 1: datasource proof

- Create the dedicated Entra App Registration.
- Assign only the approved read roles.
- Add the Azure Monitor datasource to Grafana Cloud.
- Validate metrics and logs with small, bounded queries.
- Validate Basic Logs configuration if applicable.

### Phase 2: dashboard migration

- Export the Azure Container Apps dashboard JSON.
- Import it into Grafana Cloud.
- Remap datasource UIDs and resource variables.
- Verify each panel against the Azure portal and Azure Monitor query editor.
- Add links and consistent naming.

### Phase 3: unified dashboards

- Add application panels to the overview.
- Add the Incident Correlation dashboard.
- Add revision, city, and time-range variables.
- Add Azure deployment annotations where they improve incident timelines.
- Store sanitized dashboard definitions in source control if dashboard-as-code is selected.

### Phase 4: alert canary

- Create Grafana Cloud infrastructure rules with a test contact point.
- Exercise one Azure condition and one application condition.
- Verify notification, labels, recovery, no-data behavior, and deduplication.
- Select the single paging owner for each condition.

### Phase 5: steady state

- Use Grafana Cloud for daily dashboards and cross-platform alerts.
- Keep Azure portal dashboards available for fallback and administration.
- Review Azure query latency, workspace ingestion, and dashboard usefulness after the first week.
- Reassess RBAC and credential expiry on a regular schedule.

## Validation checklist

Record the following in the implementation runbook; leave secrets out of the record.

- [ ] Tenant ID recorded.
- [ ] Subscription ID recorded.
- [ ] Resource group and Container Apps resource IDs recorded.
- [ ] Log Analytics workspace ID recorded.
- [ ] Grafana Cloud Azure Monitor datasource created.
- [ ] Datasource Save & Test succeeds with the dedicated identity.
- [ ] Azure Metrics query returns current Container Apps data.
- [ ] Azure Logs query returns a bounded result.
- [ ] Actual table names, plans, and JSON log columns verified.
- [ ] Basic Logs support enabled and tested where required.
- [ ] Exported Azure dashboard imported successfully.
- [ ] Every imported panel has the correct datasource and resource scope.
- [ ] Application and Azure panels correlate over the same UTC window.
- [ ] Revision, city, cycle, and event identifiers are usable for investigation.
- [ ] Grafana alert canary delivers one deduplicated notification.
- [ ] Recovery notification is verified.
- [ ] Azure fallback dashboard remains accessible.
- [ ] No secret appears in dashboard JSON, provisioning, logs, or screenshots.
- [ ] Query latency and Azure-side scan/ingestion behavior are acceptable.

## Risks and open decisions

| Risk or question | Mitigation / decision gate |
|---|---|
| Resource-group `Reader` scope may not satisfy datasource discovery | Test least privilege first; broaden only with approval |
| Azure log table schema differs between legacy and Azure Monitor routes | Verify a real redacted row before finalizing KQL |
| Basic Logs may reject a desired query shape | Keep queries table-scoped and simple; use Analytics only if the operational requirement is proven |
| Imported portal dashboard JSON requires remapping | Treat import as a starting point and validate every panel |
| Azure ingestion delay makes recent logs appear absent | Display ingestion expectations and use a recent-but-not-immediate test window |
| Grafana Cloud or stack plan lacks a required feature | Confirm plan capabilities during Phase 0 |
| Azure and Grafana alerts page for the same outage | Run a canary and explicitly assign alert ownership |
| Credential expiry interrupts dashboards | Assign an owner, record expiry, and test rotation |
| High-cardinality replica panels increase query cost/noise | Aggregate by app/revision by default; expose replica detail only on demand |

## Decision summary

1. Grafana Cloud is the unified operational UI.
2. Azure Monitor and Log Analytics remain the Azure-side source of truth.
3. Use Grafana Cloud's built-in Azure Monitor datasource rather than copying infrastructure data into Grafana Cloud.
4. Authenticate with a dedicated Entra App Registration; do not rely on Managed Identity from Grafana Cloud.
5. Keep the existing application metrics datasource unchanged.
6. Import Azure Container Apps dashboards as a starting point and remap them to the new datasource.
7. Centralize cross-platform alerting in Grafana Cloud after a controlled validation.
8. Retain the Azure portal dashboard as the administration and emergency fallback surface.

## References

- [Azure Container Apps: Azure Monitor dashboards with Grafana](https://learn.microsoft.com/en-us/azure/container-apps/grafana-dashboards)
- [Azure Container Apps observability](https://learn.microsoft.com/en-us/azure/container-apps/observability)
- [Azure Container Apps log monitoring](https://learn.microsoft.com/en-us/azure/container-apps/log-monitoring)
- [Microsoft: Visualize Azure Monitor data with Grafana](https://learn.microsoft.com/en-us/azure/azure-monitor/visualize/visualize-grafana-overview)
- [Microsoft: Use Azure Monitor dashboards with Grafana](https://learn.microsoft.com/en-us/azure/azure-monitor/visualize/visualize-use-grafana-dashboards)
- [Grafana: Configure the Azure Monitor data source](https://grafana.com/docs/grafana/latest/datasources/azure-monitor/configure/)
- [Grafana: Azure Monitor annotations](https://grafana.com/docs/grafana/latest/datasources/azure-monitor/annotations/)
