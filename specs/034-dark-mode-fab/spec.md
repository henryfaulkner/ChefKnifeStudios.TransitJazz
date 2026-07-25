# 034 — Dark Mode FAB

## Summary

A bottom-right FAB toggles the app between light and dark mode. Toggling swaps
the whole MatBlazor theme (already wired but inert) and hot-swaps the MapTiler
basemap to a dark variant. The setting persists in the existing Settings blob
and is honored on first paint.

Most plumbing already exists and is unused: `DarkTheme`, the `MatTheme` dark
construction in `MainLayout.razor`, `ThemeChangedEventArgs`, its handler
`HandleThemeChange`, and the four `MapTiler:StyleUrls` (`LightOn/LightOff/
DarkOn/DarkOff`) in both appsettings. This feature is what fires them.

## User story

As a user on the transit map, I tap the dark-mode FAB and the entire UI —
chrome and basemap — switches to dark; tap again to return to light. My choice
is remembered on reload.

## Functional requirements

- **FR-001** A mini FAB in the bottom FAB row toggles dark mode. Icon:
  `dark_mode` when dark is active, `light_mode` when light.
- **FR-002** Toggling persists `IsDarkModeEnabled` via the existing
  `SettingsService` (one JSON blob, local storage). No new storage.
- **FR-003** Toggling ON posts `ThemeChangedEventArgs { IsDarkMode = true }`;
  `MainLayout.HandleThemeChange` swaps to `DarkTheme`. OFF reverses it.
- **FR-004** Toggling also swaps the MapTiler basemap to the correct 2×2 style
  (shade × streetmap-on/off), reusing the existing `SetBasemapStyleAsync`
  re-render path. Data layers (routes/checkpoints/trail/buses) are re-rendered
  from cache, NEVER re-fetched (Principle VII).
- **FR-005** On initial map load the persisted `IsDarkModeEnabled` selects the
  basemap style so the map paints the saved shade from first render.
- **FR-006** If a resolved style URL is missing/empty, fall back to the flat
  `MapTiler:StyleUrl`, then no-op — the map never blanks (mirrors 017 FR-013).
- **FR-007** FAB label copy via `IStringLocalizer<RouteFilterResources>`
  (EN only; `.es` deferred per 015/016).

## Position

Insert between the streetmap (MapStyle) FAB and the City FAB in the bottom row
(all `bottom:42px`). FABs are 50px apart; inserting shifts the two siblings
left of the new slot by +50px:

| FAB | right (before) | right (after) |
|---|---|---|
| City | 24px | 24px |
| **DarkMode (new)** | — | **74px** |
| MapStyle | 74px | 124px |
| Info | 174px | 224px |

## Out of scope

- OS `prefers-color-scheme` auto-detect — YAGNI.
- Dark variants of the 4 hardcoded neutral greys (`#888`, `#f3f4f6`, `#fff`)
  in component CSS — defer to visual QA; they read acceptably on both themes.
- Separate theme persistence — reuse the one Settings blob.
- Spanish localization — deferred per 015/016.

## Non-goals / clarifications

- "Adjust all existing CSS" is NOT required: components read MDC theme
  variables (which flow automatically from the swapped `MatTheme`) or
  hardcoded neutral greys. Only `variables.css` consumers (`app.css` body +
  a couple components using `var(--background)`/`var(--on-background)`) matter
  — consolidated onto MDC vars in the plan so there's no second palette to
  keep in sync.

## Acceptance

1. Tap FAB → chrome + basemap go dark; icon flips to `light_mode`.
2. Tap again → both revert; icon flips to `dark_mode`.
3. Reload with dark active → app paints dark from first render, map included.
4. Streetmap toggle still works in both shades (2×2 matrix resolves).
5. No route re-fetch on any toggle (verify no GTFS network call).
