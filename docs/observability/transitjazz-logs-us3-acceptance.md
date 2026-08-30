# TransitJazz logs — US3 acceptance fixture

Status: `NOT RUN` in this checkout. The cases below are executable only with approved Azure
workspace aliases and caller identity. They must leave Azure unchanged.

| Case | Expected evidence | Result |
|---|---|---|
| No explicit table | Approved default table selected and shown | `PENDING` |
| Explicit console table | `ContainerAppConsoleLogs` preserved | `PENDING` |
| Explicit system table | `ContainerAppSystemLogs` preserved | `PENDING` |
| Direct conforming KQL | KQL preserved byte-for-byte and shown | `PENDING` |
| Requested JSON output | Same effective context plus JSON rows | `PENDING` |
| Copied Azure Logs link | Link context resolved without credentials | `PENDING` |
| Explicit selectors plus copied context | Explicit input wins; final context shown | `PENDING` |
| Limit 1 | One-row bounded query | `PENDING` |
| Limit 100 | Maximum accepted bounded query | `PENDING` |
| Limit 101 | Rejected before Azure call | `PENDING` |
| Empty result | Not described as proof of absence; explanations shown | `PENDING` |

## Invocation evidence

For the constrained helper, use only the fixed invocation documented in
`skills/transitjazz-logs/references/query-helper.md`. The helper's workspace alias, table,
endpoint, method, resource audience, finite range, projection, and final `take` are all guarded.

```text
Environment: PENDING
Effective workspace alias: PENDING
Effective workspace ID: not recorded in this document
Table: PENDING
UTC range: PENDING
KQL: PENDING
Limit/output: PENDING
Rows: PENDING
First failure/next action: PENDING
```

No test result may include a token, key, authorization header, connection string, credential path,
raw URL query, or arbitrary exception text.
