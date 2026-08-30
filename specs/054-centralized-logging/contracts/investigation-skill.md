# Contract: `transitjazz-logs` Read-Only Investigation Skill

## Scope and ownership

The canonical source is `skills/transitjazz-logs/`, registered in `skills/_skill-sync/catalog.json` and rendered by `tools/sync-skills.ps1`. Generated agent copies are outputs, not authoring locations. The skill composes with `$grafana` for panel context and replaces the legacy Parquet-oriented `mj-data-explorer` only after cutover.

Allowed operations are workspace/table discovery, bounded KQL queries, event retrieval, and `doctor`. The skill must refuse, without making a call, changes to Azure resources, diagnostic settings, table plan/retention, role assignments, alerts, contact points, workbooks, saved queries, credentials, or any other observability administration.

## Request and precedence

| Input | Rules |
|---|---|
| Workspace | Approved TransitJazz alias/resource only. |
| Table | Exactly `ContainerAppConsoleLogs` or `ContainerAppSystemLogs`. |
| Range | Finite UTC start/end; modest recent range only when omitted. |
| Selectors | `EventId`, `CycleId`, `City`, `DeploymentRevision`, `EventName`, `ReasonCode`. |
| KQL | Preserve byte-for-byte when conforming; reject unsafe KQL or refine only on user request. |
| Limit | Integer `1..100`. |
| Output | `table` by default; `json` on request. |

Context precedence is **explicit user input > Azure Logs link > Grafana panel context > selected workspace default**. A Grafana link uses the existing `$grafana` workflow to obtain effective panel, city variables, metric, and time range; this skill does not reimplement dashboard parsing.

## Bounded query rules

Basic `ContainerAppConsoleLogs` queries must use exactly that one table, have a finite `TimeGenerated` UTC predicate, use only Basic-compatible filtering/parsing/scalar/projection/aggregation, project useful columns, and end with a 1-100 row bound. Reject `join`, `find`, KQL `search`, `externaldata`, functions, cross-resource/service queries, a second source table, unbounded range, unknown workspace/table, and oversized limits. System-table queries remain table/range/limit bounded. Recipes wait for the captured `Log` JSON shape.

## Interface selection and fallback

1. Discover and prefer a registered official Azure Monitor read-only interface; inspect only needed help when its contract is unknown.
2. Prove it queries Basic console logs before production cutover.
3. If it cannot, use only a project-owned helper fixed to read-only `POST /v1/workspaces/{workspaceId}/search`, approved workspace/table allow-lists, finite range, and result limit. It acquires caller identity normally and accepts no arbitrary endpoint, header, method, shell command, or credential.
4. If neither path supports Basic, report `BasicQueryUnsupported` and record the explicit Analytics-console fallback; never revive Parquet or add a datastore.

The skill never asks for, receives, prints, stores, or inspects a token, shared key, client secret, connection string, authorization/cookie header, credential file, or environment dump.

## Results and `doctor`

Every result displays effective workspace, table, UTC range, KQL, limit, and output format. Present a concise table by default and JSON only on request. Distinguish evidence from interpretation. An empty result must explain possible activation/ingestion delay, retention, rate limiting, level/filtering, or selector mismatch; it is not proof that no event occurred.

`doctor` runs once on failure and stops at the first failing layer: interface availability, short-lived identity, workspace resolution, query authorization, table existence/plan and Basic compatibility, ingestion freshness, minimal bounded query, or empty result. It returns a secret-free next action and never retries a persistent configuration/permission error or repairs the environment.
