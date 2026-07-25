# Quickstart: Route Filter UI — Focus, Map Blur & Blurb

Manual verification of the focus → highlight/blur → blurb behavior. No automated client UI test harness
exists in this repo, so verification is by build + in-browser observation.

## Prerequisites

- Routes render on the map (existing behavior — the `route-layer-*` lines are visible).
- The ROUTES filter grid is populated (existing `RouteFilters` from #14).

## Build

```pwsh
dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/ChefKnifeStudios.MartaJazz.Client.WebApp.csproj
```

Run the app (Aspire AppHost or the WebApp directly) and open `/transit-map`.

## Verification steps

### 1. Map highlight (User Story 1 / FR-002)
- Hover a route input in the ROUTES grid.
- **Expect**: that route's line stays full-color/opacity and reads as dominant; happens within ~100ms.
- Move to a different route input.
- **Expect**: emphasis moves to the new route; previous returns to greyed; never two emphasized.

### 2. Map blur of non-selected (User Story 2 / FR-003, FR-004, FR-005)
- With a route focused, look at all other routes.
- **Expect**: every other line is greyed (`#9ca3af`) and low-opacity (~0.15).
- Unhover (move pointer off the grid) / tap outside on mobile.
- **Expect**: ALL routes snap back to normal color + opacity 0.85 immediately — no fade-out.
- Load the page fresh, focus nothing.
- **Expect**: all routes full appearance; none blurred by default.

### 3. Blurb bar + placeholder (User Story 3 / FR-006..FR-009)
- Focus any unauthored route.
- **Expect**: a full-width dark translucent bar slides/fades up from the bottom within ~100ms showing a
  placeholder that names the route (e.g. "Route 110 — tone and Atlanta story coming soon.").
- Move focus directly to another route.
- **Expect**: the bar stays open and its text swaps — no close-then-reopen flicker.
- Unfocus.
- **Expect**: the bar disappears instantly (no exit animation).

### 4. Consistency (FR-010 / SC-004)
- Sweep the pointer rapidly across many route inputs, then move off the grid entirely.
- **Expect**: UI settles on the last-focused route while sweeping; after leaving the grid, the map is
  fully un-blurred AND the bar is gone — nothing stuck.

### 5. Style-swap resilience (Principle VII edge case)
- (If the GIS basemap toggle is available) focus a route, then toggle the basemap.
- **Expect**: the focus highlight/blur on the route layers is preserved (data layers persist).

### 6. Localization seam (FR-011, English-only this feature)
- Confirm the placeholder text comes from `RouteFilterResources.resx` (grep: no hardcoded placeholder
  string literal in `RouteBlurbStore`/`RouteBlurbBar`).
- **Note**: Spanish is intentionally deferred; only the English resource + the `IStringLocalizer` seam
  are expected here.

## Done when

- All six sections behave as described.
- `dotnet build` of the WebApp succeeds with no new warnings introduced by this feature.
