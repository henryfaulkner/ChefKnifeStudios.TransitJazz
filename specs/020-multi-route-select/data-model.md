# Phase 1 Data Model: Multi-Route Selection

**Feature**: 020-multi-route-select | **Date**: 2026-06-16

No persisted storage, no backend schema, no shared-contract change. The "data model" here is the
**in-memory client state** on the existing `RouteFilterViewModel` (the single source of truth) and the
derived values every consumer reads.

## Entity: Route selection state (on `IRouteFilterViewModel`)

The selection is expressed as the `IsSelected` flag across the existing `RouteItem` collection plus derived
read-only members. It changes from single-valued to set-valued.

### `RouteItem` (existing — unchanged shape)

| Field | Type | Notes |
|-------|------|-------|
| `RouteId` | `string` | `route_short_name` (e.g. `"74"`). Join key everywhere (Principle VI). |
| `Color` | `string` | Hex; route's map color. |
| `IsSelected` | `bool` | **Now part of a SET** — any number of items may be `true` simultaneously. |

### Derived members on `IRouteFilterViewModel`

| Member | Type | Definition | Replaces / status |
|--------|------|-----------|-------------------|
| `RouteItems` | `IEnumerable<RouteItem>` | All routes with current `IsSelected`. | existing |
| `SelectedRouteIds` | `IReadOnlyCollection<string>` | `RouteItems.Where(IsSelected).Select(RouteId)`. **NEW.** Empty = unscoped. | new |
| `HasSelection` | `bool` | `SelectedRouteIds.Count > 0`. | existing (semantics now "set non-empty") |
| `IsSingleSelection` | `bool` | `SelectedRouteIds.Count == 1`. **NEW.** Gates the blurb. | new |
| `SelectedRouteId` | `string?` | The single selected route when `IsSingleSelection`, else `null`. | existing (now "the one when exactly one") |
| `ActiveBusCount` | `int` | Selected-routes running count when `HasSelection`, else all running buses. | existing (rule changes) |

### Operations

| Operation | Before | After |
|-----------|--------|-------|
| `SelectRoute(RouteItem)` | sets exactly that one `IsSelected = true`, all others false | **toggles** that route's `IsSelected`, others unchanged |
| `ClearSelection()` | all `IsSelected = false` | all `IsSelected = false` (unchanged — now also means "return to unscoped") |
| `SelectAll()` | — | **NEW** — set every `IsSelected = true` |

All mutations reassign the `RouteItems` property (new list instances) so `PropertyChanged(RouteItems)` fires
and `[NotifyPropertyChangedFor]` cascades to `HasSelection` (extend to also notify `IsSingleSelection` /
`SelectedRouteIds` consumers).

### Validation / rules

- **R1 (cardinality):** the selection set may hold 0..N routes (N = number of routes in the grid). No upper
  bound beyond available routes.
- **R2 (toggle):** acting on a selected route removes it; acting on an unselected route adds it.
- **R3 (persistence-in-session):** selection survives unrelated interactions and basemap swaps; it is NOT
  cleared by ending a hover/tap. It is NOT persisted across page reloads (in-memory only).
- **R4 (incremental load):** when `BuildRouteItems` rebuilds the list after routes load, an existing
  selection MUST be preserved by id; newly added routes default to `IsSelected = false` (never auto-selected,
  except by a subsequent `SelectAll`).
- **R5 (empty = unscoped):** when `SelectedRouteIds` is empty, all scoped behaviors revert to their
  all-routes default.

## Entity: Selected-routes bus count

| Aspect | Detail |
|--------|--------|
| Value | `ActiveBusCount` (int) |
| Rule | `HasSelection ? (running buses whose route ∈ SelectedRouteIds) : (all running buses)` |
| Inputs | last `VehiclePositionBatchEvent` batch (per-route running counts) + current `SelectedRouteIds` |
| Recompute triggers | (a) new vehicle batch arrives; (b) selection set changes |
| State needed | the VM must **retain the last batch's per-route running counts** so (b) can recompute without a new batch (see contract `bus-count-rule.md`) |
| Edge | selected routes with zero running buses → 0 (not the system total). |

## Entity: Tone-scope gate (no stored state)

A pure predicate applied per crossing at emission time. Not stored.

| Aspect | Detail |
|--------|--------|
| Predicate | `_audioEnabled && (SelectedRouteIds.Count == 0 || SelectedRouteIds.Contains(crossing.RouteId))` |
| Order | mute (`_audioEnabled`) checked first → strictly dominant (FR-009) |
| Empty set | predicate reduces to `_audioEnabled` → all routes sound (unscoped) |

## Entity: Map focus treatment (no stored state on .NET side)

Driven entirely from `SelectedRouteIds`. The JS side keys off the persistent `route-layer-<routeId>` layers
and the existing `_routeColors` lookup.

| Selection | Map call | Effect |
|-----------|----------|--------|
| non-empty set | `Map.FocusRoutesAsync(SelectedRouteIds)` | each selected `route-layer-*` emphasized (full opacity, own color); all others opacity 0.3, grey |
| empty set | `Map.ClearRouteFocusAsync()` | all routes restored to default appearance |

State transition is idempotent and last-write-wins: after a burst of toggles, the final
`SelectedRouteIds` determines the single resulting map call.

## Relationships (single source of truth fan-out)

```
                         ┌────────────────────────────┐
                         │   RouteFilterViewModel      │
                         │   (singleton, in-memory)    │
                         │   SelectedRouteIds (SET)    │
                         └────────────┬───────────────┘
            PropertyChanged           │
   ┌──────────────┬───────────────────┼───────────────────┬──────────────────┐
   ▼              ▼                    ▼                   ▼                  ▼
RouteFilters  BusesRunningLabel   RouteBlurbBar       TransitMap          TransitMap
(grid select  (ActiveBusCount =   (show iff           (FocusRoutesAsync   (OnCrossingsAsync
 + buttons +   selected-scoped)    IsSingleSelection)  / ClearRouteFocus)  tone gate)
 de-emphasis)
```
