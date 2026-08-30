# Centralized logging release evidence

Every gate below requires dated, secret-free evidence. `BLOCKED` is an intentional state and must
not be changed to `PASS` by configuration inspection alone.

> ## Superseded 2026-08-30 — retired, not passed
>
> Feature 055 removed the Parquet sidecar and its infrastructure. The release owner
> **waived** the seven-day dual-run window rather than completing it, and **deleted the
> Azure telemetry storage manually** rather than through the gated Bicep deployment.
>
> **The rows below were never satisfied.** They are retained verbatim as a historical
> record of what this gate asked for and what was actually supplied. Do not read any
> `PENDING` row as outstanding work — the subject of every one of them no longer exists.
> See the 055 removal authorization at the end of this file.

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

**WAIVED 2026-08-30 by the release owner.** This window was never run — zero of seven days
were recorded. The table below is a historical record of the evidence that was planned, not
evidence that was collected. Parquet writes and Blob access ended with feature 055.

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
| Bicep build / ARM regeneration | `PASS` | Unblocked in the feature 055 workspace: Bicep CLI 0.43.8 available. `az bicep build` regenerated `bicep/main.json` from source on 2026-08-30; never hand-edited. Semantic delta vs. a baseline build is exactly the telemetry removal (1 module, 1 param, 2 outputs) |
| Subscription validate / what-if | `NOT RUN` | Requires approved Azure identity, subscription, and reader principal |
| Controlled routing/canary/7-day evidence | `NOT RUN` | Requires controlled environment and release approval |
| Codex generated skill copy | `PASS` | Not reproduced in the feature 055 workspace: the tracked `skills/` source and all three mirrors (`.claude`, `.agents`, `.opencode`) were carved together; `tools/sync-skills.ps1 -Mode Check` reports agreement |

Current state: **RETIRED — the Parquet path this gate protected no longer exists.**

The former guard ("do not disable `Logging:Telemetry`, delete resources, or remove Parquet
consumers while any gate is pending") is discharged: feature 055 removed every Parquet
consumer, the `Logging:Telemetry` configuration, and the storage account. It is retained
above only as a record of what was asked.

## 055 removal authorization

```
Evidence window:  NOT RUN — the seven-day dual-run window was WAIVED by the release owner
                  on 2026-08-30. Zero of seven days were recorded. Gates G1-G4 were never
                  satisfied; they were set aside.
Historical data decision: DISCARD (FR-020). All telemetry data, including the
                  batch_wire_bytes series carrying the feature 051 Phase 3 egress baseline,
                  was discarded with the storage account. No export was performed.
Infrastructure:   DELETED MANUALLY on 2026-08-30, outside the Bicep deployment. The IaC
                  (main.bicep, main.json, modules/telemetryStorage.bicep) was updated to
                  match the deleted state rather than to drive the deletion.
Authorized by:    Release owner   Date: 2026-08-30 (UTC)
```

**What accepting this trades away.** The gate existed to prove, with controlled evidence,
that centralized logging can answer every question the Parquet store used to answer
(contract C6). That proof was not produced. If a diagnosis later turns out to need a signal
only the Parquet store carried, there is no fallback: the data is gone and the code paths
are deleted. The retained-observability contract's C1–C5 guarantees *were* verified locally
(302 tests pass, all eleven structured event names intact, zero alert rules affected); only
C6 — the end-to-end investigation-capability claim — rests on assertion rather than
evidence.

**Manual-deletion caveat.** Because the storage account was removed by hand, standard
post-deploy verification (T058/T058a — a clean cycle window and confirmed absence of the
`Storage Blob Data Contributor` role assignment on `serverIdentity`) was never performed
against a deployment. The next infrastructure deployment reconciles the IaC with reality; if
any telemetry resource or role assignment survived the manual deletion, that deployment is
where it will surface.
