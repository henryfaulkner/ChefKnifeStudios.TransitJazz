# Feature Specification: Route Filter UI — Focus, Map Blur & Blurb

**Feature Branch**: `015-route-filter-ui`  
**Created**: 2026-06-13  
**Status**: Draft  
**Input**: User description: "Create Route Filter UI / Listen to Route Filter Input events / Highlight selected route / Blur non-selected route / Add placeholder blurb"

## Context & Scope

A grid of selectable route inputs already exists (the `RouteFilters` component, shipped in #14): it
renders one circular input per route, tracks a single-focus selection on hover/tap, and greys the
non-selected inputs *within the grid*. This feature wires that existing selection state into the rest
of the experience, completing the **single-focus interaction model** required by the project's UX
constitution (Principle IX):

When a route is focused, the app MUST react across the **map** and a new **bottom blurb bar** — not
just the grid. Specifically: the focused route's geometry is highlighted on the map, every other
route's geometry is blurred and greyed, and a full-width bottom bar appears showing that route's
information (or a placeholder when no authored copy exists yet). Losing focus reverses all of it
instantly.

Out of scope for this feature (deferred): the audio tone filtering on focus, the top active-bus-count
filtering, zoom-adaptive grid anchoring, and hand-authoring the real blurb copy. Those are governed by
other principles and tracked separately; this feature delivers the visual focus + placeholder blurb
slice only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Highlight the focused route on the map (Priority: P1)

A rider exploring the map hovers (web) or taps (mobile) a route input in the grid. The map immediately
draws attention to that one route: its line stays crisp and full-strength while it becomes the visually
dominant route on the map.

**Why this priority**: This is the core payoff of the filter — the connection between "the input I
touched" and "the line on the map." Without it the grid selection has no visible consequence on the
map. It is the smallest slice that delivers user value on its own.

**Independent Test**: Hover a single route input and confirm that route's polyline on the map is the
one rendered at full strength / emphasized, with no other code paths required.

**Acceptance Scenarios**:

1. **Given** no route is focused, **When** the user hovers a route input, **Then** that route's
   geometry on the map is highlighted (full opacity / emphasized) within 100ms.
2. **Given** route A is focused, **When** the user moves focus to route B, **Then** route B becomes the
   highlighted route and route A returns to the non-focused treatment, with at most one route
   highlighted at any instant.

---

### User Story 2 - Blur every non-selected route on the map (Priority: P1)

When a rider focuses one route, all the *other* routes on the map recede — they are greyed and blurred
so the focused route reads clearly against a quieted background. The moment focus is lost, every route
returns to its normal appearance immediately.

**Why this priority**: Highlighting alone is weak when a dozen colored lines still compete for
attention. The blur is what produces genuine single-focus legibility, and the constitution mandates it
alongside the highlight. Together with Story 1 it forms the complete map-side focus behavior.

**Independent Test**: Focus a route and confirm all non-focused polylines drop to a reduced-opacity,
greyed/blurred treatment; unfocus and confirm they all restore to full appearance with no animation
delay.

**Acceptance Scenarios**:

1. **Given** a route is focused, **When** the focus is active, **Then** every route other than the
   focused one is rendered greyed and blurred (visibly de-emphasized relative to the focused route).
2. **Given** a route is focused with other routes blurred, **When** the user unhovers / taps outside,
   **Then** all routes return to full opacity and normal appearance immediately (no exit transition).
3. **Given** no route is focused, **When** the map first renders, **Then** all routes are shown at full
   appearance (no route is blurred by default).

---

### User Story 3 - Show a bottom blurb bar with a placeholder (Priority: P2)

When a rider focuses a route, a full-width bar slides up from the bottom over the map showing
information about that route. Because the hand-authored copy is being written incrementally and most
routes do not have it yet, a route without authored copy shows a friendly placeholder instead of empty
space. The bar disappears the instant focus is lost.

**Why this priority**: The blurb completes the focus experience and is explicitly required by the
constitution, but the map highlight/blur (Stories 1–2) is the higher-value, self-sufficient core. The
blurb depends on focus state existing but not on the map behavior, so it can be built and shipped after
the map slice.

**Independent Test**: Focus any route and confirm a full-width bottom bar appears with placeholder text;
unfocus and confirm it disappears immediately.

**Acceptance Scenarios**:

1. **Given** no route is focused, **When** the map is shown, **Then** no blurb bar is visible.
2. **Given** a route with no authored copy is focused, **When** focus becomes active, **Then** a
   full-width bottom bar fades/slides in within 100ms showing a placeholder message that names the
   focused route.
3. **Given** the blurb bar is visible, **When** the user unhovers / taps outside, **Then** the bar
   disappears immediately with no exit animation.
4. **Given** route A's blurb is showing, **When** focus moves directly to route B, **Then** the bar
   updates to route B's content without flickering closed and reopening.

---

### Edge Cases

- **No routes loaded yet**: Route geometry loads asynchronously. Until routes exist, the grid is empty,
  so no focus is possible and no map/blurb reaction occurs — the feature is inert, not broken.
- **Focused route has no geometry on the map**: If a grid input exists for a route whose polyline is
  not currently rendered, highlighting that route is a no-op on the map while the blurb and grid still
  reflect the focus; non-existent geometry MUST NOT throw or leave other routes stuck in the blurred
  state.
- **Rapid focus changes** (sweeping the pointer across many inputs): each focus change supersedes the
  prior one; the map and blurb MUST always end in a state consistent with the *last* focused route, and
  unfocusing MUST fully clear regardless of how fast focus changed.
- **Focus lost without an explicit unhover** (e.g., pointer leaves the whole grid, or a tap-outside on
  mobile): treated identically to unfocus — full reversal.
- **Map style swap while focused** (GIS basemap toggle): the focus treatment applies to the persistent
  data layers, so a basemap change MUST NOT clear or corrupt the current highlight/blur state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST react to route focus changes originating from the existing route filter grid
  (hover on web, tap on mobile), preserving the established single-focus rule: at most one route is
  focused at any time.
- **FR-002**: On focus, the app MUST highlight the focused route's geometry on the map so it is the
  visually dominant route (full strength / emphasized).
- **FR-003**: On focus, the app MUST blur and grey every non-focused route's geometry on the map so it
  is visibly de-emphasized relative to the focused route.
- **FR-004**: On loss of focus, the app MUST restore every route's map geometry to its normal,
  full-strength appearance immediately, with no exit animation or delay.
- **FR-005**: When no route is focused, all route geometry MUST render at full appearance (no default
  blur).
- **FR-006**: On focus, the app MUST display a full-width bottom blurb bar overlaid on the map, with a
  semi-transparent dark background, that appears via a fade/slide-in completing within 100ms.
- **FR-007**: The blurb bar MUST source its content from a static client data store keyed by route. When
  the focused route has no authored entry, the bar MUST render a placeholder message rather than empty
  space, and the placeholder MUST identify the focused route (e.g., by its route number).
- **FR-008**: When focus moves directly from one route to another, the blurb bar MUST update its
  content in place to reflect the newly focused route without first closing and reopening.
- **FR-009**: On loss of focus, the blurb bar MUST disappear immediately with no exit animation.
- **FR-010**: The map highlight/blur treatment and the blurb visibility MUST remain mutually consistent:
  whenever a route is focused, exactly that route is highlighted on the map AND the blurb reflects it;
  whenever nothing is focused, no route is blurred AND no blurb is shown.
- **FR-011**: All user-facing text introduced by this feature (including the placeholder message) MUST
  be provided as localizable resource strings (English and Spanish), consistent with the project's
  localization standard — no hardcoded inline copy where a resource string is feasible.

### Key Entities *(include if feature involves data)*

- **Route focus state**: The currently focused route (or "none"). Single-valued — never more than one
  route. Already maintained by the existing route filter grid; this feature consumes it.
- **Route blurb entry**: Per-route presentation content surfaced in the bottom bar. Keyed by route
  identifier. May be absent for a given route, in which case the placeholder is used. Stored in a
  static client data file authored incrementally; this feature defines the placeholder fallback, not
  the authored prose.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a user focuses a route, the map shows exactly one emphasized route and all other
  routes de-emphasized, achieved within 100ms of the focus action.
- **SC-002**: When a user releases focus, the map returns to its all-routes-equal appearance with no
  perceptible delay (no exit animation), in 100% of focus/unfocus cycles.
- **SC-003**: For any focused route — whether or not authored copy exists — the bottom bar always
  presents non-empty, route-identifying content (authored copy or placeholder); there are zero cases of
  an empty or blank blurb bar.
- **SC-004**: Sweeping focus rapidly across multiple route inputs always leaves the UI in a state
  matching the last focused route, and releasing focus always fully clears the map blur and the blurb
  bar — no route remains stuck blurred and no orphaned blurb remains.
- **SC-005**: A reviewer can switch the app language between English and Spanish and see the placeholder
  blurb text change accordingly, confirming no hardcoded copy.

## Assumptions

- The route filter grid, its single-focus selection model, and the in-grid greying of non-selected
  inputs already exist (shipped in #14) and are reused as-is; this feature does not re-implement grid
  selection.
- "Highlight" means rendering the focused route at full strength while others are de-emphasized; no new
  color, glow, or width treatment beyond emphasis-vs-blur is required for this slice.
- The map already exposes the route polylines as persistent data layers separate from the basemap, so
  per-route appearance can be adjusted without re-fetching geometry.
- Audio filtering on focus, top active-bus-count filtering, and zoom-adaptive grid anchoring are
  explicitly out of scope here and tracked under their own constitution principles.
- Authored blurb prose is out of scope; this feature ships only the placeholder and the data-store shape
  the placeholder falls back from. Routes will be authored incrementally later (prominent routes first).
- "Immediately" / "no exit animation" follows the constitution's motion timing: gentle 100ms in, instant
  out for all transient overlays and for the blur teardown.
