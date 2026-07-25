# Phase 0 Research: Multi-Route Selection

**Feature**: 020-multi-route-select | **Date**: 2026-06-16

This feature is an evolution of existing, shipped code. "Research" here is mostly *reconnaissance of the
current implementation* to confirm where each behavior lives and what the minimal, idiomatic change is.
There were no open `NEEDS CLARIFICATION` markers in the spec (the one product decision — empty-selection
semantics — was resolved in the spec's Clarifications and reconfirmed by the user).

## Decision 1 — Selection model: persistent set on `IRouteFilterViewModel`

**Decision**: Change the selection from "at most one `RouteItem.IsSelected`" to "a persistent set of
selected routes", kept as in-memory state on the existing singleton `RouteFilterViewModel`. Acting on a
route **toggles** its `IsSelected`. Add derived members: `SelectedRouteIds` (the set),
`IsSingleSelection` (exactly one), and keep `SelectedRouteId` as a single-selection convenience (the route
when exactly one is selected, else null). `HasSelection` stays (set is non-empty).

**Rationale**: Every downstream consumer — the grid (`RouteFilters`), the map page (`TransitMap`), the bus
count (`BusesRunningLabel`), and the blurb (`RouteBlurbBar`) — already subscribes to this VM's
`PropertyChanged` and reads `RouteItems` / `HasSelection` / `SelectedRouteId`. Keeping the set on this VM
means **no new infrastructure**: the existing notification plumbing already fans selection changes out to
all four consumers. The current `SelectRoute` already rebuilds the `RouteItems` collection (reassigning the
property so `PropertyChanged` fires) — toggling is the same mechanic with `IsSelected = !current` for the
acted route and unchanged for the rest.

**Alternatives considered**:
- *A separate `SelectionService`* — rejected; redundant with the VM that already is the selection owner and
  already broadcasts changes. Would split the source of truth.
- *Persisting selection to local storage* (like Settings) — rejected; selection is a transient exploration
  tool, not a preference. The prior model didn't persist; matching that keeps scope minimal. (Easy to add
  later if desired.)

## Decision 2 — Interaction trigger: persistent toggle, not transient hover

**Decision**: On web, acting on a route input toggles its membership (no auto-clear on `@onmouseout`). The
current `RouteFilters.HandleMouseOut → ClearSelection()` line is removed; `HandleMouseOver` (or a click
handler) becomes a toggle. On mobile, tap toggles. The "deselect immediately on unhover" rule from
Principle IX is intentionally dropped (see plan Complexity Tracking).

**Rationale**: The user needs the selection to persist while they move the pointer to read the map / blurb /
count. Hover-to-toggle on `mouseover` is workable but error-prone (sweeping the pointer toggles many); a
**click/tap toggle** is the clearer interaction for a *persistent* set. Recommendation: bind the toggle to
`@onclick` (web click = mobile tap, one handler) and drop the mouseover/mouseout handlers. This is a small
behavioral choice surfaced in the contract; either works, click is preferred for persistence.

**Alternatives considered**:
- *Keep `mouseover` as the toggle trigger* — rejected as the primary because hover-toggle makes accidental
  multi-toggles likely when the pointer crosses the grid; persistence makes those mistakes sticky.
- *Hover to preview + click to commit* — rejected as over-scoped for this slice; not requested.

## Decision 3 — Bus count rule: count only selected routes (empty = all)

**Decision**: In `RouteFilterViewModel.OnNotificationReceived`, instead of summing **all**
`VehiclePositionBatchEvent` records, count only records whose route is in `SelectedRouteIds` **when the set
is non-empty**; when empty, sum all (current behavior). Because the count must also update when the
*selection* changes (not just when a new batch arrives), the VM must **retain the most recent batch's
per-route running counts** (or the last batch) and recompute `ActiveBusCount` both on batch arrival and on
selection change.

**Rationale**: FR-006/FR-007 require the count to track selection changes, but batches arrive on the
SignalR cadence. Without caching the last batch, changing the selection between batches would not update the
number. Storing a small `routeId → runningCount` snapshot from the last batch (or the last batch itself) is
the minimal way to recompute on demand. The batch payload (`VehiclePositionBatchEvent.BatchRecords`) carries
the route per record (via the vehicle's trip route), which is the join key.

**Open implementation detail (resolved in contract)**: confirm the route identifier on a
`VehiclePositionBatchEvent` record matches the `RouteItem.RouteId` (= `route_short_name`) used by the
selection set, so the membership test is a direct string compare (Principle VI — join on
`route_short_name`). The contract specifies normalizing on `route_short_name`.

**Alternatives considered**:
- *Recompute only on batch arrival* — rejected; violates FR-007 (count wouldn't move when the user changes
  selection between batches).
- *Ask the server for a scoped count* — rejected; frontend-only constraint and unnecessary (the client
  already receives all records).

## Decision 4 — Tone scoping: gate at `OnCrossingsAsync` (subordinate to mute)

**Decision**: In `TransitMap.OnCrossingsAsync`, after the existing `if (!_audioEnabled) return;` mute gate,
add a per-crossing check: if `SelectedRouteIds` is **non-empty** and the crossing's `RouteId` is **not** in
it, skip that crossing (no tone). When the set is empty, all crossings sound (current behavior). The
selected set is read from the injected `RouteFilterViewModel`.

**Rationale**: `OnCrossingsAsync` is already the single choke point where crossings become tones
(`TransitSynth.TriggerNoteAsync(crossing.RouteId, crossing.VehicleId)`), and it already enforces the mute
gate first. Adding the selection gate **after** the mute check makes mute strictly dominant (FR-009) and
keeps tone *generation* (Principle VIII) untouched — we only suppress emission for non-selected routes. The
crossing already carries `RouteId`, so membership is a direct compare.

**Alternatives considered**:
- *Mute non-selected routes inside the synth (per-route gain)* — rejected as heavier; the crossing-level
  skip is simpler, needs no synth API change, and is exactly subordinate to the existing mute.
- *Stop forwarding non-selected routes earlier (in the batch handler)* — rejected; the batch handler also
  feeds the map/animation, which must keep rendering all routes (only audio is scoped). Gating at the audio
  boundary keeps map and audio concerns separate.

## Decision 5 — Map: multi-route emphasis via a new `focusRoutes` interop

**Decision**: Add `ChefMap.focusRoutes(containerDivId, routeIds[])` mirroring the existing
`focusRoute`/`clearRouteFocus`: iterate every `route-layer-*`; if the layer's route is in the set, paint it
emphasized (full opacity, its own color); else blur/grey it (opacity 0.3, grey). Expose
`Map.FocusRoutesAsync(IEnumerable<string>)`. In `TransitMap.OnRouteFilterPropertyChanged`, call
`FocusRoutesAsync(SelectedRouteIds)` when non-empty, else `ClearRouteFocusAsync()`.

**Rationale**: The existing `focusRoute` already does the single-route version of exactly this
(emphasize-one / blur-rest) with `setPaintProperty` on the persistent `route-layer-*` GeoJSON layers. The
multi version is the same loop with a set-membership test instead of an equality test. Reusing the layer
naming (`route-layer-<routeId>`) and the stored `_routeColors` map means no new state. The treatment is
re-applied after a basemap `style.load` exactly as #17 re-renders routes (Principle VII upheld).

**Alternatives considered**:
- *Call `focusRoute` repeatedly* — rejected; each call resets non-target layers to blurred, so the last
  call would blur all previously-emphasized routes. A single set-aware pass is required.
- *Generalize `focusRoute` to take a list and remove the singular* — viable but larger blast radius; adding
  `focusRoutes` alongside keeps the existing single-route path intact for any other caller and is lower-risk.

## Decision 6 — Blurb visibility: exactly-one selection only

**Decision**: `RouteBlurbBar` shows its blurb only when `RouteFilterViewModel.IsSingleSelection` is true
(exactly one route selected), using that route's id for `RouteBlurbStore.GetForRoute`. For zero or
two-plus selected, `_blurb = null` (hidden). The existing in-place update on single→single change is
preserved.

**Rationale**: FR-004 makes the blurb a single-route detail view. The current code already keys off
`SelectedRouteId`; the only change is to source visibility from `IsSingleSelection` rather than
`HasSelection`, so a multi-selection hides the bar. Minimal and localized to `RouteBlurbBar.razor.cs`.

**Alternatives considered**:
- *Show a multi-route summary blurb* — rejected; out of scope and not requested (the user said "blurb only
  shows when a single route is selected").

## Cross-cutting: empty selection = no filter (reconfirmed)

All four scoped behaviors (map blur, bus count, tones, blurb) treat an **empty** selection as **unscoped**:
map shows all routes at full appearance, count shows all running buses, all routes sound, blurb hidden. This
was confirmed by the user. It guarantees clearing the selection can never silence the app or zero the count —
the safe default and the one product rule worth pinning.

## Localization

Two new EN strings in `RouteFilterResources.resx`: `SelectAllRoutes` ("Select all") and `ClearSelections`
("Clear selections"). Spanish `.es` deferred, consistent with 015/016/017 (Principle XII partial, tracked).
No other user-facing copy introduced.

## Build / verification posture

No client UI test harness exists in the repo; verification is `dotnet build` + manual quickstart steps
(select multiple, observe count/tones/map/blurb, Select-all, Clear). A VM-level unit test of the count rule
is *possible* but there is no client test project today, so it stays manual unless one is added.
