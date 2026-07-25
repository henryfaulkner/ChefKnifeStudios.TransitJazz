# Contract: Selected-Routes Bus Count Rule

**Feature**: 020-multi-route-select | Surface: `RouteFilterViewModel.OnNotificationReceived` + recompute on
selection change | Consumed by: `BusesRunningLabel` (binds `ActiveBusCount`, unchanged)

## Rule

```
ActiveBusCount =
    HasSelection
        ? count of currently-running buses whose route ∈ SelectedRouteIds
        : count of all currently-running buses        // empty selection = unscoped
```

"Currently-running buses" = the running-vehicle records from the **most recent**
`VehiclePositionBatchEvent` batch. Route identity per record uses `route_short_name` (Principle VI), matching
`RouteItem.RouteId` / `SelectedRouteIds`.

## Recompute triggers (BOTH required — FR-007)

1. **New batch arrives** (`OnNotificationReceived`): rebuild the per-route running snapshot from the batch,
   then apply the rule.
2. **Selection changes** (`SelectRoute` / `SelectAll` / `ClearSelection`): re-apply the rule against the
   **retained** last-batch snapshot — no new batch needed.

To support (2) without a fresh batch, the VM MUST retain the last batch's data. Minimal form: a
`Dictionary<string,int>` of `routeId → runningCount` (or `IReadOnlyCollection` of the last batch records).
Recompute `ActiveBusCount` from this snapshot + `SelectedRouteIds` on every selection mutation.

## Behavior table

| Selection | Last batch (route:count) | ActiveBusCount |
|-----------|--------------------------|----------------|
| {} | A:5, B:3, C:2 | 10 (all) |
| {A} | A:5, B:3, C:2 | 5 |
| {A,C} | A:5, B:3, C:2 | 7 |
| {B} | A:5, B:0, C:2 (B has none running) | 0 |
| {all} | A:5, B:3, C:2 | 10 (equals unscoped) |
| {D} where D not in batch | A:5, B:3, C:2 | 0 |

## Notification

- `ActiveBusCount` is an `[ObservableProperty]`; setting it fires `PropertyChanged(nameof(ActiveBusCount))`,
  which `BusesRunningLabel` already listens for. No label change required.
- Setting `ActiveBusCount` to the same value need not fire (toolkit no-ops equal sets), which is fine.

## Edge cases

- **Empty batch / no buses**: count is 0 in both scoped and unscoped modes.
- **Selected route absent from batch**: contributes 0.
- **Rapid selection changes**: each mutation recomputes against the same retained snapshot; the final
  selection's count wins (last-write).
- **`OnNotificationReceived` currently sets count only when `> 0`** — the new rule MUST allow the count to
  drop to 0 for a non-empty selection whose routes have no running buses (do not keep a stale prior count).
