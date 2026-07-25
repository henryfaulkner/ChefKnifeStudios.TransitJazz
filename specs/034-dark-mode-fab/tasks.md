# 034 — Tasks

Dependency-ordered. Each task is a small, verifiable diff.

## T1 — Settings bool
- [ ] Add to `Models/Settings.cs`:
  ```csharp
  [ObservableProperty]
  [property: Description("SettingDarkMode")]
  bool _isDarkModeEnabled = false;
  ```
- [ ] Bump `CurrentVersion` 2 → 3.
- **Verify:** app builds; old settings reseed to defaults on load.

## T2 — Resx key
- [ ] Add `SettingDarkMode` (EN) to `Resources/RouteFilterResources.resx`,
      value e.g. "Dark mode".
- **Verify:** key resolves via `IStringLocalizer<RouteFilterResources>`.

## T3 — Generalize style-key (both call sites)
- [ ] `Pages/TransitMap.razor.cs` ~line 292 (toggle handler): replace the
      `LightOn/LightOff` ternary with the `{shade}{on}` block from plan.md.
- [ ] `Components/Map.razor.cs` ~line 66 (`GetMapSettings`): same replacement.
- **Verify:** with `IsDarkModeEnabled=true` set manually in storage, initial
  load paints a dark basemap; streetmap toggle still resolves all 4 URLs.
- **Depends on:** T1.

## T4 — DarkModeFab component
- [ ] Create `Components/FABs/DarkModeFab.razor` — copy `MapStyleFab.razor`:
  - Icon: `_settings.IsDarkModeEnabled ? "dark_mode" : "light_mode"`.
  - On click: flip `IsDarkModeEnabled`, persist, then post
    `ThemeChangedEventArgs { IsDarkMode = newValue }` AND
    `GisSettingChangedEventArgs { IsStreetMapEnabled = _settings.IsStreetMapEnabled }`.
- [ ] Create `Components/FABs/DarkModeFab.razor.css` — container `bottom:42px;
      right:74px; z-index:25;` + the shared `::deep .mdc-fab { box-shadow:none }`.
- **Depends on:** T1, T2.

## T5 — Register + reposition siblings
- [ ] `Layout/MainLayout.razor`: add `<DarkModeFab />` beside the other FABs.
- [ ] `Components/FABs/MapStyleFab.razor.css`: `right: 74px → 124px`.
- [ ] `Components/FABs/InfoFab.razor.css`: `right: 174px → 224px`.
- **Verify:** bottom row R→L is City(24) · DarkMode(74) · MapStyle(124) ·
  Info(224); no overlap; all clickable.
- **Depends on:** T4.

## T6 — End-to-end verify (manual / QA)
- [ ] Tap FAB → chrome + basemap dark; icon → `light_mode`. Tap → reverts.
- [ ] Reload with dark active → dark from first paint, map included.
- [ ] Streetmap toggle works in both shades.
- [ ] No GTFS re-fetch on any toggle (check network tab — routes from cache).
- [ ] Note any contrast issues in the 4 hardcoded greys for a follow-up; do
      NOT theme them pre-emptively.
- **Depends on:** T3, T5.

## Deferred (not tasks — open only if QA flags)
- `variables.css` → MDC-var consolidation (body background in dark).
- Dark-tuning the hardcoded greys `#888` / `#f3f4f6` / `#fff`.
- OS `prefers-color-scheme` auto-detect.
- Spanish (`.es`) resx.
