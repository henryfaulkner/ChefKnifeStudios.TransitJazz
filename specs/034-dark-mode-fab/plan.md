# 034 — Plan

## Principle: reuse the inert plumbing

The theme swap, dark palette, dark URLs, and basemap re-render path all exist.
The diff adds one bool, one FAB, and generalizes two style-key lines from
`Light*` to a `{shade}{on}` 2×2 lookup. Frontend-only; no server/worker/shared
changes.

## The 2×2 style key (used in TWO places, identical logic)

Config already holds `MapTiler:StyleUrls:{Light,Dark}{On,Off}`. Both the
toggle handler and the initial-load path resolve the key the same way:

```csharp
var settings = SettingsService.GetSettings();
var shade = settings.IsDarkModeEnabled ? "Dark" : "Light";
var on    = settings.IsStreetMapEnabled ? "On" : "Off";
var key   = $"MapTiler:StyleUrls:{shade}{on}";
var url   = Configuration.GetValue<string>(key)
            ?? Configuration.GetValue<string>("MapTiler:StyleUrl")
            ?? string.Empty;   // caller no-ops on empty (FR-006)
```

- **Toggle path** — `TransitMap.razor.cs:292` (`GisSettingChangedEventArgs`
  handler). Currently `LightOn/LightOff` only. Replace with the block above.
- **Initial-load path** — `Map.razor.cs:66` (`GetMapSettings`, `[JSInvokable]`).
  Currently `LightOn/LightOff` only. Replace with the same block.

Both already inject `SettingsService` + `Configuration`. No new injection.

> ponytail: two call sites, not a shared helper — a private static in one file
> can't be reached from the other without a new type for a 4-line string
> concat. Duplicate the block; extract only if a third caller appears.

## Event flow

DarkModeFab posts **both**:
1. `ThemeChangedEventArgs { IsDarkMode }` → `MainLayout.HandleThemeChange`
   swaps `MatTheme` (already wired, `MainLayout.razor:48`).
2. `GisSettingChangedEventArgs { IsStreetMapEnabled = <current> }` → reuses the
   existing `TransitMap` handler (`:287`) that runs `SetBasemapStyleAsync` +
   re-render-from-cache. The handler now resolves shade from settings, so
   re-posting the *current* streetmap value is enough to trigger a restyle at
   the new shade.

No new event type. No new interop. The re-render machinery (routes from cache,
checkpoint/trail/bus visibility restore) runs unchanged.

## CSS

- Component CSS reads MDC theme vars → flows from swapped `MatTheme`. No change.
- New `DarkModeFab.razor.css` (copy the FAB container pattern), `right:74px`.
- Two one-line shifts: `MapStyleFab` `74→124`, `InfoFab` `174→224`.
- `variables.css` consolidation onto MDC vars is DEFERRED to visual QA — the
  light `:root` set still applies under both themes today; only the body
  background looks off in dark. If QA flags it, migrate `var(--background)`/
  `var(--on-background)` consumers to `--mdc-theme-background`/
  `--mdc-theme-on-surface` and delete the light-only duplicates. Not blocking.

## Localization

Add resx key `SettingDarkMode` (EN) to `RouteFilterResources.resx`, matching
the `SettingStreetMap` convention. Used for the FAB aria/label.

## Files touched

| File | Change |
|---|---|
| `Models/Settings.cs` | + `_isDarkModeEnabled` bool, bump `CurrentVersion` 2→3 |
| `Components/FABs/DarkModeFab.razor` (new) | copy of `MapStyleFab`, posts 2 events |
| `Components/FABs/DarkModeFab.razor.css` (new) | container, `right:74px` |
| `Components/FABs/MapStyleFab.razor.css` | `right:74→124` |
| `Components/FABs/InfoFab.razor.css` | `right:174→224` |
| `Layout/MainLayout.razor` | register `<DarkModeFab />` |
| `Pages/TransitMap.razor.cs` | generalize style-key (toggle path) |
| `Components/Map.razor.cs` | generalize style-key (initial-load path) |
| `Resources/RouteFilterResources.resx` | + `SettingDarkMode` |

## Risks

- Bumping `Settings.CurrentVersion` discards old serialized settings (by
  design — see the ponytail comment in the model). Acceptable: settings reseed
  to defaults on next load.
- `DarkTheme` palette (`ColorConstants.Dark`) is complete but never rendered —
  first real use may surface contrast issues in the 4 hardcoded greys. Covered
  by the visual-QA task, not pre-emptively themed.
