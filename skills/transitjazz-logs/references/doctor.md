# `doctor` first-failure workflow

Run each layer once, in order, and stop at the first persistent failure. Return the layer, status,
secret-free next action, and effective context; never return credentials or retry a permission or
configuration failure.

| Order | Layer | Check | First-failure action |
|---:|---|---|---|
| 1 | Interface | Registered Azure Monitor read-only capability or fixed helper is available | Install/register the approved read-only interface through normal ops ownership; do not mutate from this skill |
| 2 | Identity | Caller identity can be acquired without pasted credentials | Sign in through the normal short-lived identity flow |
| 3 | Workspace | Approved TransitJazz workspace resolves | Verify the approved alias/resource ID with the workspace owner |
| 4 | Query permission | Workspace query is authorized | Request workspace-scoped `Log Analytics Reader`; do not change RBAC here |
| 5 | Table/plan | Selected table exists and Basic compatibility is known | Record `BasicQueryUnsupported` and use the reviewed Analytics console fallback if necessary |
| 6 | Ingestion freshness | Recent safe marker is within the allowed delay | Check routing activation/ingestion timing and log streaming; do not change diagnostics |
| 7 | Minimal query | One table, finite UTC range, projection, and 1–100 limit execute | Correct the bounded request or preserve the failure for the owner |
| 8 | Empty result | Query succeeded but matched no row | Explain retention, delay, level/filter, selector, or wrong-table possibilities |

The result is diagnostic evidence, not a repair plan. `BasicQueryUnsupported` is distinct from an
empty result and must not revive Parquet or create another datastore.

