# Feature Specification: TransitJazz Stats Overlay

**Audio scope update (2026-10-12)**: Audio preference changes are withdrawn at the user's request. Restore the original AudioFAB settings/event implementation; Stats has no audio preference channel, mute/unmute controls or synth dependency. Earlier shared-audio tasks and evidence are historical.

**Navigation simplification (2026-10-12)**: Overlay navigation is withdrawn at the user's request. Stats uses a local visibility flag and the browser's native dialog; Info keeps its existing local open/close behavior. No shared coordinator, history entries/traversals, Info/Stats switching, modal lifecycle manager or special settings replacement is required. Earlier navigation prototype tasks and evidence are historical.

**Scope update (2026-10-12)**: The user has assessed the feature and confirmed it works. Remove the temporary chart prototype and browser-history diagnostic UI. Ship English only: no language selector, saved language preference, runtime language switching or Spanish satellite resource. Earlier Spanish/prototype validation records describe the prior implementation.

**Feature Branch**: `058-stats-overlay`  
**Feature Directory**: `specs/058-stats-overlay`  
**Created**: 2026-10-11  
**Status**: Draft  
**Input**: User description: "Create the TransitJazz Stats Overlay using [docs/STATS_OVERLAY_DESIGN_DOCUMENT.md](../../docs/STATS_OVERLAY_DESIGN_DOCUMENT.md) as the base feature description."

Visitors need a clear view of observed transit travel without leaving the map experience. Stats presents today's movement for the current city, lets visitors select historical routes independently of map and music selection, and compares all configured cities using each city's own local day. Every measurement explains its reporting window and available evidence.

The source design's confirmed product decisions are authoritative. This specification describes required behavior and measurable outcomes; the source design remains the planning input for delivery details and engineering validation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Explore the current city's observed travel (Priority: P1)

A visitor opens Stats from the welcome screen or map and sees the current city's category shares, today's distance, distance over the last 24 completed hours, route comparisons, and cumulative observed distance. The visitor can explore silently before unlocking audio and can continue listening when audio is already playing.

**Why this priority**: The current-city dashboard is the main value of this feature and must be useful without live map readiness or audio consent.

**Independent Test**: Open Stats against a known current-city reporting fixture from both entry points. Verify all four panels, measurement explanations and audio behavior without selecting routes or opening All mode.

**Acceptance Scenarios**:

1. **Given** the welcome screen with audio locked, **When** the visitor chooses "View statistics", **Then** Stats fills the screen in current-city mode with no selected routes, shows the reporting snapshot without requiring live map/feed readiness, and leaves audio locked and the audio preference unchanged.
2. **Given** audio is unlocked and playing, **When** the visitor opens Stats from the map, **Then** playback and the current audio preference continue unchanged; Stats has no audio controls.
3. **Given** a complete reporting fixture with positive observed movement, **When** Stats finishes loading, **Then** the category pie describes shares of today's observed distance, the two cards show their separate windows, one bubble describes each eligible route, and the cumulative chart groups today's contributions by transit category.
4. **Given** healthy eligible observations with zero movement, **When** Stats loads, **Then** the cards show numeric zero, the pie explains that no movement was observed and retains category table rows, and a stationary active route remains a valid zero-coordinate bubble.
5. **Given** no completed local hour is yet eligible today, **When** Stats opens, **Then** today's panels explain that the first complete hour is still being collected; any available last-24-hours distance remains independently usable.
6. **Given** partial or unavailable retained history, **When** Stats loads, **Then** known amounts carry their coverage qualification, unavailable amounts remain distinct from zero, and each affected panel explains the limitation.

---

### User Story 2 - Focus statistics on selected historical routes (Priority: P2)

A visitor uses the current-city Routes grid to select one or more routes and understand their combined observed travel. The selection changes every Stats panel while preserving the map's existing selections and music scope.

**Why this priority**: Independent route selection turns the city summary into useful route exploration without disrupting the listening experience.

**Independent Test**: Start with map routes A and B selected, select different historical routes in Stats, and verify filtering, coverage and resets while the map and audio retain their original selection.

**Acceptance Scenarios**:

1. **Given** reporting history contains active, recorded inactive and subsequently removed routes, plus a current-catalog-only route with no reporting history, **When** Routes opens, **Then** the grouped scrolling grid offers the retained routes from either reporting window, including recorded zeros and ineligible history, and omits the current-catalog-only route; no search field is present.
2. **Given** the unfiltered city view, **When** the visitor selects two bus routes, **Then** both distance totals, the pie, route bubbles and cumulative chart use that route set, positive category distance is entirely bus, and a visible selection summary identifies the scope even when Routes is collapsed.
3. **Given** a category with several options, **When** the visitor selects all or clears that category, **Then** only that category's selected membership changes; clearing the whole selection restores the citywide view and never changes map selection, route emphasis or music.
4. **Given** a ten-hour window in which one selected route supplies 100 km over ten eligible hours and another supplies 60 km over six, **When** the visitor views their combined statistics, **Then** the subtotal is 160 km, route coverage is 10/10 and 6/10, aggregate requested route-hour coverage is 16/20, and each route's ratios use its own contributing hours.
5. **Given** two of three selected routes have eligible evidence for an hour, **When** the cumulative view is drawn, **Then** the entire category stack has a gap for that hour, the table reports 2 of 3 routes available, and the two known contributions remain in cards, shares, route ratios and later cumulative values.
6. **Given** a selected route becomes absent from both windows after refresh, **When** new metadata arrives, **Then** its selection remains visible with an unavailable reason and identity fallback until cleared; it is never silently removed or expanded into an unfiltered city request.
7. **Given** selected routes, **When** the visitor changes either mode, closes Stats or changes the app city, **Then** the route selection clears and the next current-city session uses the map's current city.

---

### User Story 3 - Compare cities across their own local days (Priority: P2)

A visitor chooses All to compare category contributions, summed distance, city bubbles and cumulative city lines. Each city retains its own date and coverage; inspecting a city does not navigate away or change playback.

**Why this priority**: The comparison is a second primary journey and must explain the unequal local-day windows rather than suggest a simultaneous or normalized ranking.

**Independent Test**: Load an All fixture with cities on different dates, a missing city and a daylight-saving transition. Compare all panels, inspect data and toggle city lines without changing app city or other measurements.

**Acceptance Scenarios**:

1. **Given** current-city mode with selected routes, **When** the visitor chooses All, **Then** route selection clears, Routes is hidden, the configured public city set is represented, and unavailable cities remain identified with reasons.
2. **Given** cities on different local dates or at different points in their day, **When** All loads, **Then** the today card is labeled as the sum of each city's local day, dates/zones/cutoffs and per-city coverage are available, the bubble chart has one eligible point per city, and the unstacked timeline uses hours since each city's local midnight.
3. **Given** an All city bubble or line, **When** the visitor inspects it, **Then** its tooltip explains the measures and window without city drill-down, app-city change, route selection change or playback change.
4. **Given** several city lines, **When** the visitor hides one through the chart legend or its keyboard equivalent, **Then** both controls agree, the visible-city count updates, all table rows remain, and the pie, cards and bubbles still describe all cities.
5. **Given** all city lines are hidden, **When** the visitor views the timeline, **Then** it explains that the lines are hidden and offers "Show all" rather than reporting missing data; Show all restores every current city line.
6. **Given** hidden city lines during an All session, **When** data, ordering, theme changes, **Then** visibility follows stable city identity, new cities start visible and removed cities disappear; leaving All or closing Stats resets all lines to visible.
7. **Given** a 23-hour or 25-hour local day, **When** the visitor reads the timeline and table, **Then** the extent reflects the actual duration, repeated clock hours remain distinguishable by offset, and future hours contain no reported measurements.

---

### User Story 4 - Open and dismiss Stats with simple controls (Priority: P2)

A visitor opens Stats from welcome or the map and closes it with the close button or Escape. Stats visibility is local UI state. Info retains its existing independent controls.

**Why this priority**: The full-screen view needs clear entry and exit controls and usable keyboard focus.

**Independent Test**: Open Stats from welcome and the map, close with the button and Escape, reopen, and reload. Confirm the URL stays unchanged, native modal focus is contained and restored, and closing does not unlock audio.

**Acceptance Scenarios**:

1. **Given** welcome or the map, **When** Stats opens, **Then** it shows a named full-screen native dialog and starts a fresh default Stats session without changing the URL or browser history.
2. **Given** Stats is open, **When** the visitor presses Escape or Close, **Then** it dismisses, cancels session work and returns focus to the available launcher.
3. **Given** Stats was closed, **When** it reopens, **Then** current-city mode starts with empty route selection and default timeline visibility.
4. **Given** an open Stats view, **When** the page reloads, **Then** Stats starts closed and the existing welcome/audio-unlock flow runs.
5. **Given** Stats was opened from welcome while audio was locked, **When** it closes, **Then** welcome remains visible and audio remains locked.
6. **Given** Stats or Info controls, **When** they open or close their view, **Then** browser Back/Forward retains ordinary navigation behavior; there are no controls that switch directly between Stats and Info.

---

### User Story 5 - Read and operate statistics accessibly (Priority: P2)

A visitor can understand and control Stats on a phone, with a keyboard or screen reader, in English, and in either theme. Text and tables remain useful when charts cannot render.

**Why this priority**: Accessible evidence and explanations are essential because charts alone cannot communicate unavailable values, coincident points or every route's coverage.

**Independent Test**: Use fixed data to traverse the full overlay and tables with keyboard and assistive technology, a narrow viewport, zoom, English and both themes, and unavailable chart rendering.

**Acceptance Scenarios**:

1. **Given** a desktop or narrow phone viewport, **When** Stats opens, **Then** it covers the screen, presents a desktop two-by-two grid or a single-column reading order of pie, totals, bubbles and timeline, and keeps scope/close controls reachable without horizontal page scrolling.
2. **Given** a keyboard or screen-reader visitor, **When** the overlay opens, **Then** the dialog is named, focus enters and remains within it, background controls are inert, every action has an accessible name and selected state, and Escape and explicit close are operable.
3. **Given** any displayed chart, **When** the visitor expands its data section, **Then** a captioned table with units, row/column labels and matching evidence includes zero, unavailable, omitted and coincident points, plus hidden-city state where applicable.
4. **Given** English, light or dark theme, 200% zoom or reduced-motion preference, **When** the visitor uses Stats or changes supported presentation settings, **Then** labels, formatting, help and controls remain readable and operable, essential warnings remain visible text, and motion respects the preference.
5. **Given** the chart rendering asset fails, **When** data loads, **Then** metric text, definitions, summaries and accessible tables remain available with a chart failure explanation.

---

### User Story 6 - Trust refreshes and recover from unavailable statistics (Priority: P3)

A visitor sees a consistent snapshot, knows how current its completed-hour evidence is, and can recover from errors without disturbing the map or music. Operators can expose or withdraw Stats independently of historical capture.

**Why this priority**: Reliable refresh and exposure controls protect the experience when network service, history coverage or deployment readiness varies.

**Independent Test**: Drive success, delayed responses, day/cutoff changes, background visibility, service failures and exposure disablement. Verify snapshot ownership, retry timing, stale copy and existing app behavior.

**Acceptance Scenarios**:

1. **Given** an open visible overlay, **When** it opens or reaches the next 60-second refresh, **Then** one snapshot supplies every panel, its actual generation time/cutoff and coverage are shown, and at most one request is active for that session.
2. **Given** a successful same-selection snapshot, **When** a routine refresh is in progress or fails, **Then** existing evidence remains visible with a refresh or stale indication and actual snapshot time; failure offers Retry, while an initial failure shows an error state.
3. **Given** a slow old response, **When** the visitor changes selection, mode or session before it arrives, **Then** old evidence cannot appear beneath the new scope; the latest selection loads atomically and obsolete work cannot overwrite it.
4. **Given** the browser becomes hidden or Stats closes, **When** refresh work is pending, **Then** it stops without changing map visibility behavior; returning to a visible open overlay refreshes immediately subject to any active retry cooldown and resumes one refresh schedule.
5. **Given** local midnight or a new completed-hour cutoff, **When** the next snapshot arrives, **Then** dates, windows, totals, charts, coverage and status change together rather than mixing two reporting periods.
6. **Given** rate limiting, service unavailability or a scope beyond supported capacity, **When** a visitor requests statistics, **Then** a safe localized explanation is shown; retry cooldown is respected, oversized results are never silently truncated, and map/music remain usable after dismissal.
7. **Given** Stats is disabled for staged release or rollback, **When** the app starts, **Then** both launch actions obey the same exposure setting and existing map/audio operation continues without modifying retained history or its capture controls.

### Edge Cases

- An unfinished current hour is pending; missing expected completed hours are unavailable evidence. Just after midnight, today can be pending while last-24 distance is available.
- Fractional-offset local midnight can intersect UTC hours. Only fully contained hours contribute; excluded boundaries are disclosed without proration or a claim of complete local-day coverage.
- A spring/fall clock transition changes actual day length and can repeat a local label. UTC interval identity and local offset distinguish those hours.
- One conflicted or otherwise incompatible route row makes its entire retained city/hour cohort ineligible, including when that route is outside the visitor's filter.
- A selected route absent from a healthy cohort has unknown membership for that hour. Other selected routes retain their good evidence; the plotted stack remains a gap.
- All plotted buckets can be gaps while useful selected-route subtotals exist. Retain those amounts and tables with an explanation that coverage prevents plotting.
- A missing final plotted hour leaves an earlier last visible point; cards and tables still report the full known subtotal. A midnight origin of zero is only a visual reference, not evidence of healthy capture.
- An entirely missing unfiltered cohort cannot establish expected historical route membership. Expected route-hours stay unknown instead of being inferred from the current catalog.
- Recorded inactive routes, removed routes, unknown categories, category changes and selected-but-absent keys remain explainable through historical metadata and availability reasons.
- Zero divisors make ratios unavailable. Active stationary observations can have zero X and Y with positive bubble area; undefined points are retained in tables rather than plotted at the origin.
- Coincident bubbles remain individually discoverable in tables. Minimum visible bubble size is disclosed and exact average active vehicles remains available.
- All cities may be unavailable, or all city lines may be hidden; these are different states with different explanations and recovery actions.
- A city or route union exceeding the supported display bounds fails with a scope explanation rather than dropping members. A smaller selection is not promised to fix oversized route metadata.
- Rapid selection, refresh, reopening, theme changes and close during loading cannot restore stale scope, restart disposed work or erase a current All-session visibility choice.
- Expired page-session or originating-city history entries cannot reopen old surfaces. Repeated explicit close and Back acknowledgements cannot consume extra entries.
- Disabled history, unavailable service or rendering failure cannot make audio unlock a prerequisite for Stats or automatically change the visitor's persisted audio preference.

## Requirements *(mandatory)*

### Functional Requirements

A city/hour cohort is the collection of all route observations retained together for one city during one completed UTC hour. Eligible evidence is a complete, internally consistent cohort that satisfies the existing route-history rules; a useful partial reporting window contains some eligible hours and some unavailable hours.

#### Entry, scope and playback

- **FR-001**: Stats MUST have an accessible map launcher and a secondary "View statistics" welcome action, controlled together by the same client exposure setting. The launcher/close icon MUST distinguish "Open statistics" and "Close statistics".
- **FR-002**: Every new Stats session MUST start in the map's current canonical city with an empty route selection. The city segment MUST use its display name; the other segment MUST be "All".
- **FR-003**: Stats MUST open before audio unlock and without requiring live map, route geometry or vehicle-feed readiness. Opening MUST preserve audio lock and preferences; unlocked playback MUST continue.
- **FR-004**: Stats MUST NOT offer audio preference controls or modify audio settings. Existing AudioFAB/settings controls retain their original behavior.
- **FR-005**: Stats filtering, hovering and chart inspection MUST leave the map's city, route selection, emphasis, ongoing update lifecycle and music scope unchanged. A city change MUST close/reset Stats before its next session.

#### Historical route selection

- **FR-006**: City mode MUST offer the category-grouped route grid with scrolling, category select-all/clear, individual toggles and whole-selection clear. All mode MUST hide it. This release MUST have no route search field.
- **FR-007**: Selectable routes MUST be the unfiltered union retained in today's or last-24 completed-hour reporting history, including inactive and ineligible retained rows. Current-catalog-only routes without such history MUST be absent; no retained options MUST yield an empty-history explanation.
- **FR-008**: Historical route identity MUST be city-scoped and compared exactly, preserving spelling and case. Display labels and static identifiers MUST NOT substitute for that identity. Options MUST use historical labels/categories, expose metadata/category changes and remain stably ordered independent of live vehicle counts.
- **FR-009**: Zero selected routes MUST mean the whole city. Otherwise the exact selected set MUST filter the pie, both distance cards, route bubbles and timeline. A shared visible selection summary MUST explain that scope.
- **FR-010**: Selection MUST remain local to Stats, clear on close, either mode change or current-city change, and support rapid changes with a 200-ms settling interval. Loading for a new selection MUST replace old-scope evidence rather than displaying it under new labels.
- **FR-011**: A selected key that becomes unavailable MUST stay selected and visible, using its identity as fallback text when necessary. Unknown keys MUST produce unavailable selection evidence; they MUST NOT be discarded or widen the selection. Filtering MUST NOT reduce the selectable options list.

#### Reporting windows and evidence

- **FR-012**: Every panel and both modes MUST use the same retained hourly route-history evidence and published metric definitions. The feature MUST expose only aggregate observations and MUST NOT fill gaps from independently collected city/category totals or live data.
- **FR-013**: Each snapshot MUST have one generation instant and cutoff. The initial cutoff MUST be the UTC hour boundary obtained by subtracting two minutes from that instant and rounding down to the hour. Reports MUST contain only completed UTC hours and display their actual cutoff.
- **FR-014**: Today MUST use each city's actual local calendar day from midnight to the earlier of the cutoff or next midnight, clamped to empty when the cutoff precedes today's midnight. Last 24 hours MUST mean the 24 completed UTC hours ending at the same cutoff. Windows MUST include their start and exclude their end.
- **FR-015**: Only full UTC hours contained in a requested window MUST contribute. Local dates, configured city zones, actual UTC bounds, boundary exclusions and pending-through time MUST be available. Reporting MUST preserve 23/25-hour days and distinguish repeated local hours by offset.
- **FR-016**: Eligibility MUST be established for the whole retained city/hour cohort before filtering: all retained rows must be complete, nonconflicting, compatible with the supported definition and configured capture cadence, catalog-stable and consistent in capture run and observation sample count. Catalog identity is route-specific; unrelated routes need not share it. Any conflict or unexpected cohort inconsistency MUST exclude the whole cohort.
- **FR-017**: Missing, partial, no-data, conflicting, incompatible and unknown-membership evidence MUST have distinct exclusion reasons. Complete inactive rows MUST remain eligible zero evidence; different definitions/cadences MUST never be blended.
- **FR-018**: Each selected route MUST contribute all of its own eligible available hours. Other selected routes' missing membership MUST NOT remove those contributions or become inferred zero activity. Unfiltered hours MUST use their actual historical cohort rather than today's route catalog.
- **FR-019**: Numeric zero MUST mean eligible observations with zero distance; an unavailable amount or undefined ratio MUST have no numeric value. Today before its first completed reportable hour MUST be pending; a window with reportable completed hours but no eligible evidence MUST be unavailable. Partial amounts MUST be labeled as observed subtotals with partial coverage; no missing travel MUST be estimated.
- **FR-020**: Coverage MUST show expected closed hours, hours with any contribution, fully covered scope hours, first/last contributing bounds, exclusions and boundary/pending context. Selected routes MUST each show available/expected hours and combined available/requested route-hours, with requested route-hours equal to selected-key count times expected closed hours. Unknown unfiltered historical populations MUST leave expected route-hours unknown.
- **FR-021**: All MUST include every configured public city, including unavailable cities and their reasons, and show per-city coverage plus contributing/expected city-hours and city counts. A single combined percentage MUST NOT conceal a missing city.
- **FR-022**: Every snapshot MUST present a consistent set of observations and metadata across all panels and both windows, including when retained evidence is concurrently written or quarantined. Reused evidence MUST retain its original generation time and may lag newly changed retained evidence by no more than 60 seconds.

#### Metrics and chart meaning

- **FR-023**: Distances MUST represent accepted observed travel along resolved routes, expressed in kilometers. Repeated movement contributes repeatedly. Definitions MUST distinguish this measure from scheduled length, network coverage, passenger distance, completed trips and audio playback.
- **FR-024**: Cards MUST sum eligible distance within their own reporting window and selected scope. Category amounts MUST use each contributing hour's historical category, retain unknown categories and reconcile to today's card before display rounding. Shares MUST be category distance divided by positive total distance; a zero total MUST have undefined percentages.
- **FR-025**: The pie MUST be titled "Who's playing today?" and explain that its shares describe observed distance. Partial positive amounts MUST remain useful with warnings; observed all-zero amounts MUST show a no-observed-movement explanation and category table rows. Category hiding MUST NOT redefine the shares.
- **FR-026**: City mode MUST compare routes and All MUST compare cities through bubble X, Y and area values using the definitions below. X MUST be labeled "km per observed route-vehicle-hour"; Y MUST be "km per observed active clock hour". Help MUST explain transfers, hourly membership and the distinction from elapsed driving time or operating speed.
- **FR-027**: Bubble ratios MUST aggregate matching numerator/denominator observations before division, preserve precision until display rounding and expose their underlying counts and per-entity coverage. Route comparisons with different observed periods MUST explicitly carry that qualification.
- **FR-028**: Undefined coordinates or nonpositive area measures MUST omit a bubble while preserving its table row and reason. Positive-area stationary routes at X=0 and Y=0 MUST remain visible. Axes MUST begin at zero; point inspection MUST NOT change selections.
- **FR-029**: Bubble area MUST represent average observed active vehicles proportionally within the current snapshot, with a disclosed minimum marker size and scale key. Exact values MUST remain in tooltips/tables; coincident points MUST remain discoverable without shifting measured coordinates.
- **FR-030**: City timelines MUST show cumulative raw category contributions stacked exactly once in stable order. All timelines MUST show unstacked, unfilled city lines. Tooltips/tables MUST distinguish each category's contribution, combined subtotal, hourly known contribution and cumulative known amount; category hiding MUST NOT change city-stack meaning.
- **FR-031**: City X positions MUST use actual elapsed hours since local midnight with local-clock labels. All MUST align each series on elapsed hours since that city's own midnight and label that axis explicitly. Future hours MUST remain unavailable; interpolation, smoothing or connecting across missing evidence MUST NOT suggest measurements that do not exist.
- **FR-032**: An excluded city/hour or any missing selected route in an hour MUST create a gap in the entire plotted category stack. Known contributions MUST remain in amounts and cumulative arithmetic, and later fully covered points MUST resume the cumulative known subtotal with the partial-coverage warning. If all plot points are gaps, useful amounts and tables MUST remain available with a plotting explanation.
- **FR-033**: Healthy category shares, cards and cumulative endpoints MUST reconcile before display rounding. In All, every city's known endpoint MUST equal its today subtotal and their sum MUST equal the All today card, including hidden lines. Missing final plot points MUST NOT remove later known amounts from cards/tables; a midnight zero origin MUST NOT assert capture health.
- **FR-034**: All-city timeline legend controls MUST hide/show lines only. Keyboard equivalents, visible-city count, table hidden-state labels and Show all MUST share that visibility state; hide-all MUST have its own explanation. Visibility MUST follow city identity through refresh/reordering/theme changes, with new cities visible, and reset on leaving All or closing Stats.
- **FR-035**: All-chart inspection MUST expose tooltips without drill-down or navigation. Returning to city mode MUST always represent the map's current city. All today copy MUST state "Sum of each city's local day" and explain differing dates/day extents without implying normalized performance rankings.

#### Overlay layout, navigation and accessibility

- **FR-036**: Stats MUST fill the viewport on desktop and mobile with no exposed map backdrop. Desktop MUST use Routes above a two-by-two panel grid; narrow screens MUST use one scrolling column in pie/totals/bubbles/timeline order, with persistent scope and close controls, safe-area accommodation and no horizontal page scrolling.
- **FR-037**: Stats MUST behave as a labeled native modal: focus enters, stays contained and returns to the invoking control when available; background UI is inert. There MUST be no outside-backdrop dismissal target.
- **FR-038**: Stats and Info MUST use local open/close state. Opening or closing MUST NOT create or replace browser history entries or change the URL.
- **FR-039**: Stats close and Escape MUST dismiss promptly and cancel session work without traversing browser history. Info retains its existing close controls.
- **FR-040**: Browser Back/Forward MUST retain ordinary navigation behavior; overlay state MUST NOT be stored in or restored from history entries.
- **FR-041**: A full reload MUST start the existing welcome/audio-unlock flow with Stats and Info closed; neither open state is persisted.
- **FR-042**: Stats and Info MUST remain independent views with no shared navigation coordinator or controls that replace one with the other.
- **FR-043**: Overlay entry motion MUST be 100 ms, dismissal immediate and hover pulse brief and reversible. Reduced-motion preference MUST suppress entrance/pulse motion; selected state MUST be conveyed through text and accessible semantics as well as color.
- **FR-044**: Every panel MUST have a visible title, definition, period and coverage. Essential warnings MUST be persistent text; help MUST be available through focus/click as well as hover. Route buttons and all select/clear/visibility/scope/close actions MUST have full accessible labels, focus indication and keyboard activation.
- **FR-045**: Every chart MUST provide an expandable keyboard-accessible table from the same evidence, with captions, row/column headers, units and all zero/unavailable/omitted points. Timeline rows MUST identify actual intervals, local date/time/offset, hourly and cumulative known amounts and coverage. Screen-reader status updates MUST announce refresh/errors/coverage without rereading whole tables.
- **FR-046**: All user-visible text, help, statuses and accessible names MUST use English through the app's existing localization conventions. No language selection or switching is available. Number formatting MUST use English; dates MUST follow the city zone. All panels/controls MUST have light/dark parity, stable category/city colors and non-color ways to interpret data.
- **FR-047**: Touch, keyboard-only use, screen-reader traversal, 320-pixel viewport and 200% zoom MUST preserve readable data and reachable controls. Failed chart rendering MUST leave text, summaries, metric cards and tables usable with a safe explanation.

#### Refresh, errors and operational readiness

- **FR-048**: Stats MUST load immediately on opening and refresh every 60 seconds while open and visible, with at most one active request per session. Hidden state MUST suspend/cancel refresh; visibility restoration MUST refresh promptly within active cooldown rules. Close/navigation MUST stop work and prevent further state writes.
- **FR-049**: Session, selection, mode and reporting-window changes MUST reject obsolete responses and publish labels, dates, charts, amounts, coverage and status together. Routine refresh MUST retain same-scope evidence with a refresh indication; failed refresh MUST label it stale with actual generation time and Retry. Initial failure MUST show an error instead of invented data.
- **FR-050**: Theme changes MUST update presentation locally without requesting different evidence. Explicit retry and changed selections MUST respect active service cooldowns. Initial retry delay MUST be 60 seconds, increase to at most five minutes under continued failures, honor any longer service-provided delay and reset after success.
- **FR-051**: Public reporting MUST validate configured city identity and scope, allow up to 4,096 distinct selected/returned route keys per city and up to 32 cities and 32 categories, and reject malformed or excessive scope safely. Results MUST never silently truncate, drop unknown selection keys or expose arbitrary dates, city lists or metric expressions in this release.
- **FR-052**: Reporting MUST remain bounded in execution and memory, protect live capture/update responsiveness under public load, and produce a safe unavailable result by the five-second read deadline. Service disablement, unavailable dependencies, rate limiting and scope limits MUST have localized recoverable explanations without raw operational errors.
- **FR-053**: Reporting MUST be publicly readable aggregate history without individual vehicle, trip or listener identities, internal capture identifiers, raw feed records, credentials or unrestricted query access. The read feature MUST NOT mutate observations or capture settings.
- **FR-054**: Stats read exposure MUST start disabled pending validation, independently of capture enablement, dry-run and city settings. Launchers MUST share one staged exposure setting; operators MUST be able to withdraw Stats presentation/reads without deleting history, disabling capture or breaking map/music.
- **FR-055**: Before release, the handoff MUST record supported definitions/windows/capacity, actual retaining cities and representative coverage, refresh/cutoff expectations, load and response-size evidence, safe error behavior, deployment dependencies and rollback. Required evidence MUST include arithmetic reconciliation, real retained-data consistency, browser chart/navigation/accessibility behavior and live-processing impact; skipped checks MUST remain explicit release gates.

### Metric Definitions

All sums below use only eligible observations for the stated scope/window. Each route uses its own contributing hours. An observed active clock hour is a complete contributing hour with at least one observed active vehicle, including a stationary vehicle. One route-vehicle-hour is one vehicle observed on one route in one eligible hour; a transfer can count on both routes, and brief presence still counts once for that route/hour.

| Measure | Required meaning |
| --- | --- |
| Distance | Sum of eligible observed route/hour distance in kilometers. |
| Category share | Category distance divided by positive scoped today distance, multiplied by 100; undefined when total distance is zero. |
| Route bubble X | Route distance divided by the sum of its hourly observed vehicle memberships. |
| Route bubble Y | Route distance divided by its number of observed active clock hours. |
| Route bubble area measure | Sum of the route's active-vehicle counts across observation samples divided by its total eligible sample count, including inactive hours. |
| City bubble X | City distance divided by the sum of route/hour vehicle memberships; this retains route-based counting. |
| City bubble Y | City distance divided by the number of contributing clock hours with any active route; each city hour counts once. |
| City bubble area measure | Sum of route activity counts divided by the common observation sample count counted once per city/hour, rather than once per route. |
| Cumulative observed distance | Prefix sum of known eligible hourly distance by category or city, preserving known contributions during plotted gaps. |

An undefined divisor yields no ratio. No measure claims city-unique hourly vehicles, unique period vehicles, exact elapsed service duration or actual travel through unobserved hours. Display rounding occurs after aggregation; displayed category percentages may differ slightly from 100 because of rounding.

### Scope Boundaries

This feature delivers the Stats entry points and full-screen overlay, both modes and four panels, independent reusable route presentation, honest coverage and accessible evidence, English and theme parity, bounded public aggregate reads, simple Stats open/close controls, and operational release guidance. Existing Info content, controls and theme behavior remain independent.

Historical date pickers, exports, arbitrary metrics/ranges, route search, city drill-down, playback/listener statistics, individual vehicle records, unfinished-hour measurements, city-unique vehicle-hour capture and new retention/backfill jobs are excluded. The existing route-history collection definitions and prior internal insight contracts remain the source of truth.

### Key Entities *(include if feature involves data)*

- **Reporting selection**: Current-city or All scope, canonical city where applicable and an exact set of city-scoped route keys. Empty city selection means the whole city; All has no route selection.
- **City reporting window**: City identity/display name, configured zone, local date, local-day bounds, completed-hour cutoff, eligible interval grid and last-24 interval.
- **Historical route option**: City-scoped route identity, retained label/category, latest retained metadata time, category-change notice and availability. Membership is independent of current metric eligibility.
- **Retained route-hour observation**: Aggregate distance, hourly vehicle memberships, activity sample counts, historical route metadata, published definition/cadence and evidence status within a city/hour cohort.
- **Coverage report**: Complete/partial/unavailable/pending state, expected/contributing/full-scope hours, known or unknown route-hour population, selected-route availability, exclusions, observed bounds and pending/boundary notices.
- **Reporting snapshot**: One consistent generation instant/cutoff and normalized selection, city windows, unfiltered route options, distance summaries, category shares, bubble measures, timeline known/plot values and safe warnings.
- **All-timeline visibility**: The visible city set for the current All session, synchronized across legend, accessible controls and table labels; it is presentation state rather than reporting scope.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 95% of warm opens on an agreed representative device/network show useful current-city amounts, coverage and controls within two seconds, from either welcome or map, without requiring audio unlock.
- **SC-002**: At least 95% of reporting reads return within two seconds when fresh computation is required and within 250 ms when already-prepared evidence can be reused; every read ends with a result or safe failure within five seconds.
- **SC-003**: Independently calculated acceptance fixtures reconcile 100% of distance cards, category amounts, bubble numerators/denominators and known cumulative endpoints before rounding. The two-route 100-km/60-km fixture yields 160 km and 16/20 requested route-hours; 100 km before a gap plus 40 km afterward resumes at 140 km.
- **SC-004**: Every missing/ineligible expected hour is distinguishable from observed zero, every relevant partial result shows its coverage qualification, and 100% of selected-route gap cases retain known contributions without estimating missing distance.
- **SC-005**: In every acceptance interaction, Stats route selection, route hover, All inspection and line visibility leave map selection/city and playback scope intact; Stats interactions do not change audio preferences. Silent entry and close preserve locked audio in every welcome-flow case.
- **SC-006**: Stats opens from welcome and the map, closes with Close/Escape, reopens with default selection and remains closed after reload. These actions preserve the URL and audio state and do not add or traverse browser history.
- **SC-007**: Every control and every reported measure is reachable through keyboard and accessible text/table presentation in English, both themes, at a 320-pixel viewport and 200% zoom. Chart failure preserves 100% of available text/table evidence.
- **SC-008**: An open visible overlay refreshes at 60-second intervals with no more than one request in flight; hidden/closed/disposed sessions perform zero periodic reads, and no obsolete result overwrites a newer scope in any race acceptance case.
- **SC-009**: All scope remains correct through hide/show, hide-all, refresh, reordered cities and presentation changes: 100% of amounts retain the all-city scope, hidden state follows city identity and Show all restores every current line.
- **SC-010**: At the supported bounds of 4,096 routes per city, 32 cities and 32 categories, results remain complete within their stated scope or fail with an explicit capacity explanation. Typical compressed city results remain below 1 MiB and every uncompressed result stays at or below 8 MiB; no oversized result is presented as truncated success.
- **SC-011**: Under agreed matched dashboard load, live transit processing p95 increases by no more than the existing 5% history overhead budget, and reads cause no capture/write blockage. Release exposure requires recorded latency, capacity and representative coverage evidence, including unavailable and early-morning cases.
- **SC-012**: In a review with five representative visitors, at least four can identify the current scope, distinguish partial/unavailable/zero data, explain completed-hour freshness and recognize that All uses each city's local day after using the visible explanations.

## Assumptions

- The [source Stats design](../../docs/STATS_OVERLAY_DESIGN_DOCUMENT.md) records the completed product interview. Its confirmed decisions require no new product clarification; its engineering defaults become validation targets during planning and implementation.
- The [hourly route-history feature](../057-hourly-route-history/plan.md) and [observation contract](../057-hourly-route-history/contracts/observed-route-hour-statistics-v1.md) provide authoritative collection/eligibility semantics. Real retained-data, load and operator acceptance remain dependencies; no assumption is made that every configured city currently retains healthy history.
- The [chart foundation companion design](../../docs/PAX_BLAZOR_CHARTJS_COMPONENTS_DESIGN_DOCUMENT.md) is proposed work, so delivering and validating suitable pie, bubble and cumulative chart presentation is included in this feature's scope.
- The configured public city set and existing canonical city/route identities are reused. Configured city zones control dates even when the visitor's own zone differs; future fractional-offset cities require honest boundary-exclusion presentation.
- Public aggregate access follows the existing transit experience without requiring a new account. Stats exposes no individual transit or listener records.
- The two-minute finalization allowance, 60-second evidence reuse/refresh interval, capacity bounds, response budgets and performance targets are initial documented policies to validate against retained coverage and representative load before exposure. A result requiring a change to confirmed behavior must bring that concrete tradeoff back for review.
- The existing [constitution](../../.specify/memory/constitution.md) governs localization, themes and interaction. English and the current app settings/visibility/audio paths are reused; the full-screen Stats route grid adapts the shared route presentation without applying dashboard selection to map/audio.
- The 5% live-processing target interprets the source design's instruction to stay within the existing history overhead budget. The representative user review in SC-012 supplies a concrete acceptance threshold for understandable metric explanations.
- This invocation creates specification documentation only. Planning must carry forward the source design's bounded read policies, chart/history/browser prototypes, consistency checks and release gates; this specification does not claim implementation, completed operational validation or deployment.
