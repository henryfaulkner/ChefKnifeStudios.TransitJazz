# Centralized logging release evidence

Every gate below requires dated, secret-free evidence. `BLOCKED` is an intentional state and must
not be changed to `PASS` by configuration inspection alone.

## Release identity

| Field | Value |
|---|---|
| Feature | 054 — Centralized Structured Logging |
| Environment | `PENDING` |
| Deployment revision | `PENDING` |
| Workspace alias/resource | `PENDING` |
| Intended reader principal | `PENDING` |
| Release owner/approver | `PENDING` |

## Carried prerequisites

The worker remains co-hosted in the existing Web API Container App topology. Feature 053's
constitutional/topology/artifact prerequisites are carried into this release and are not
self-approved by feature 054. Record the separately approved artifacts and intended reader before
deployment:

| Prerequisite | Status / evidence |
|---|---|
| Feature 053 topology/artifact approval | `PENDING` |
| Workspace-scoped `Log Analytics Reader` principal | `PENDING` |
| Existing Grafana metrics path unchanged | `PASS (local tests); controlled evidence PENDING` |
| Existing Blob/Parquet dual-run path preserved | `PASS (local configuration/source checks); controlled evidence PENDING` |

## FR-024 evidence matrix

| Gate | Required evidence | Result | Location / UTC |
|---|---|---|---|
| JSON shape and v1 fields | Contract tests plus captured console row | `PENDING` | `PENDING` |
| Redaction | Secret-bearing URL/header/connection-string/exception tests | `PENDING` | `PENDING` |
| Volume and coalescing | Normal cycle, ten failures, reminder, recovery | `PENDING` | `PENDING` |
| Routing | Azure Monitor destination and exact two categories | `PENDING` | `PENDING` |
| Table plans/retention | Console Basic 30 days; system Analytics 30 days | `PENDING` | `PENDING` |
| Read-only skill | Query, output, context, mutation refusal | `PENDING` | `PENDING` |
| `doctor` | First-failure matrix, no retries/credentials | `PENDING` | `PENDING` |
| Grafana correlation | City/time/panel context under five minutes | `PENDING` | `PENDING` |
| Removal guard | Consumer audit, historical blobs preserved | `PENDING` | `PENDING` |

## Routing canaries

| Marker | Timestamp UTC | City/revision | Query/evidence | Result |
|---|---|---|---|---|
| Safe pre-change canary | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Fresh post-change marker 1 | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Fresh post-change marker 2 | `PENDING` | `PENDING` | `PENDING` | `PENDING` |

Allow up to 90 minutes for diagnostic activation and several minutes for ingestion. Use log
streaming and Grafana during that interval; an immediate empty query is not route-failure proof.

## Dual-run record

Parquet writes and Blob access remain enabled until all rows below pass for seven consecutive days.

| Day/scenario | Central evidence | Legacy evidence | Result | Approver/notes |
|---|---|---|---|---|
| Day 1 safe event | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Input failure | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Publish failure | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Zero-tone anomaly | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Repeated failure/recovery | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Redaction | `PENDING` | `PENDING` | `PENDING` | `PENDING` |
| Day 7 retention/cost/query | `PENDING` | `PENDING` | `PENDING` | `PENDING` |

## Cutover guard

## Local implementation-run evidence

| Check | Result | Notes |
|---|---|---|
| Worker tests | `PASS` | 129 passed on 2026-08-30 |
| Web API tests | `PASS` | 121 passed on 2026-08-30 |
| Query guard tests | `PASS` | Direct PowerShell suite passed on 2026-08-30, including Basic `/search` body-file and cleanup regression |
| Bicep build / ARM regeneration | `BLOCKED` | Azure CLI could not download the Bicep compiler in the restricted workspace; `bicep/main.json` was not hand-edited |
| Subscription validate / what-if | `NOT RUN` | Requires approved Azure identity, subscription, and reader principal |
| Controlled routing/canary/7-day evidence | `NOT RUN` | Requires controlled environment and release approval |
| Codex generated skill copy | `BLOCKED` | Repository `.agents/skills` is read-only in this workspace; Claude/OpenCode copies synchronized |

Current state: **BLOCKED — release evidence and approvals are not yet supplied.** Do not disable
`Logging:Telemetry`, delete resources, or remove Parquet consumers while any gate is pending.
