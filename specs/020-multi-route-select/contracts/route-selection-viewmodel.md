# Contract: `IRouteFilterViewModel` Multi-Select

**Feature**: 020-multi-route-select | Surface: `Client.Shared/ViewModels/RouteFilterViewModel.cs`

The selection source of truth. This contract defines the members consumers may rely on and the behavior of
the mutators. Backward-compatible additions; two existing methods change behavior.

## Interface (additions in **bold**)

```csharp
public interface IRouteFilterViewModel : IViewModel, IDisposable
{
    IEnumerable<RouteItem> RouteItems { get; }

    void SelectRoute(RouteItem routeItem);     // CHANGED: now TOGGLES membership
    void ClearSelection();                     // unchanged: empties the set
    void SelectAll();                          // NEW: selects every route

    bool HasSelection { get; }                 // set non-empty
    bool IsSingleSelection { get; }            // NEW: exactly one selected
    string? SelectedRouteId { get; }           // the one route iff IsSingleSelection, else null
    IReadOnlyCollection<string> SelectedRouteIds { get; }  // NEW: the selected set
    int ActiveBusCount { get; }                // CHANGED rule: see bus-count-rule.md
}
```

## Behavioral contract

| Method | Precondition | Postcondition |
|--------|--------------|---------------|
| `SelectRoute(item)` | item ∈ RouteItems | item.IsSelected flips; all other items unchanged; `RouteItems` reassigned → `PropertyChanged` fires |
| `SelectAll()` | — | every item.IsSelected = true; if already all-selected, still fires once (idempotent state, may no-op the notification) |
| `ClearSelection()` | — | every item.IsSelected = false; selection empty (unscoped) |

## Notification contract

- Mutators MUST reassign `RouteItems` (new `RouteItem` instances) so `PropertyChanged(nameof(RouteItems))`
  fires — mutating items in place does not notify (existing comment in the VM documents this).
- `HasSelection`, `IsSingleSelection`, `SelectedRouteId`, and `SelectedRouteIds` are derived; they MUST
  appear consistent immediately after any mutator returns. Existing consumers subscribe to
  `nameof(RouteItems)` and/or `nameof(HasSelection)`; the VM SHOULD continue to notify those names so no
  consumer needs to change its subscription filter (add `IsSingleSelection` to the notify-for set if a
  consumer subscribes to it directly).

## Invariants

- **INV-1**: `SelectedRouteIds == RouteItems.Where(x => x.IsSelected).Select(x => x.RouteId)` at all times.
- **INV-2**: `IsSingleSelection == (SelectedRouteIds.Count == 1)`;
  `SelectedRouteId == (IsSingleSelection ? the one id : null)`.
- **INV-3**: `HasSelection == (SelectedRouteIds.Count > 0)`.
- **INV-4 (incremental load)**: after `BuildRouteItems()` rebuilds the list (routes loaded later), routes
  that were selected and still exist remain selected; new routes are unselected.

## Acceptance vectors

| # | Start | Action | SelectedRouteIds | HasSelection | IsSingleSelection | SelectedRouteId |
|---|-------|--------|------------------|--------------|-------------------|-----------------|
| 1 | {} | SelectRoute(A) | {A} | true | true | A |
| 2 | {A} | SelectRoute(B) | {A,B} | true | false | null |
| 3 | {A,B} | SelectRoute(A) | {B} | true | true | B |
| 4 | {A,B} | ClearSelection() | {} | false | false | null |
| 5 | {B} | SelectAll() | {all} | true | false* | null* |
| 6 | {} (no routes loaded) | SelectAll() | {} | false | false | null |
| 7 | {A} then routes reload incl. new C | BuildRouteItems() | {A} | true | true | A |

\* unless the grid contains exactly one route total.
