# Contract: Azure Log Routing and Access

## Required deployed state

| Area | Required state |
|---|---|
| Container Apps environment | `appLogsConfiguration.destination = 'azure-monitor'`; no workspace customer-ID/shared-key flow remains. |
| Diagnostic setting | Created on the managed environment and routes exactly `ContainerAppConsoleLogs` and `ContainerAppSystemLogs` to the existing workspace. |
| Application table | `ContainerAppConsoleLogs`: Basic plan, 30-day total retention and Basic's fixed 30-day interactive query window. |
| Platform table | `ContainerAppSystemLogs`: Analytics plan, 30-day interactive and total retention. |
| Reader principal | Intended human/agent receives `Log Analytics Reader` at workspace scope only; this feature grants no Contributor, Monitoring Contributor, or administration role. |
| Legacy path | Blob storage, its identity access, and `Logging:Telemetry` remain enabled through seven-day dual run. |

The infrastructure source of truth is Bicep. `bicep/main.json` is generated from Bicep and must never be edited by hand. Resource-specific tables materialize after routing, so table-plan/retention configuration follows that creation and records Azure's plan-change limits in release evidence.

## Explicit exclusions

- No ingress change, public metrics endpoint, application-to-workspace SDK, workspace shared key, application connection string, collector, Event Hubs, second archive, or new datastore.
- No application-level diagnostic setting for these managed-environment log categories.
- No deletion of legacy `_CL` rows, Parquet blobs, telemetry storage, or storage account.

## Validation contract

Before production cutover, record Bicep build plus deployment `validate`/`what-if`, workspace-only reader assignment, current console-ingestion/category baseline, projected sparse-event volume, a timestamped safe pre-change canary, post-deployment routing/plans/retention, and two fresh standard-table markers.

Allow up to 90 minutes for diagnostic-setting activation and several minutes for ingestion before declaring a route failure; use log streaming and Grafana during that window. Legacy `_CL` table data remains read-only history and is not copied into the standard tables.
