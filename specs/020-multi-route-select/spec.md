# Feature Specification: Multi-Route Selection — Persistent Filter, Bus Count & Tone Scoping

**Feature Branch**: `019-lerp-event-cache` *(documentation created on existing branch — no new branch per request)*
**Created**: 2026-06-16
**Status**: Draft
**Input**: User description: "Continue: selected route filter / I want to be able to select multiple routes / Blurb only shows when a single route is selected / I want a 'Select all' and 'Clear selections' button / Selected routes should change the '# buses running' count (business logic: count of selected buses) / Selected routes should be the only routes producing tones"

## Context & Scope

The route filter grid (#14) and the focus reactions wired in #15 (map highlight/blur + bottom
blurb bar) currently implement a **single-focus, transient** interaction model: the user *hovers*
(web) or *taps* (mobile) one route input, exactly one route is "selected" for the duration of that
hover/tap, and releasing focus clears everything. The "# buses running" label (#18) presently
reports the count of **all** vehicles in the live batch regardless of selection, and the audio
engine produces tones for **every** route's vehicles as they cross trigger points.

This feature evolves the filter from transient single-focus into a **persistent multi-selection**
model that the rest of the experience respects. A user can now select several routes at once, and
that selection *sticks* until they change it. Three things become scoped to the current selection:

1. **The map blurb bar** — shown only when **exactly one** route is selected (the single-route
   detail view). With zero or multiple routes selected, the blurb is hidden.
2. **The "# buses running" count** — reflects only vehicles running on the **selected** routes
   (when a selection is active), rather than all vehicles.
3. **Audio tones** — only **selected** routes produce tones; vehicles on non-selected routes are
   silent (when a selection is active).

Two new controls support managing the selection set: a **"Select all"** action that selects every
available route, and a **"Clear selections"** action that deselects every route.

This feature is **frontend-only** and does not change the server, worker, shared contracts, or how
route/vehicle data is fetched. It changes the *meaning* and *persistence* of the selection state and
which downstream behaviors consume it.

### What stays the same

- The route filter grid layout, per-input rendering, colors, and the in-grid de-emphasis of
  non-selected inputs.
- The map highlight/blur treatment of route geometry on the map.
- The bottom blurb bar's content sourcing (static per-route store, placeholder fallback) and its
  appearance/animation timing.
- How vehicle positions and route geometry are fetched and rendered.

## Clarifications

### Session 2026-06-16

- Q: When **no** routes are selected (empty selection), what should the bus count and tones do? →
  A: Treat empty selection as "no filter applied" — the bus count shows all running buses and all
  routes produce tones, exactly as today. Scoping to the selection only takes effect once at least
  one route is selected.
- Q: When **multiple** routes are selected, what does the map show? → A: All selected routes are
  highlighted (emphasized) and all non-selected routes are blurred/greyed; the blurb bar is hidden
  because it is a single-route detail view.
- Q: How is a route selected/deselected in the new model? → A: Selection is a persistent toggle —
  acting on a route input toggles its membership in the selection set (select if not selected,
  deselect if selected); it no longer clears on hover-out / tap-away.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select multiple routes that persist (Priority: P1)

A rider wants to follow a handful of specific routes at once. They act on several route inputs in
the grid; each one becomes part of a persistent selection set and stays selected as they continue
interacting elsewhere. Acting on an already-selected route removes it from the set. The selection
does not evaporate when the pointer moves away or the tap ends.

**Why this priority**: Persistent multi-selection is the foundation of the whole feature — every
other behavior (blurb gating, bus count, tone scoping, Select all / Clear) reads from this selection
set. Without it, none of the downstream scoping can exist.

**Independent Test**: Select three different routes and confirm all three remain visibly selected in
the grid simultaneously and after moving focus away; deselect one and confirm the other two remain
selected.

**Acceptance Scenarios**:

1. **Given** no routes are selected, **When** the user selects route A, **Then** route A becomes
   selected and remains selected after the interaction ends (no hover-out / tap-away clears it).
2. **Given** route A is selected, **When** the user selects routes B and C, **Then** A, B, and C are
   all simultaneously selected.
3. **Given** routes A, B, C are selected, **When** the user acts on route B again, **Then** route B
   becomes deselected while A and C remain selected.
4. **Given** several routes are selected, **When** the map and grid are observed, **Then** every
   selected route is emphasized on the map and every non-selected route is blurred/greyed.

---

### User Story 2 - Bus count reflects only selected routes (Priority: P1)

A rider with a selection active glances at the "# buses running" label and sees the number of buses
running **on the routes they selected**, not the whole system. When they change the selection, the
number updates to match. With no selection active, the label behaves as before (all running buses).

**Why this priority**: The bus count is one of the two explicit data outputs the user asked to scope
to the selection, and it is the most visible numeric feedback that the selection "means something."
It depends only on the selection set (Story 1) and live vehicle data, both of which already exist.

**Independent Test**: With known live vehicles, select a subset of routes and confirm the displayed
count equals the number of running buses whose route is in the selection; change the selection and
confirm the count changes accordingly.

**Acceptance Scenarios**:

1. **Given** routes A and B are selected, **When** the bus count is displayed, **Then** it equals
   the count of currently running buses whose route is A or B (buses on other routes are excluded).
2. **Given** a selection is active, **When** the user adds or removes a route from the selection,
   **Then** the bus count updates to reflect the new selected-routes total.
3. **Given** no routes are selected, **When** the bus count is displayed, **Then** it reflects all
   running buses (unscoped), consistent with prior behavior.
4. **Given** selected routes currently have zero running buses, **When** the bus count is displayed,
   **Then** it shows zero (not the system-wide total).

---

### User Story 3 - Only selected routes produce tones (Priority: P1)

A rider with a selection active hears tones **only** from buses on the routes they selected; buses
on non-selected routes cross their trigger points silently. With no selection active, all routes
produce tones as before, so the soundscape is never accidentally muted.

**Why this priority**: Tone scoping is the second explicit data output the user requested and is the
core "listen to just these routes" payoff of the soundscape experience. It depends on the same
selection set as the bus count.

**Independent Test**: Select one route, then observe that crossing events for vehicles on that route
trigger tones while crossing events for vehicles on other routes do not.

**Acceptance Scenarios**:

1. **Given** route A is selected, **When** a vehicle on route A crosses a trigger point, **Then** a
   tone is produced for that crossing.
2. **Given** route A is selected, **When** a vehicle on a non-selected route crosses a trigger
   point, **Then** no tone is produced for that crossing.
3. **Given** no routes are selected, **When** any vehicle crosses a trigger point, **Then** a tone
   is produced (unscoped), consistent with prior behavior.
4. **Given** audio is muted via the settings blade, **When** any vehicle on any route crosses a
   trigger point, **Then** no tone is produced regardless of selection (mute still wins).

---

### User Story 4 - Select all / Clear selections controls (Priority: P2)

A rider wants to quickly select every route or reset the filter. A **"Select all"** control adds
every available route to the selection in one action; a **"Clear selections"** control empties the
selection set in one action, returning the experience to its unscoped state.

**Why this priority**: These are convenience accelerators over the per-route toggling in Story 1.
They make multi-selection practical but are not required for the core scoping behaviors to work, so
they ship after the selection model and its consumers.

**Independent Test**: With a partial selection, activate "Select all" and confirm every route is
selected; then activate "Clear selections" and confirm no route is selected and the experience
returns to its unscoped (all-buses, all-tones, no-blurb) state.

**Acceptance Scenarios**:

1. **Given** some or no routes are selected, **When** the user activates "Select all", **Then**
   every available route becomes selected.
2. **Given** any selection state, **When** the user activates "Clear selections", **Then** no route
   is selected and the bus count, tones, blurb, and map return to their unscoped/default state.
3. **Given** no routes are available yet (still loading), **When** the user views the controls,
   **Then** the controls do not error and selecting/clearing an empty set is a safe no-op.

---

### User Story 5 - Blurb only for a single selected route (Priority: P2)

The bottom blurb bar is a single-route detail view. It appears **only** when exactly one route is
selected. When the user selects a second route (or clears to zero), the blurb disappears, because it
no longer describes a single subject.

**Why this priority**: This refines an existing surface (#15 blurb) to the new multi-selection
reality. It is important for coherence but depends on the selection model being in place first.

**Independent Test**: Select exactly one route and confirm the blurb appears for it; select a second
route and confirm the blurb disappears; deselect back to one route and confirm it reappears for the
remaining route.

**Acceptance Scenarios**:

1. **Given** exactly one route is selected, **When** the selection is observed, **Then** the blurb
   bar is shown with that route's content (authored copy or placeholder).
2. **Given** two or more routes are selected, **When** the selection is observed, **Then** the blurb
   bar is hidden.
3. **Given** no routes are selected, **When** the selection is observed, **Then** the blurb bar is
   hidden.
4. **Given** two routes are selected (blurb hidden), **When** the user deselects one so exactly one
   remains, **Then** the blurb bar appears for the remaining route.

---

### Edge Cases

- **Empty selection = unscoped, not silent**: With zero routes selected, the bus count shows all
  running buses and all routes produce tones (no selection = no filter). Scoping only engages once
  at least one route is selected. This prevents an empty selection from accidentally muting the app
  or zeroing the count.
- **Select all then a route has no buses**: "Select all" selecting every route makes the scoped bus
  count equal the system-wide count (all routes are in scope), and tones behave as unscoped — the
  observable result matches the empty-selection default even though the mechanism differs.
- **Selected route has no live vehicles**: contributes zero to the bus count and produces no tones
  (nothing to sound); the selection is still valid and the route stays emphasized on the map.
- **Selected route has no map geometry**: emphasizing it on the map is a no-op, but it still counts
  toward the bus count and tone scoping if vehicles report against it; must not throw or leave other
  routes stuck blurred.
- **Rapid selection changes**: the bus count, tone scope, blurb visibility, and map treatment must
  always end consistent with the **final** selection set after a burst of toggles.
- **New routes load after a selection exists**: if route geometry/inputs load incrementally, an
  existing selection must remain intact; newly arriving routes are not auto-selected (except that a
  subsequent "Select all" includes them).
- **Map style swap while a selection is active** (GIS basemap toggle): the selection and its map
  highlight/blur treatment must survive a basemap change without being cleared or corrupted.
- **Audio mute interaction**: the settings-blade audio mute is independent of and overrides tone
  scoping — when muted, nothing sounds regardless of selection.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The route filter MUST maintain a **persistent set** of selected routes (zero, one, or
  many). Acting on a route input MUST toggle that route's membership in the set (select if absent,
  deselect if present), and the selection MUST NOT be cleared merely by ending the interaction
  (hover-out on web, tap-away on mobile).
- **FR-002**: The grid MUST visually distinguish selected from non-selected route inputs, with all
  currently selected routes shown as selected simultaneously.
- **FR-003**: On the map, every **selected** route's geometry MUST be emphasized and every
  **non-selected** route's geometry MUST be blurred/greyed whenever the selection is non-empty; when
  the selection is empty, all routes MUST render at full appearance (no default blur).
- **FR-004**: The bottom blurb bar MUST be shown **only when exactly one** route is selected,
  displaying that route's content (authored copy or placeholder). When zero or two-or-more routes
  are selected, the blurb bar MUST be hidden.
- **FR-005**: When the blurb is shown for a single selected route and the selection changes to a
  different single route, the blurb MUST update in place to the newly selected route (consistent
  with the existing in-place update behavior).
- **FR-006**: The "# buses running" count MUST reflect only buses running on routes in the selection
  set when the selection is **non-empty** (business logic: count of running vehicles whose route is
  selected). When the selection is **empty**, the count MUST reflect all running buses (unscoped),
  preserving prior behavior.
- **FR-007**: The bus count MUST update whenever the selection set changes or the live vehicle data
  changes, so it always reflects the current selected-routes total.
- **FR-008**: Audio tones MUST be produced **only** for vehicles on selected routes when the
  selection is **non-empty**; vehicles on non-selected routes MUST NOT produce tones. When the
  selection is **empty**, all routes MUST produce tones (unscoped), preserving prior behavior.
- **FR-009**: Tone scoping MUST be subordinate to the existing audio mute setting: when audio is
  muted, no tones are produced regardless of selection.
- **FR-010**: The filter MUST provide a **"Select all"** control that, in a single action, adds
  every currently available route to the selection set.
- **FR-011**: The filter MUST provide a **"Clear selections"** control that, in a single action,
  empties the selection set, returning the bus count, tones, blurb, and map to their unscoped/default
  state.
- **FR-012**: "Select all" and "Clear selections" acting on an empty/unavailable route set MUST be
  safe no-ops (no error).
- **FR-013**: An existing selection MUST survive incremental route loading and basemap style swaps
  without being cleared or corrupted; newly loaded routes MUST NOT be auto-added to an existing
  selection (only a subsequent "Select all" includes them).
- **FR-014**: All map, blurb, bus-count, and tone behaviors MUST remain mutually consistent with the
  single source-of-truth selection set at all times, including after rapid selection changes (final
  state wins).
- **FR-015**: All new user-facing text introduced by this feature (e.g., "Select all", "Clear
  selections" labels) MUST be provided as localizable resource strings, consistent with the
  project's localization standard (English now; Spanish deferred consistent with 015/016/017).

### Key Entities *(include if feature involves data)*

- **Route selection set**: The set of currently selected route identifiers. May contain zero, one,
  or many routes. The single source of truth that the grid, map, blurb, bus count, and tone scoping
  all read from. Replaces the prior single-valued transient focus state.
- **Selection cardinality**: A derived notion (none / exactly-one / many) that gates the blurb bar
  (shown only for exactly-one) and the unscoped-vs-scoped behavior of the bus count and tones
  (unscoped when none).
- **Selected-routes bus count**: A derived number — the count of currently running vehicles whose
  route is in the selection set (or all running vehicles when the set is empty).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can have multiple routes selected at the same time and the selection persists
  across unrelated interactions; deselecting one selected route leaves the others selected in 100%
  of cases.
- **SC-002**: With a non-empty selection, the displayed "# buses running" value always equals the
  number of currently running buses on the selected routes; with an empty selection it equals the
  total running buses.
- **SC-003**: With a non-empty selection, tones are produced for 100% of qualifying crossings on
  selected routes and for 0% of crossings on non-selected routes; with an empty selection, tones
  behave as before (all routes).
- **SC-004**: The blurb bar is visible in exactly the single-selected-route case and hidden in the
  zero-selected and multiple-selected cases, with no flicker when transitioning between single-route
  selections.
- **SC-005**: "Select all" results in every available route selected, and "Clear selections" returns
  the app to its unscoped default (all buses counted, all routes audible, no blurb, no map blur) in
  100% of activations.
- **SC-006**: After any burst of rapid selection changes, the bus count, tone scope, blurb
  visibility, and map treatment all match the final selection set, with no route stuck blurred and
  no orphaned blurb.

## Assumptions

- The route filter grid, its per-route inputs, colors, and in-grid de-emphasis (#14) already exist
  and are reused; this feature changes selection *semantics* (single transient → multi persistent)
  rather than re-implementing the grid.
- The map exposes route polylines as persistent data layers (#15) so per-route emphasis/blur can be
  applied to a *set* of routes without re-fetching geometry; the existing highlight/blur treatment
  is reused, extended from one focused route to a set of selected routes.
- The "# buses running" label (#18) and its live vehicle data feed already exist; this feature
  changes the *count rule* it applies (all → selected-only when a selection is active).
- The audio crossing/tone pipeline (#09 soundscape) already exists and triggers per crossing; this
  feature adds a selected-routes gate in front of tone production, subordinate to the existing audio
  mute setting (#16).
- Empty selection is intentionally treated as "no filter" (unscoped) so the app is never accidentally
  muted or zeroed; scoping engages only when at least one route is selected.
- "Select all" and "Clear selections" are presented near the route filter; their exact placement and
  styling are a design detail left to planning, not specified here.
- Spanish localization of the new control labels is deferred, consistent with the project's current
  localization posture (015/016/017 shipped English-only with Spanish tracked separately).
- This feature is frontend-only: no server, worker, or shared-contract changes; route and vehicle
  data continue to be fetched as they are today.
- No new branch is created for this work per the explicit request; documentation is authored on the
  current branch.
