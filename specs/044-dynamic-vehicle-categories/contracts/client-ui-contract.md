# Contract: Client UI (filter panel, count label, map paint, resx)

How the client renders N dynamic categories. All copy through `IStringLocalizer<RouteFilterResources>` (Principle XII); all color-bearing CSS ships light **and** dark (Principle XIII).

## 1. Category display order (`CategoryOrder`)

- Source: `IRouteFilterViewModel.CategoryOrder` — built **once** in `BuildRouteItems`, never re-derived ad hoc in components.
- Algorithm: group route catalog by `Category`; each group's sort key = `min(RouteType)` over its routes; sort ascending; ordinal category-key tie-break (D8).
- Guarantees: TTC `{streetcar:0, rail:1, bus:3}` → `[streetcar, rail, bus]`; MARTA `{rail:0/1/2, bus:3}` → `[rail, bus]` (today's Rail-first order preserved, **no regression** — SC-005).
- Components (`RouteFilters`, `TransitRunningLabel`) **iterate `CategoryOrder`** and never try to reconstruct order from `RouteItems` (which carries per-route category but no ordering signal).

## 2. Filter panel (`RouteFilters.razor`)

- `@if`-pair (rail/bus) → single `@foreach (var category in CategoryOrder)`.
- Each category with routes renders one `<div class="route-filters__section" data-category="@category">` (generic wrapper class + attribute, D10) with:
  - clickable label `@Loc[category]` (resx miss → raw key, D11) wired to `HandleSelectAll(category)`;
  - a clear button when `HasSelectionFor(category)` → `HandleClearSelections(category)`;
  - a pills container `data-category="@category"` looping the routes where `r.Category == category`.
- `SelectAll` / `ClearSelection` / `HasSelectionFor` all retype `TransitMode` → `string category`. Persistent multi-selection semantics (Principle IX) unchanged.
- Empty category (no routes) not rendered.

## 3. Running-count label (`TransitRunningLabel.razor`)

- Two hardcoded rows → `@foreach (var category in CategoryOrder)`; skip rows where `ActiveCountsByCategory.GetValueOrDefault(category) == 0` (empty categories hidden).
- Each row: count element + `RunningNoun(category)` (D12):
  ```csharp
  string RunningNoun(string category) {
      var noun = Loc[$"RunningNoun_{category}"];
      return noun.ResourceNotFound
          ? string.Format(Loc["VehiclesRunningTemplate"], Loc[category])
          : noun.Value;
  }
  ```
- **Reactivity (the load-bearing trap):** broaden `OnViewModelPropertyChanged` from `nameof(ActiveBusCount) or nameof(ActiveRailCount)` to **`nameof(IRouteFilterViewModel.ActiveCountsByCategory)`** (and `CategoryOrder` if rows ever order by it). `ActiveCountsByCategory` MUST be an `[ObservableProperty]`-backed field **reassigned** to a fresh dict each recompute — a mutated-in-place dictionary never raises `PropertyChanged` → stale counts (FR-018, SC-008). This is the single most likely silent bug; call it out in review.

## 4. Localization keys (`RouteFilterResources.resx`, EN-only this change)

| Action | Key | Value |
|---|---|---|
| remove | `Rail`, `Buses`, `NumTrainsRunning`, `NumBusesRunning` | (old two-bucket keys) |
| add (labels) | `rail` | `Rail` |
| | `bus` | `Bus` |
| | `streetcar` | `Streetcar` |
| add (nouns) | `RunningNoun_rail` | `trains running` |
| | `RunningNoun_bus` | `buses running` |
| | `RunningNoun_streetcar` | `streetcars running` |
| add (fallback) | `VehiclesRunningTemplate` | `{0} running` |
| add (unknown) | `unknown` | `Unknown` |
| | `RunningNoun_unknown` | `unknown vehicles running` |

- Key casing = wire value casing (lowercase) so `Loc[category]` resolves directly.
- Pre-change count copy preserved verbatim (`RunningNoun_rail`/`_bus` carry the old `NumTrainsRunning`/`NumBusesRunning` values) — SC-002.
- **Do NOT touch** `SettingBusesVisible` (settings-blade resx key, unrelated).

## 5. Map paint (`map-interop.js`) + GeoJSON property (`vehicle-animator.js`)

- Rename GeoJSON property `transitMode` → `category` at all read (map-interop) and write (vehicle-animator) sites. **Grep for `transitMode`** — do not trust the design doc's line numbers.
- Re-key the two paint match expressions (radius + stroke), preferring the case-safe form:
  ```js
  'circle-radius':       ['match', ['downcase', ['get', 'category']], 'rail', 9, 6],
  'circle-stroke-width': ['match', ['downcase', ['get', 'category']], 'rail', 2, 1],
  ```
  Both the primary block and the `setStyle`-restore duplicate block must be updated (Principle VII — layers re-added after style swap).
- `vehicle-animator.js` fallback `rec.transitMode || 'bus'` → `rec.category || 'unknown'` (D6).
- `TransitMap.razor.cs`: `r.TransitMode.ToString().ToLowerInvariant()` → `r.Category`.
- **Behavior note (must be in the PR description):** this makes the rail size/stroke tier fire for the **first time** — rail dots render larger than bus/streetcar dots (SC-009, FR-017). It is a deliberate, visible change (fixes the latent capital-`'Rail'` mismatch), not a no-op re-key. Streetcar/bus dots stay the small tier (binary sizing; per-category sizing deferred).

## 6. Dark-mode parity (Principle XIII)

- The existing `route-filters__rail`/`__buses` and `--rail`/`--bus` icon color rules migrate to `.route-filters__section[data-category="…"]` / `.transit-running-label__icon[data-category="…"]` selectors, **preserving both light and dark renderings** in the same change.
- A neutral default `.route-filters__section` / `__icon` rule (both themes) covers any category without a bespoke color (D11).
- Dark values SHOULD come from `ColorConstants.Dark`, not ad-hoc hexes; PR review rejects new color-bearing CSS lacking a dark counterpart.
