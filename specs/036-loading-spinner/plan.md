# Implementation Plan: Loading Spinner

**Branch**: `036-loading-spinner` | **Date**: 2026-07-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/036-loading-spinner/spec.md`

## Summary

Replace the bare `<div id="app">Loading...</div>` boot placeholder in
`wwwroot/index.html` with an animated rotating-ring spinner + "loading..." label,
styled to project standards and rendered **inside** `#app` so Blazor's first
render naturally replaces it (no teardown code). The spinner themes itself from
the persisted dark-mode preference **before Blazor boots** via a tiny inline
`<script>` that reads `localStorage`, parses the settings JSON, and sets a
`data-theme` attribute the CSS keys off of. Falls back to light on
missing/unreadable data. Pure HTML+CSS+one inline script; no C#, no Blazor
component, no JS interop, no new dependency.

## Technical Context

**Language/Version**: HTML5 + CSS3 + a small inline vanilla JS snippet (runs in the browser before the .NET WASM runtime loads). Host app is .NET 10 Blazor WASM.
**Primary Dependencies**: None new. Reuses existing `wwwroot/css/` stylesheets and the existing Blazored.LocalStorage-written settings blob (read-only, from JS).
**Storage**: Reads the existing `localStorage` key `"Setting"` (Blazored.LocalStorage 4.5.0, camelCase JSON) — field of interest `isDarkModeEnabled` (bool). Read-only; never written by this feature.
**Testing**: Manual browser verification per quickstart (throttled load, dark/light reload, first-visit, corrupt-value). No automated test framework for pre-boot HTML.
**Target Platform**: Modern evergreen browsers (WASM already requires them).
**Project Type**: Web application frontend (Blazor WASM boot page).
**Performance Goals**: Spinner visible on first paint; theme resolved with zero flash of wrong theme (inline script runs synchronously in `<head>`/before app render).
**Constraints**: Must run before the .NET runtime, so it CANNOT use `SettingsService` / any C# — must read `localStorage` directly. Must not leave residue after boot.
**Scale/Scope**: One file changed (`index.html`) + one small CSS block (in `app.css` or a new `loading.css`). ~40 lines total.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **XIII. Dark-Mode Parity** — DIRECTLY BINDING. This feature adds color-bearing
  CSS (background, ring color, text color), so it MUST ship both light and dark
  renderings in the same change. ✅ Met by design: the spinner has explicit
  light and dark values keyed off `[data-theme="dark"]`, sourced from the
  `ColorConstants.Dark` palette (`Background #1A1C1E`, `OnSurface #E2E2E6`) and
  the light `variables.css` values (`--background #F5F5F5`, `--on-surface #1A1C1E`).
  Note the ~250ms theme-ease clause does NOT apply here: this is a one-shot
  pre-boot screen that is torn down, not persistent chrome the user toggles.
- **XII. Internationalized Presentation / Localization** — The `.resx` +
  `IStringLocalizer` rule governs strings rendered by the Blazor app. The
  "loading..." label renders in raw `index.html` **before** the localization
  runtime exists, so `IStringLocalizer` is not reachable. Treated as a boot-time
  literal (spec Assumptions explicitly defer its localization), consistent with
  other pre-runtime boot text. ✅ No violation; documented deferral, not
  hardcoded app copy.
- **VII. OpenStreetMap / data-layer persistence** — N/A (no map interaction; runs before the map exists).
- **II. No Frontend Secrets** — N/A (reads only a local UI preference boolean).
- All other principles (I, III–VI, VIII–XI) — N/A (no worker, audio, filtering, SignalR, or overlay-timing surface touched).

**Result: PASS.** No violations; no Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/036-loading-spinner/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── boot-theme-read.md   # The localStorage read + data-theme contract
└── checklists/
    └── requirements.md  # (from /speckit-specify)
```

### Source Code (repository root)

```text
src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/
├── index.html           # MODIFIED: spinner markup inside #app + inline pre-boot theme script
└── css/
    └── app.css          # MODIFIED: spinner + keyframes CSS with light/dark variants
                         #  (existing .loading-indicator block is replaced/extended here)
```

**Structure Decision**: Frontend-only, single-project touch. All changes live in
the WebApp's `wwwroot/`. CSS goes in the existing `app.css` (already linked from
`index.html`, already holds the current `.loading-indicator`) rather than a new
file — one fewer `<link>`, and `app.css` is the established home for boot/error
chrome (`#blazor-error-ui` lives there too). No new project, component, service,
or interop module. ponytail: no `.razor` component because the spinner must
exist before Blazor renders anything.

## Complexity Tracking

> No constitution violations. Section intentionally empty.
