# Phase 1 Data Model: Route Filter UI — Focus, Map Blur & Blurb

This feature is presentation-only; the "data" is client-side focus state (already owned by an existing
VM) plus a static blurb store. No backend, DB, or shared-contract changes.

## Entity: Route focus state (existing — reused, not redefined)

Owned by `RouteFilterViewModel` (`Client.Shared/ViewModels/RouteFilterViewModel.cs`). This feature
**consumes** it and adds one read-only convenience member.

| Field | Type | Notes |
|-------|------|-------|
| `RouteItems` | `IEnumerable<RouteItem>` | One per renderable route; existing `[ObservableProperty]` |
| `RouteItem.RouteId` | `string` | Route short name, e.g. `"110"` — the map layer key |
| `RouteItem.Color` | `string` | Hex, e.g. `"#0078D4"` — matches the map line color |
| `RouteItem.IsSelected` | `bool` | True for at most one item (single-focus invariant) |
| `HasSelection` | `bool` | True iff some item `IsSelected` (existing) |
| `SelectedRouteId` | `string?` | **NEW** convenience: `RouteItems.FirstOrDefault(x => x.IsSelected)?.RouteId` |

**Invariant**: at most one `RouteItem.IsSelected == true` at any instant (enforced today by
`SelectRoute` rebuilding the list with a single match). This feature does not weaken it.

**State transitions** (driven by the existing grid, observed by map + blurb):

```
            hover/tap route R           hover/tap route S (R≠S)
 [none] ───────────────────────► [focused: R] ──────────────────────► [focused: S]
   ▲                                   │                                     │
   └──────── unhover / tap-outside ────┴──────── unhover / tap-outside ──────┘
                                   (ClearSelection → [none])
```

On every transition, `PropertyChanged(RouteItems)` (and `HasSelection` when crossing the none↔focused
boundary) fires; subscribers recompute their reaction from `SelectedRouteId`.

## Entity: RouteBlurb (NEW — static content)

`Client.Shared/Data/RouteBlurb.cs`. Immutable presentation record surfaced in the bottom bar.

| Field | Type | Notes |
|-------|------|-------|
| `RouteId` | `string` | Key; route short name |
| `ToneDescription` | `string` | Text-only tone line, e.g. `"Instrument: FM Synth · Key: D minor."` |
| `Significance` | `string` | Atlanta significance / fun fact prose |
| `IsPlaceholder` | `bool` | True when this is the fallback (no authored entry) |

```csharp
public sealed record RouteBlurb(
    string RouteId,
    string ToneDescription,
    string Significance,
    bool IsPlaceholder = false);
```

**Validation / rules**:
- A `RouteBlurb` returned from the store is never null and never has empty display text — the
  placeholder guarantees non-empty, route-identifying content (FR-007, SC-003).
- Authored entries set `IsPlaceholder = false`; the fallback sets it `true` (lets the UI style/aria the
  placeholder distinctly if desired).

## Entity: IRouteBlurbStore (NEW — lookup)

`Client.Shared/Data/RouteBlurbStore.cs`. Singleton.

| Member | Signature | Behavior |
|--------|-----------|----------|
| `GetForRoute` | `RouteBlurb GetForRoute(string routeId)` | Returns the authored entry if present; otherwise a placeholder `RouteBlurb` built from `IStringLocalizer["RouteBlurbPlaceholder"]` formatted with `routeId`, `IsPlaceholder = true` |

- The authored dictionary MAY be empty at ship time (every route → placeholder); that is a valid,
  expected state, not a defect.
- Lookup is by `RouteId` exact match (ordinal), mirroring the route-shape cache key convention.

## Localization resource (NEW)

`Client.Shared/Resources/RouteFilterResources.resx` (English). Consumed via
`IStringLocalizer<RouteFilterResources>`.

| Key | English value (example) | Used by |
|-----|------------------------|---------|
| `RouteBlurbPlaceholder` | `"Route {0} — tone and Atlanta story coming soon."` | `RouteBlurbStore` placeholder |
| `RouteBlurbBarAriaLabel` | `"Route information"` | `RouteBlurbBar` accessibility |

Spanish (`.es.resx`) is intentionally **not** added in this feature (deferred).

## Relationships

```
RouteFilterViewModel (focus state, existing scoped singleton)
        │  PropertyChanged(RouteItems / HasSelection)
        ├──────────────► TransitMap ──► Map.FocusRouteAsync(SelectedRouteId) / ClearRouteFocusAsync()
        │                                     │
        │                                     └──► window.ChefMap.focusRoute / clearRouteFocus (paint props)
        │
        └──────────────► RouteBlurbBar ──► IRouteBlurbStore.GetForRoute(SelectedRouteId) → RouteBlurb
                                                  (placeholder if no authored entry)
```
