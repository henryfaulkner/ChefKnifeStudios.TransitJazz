# Feature Specification: Metric and Imperial Statistics

**Working Branch**: `062-stats-retrieval-optimization` (retained by user instruction; no feature branch is required)  
**Created**: 2026-10-30  
**Status**: Draft  
**Input**: [Metric and Imperial Statistics design](../../docs/STATS_UNIT_TOGGLE_DESIGN_DOCUMENT.md). Add a persisted statistics units selector while preserving existing data, formatting, request behavior, and exploration state. This invocation produces specification and planning documentation only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read all statistics in familiar units (Priority: P1)

As a visitor viewing statistics, I can choose Metric or Imperial and read every distance and distance-based rate in that system, with consistent labels across cards, charts, tooltips, and data tables.

**Why this priority**: A consistent view makes transit activity understandable to visitors who think in different measurement systems.

**Independent Test**: Change units with representative statistics and compare every distance-bearing surface with the exact conversion relationship. Confirm that unrelated evidence retains its original values and meaning.

**Acceptance Scenarios**:

1. **Given** no saved units preference, **When** statistics opens, **Then** Metric is selected, with kilometers and the existing English number formatting and precision.
2. **Given** a distance of 1.609344 kilometers and vehicle speed of 16.09344 kilometers per hour, **When** Imperial is selected, **Then** they display as 1 mile and 10 miles per hour under the existing formatter, and all cards, charts, tooltip evidence, and open data tables show matching Imperial units and values.
3. **Given** category, bubble, and timeline statistics, **When** units change, **Then** category distances, both bubble rates and distance evidence, timeline plotted/known/hourly distances, totals, and corresponding labels convert consistently; counts, percentages, bubble areas, hours, time coordinates, timestamps, coverage, and exclusions retain their values.
4. **Given** current-city, selected-route, All-cities, or previous-day statistics, **When** units change, **Then** all available distance-bearing surfaces use the choice while their existing scope, time-window rules, and missing-data distinctions remain intact.
5. **Given** accepted metric evidence, **When** Metric and Imperial are selected repeatedly, **Then** switching back to Metric restores its initial values without accumulated conversion error or changes to the accepted source evidence.

---

### User Story 2 - Remember my statistics units (Priority: P2)

As a returning visitor, I want my explicit units choice remembered across cities, overlay openings, and reloads without affecting other preferences.

**Why this priority**: Visitors should not repeat a personal measurement preference each time they inspect statistics.

**Independent Test**: Save Imperial, reopen statistics, change city and route scope, and reload. Exercise missing, invalid, and unwritable preferences and confirm other settings survive.

**Acceptance Scenarios**:

1. **Given** a successfully saved Imperial choice, **When** the visitor closes and reopens statistics, changes city or route selection, or reloads, **Then** Imperial remains selected and applies to subsequent data.
2. **Given** saved settings that predate the units preference or contain an undefined units value, **When** statistics initializes or opens, **Then** Metric is used without losing unrelated settings.
3. **Given** preference saving fails, **When** the visitor selects another system, **Then** the current overlay session immediately uses it and stays usable; reopening or reloading may restore the last successfully saved choice.
4. **Given** another preference changed before units are saved, **When** the units choice is persisted, **Then** the other preference is preserved and the general settings panel does not gain a duplicate units control.

---

### User Story 3 - Change units without interrupting exploration (Priority: P2)

As a visitor exploring statistics with keyboard or touch, I can change units while retaining focus, open tables, hidden series, and the current loading, refresh, or error state.

**Why this priority**: Measurement choice should be accessible and should not interrupt an ongoing statistical investigation.

**Independent Test**: Toggle with keyboard and touch in both themes, with an open table and hidden series, while loading, during a pending refresh, and while stale data and an error are visible.

**Acceptance Scenarios**:

1. **Given** loading or an error without data, **When** Imperial is selected, **Then** the selector stays available and the choice applies when data arrives.
2. **Given** a pending refresh, **When** units change before its result is accepted, **Then** the result uses the current choice without an extra statistics request, cancelled refresh, changed request ordering, or reset refresh cadence.
3. **Given** stale statistics and an error, **When** units change, **Then** available values convert while the error and stale indication persist, without loading placeholders replacing the data.
4. **Given** an open table, hidden timeline series, and focus on a units option, **When** units change, **Then** the table stays open, hidden series stay hidden, and focus stays on the selected input.
5. **Given** a narrow screen in either theme, **When** the visitor uses the units group, **Then** the label and both options remain usable above charts and any previous-day notice, with visible focus, selection indicated independently of color, and at least 44-pixel tap targets.

### Edge Cases

- Null distances remain absent; actual zero distances stay zero. Missing timeline buckets remain gaps, including All-cities data.
- Previous-day fallback converts its displayed card and charts while retaining its distinction from the last 24 completed hours.
- Selecting the active option changes neither presentation, preference persistence, requests, nor refresh state.
- A choice made before any snapshot exists applies to the next accepted snapshot.
- Repeated changes use original full-precision evidence, never converted or rounded display values.
- Fleet distance per active clock hour remains a fleet activity rate, distinct from individual vehicle speed.
- Preference I/O failures leave statistics usable. Failed reads use Metric for that opening; failed writes retain the active choice for the current session.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Statistics MUST expose a labeled `Units` group with exactly two mutually exclusive radio options, `Metric` and `Imperial`, at the top of scrollable content above charts and any previous-day notice. It MUST remain available during loading/errors, wrap on narrow screens, and coexist with the fixed city and route controls.
- **FR-002**: Metric MUST be the initial choice when no valid saved preference exists. A valid saved choice MUST be loaded at initialization and whenever statistics opens, apply across cities and route selections, and survive overlay closure and page reload after successful saving.
- **FR-003**: Selecting another system MUST immediately update the accepted-data presentation, then attempt to save only this preference against the latest other settings. Selecting the active system MUST have no effect.
- **FR-004**: Missing or undefined stored units MUST resolve to Metric without resetting unrelated settings. Preference I/O failures MUST leave statistics usable: failed reads use Metric for that opening and failed writes retain the in-memory choice for the session. The general settings panel MUST NOT gain another units control.
- **FR-005**: Conversion MUST use exactly `1 mile = 1.609344 kilometers`: Metric retains the original value and Imperial divides it by 1.609344. Values MUST retain precision until existing display formatting, preserve nulls and zeros, and always derive from original metric evidence without altering it or accumulating conversion error.
- **FR-006**: Units MUST apply consistently to daily/previous-day and last-24-completed-hours distance cards, category distances and pie magnitudes, bubble distance evidence and both supplied rate values, and timeline plotted, cumulative-known, hourly-known, and total distances. Chart values, tooltips, accessible tables, and evidence MUST agree.
- **FR-007**: Cards MUST label distances `kilometers` or `miles`; compact distance headings and axes MUST use `km` or `mi`. Bubble speed MUST use `Average vehicle speed (km/h)` or `(mph)`, and fleet activity MUST use `Average distance per hour (km/h)` or `(mi/h)`. Timeline cumulative-distance labels MUST identify the selected compact unit. Tables MUST identify units in headings or captions so their evidence makes sense independently of a chart.
- **FR-008**: Unit changes MUST scale supplied rates directly, preserving their route-vehicle-hour and active-clock-hour denominators. Counts, mean active vehicles, bubble radius/areas, category percentages/keys, route-vehicle and active-clock hours, elapsed-hour coordinates, sample counts, timestamps, coverage, exclusions, and relative ordering MUST retain their original values and meaning.
- **FR-009**: The choice MUST apply to current-city, selected-route, All-cities, and previous-day presentations while preserving window rules, fallback behavior, scope qualifications, and missing-data behavior.
- **FR-010**: Unit changes MUST cause zero additional statistics requests and MUST NOT cancel pending work, create a new statistics session, alter request ordering/generations or route-selection scheduling, reset refresh timers, clear errors, replace available data with placeholders, or mark stale data fresh. Newly accepted data MUST use the choice current at acceptance time.
- **FR-011**: Updates MUST preserve focus on the selected input, open-table state, hidden timeline series and legend choices, and existing overlay/chart state.
- **FR-012**: The selector MUST provide standard keyboard radio behavior, an accessible group name, visible focus, selection cues independent of color, and tap targets of at least 44 pixels. Both existing themes MUST use their established palette and transitions.
- **FR-013**: Unit selection MUST remain independent of language and culture and retain existing English statistics number formatting and precision. New visible and assistive text MUST use the existing shared statistics resource source, retaining metric text needed by other consumers.
- **FR-014**: The metric service request/response contract, contract version, cache identities, and payload size MUST remain unchanged. Units MUST NOT add a request parameter or duplicate Imperial source evidence. Map distances, route geometry, audio timing, stored transit records, and worker calculations are outside scope.
- **FR-015**: Verification MUST cover exact conversion, Metric identity, null/zero preservation, display-only rounding, all distance surfaces and unchanged evidence, original-data round trips, previous-day and missing-bucket All-cities data, legacy/invalid/saved preferences, other-settings preservation, failed writes, pending/stale/error lifecycle, and zero additional requests. Existing statistics regression checks and client build MUST be supplemented by browser checks for keyboard/touch, narrow screens, themes, persistence, scope transitions, open tables, tooltips, and hidden series.

### Key Entities *(include if feature involves data)*

- **Units preference**: One visitor-level Metric or Imperial choice stored with existing browser preferences and shared across cities; absent or invalid values mean Metric.
- **Accepted statistics snapshot**: Original metric evidence for the current scope and completed-hour windows, including counts, rates, coverage, fallback data, and missing values. Unit selection changes neither its evidence nor freshness.
- **Statistics presentation**: Selected units, converted distances/rates, matching labels, and unchanged non-distance evidence consumed consistently by cards, charts, tooltips, and tables.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a fixture covering all distance-bearing surfaces, 100% of displayed distances, rates, tooltip evidence, and table values match the exact selected-unit conversion at existing display precision; zero surfaces retain a contradictory metric-only label in Imperial.
- **SC-002**: After ten Metric-to-Imperial-to-Metric cycles from the same accepted evidence, all Metric values equal their initial values, all non-distance evidence is unchanged, and every missing value stays missing.
- **SC-003**: In all four scope cases (current city, selected routes, All cities, previous day), one units choice updates available statistics with zero additional statistics requests and zero new loading placeholders; scheduled refresh cadence continues unchanged.
- **SC-004**: All preference acceptance cases pass: successful choices survive reopen/reload/city changes, legacy and invalid settings use Metric, unrelated preferences survive, and storage failure leaves the current session usable.
- **SC-005**: Keyboard/touch acceptance passes in both themes and narrow-screen layout, including 44-pixel minimum targets, retained focus, open tables, and hidden series; every distance-bearing table identifies its units.
- **SC-006**: Pending-refresh and stale/error acceptance cases retain their original lifecycle state while toggling, and subsequently accepted data always uses the latest units choice.

## Assumptions

- The supplied design is authoritative. Existing windows, scope, English precision, theme conventions, and accepted-data lifecycle remain the baseline.
- Existing browser settings support an additive preference without a settings-version reset; legacy compatibility must be verified with the configured serializer during implementation.
- A preference-read failure uses Metric for that opening, extending the design's missing/invalid-value default conservatively; failed writes retain the active session choice as explicitly required by the design.
- Existing accepted metric evidence contains everything needed for both systems; no collection, server, database, endpoint, or payload extension is needed.
- Existing chart updates can consume changed values and labels; implementation verification must confirm hidden-series and exploration-state preservation.
- Cross-tab synchronization, duplicate units controls, language selection, and new storage mechanisms are excluded.
- Local reprojection must avoid requests, placeholders, and timer resets. No additional numerical latency promise is invented because the source design supplies none.
