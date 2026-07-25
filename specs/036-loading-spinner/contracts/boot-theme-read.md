# Contract: Pre-Boot Theme Read

The single "interface" this feature exposes is the behavior of the inline
pre-boot script and the CSS selectors it drives. This documents that contract so
implementation and tests agree.

## Inputs

- `localStorage["Setting"]` — a JSON string, or `null` if absent.

## Behavior (script)

1. Attempt to read `localStorage.getItem("Setting")`.
2. If non-null, `JSON.parse` it.
3. Find a boolean property whose name equals `isdarkmodeenabled`
   (case-insensitive). Coerce its truthiness.
4. Set `document.documentElement.setAttribute("data-theme", isDark ? "dark" : "light")`.
5. **Any** failure in steps 1–3 (missing key, parse error, missing field, storage
   access throws) MUST result in `data-theme="light"` and MUST NOT throw out of the
   script (wrap in try/catch). The spinner must still render.

## Behavior (CSS)

- Default (`:root` / no `[data-theme="dark"]`): light spinner — background
  `var(--background)`, ring & label `var(--on-surface)`.
- `[data-theme="dark"]` scope: dark spinner — background `#1A1C1E`, ring & label
  `#E2E2E6`.
- Ring: rotates 360° over 2s, linear, infinite.
- Label: centered "loading..." text.

## Accept / Reject vectors

| # | `localStorage["Setting"]` | Expected `data-theme` |
|---|----------------------------|-----------------------|
| A1 | `{"isDarkModeEnabled":true}` | `dark` |
| A2 | `{"isDarkModeEnabled":false}` | `light` |
| A3 | `{"IsDarkModeEnabled":true}` (PascalCase) | `dark` (case-insensitive match) |
| A4 | `null` (key absent) | `light` |
| A5 | `"not json{"` (corrupt) | `light` (no throw) |
| A6 | `{"isAudioEnabled":true}` (field absent) | `light` |
| A7 | full real blob incl. `"isDarkModeEnabled":true` | `dark` |

## Non-goals

- Does not write storage.
- Does not localize the "loading..." label.
- Does not survive/affect the app after boot (Blazor owns theming post-render).
