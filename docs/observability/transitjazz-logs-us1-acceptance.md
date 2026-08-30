# TransitJazz logs — US1 acceptance fixture

Status: `NOT RUN` in this checkout. Routing and live evidence require a controlled Azure
environment and human release approval.

## Controlled fixture

1. Select one non-production managed environment and record the deployment revision and UTC
   start time below.
2. Use a safe, synthetic zero-tone condition for one configured city. Do not alter feed
   credentials, diagnostic settings, retention, or application ingress as part of the fixture.
3. Capture the resulting `ContainerAppConsoleLogs.Log` value after diagnostic activation and
   ingestion delay. Redact secrets and attach only the reviewed row and query output.

| Field | Value |
|---|---|
| Environment | `PENDING` |
| City | `PENDING` |
| Deployment revision | `PENDING` |
| CycleId | `PENDING` |
| EventId | `PENDING` |
| UTC start/end | `PENDING` |
| Actual Log JSON shape captured | `PENDING` |
| Evidence owner/date | `PENDING` |

## City/time and CycleId flow

Run a bounded query from `skills/transitjazz-logs/references/kql-recipes.md`, replacing only the
finite UTC range and reviewed scalar projections. Confirm the result includes `CityCycleAnomaly`,
the expected reason code, count fields, publish state, `CycleId`, and deployment revision.

Record the effective workspace, table, range, exact KQL, limit, result count, and elapsed time:

```text
Workspace: PENDING
Table: PENDING
Range: PENDING
KQL: PENDING
Limit: PENDING
Result count: PENDING
Elapsed: PENDING
```

## Grafana-context flow

Start from the existing Grafana panel that shows the numeric symptom. Pass the copied panel link
to the `transitjazz-logs` skill. The skill must obtain the panel's effective city/time/metric
through the Grafana capability, then compose the same bounded table-first query. Explicit user
selectors override copied context, and the final response must show the effective context and
KQL.

| Check | Result |
|---|---|
| Panel context resolved read-only | `PENDING` |
| City/time matched the anomaly | `PENDING` |
| Central event correlated by CycleId/EventId | `PENDING` |
| Query completed within five minutes | `PENDING` |
| Empty-result explanation recorded if applicable | `PENDING` |

Until the actual `Log` shape is captured, the recipes remain draft and must not be promoted by
guessing the Azure JSON formatter shape.
