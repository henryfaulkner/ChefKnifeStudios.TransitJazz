---
name: transitjazz-logs
description: Investigate TransitJazz worker anomalies through bounded, read-only Azure Monitor Log Analytics queries. Use when an operator needs centralized worker logs, a copied Azure Logs or Grafana context, event/cycle/city evidence, or access and ingestion diagnosis.
---

# TransitJazz centralized logs

This skill is query-only. It may discover the approved workspace/table, run a bounded query, fetch
event evidence, and run `doctor`. It must refuse Azure or observability mutations before making a
call, including routing, diagnostic settings, table plans, retention, RBAC, alerts, workbooks,
saved queries, and credentials.

## Request handling

1. Accept explicit workspace/table, finite UTC range, selectors, copied Azure Logs link, event or
   cycle ID, city, revision, event name, reason, output (`table` or requested `json`), and limit.
2. Resolve context in this order: explicit input, Azure Logs link, Grafana panel context through
   `$grafana`, then the approved workspace default. Do not reimplement Grafana link parsing.
3. Use the registered official Azure Monitor read-only interface when available. If it cannot query
   Basic console rows, use only the fixed project helper described in [query-helper.md](references/query-helper.md).
4. Never ask for, accept, print, persist, or inspect a token, key, header, connection string,
   credential file, or environment dump. Use the caller's short-lived identity.

## Query and output rules

- Allow only `ContainerAppConsoleLogs` or `ContainerAppSystemLogs` and an approved workspace.
- Require a finite UTC `TimeGenerated` range, one source table, useful `project`, and `take` 1–100.
- Basic console queries may filter, parse JSON, extend scalar fields, project, aggregate, and take.
  Refuse `join`, `union`, `find`, KQL `search`, `externaldata`, user functions, cross-resource
  queries, unbounded ranges, unknown tables, and oversized limits.
- Preserve conforming user KQL byte-for-byte; reject or refine only when the user requests refinement.
- Always show effective workspace, table, UTC range, KQL, limit, and output format. Present a concise
  table by default; return JSON only on request. Sanitize returned strings before display.
- A zero-row result is not proof of absence. Explain ingestion delay, activation, retention, level or
  filtering, rate limiting, and selector mismatch possibilities.

## Investigation and doctor

Prefer `EventId`, then `CycleId` plus revision, then city and a narrow time range. For Grafana input,
obtain the panel's effective city/time/metric through `$grafana` and compose the resulting context.
On failure, run `doctor` once, stop at its first failing layer, provide a secret-free next action,
and never retry persistent permission/configuration failures or repair the environment. See
[event-contract.md](references/event-contract.md), [kql-recipes.md](references/kql-recipes.md), and
[doctor.md](references/doctor.md).

