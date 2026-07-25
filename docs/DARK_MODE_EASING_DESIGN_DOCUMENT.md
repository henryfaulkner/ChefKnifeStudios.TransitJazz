# Dark Mode / Light Mode Easing — Design Document

## Problem

The dark-mode toggle (034 FAB + 035 polish) flips theme colors as a **hard cut**.
Backgrounds, text, and borders snap from light to dark in a single frame. It works,
but it feels abrupt. We want the chrome/UI layer to *interpolate* between light and
dark values over ~250ms.

## The trick

CSS `transition` on the properties being swapped. Nothing else.

The toggle already changes color two ways:

1. **MDC variables** flow down from `MatThemeProvider Theme="@_currentTheme"` in
   `MainLayout.razor` — swapping `LightTheme` ↔ `DarkTheme` re-writes the MDC CSS
   custom properties on the subtree.
2. **`--dark` classes** on individual components (`route-filters--dark`,
   `transit-running-label--dark`, etc., per the 035 pattern) swap hard-coded colors.

Both resolve to `background-color` / `color` / `border-color` changes on DOM elements.
A `transition` declared on those elements makes the browser animate between the old
and new computed values automatically. No JS, no keyframes, no new event.

```css
transition: background-color 0.25s ease, color 0.25s ease, border-color 0.25s ease;
```

## Scope of the transition

Put the transition on the **containers whose colors actually change** — not on `*`.

Targeting the elements the 035 work already touches:

| Element | Where the transition goes |
|---|---|
| Overlay/panel backgrounds (`AudioUnlockOverlay`, InfoOverlay) | root element of each overlay |
| `TransitRunningLabel` | label root |
| `RouteFilters` (`--bus-count`, `--section-label`) | `.route-filters` root |
| FAB surfaces (MDC-driven) | see "MatBlazor surfaces" below |

Because these are all scoped stylesheets, add the `transition` line to each
component's existing root rule. One line per component. No structural change.

### Global fallback (optional, lazier)

If we'd rather not touch every component, a single rule in the WebApp's global
stylesheet catches the common cases:

```css
.route-filters,
.transit-running-label,
.audio-unlock-overlay,
.info-overlay {
    transition: background-color 0.25s ease, color 0.25s ease, border-color 0.25s ease;
}
```

Do **not** do `* { transition: ... }` — it animates unrelated color changes (hover
states, route-filter circle recolors) and taxes paint on every frame.

## MatBlazor / MDC surfaces

The MDC theme swap re-writes CSS variables, but MDC components read those variables
at paint time — a variable change is not itself a transitionable property. What *is*
transitionable is the element's resolved `background-color` / `color`. So the same
`transition` line on the MDC element (or a wrapper that inherits the color) eases it.

In practice: FABs and toasts are small and already have MDC ripple/elevation
transitions; leave them alone unless they visibly jar. The high-value targets are the
**large flat surfaces** — overlays, panels, the running label — where a hard color
flip is most noticeable.

## Two honest caveats

### 1. The basemap will not ease

`map.setMapStyle` (`map-interop.js:125`) calls `map.setStyle(url, { diff: false })` —
a **synchronous full reload** of the MapLibre style. The tile layer flips in one
frame. There is no CSS property to transition; the tiles are canvas-rendered, not
DOM-styled.

The chrome around the map eases; the map itself hard-cuts.

**Optional opacity-fade workaround** (extra work, slightly janky):

```
1. fade the map container div to opacity 0   (150ms)
2. await setMapStyle(url)                     (style.load + layer restore)
3. fade back to opacity 1                     (150ms)
```

The map div is `.map#@ElementId` inside a plain wrapper (`Map.razor`). A
`transition: opacity 0.15s ease` on `.map`, plus toggling an `.map--swapping`
opacity:0 class around the existing `SetBasemapStyleAsync` await, would cover it.

**Recommendation: skip the fade for v1.** The layer-restore already takes a beat
(vehicles / trigger-points / routes re-add on `style.load`), so the swap isn't
instantaneous anyway — a fade would mostly mask that restore flicker, not the tile
cut. Ship the chrome easing; revisit the map fade only if the tile flip reads as
jarring next to the now-smooth chrome.

### 2. Principle XI ("instant dismissal") does not conflict

Principle XI governs **overlay exit** animations — overlays must dismiss instantly,
not fade out. Theme easing is a *color interpolation on persistent chrome*, not an
overlay lifecycle animation. They don't collide.

But keep the duration honest: **200–300ms**. Long enough to read as a smooth
transition, short enough that the UI still feels snappy. 250ms is the target.

## Implementation summary

| File | Change |
|---|---|
| `Components/AudioUnlockOverlay.razor` (scoped css) | + `transition` on overlay root |
| `Components/TransitRunningLabel.razor` (scoped css) | + `transition` on label root |
| `Components/RouteFilters.razor.css` | + `transition` on `.route-filters` |
| `Components/FABs/InfoFab.razor.css` (overlay) | + `transition` on overlay root |
| *(alt)* WebApp global stylesheet | one grouped rule instead of per-component |

No new services, interop, events, or JS. Purely additive CSS. Reversible by deleting
the `transition` lines.

## Deferred / out of scope

- **Map tile fade** — the opacity trick above. Add only if the tile hard-cut is
  visually unacceptable next to eased chrome.
- **`prefers-reduced-motion`** — if we add easing broadly, wrap the transitions in
  `@media (prefers-reduced-motion: no-preference)` so motion-sensitive users get the
  instant swap. Cheap accessibility win; add when the transitions land.

---

# Spec → Plan → Tasks (036 — Dark Mode Easing)

## Spec

**As** a user toggling dark mode, **I want** the chrome to fade between light and
dark **so that** the switch feels smooth instead of a hard snap.

**Acceptance:**
- FR-1: Toggling dark/light interpolates `background-color`, `color`, and
  `border-color` on the affected chrome over ~250ms.
- FR-2: The transition applies to `RouteFilters`, `TransitRunningLabel`,
  `AudioUnlockOverlay`, and the InfoOverlay — the surfaces 035 already darkens.
- FR-3: No change to *which* colors are shown (light values and dark values are
  untouched — only the transition between them is added).
- FR-4: Overlay dismissal stays instant (Principle XI unaffected).
- FR-5: The map tile layer is explicitly **out of scope** — hard cut stays.

**Non-goals:** map opacity-fade, `prefers-reduced-motion`, MDC FAB/toast easing.

## Plan

**Type:** Frontend-only, CSS-only. No services, interop, events, or `.razor` logic.
**Constitution:** XI ✅ (color easing ≠ overlay exit anim); VII ✅ (basemap path
untouched); XII ✅ (reads existing setting). No violations.

**Approach:** Per-component — add one `transition` declaration to each affected
root rule. Chosen over the global-stylesheet grouped rule because all four already
have scoped `--dark` rules, so the transition lives next to the colors it eases and
stays deletable per-component.

**Duration token:** `0.25s ease` on `background-color, color, border-color`.

## Tasks

- [ ] **T1** `RouteFilters.razor.css` — add `transition: background-color 0.25s ease,
  color 0.25s ease, border-color 0.25s ease;` to the `.route-filters` rule.
- [ ] **T2** `TransitRunningLabel.razor` (inline `<style>`) — add the same transition
  to the `.transit-running-label` rule.
- [ ] **T3** `AudioUnlockOverlay.razor` — add the transition to the overlay root rule.
- [ ] **T4** `InfoFab.razor.css` — add the transition to `.info-overlay`. **Note:**
  `.info-overlay__button` already has a `transition` line (`transform, border-color,
  background`) — append `, color 0.25s ease` to it rather than adding a second rule.
- [ ] **T5** Build (`dotnet build` on the Client.Shared RCL).
- [ ] **T6** Manual QA: toggle dark ↔ light, confirm chrome eases ~250ms, overlays
  still dismiss instantly, map still hard-cuts (expected), no jank on hover/recolor.

T1–T4 are independent and parallelizable; T5–T6 gate on them.
