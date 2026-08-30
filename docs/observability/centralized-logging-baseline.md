# Centralized logging baseline and cost record

This is the release-owned evidence record for feature 054. Replace each `PENDING` value only
with read-only evidence from the selected environment; never put credentials, tokens, headers, or
raw feed URLs in this document.

## Target

| Item | Required value | Evidence |
|---|---|---|
| Workspace | `PENDING` | Resource ID or approved alias only |
| Application table | `ContainerAppConsoleLogs` | Basic plan, 30-day total retention |
| Platform table | `ContainerAppSystemLogs` | Analytics plan, 30-day interactive/total retention |
| Managed environment destination | `azure-monitor` | Read-only resource inspection |
| Diagnostic categories | Exactly console and system | Diagnostic-setting resource inspection |
| Workspace reader principal | `PENDING` | Workspace-scoped `Log Analytics Reader` only |

## Pre-route console baseline

Record a finite UTC observation window, the current `_CL`/console ingestion count, top noisy
categories/messages, and a projected post-filter sparse-event volume. Message examples must be
redacted and bounded; do not paste raw exception text or URLs.

| Measurement | UTC window | Result | Evidence/query |
|---|---|---|---|
| Current console ingestion | `PENDING` | `PENDING` | `PENDING` |
| Noisy categories | `PENDING` | `PENDING` | `PENDING` |
| Expected v1 event volume | `PENDING` | `PENDING` | `PENDING` |
| Expected query scan volume | `PENDING` | `PENDING` | `PENDING` |
| Cost review / commitment tier | `PENDING` | No commitment tier selected | `PENDING` |

## Basic-query compatibility proof

The preferred registered Azure Monitor read-only interface must query one known safe row from
`ContainerAppConsoleLogs` with a finite UTC range before production routing. Record the actual
captured `Log` JSON shape only after redaction review. If the interface cannot query Basic, record
the failure and the separately reviewed Analytics fallback decision; do not create a second store.

| Proof | Result | Evidence |
|---|---|---|
| Preferred interface available | `PENDING` | `PENDING` |
| Basic console query succeeded | `PENDING` | `PENDING` |
| Captured `Log` shape reviewed | `PENDING` | `PENDING` |
| Fallback decision, if needed | `PENDING` | `PENDING` |

## Safety notes

- This file is evidence scaffolding, not proof of deployment or retention.
- Historical Parquet blobs remain preserved and are not copied or deleted by this feature.
- Grafana metrics remain the numeric source of truth; centralized logs explain discrete events.

