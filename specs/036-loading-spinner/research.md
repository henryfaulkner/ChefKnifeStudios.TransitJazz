# Research: Loading Spinner

## R1. How to theme the spinner before Blazor/C# runs

**Decision**: An inline `<script>` in `index.html` reads `localStorage.getItem("Setting")`,
parses it, and sets `document.documentElement.setAttribute("data-theme", isDark ? "dark" : "light")`.
CSS selectors `[data-theme="dark"] .app-loader { ... }` supply the dark values.

**Rationale**: The spinner is visible during the app's cold-start window —
literally before the .NET WASM runtime (and therefore `SettingsService`,
`MatThemeProvider`, and the `--dark` C# machinery) exists. The only theme signal
available that early is `localStorage`, read from plain JS. Setting an attribute
on `<html>` and letting CSS branch is the standard flash-of-wrong-theme
avoidance pattern and needs no framework.

**Alternatives considered**:
- *Wait for Blazor to render the spinner as a component* — rejected: there is
  nothing on screen during the multi-second WASM download, which is the exact gap
  this feature fills.
- *`prefers-color-scheme` media query* — rejected: the app's dark mode is an
  explicit user setting persisted in `localStorage`, NOT the OS preference; using
  the media query would show the wrong theme for users who overrode it (spec FR-005
  says read the saved preference).

## R2. Exact shape of the persisted settings blob

**Decision**: Read the key `"Setting"` and look for a boolean dark-mode field
**case-insensitively** (accept `isDarkModeEnabled` or `IsDarkModeEnabled`).

**Rationale**: `LocalStorageConstants.SettingsKey == "Setting"`. Blazored.LocalStorage
**4.5.0** is registered via `AddBlazoredLocalStorage()` with no options, so it uses
its default `JsonSerializerOptions(JsonSerializerDefaults.Web)` → **camelCase** →
the stored field is `"isDarkModeEnabled"`. Reading case-insensitively removes the
fragile coupling to that casing (and survives a future serializer-options change),
which matters because a wrong read just silently falls back to light. The value is
a JSON boolean `true`/`false`.

Example stored value (string):
`{"version":3,"isAudioEnabled":true,...,"isDarkModeEnabled":true}`

**Alternatives considered**:
- *Hard-code the `IsDarkModeEnabled` PascalCase key* — rejected: doesn't match the
  camelCase Blazored actually writes; would always fall back to light.
- *Deserialize via a shared C# contract* — impossible pre-boot (no runtime).

## R3. Removing the spinner after load

**Decision**: Render the spinner markup **inside** `<div id="app">…</div>`. Do
nothing else.

**Rationale**: Blazor replaces the entire contents of its root selector (`#app`)
with the rendered app on first render. Putting the spinner inside `#app` means it
is torn down automatically the instant the app renders — no observers, no manual
removal, no timing code (spec FR-004 / SC-004). This is exactly how the current
`Loading...` placeholder already works.

**Alternatives considered**:
- *Full-screen overlay outside `#app` + JS to hide it on a Blazor-ready hook* —
  rejected: adds a teardown code path and a race for zero benefit; the inside-`#app`
  approach is strictly simpler and self-cleaning.

## R4. Colors / tokens to use

**Decision**:
- Light: background `var(--background)` `#F5F5F5`, ring + text `var(--on-surface)` `#1A1C1E`.
- Dark: background `#1A1C1E` (`ColorConstants.Dark.Background`), ring + text `#E2E2E6` (`ColorConstants.Dark.OnSurface`).

**Rationale**: Constitution XIII mandates dark values come from `ColorConstants.Dark`
and light from the existing `variables.css` custom properties, not ad-hoc hexes.
The example CSS used raw `white`; we translate it to the palette (FR-008). The
`variables.css` `:root` custom properties are available to `index.html` CSS, so
light values can reference them directly; the dark values are inlined in the
`[data-theme="dark"]` block because the C# `--dark` class machinery isn't loaded yet.

**Alternatives considered**:
- *Keep the example's `white` ring on any background* — rejected: invisible/ugly
  on the light `#F5F5F5` background and violates XIII (must read correctly in both).
