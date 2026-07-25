# Phase 0 Research: Route Filter UI — Focus, Map Blur & Blurb

All technical-context items resolved against the existing codebase. No external/library research was
needed — the stack (Blazor WASM, MapLibre, CommunityToolkit.Mvvm) is already in use; the questions were
all "how does this repo do X."

## Decision 1: How focus state reaches the map and the blurb bar

**Decision**: Both `TransitMap` (map driver) and the new `RouteBlurbBar` subscribe to the existing
`IRouteFilterViewModel.PropertyChanged` and react to `RouteItems` / `HasSelection` changes — the exact
pattern `RouteFilters.razor.cs` already uses. The VM stays the single source of truth for the focused
route; no new shared state object is introduced.

**Rationale**: `RouteFilterViewModel` already tracks single-focus selection and raises
`PropertyChanged` for `RouteItems`/`HasSelection`. `RouteFilters` mutates it on hover/tap
(`SelectRoute`/`ClearSelection`). Reusing the same observable surface means the grid, map, and blurb
can never disagree about which route is focused (satisfies FR-010 by construction).

**Codebase facts that make this work**:
- `RouteFilterViewModel` is registered `Scoped`; `ApplicationViewModel` is `Singleton`. In Blazor WASM
  the app is a single scope, so the scoped VM is effectively one shared instance — the same object the
  grid uses is the one `TransitMap`/`RouteBlurbBar` will inject. (Verified in `Program.cs:62`.)
- `RouteItems` exposes `IsSelected` per item and `HasSelection`; the focused `RouteId` is
  `RouteItems.FirstOrDefault(x => x.IsSelected)?.RouteId`.

**Alternatives considered**:
- *New `IFocusStateService` singleton*: rejected — duplicates state that already lives in the VM, and
  would require the grid to publish into it, adding a sync hazard for no benefit.
- *Cascading parameter / EventCallback from RouteFilters up to TransitMap*: rejected — `RouteFilters`
  and the `Map` are siblings under `TransitMap`, not parent/child; the shared VM is the natural channel.

**Minimal VM touch**: add a read-only convenience `string? SelectedRouteId => RouteItems.FirstOrDefault(x => x.IsSelected)?.RouteId;`
to `IRouteFilterViewModel` so consumers don't re-derive it. No behavior change.

## Decision 2: How to highlight one route and blur the rest on the map

**Decision**: Add two methods to `window.ChefMap` that iterate the existing per-route line layers and
set MapLibre paint properties imperatively:
- `focusRoute(containerDivId, routeId)` — for each `route-layer-*`: the focused layer keeps full
  opacity and its own color; every other layer is set to a low `line-opacity` (~0.15) and a grey
  `line-color` (~`#9ca3af`). A CSS blur is not applicable to canvas-rendered lines, so "blur" is
  realized as the greyed + low-opacity treatment the constitution's table equates with de-emphasis.
- `clearRouteFocus(containerDivId)` — restore every `route-layer-*` to its original color and the
  default `line-opacity` (0.85, matching `addRouteShapeFeature`).

**Rationale**: Routes are already individual layers `route-layer-<routeId>` with `line-color` and
`line-opacity` set at creation (`map-interop.js:247-253`). `map.setPaintProperty(layerId, prop, value)`
is the standard MapLibre way to change appearance without touching sources — geometry is never
re-fetched, and because we only touch the data layers, a basemap/style swap leaves focus intact
(Principle VII; edge case "map style swap while focused").

**Restoring original color**: the per-route color must be known at restore time. `focusRoute` reads
each layer's current `line-color` via `getPaintProperty` **before** greying and stashes it on a JS-side
map keyed by layerId, OR — simpler and authoritative — the route's color is already carried in the
layer's source feature `properties.color` (`map-interop.js:237`), so `clearRouteFocus` reads it back
from `getSource(...).serialize()`/feature properties. **Chosen**: stash-on-focus in a module variable
`ChefMap._preFocusColors`, cleared on `clearRouteFocus`. Lowest risk, no source introspection.

**Layer-discovery**: enumerate `map.getStyle().layers` and match `id` starting with `route-layer-`
(same prefix convention `clearRouteShape` already relies on, `map-interop.js:159`). The focused layer
id is `route-layer-<routeId>`.

**Edge case — focused route has no layer**: if `route-layer-<routeId>` is absent, `focusRoute` still
greys all the others (the no-op is only on the *missing* highlight target); `clearRouteFocus` restores
everything. No throw (guard each `getLayer` call). Matches the spec edge case.

**Alternatives considered**:
- *Single combined route source with a `focused` feature-state + data-driven paint expression*: cleaner
  long-term but would require re-architecting how routes are added (today: one source+layer per route).
  Rejected as out-of-scope churn for this slice.
- *CSS filter blur on the whole map canvas*: rejected — would blur the focused route and the basemap too.

## Decision 3: Blurb data store and placeholder fallback

**Decision**: A static, in-memory store in the shared RCL: `RouteBlurb` record + `IRouteBlurbStore`
with `RouteBlurb GetForRoute(string routeId)`. Authored entries live in a hardcoded dictionary
(`RouteBlurbStore`); a lookup miss returns a placeholder `RouteBlurb` whose text is pulled from
`IStringLocalizer` and formatted with the route id. Registered as a singleton in `Program.cs`.

**Rationale**: The spec scopes authored prose **out** and requires only the placeholder + the store
shape. A static dictionary matches the constitution's "hand-authored, stored in a static client data
file" description and the established 011 neighborhood-focus pattern. Starting empty (zero authored
entries) is valid — every route falls back to the placeholder, satisfying FR-007/SC-003.

**Placeholder content**: `string.Format(localizer["RouteBlurbPlaceholder"], routeId)` →
e.g. *"Route 110 — tone description and Atlanta fun fact coming soon."* The route id guarantees the
placeholder "identifies the focused route" (FR-007).

**Alternatives considered**:
- *JSON file fetched at runtime*: rejected — adds an async load + HTTP dependency for static content
  that is more naturally a compiled C# data file; the existing route data is already C#-side.
- *Storing blurbs in the VM*: rejected — keeps presentation data out of the focus-state VM; the store
  is independently testable and swappable.

## Decision 4: Localization infrastructure (English-only now)

**Decision**: Add `builder.Services.AddLocalization()` in `Program.cs` and a single
`Resources/RouteFilterResources.resx` (English) consumed via `IStringLocalizer<RouteFilterResources>`.
**No Spanish `.resx` and no culture switcher in this feature** (deferred — see plan Complexity
Tracking). Components inject the localizer and read keyed strings; the placeholder and any blurb-bar
chrome text (e.g. an aria-label) come from here.

**Rationale**: The user directed: stand up minimal `.resx` now, English only, defer Spanish. This
honors Principle XII's "no hardcoded copy where a resource is feasible" while leaving a clean seam:
dropping in `RouteFilterResources.es.resx` later requires no code change. `Microsoft.Extensions.Localization`
is the standard Blazor WASM approach and adds no new heavyweight dependency.

**Alternatives considered**:
- *Hardcode the placeholder*: rejected — violates XII and leaves no seam.
- *Full app-wide EN/ES localization now*: rejected by the user as scope creep against the core slice.

## Resolved unknowns summary

| Unknown | Resolution |
|---------|-----------|
| Channel from grid focus → map/blurb | Shared scoped `RouteFilterViewModel` + `PropertyChanged` (Decision 1) |
| Map highlight/blur mechanism | `setPaintProperty` over `route-layer-*` layers via new `ChefMap.focusRoute/clearRouteFocus` (Decision 2) |
| Restoring original route colors | Stash pre-focus colors in a JS module variable (Decision 2) |
| Blurb storage + placeholder | Static `IRouteBlurbStore` w/ placeholder fallback (Decision 3) |
| Localization for FR-011 | `AddLocalization()` + English-only `.resx`, Spanish deferred (Decision 4) |
| Testing approach | Manual quickstart verification (no client UI test harness in repo) |
