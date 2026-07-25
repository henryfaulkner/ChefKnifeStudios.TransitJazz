# Quickstart & Verification: Multi-Route Selection

**Feature**: 020-multi-route-select | **Date**: 2026-06-16

Frontend-only Blazor WASM change. No automated client-UI harness exists, so verification is
`dotnet build` + the manual scenarios below. Each scenario maps to spec acceptance criteria.

## Build

```powershell
dotnet build ChefKnifeStudios.TransitJazz.sln
```

Run the app via the existing AppHost / WebApp launch profile used for prior client features (same as
#15/#16/#17 verification). Open the transit map (now the index page) and let routes + live vehicles load.

## Touch points (where the changes live)

| File | Change |
|------|--------|
| `Client.Shared/ViewModels/RouteFilterViewModel.cs` | selection → persistent set; `SelectRoute` toggles; add `SelectAll`, `SelectedRouteIds`, `IsSingleSelection`; count rule + last-batch retention |
| `Client.Shared/Components/RouteFilters.razor(.cs)` | click/tap toggle (drop auto-clear on mouse-out); "Select all" + "Clear selections" buttons; de-emphasis by set membership |
| `Client.Shared/Components/RouteBlurbBar.razor.cs` | show blurb only when `IsSingleSelection` |
| `Client.Shared/Components/Map.razor.Helper.cs` | `FocusRoutesAsync(IEnumerable<string>)` |
| `Client.Shared/wwwroot/js/map-interop.js` | `ChefMap.focusRoutes(id, routeIds[])` |
| `Client.Shared/Resources/RouteFilterResources.resx` | `SelectAllRoutes`, `ClearSelections` (EN) |
| `WebApp/Pages/TransitMap.razor.cs` | multi-focus wiring + tone gate in `OnCrossingsAsync` |

## Manual verification scenarios

### 1. Persistent multi-selection (US1 / FR-001–003)
1. Select route A → it shows selected and **stays** selected after you move the pointer away.
2. Select B and C → all three show selected at once; on the map A, B, C are emphasized and every other
   route is blurred/greyed.
3. Act on B again → B deselects; A and C remain selected and emphasized.
✅ Pass: selections persist; toggling one leaves the others; map matches the set.

### 2. Bus count scoped to selection (US2 / FR-006–007)
1. With nothing selected, note the "# buses running" value = all running buses.
2. Select one route with known buses → count drops to that route's running buses.
3. Add a second route → count rises to the sum for the two selected routes.
4. Select a route you can see has no buses moving → count includes 0 from it (doesn't inflate).
5. Clear selection → count returns to the all-buses total.
✅ Pass: count always equals running buses on the selected set (all when empty); updates on selection change
*without* waiting for the next batch.

### 3. Tones scoped to selection (US3 / FR-008–009)
1. Ensure audio is enabled (settings blade) and a selection is empty → tones play for crossings on any route.
2. Select one route → only that route's vehicles produce tones at crossings; other routes are silent.
3. Mute audio in the settings blade → **no** tones regardless of selection.
4. Unmute, then clear selection → all routes audible again.
✅ Pass: selected-only tones when a selection is active; mute always wins; empty = all audible.

### 4. Select all / Clear selections (US4 / FR-010–012)
1. With a partial selection, click **"Select all"** → every route becomes selected (all emphasized; blurb
   hidden because >1; count = system total).
2. Click **"Clear selections"** → nothing selected; map all-equal; count = all buses; all routes audible;
   no blurb.
3. Before routes finish loading, click either button → no error (safe no-op).
✅ Pass: bulk select/clear work and clear returns to the unscoped default.

### 5. Blurb only for a single selection (US5 / FR-004–005)
1. Select exactly one route → blurb bar appears (authored copy or placeholder) for that route.
2. Select a second route → blurb disappears.
3. Deselect back to one → blurb reappears for the remaining route, updating in place without flicker.
4. Clear to zero → no blurb.
✅ Pass: blurb visible only in the exactly-one case.

### 6. Basemap swap preserves selection (edge case / Principle VII)
1. Select two routes.
2. Toggle the GIS "Street map" setting in the settings blade.
3. After the basemap swaps, the same two routes are still selected and still emphasized (others blurred);
   count and tone scope unchanged.
✅ Pass: selection + map blur survive the style swap (re-applied after `style.load`).

### 7. Rapid toggling (edge case / FR-014, SC-006)
1. Quickly toggle several routes on and off.
2. Stop. The map blur, the bus count, the tone scope, and the blurb visibility all match the **final**
   selection set — no route stuck blurred, no orphaned blurb.
✅ Pass: last-write-wins consistency.

## Localization check (Principle XII)
- "Select all" and "Clear selections" labels render from `RouteFilterResources.resx` (no inline copy).
  Spanish `.es` is deferred (consistent with 015/016/017); EN-only is expected for this slice.

## Done = all of:
- `dotnet build` succeeds.
- Scenarios 1–7 pass.
- New button labels come from the resx; no hardcoded UI copy introduced.
- No server/worker/shared files changed.
