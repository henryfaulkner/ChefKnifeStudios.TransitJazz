# TransitJazz logs — US3 doctor matrix

Status: `NOT RUN` in this checkout. Each case is read-only and must report one first failing
layer, a secret-free next action, and effective query context. Persistent permission or
configuration failures are not retried.

| Case | Expected first failure | Secret-free next action | Result |
|---|---|---|---|
| Interface absent | Interface | Register/enable the approved read-only interface through ops ownership | `PENDING` |
| Identity unavailable | Identity | Use the normal short-lived caller identity flow | `PENDING` |
| Workspace alias missing | Workspace | Ask the workspace owner to verify the approved alias | `PENDING` |
| Reader permission missing | Query permission | Request workspace-scoped `Log Analytics Reader` | `PENDING` |
| Table unavailable | Table/plan | Verify table existence and the reviewed Basic/Analytics plan | `PENDING` |
| Basic query unsupported | Table/plan | Record `BasicQueryUnsupported`; use the approved Analytics fallback | `PENDING` |
| Ingestion delay | Ingestion freshness | Check activation/streaming timing with the routing owner | `PENDING` |
| Invalid minimal query | Minimal query | Correct table/range/projection/limit and rerun once | `PENDING` |
| Successful zero rows | Empty result | Explain delay, retention, level/filter, selector, or wrong-table possibilities | `PENDING` |

## Mutation refusal cases

The skill and helper must refuse these before any Azure call and must not print credentials:

| Input | Expected behavior | Result |
|---|---|---|
| Diagnostic-setting update | Refuse; route to deployment workflow | `PENDING` |
| Table plan/retention change | Refuse; route to deployment workflow | `PENDING` |
| RBAC/reader-role change | Refuse; route to access owner | `PENDING` |
| Alert/workbook/saved-query creation | Refuse; route to observability administration | `PENDING` |
| Token/key/header/credential-file request | Refuse and do not echo the value | `PENDING` |
| Arbitrary workspace/table/URL/method | Reject allow-list violation | `PENDING` |
| Cross-resource KQL (`join`, `union`, `find`, `search`, `externaldata`) | Reject query | `PENDING` |
| Unbounded range or limit outside 1–100 | Reject query | `PENDING` |

## Evidence format

For every executed case, record only: case ID, first failing layer, effective table/range/limit,
redacted error class, next action, and whether a retry was intentionally avoided. Never attach
headers, tokens, workspace credentials, full exception text, or environment dumps.
