# Feature Specification: Browser Memory Footprint Reduction

**Feature Branch**: `024-browser-memory-investigation`  
**Created**: 2026-06-21  
**Status**: Draft  
**Input**: User description: "docs\BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md"

## Overview

The TransitJazz web client holds roughly 1.2 GB of browser memory while running, and that number stays high and flat for the whole session in both the everyday development build and the live production environment. A prior investigation (captured in the referenced design document) used a real memory snapshot to rule out the early suspects (the map library's tile cache, the route geometry, and ordinary data leaks) and concluded the bulk of the footprint is the application runtime's own working memory, which grows to a peak early in the session and is never given back.

This feature turns that investigation into action. It has two parts: (1) **confirm where the 1.2 GB actually lives** by splitting it into runtime memory versus graphics/map memory using an in-app measurement, then (2) **reduce the steady-state footprint** by cutting the wasteful work that inflates the runtime's peak, removing duplicate copies of route data, and stopping verbose diagnostic logging from running in production.

The goal is a measurably smaller, still-flat memory footprint so the app runs reliably on memory-constrained devices (especially mobile) without changing what the user sees on screen.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Attribute the memory footprint with an in-app measurement (Priority: P1)

A developer or maintainer opens the running app (production or development), triggers a single in-app memory measurement, and receives a clear breakdown of how the total resident memory splits between the application runtime heap, the graphics/map (WebGL/canvas) memory, and other categories — so the team can target the part that actually owns the bytes instead of guessing.

**Why this priority**: Every subsequent reduction decision depends on knowing which heap owns the 1.2 GB. Without this attribution, remediation work risks targeting the wrong heap. This is the one remaining unknown the prior investigation could not resolve from source code alone, and it is independently valuable as a permanent diagnostic.

**Independent Test**: Can be fully tested by opening the running app, invoking the measurement, and confirming a per-category byte breakdown (runtime/WASM, graphics/WebGL, other) is produced and reported. Delivers value on its own because the team can attribute the footprint even if no reduction work follows.

**Acceptance Scenarios**:

1. **Given** the app is running in a supported browser context, **When** the maintainer triggers the in-app memory measurement, **Then** a per-category byte breakdown (at minimum: runtime heap, graphics/canvas memory, and a total) is returned and made visible.
2. **Given** the measurement has been taken, **When** the maintainer reviews the result, **Then** the runtime-heap share versus the graphics/map share of the total is clearly distinguishable, allowing attribution of the ~1.2 GB.
3. **Given** the browser context does not support the detailed breakdown, **When** the maintainer triggers the measurement, **Then** the app reports that the detailed breakdown is unavailable (and why) rather than failing silently or crashing.

---

### User Story 2 - Reduce the steady-state route-data footprint (Priority: P2)

The app stops keeping the full set of transit route shapes resident in multiple redundant copies. The route geometry needed to redraw routes after a basemap change is retained in a single compact form rather than duplicated across the application runtime, the animation layer, and the map library simultaneously, lowering the flat baseline without changing how routes look or behave.

**Why this priority**: The route geometry is held three to four times over and never released during a session, making it the strongest non-runtime steady-state contributor identified. Removing the duplication directly lowers the flat baseline and the peak the runtime then holds. It depends on US1 only to confirm sizing, not to proceed.

**Independent Test**: Can be tested by loading the app with the full route set, confirming routes render and still redraw correctly after a basemap style change, and measuring that the resident memory attributable to route data is lower than before (fewer simultaneous copies), with no visible change to the rendered routes.

**Acceptance Scenarios**:

1. **Given** the full route set is loaded, **When** the app finishes initial render, **Then** the route shape data is retained in fewer simultaneous copies than before this change while all routes still display correctly.
2. **Given** routes are displayed, **When** the user toggles the basemap style (street/blank), **Then** all routes are correctly re-rendered on the new basemap with no missing or corrupted route lines.
3. **Given** the route-data reduction is in effect, **When** memory is measured after initial load, **Then** the steady-state footprint is no higher than before the change (and lower where duplication was removed).

---

### User Story 3 - Stop verbose diagnostic logging in production (Priority: P2)

The production build no longer runs verbose per-batch and per-frame diagnostic logging. Debug-level logging is disabled in the production configuration and the hot-path diagnostic output is gated behind an explicit debug flag, so the browser console no longer retains references to a stream of logged objects during normal use.

**Why this priority**: Verbose debug logging currently runs on every data batch and every animation frame in production, and the console retains references to everything logged — a constant, avoidable aggravator of the footprint. It is cheap, low-risk, and independent of the heavier reduction work, but it is not expected to be the bulk of the 1.2 GB on its own.

**Independent Test**: Can be tested by running the production build, observing that verbose per-batch/per-frame diagnostic messages are no longer emitted to the console during normal operation, and confirming the app behaves identically otherwise.

**Acceptance Scenarios**:

1. **Given** the production build is running normally, **When** data batches and animation frames are processed, **Then** no verbose debug diagnostic messages are emitted to the browser console.
2. **Given** a maintainer needs diagnostics, **When** the explicit debug flag is enabled, **Then** the hot-path diagnostic output is available again.
3. **Given** logging has been quieted in production, **When** an actual warning or error occurs, **Then** it is still reported (informational/warning/error logging is unaffected).

---

### User Story 4 - Reduce per-frame and per-batch processing churn (Priority: P3)

The app stops doing redundant repeated work on every animation frame and every incoming data batch — for example, rebuilding and re-pushing an unchanged render payload 60 times a second, and making redundant passes over the same batch of records — so the runtime's transient allocation churn (and therefore the memory peak it reserves and never returns) is reduced.

**Why this priority**: Sustained allocation churn inflates the runtime's high-water mark, which the runtime then holds flat — directly worsening and making spikier the reported number. Cutting churn lowers the peak. It is lower priority than US2/US3 because its effect on the flat number is indirect and harder to measure, and it carries more behavioral risk than the configuration change.

**Independent Test**: Can be tested by running the app on live data and confirming that the render payload is no longer rebuilt/re-pushed when nothing visible changed, and that redundant per-batch passes are collapsed, with no visible change to vehicle animation smoothness.

**Acceptance Scenarios**:

1. **Given** vehicles are displayed and none have moved since the last frame, **When** the next animation frame runs, **Then** the app skips rebuilding and re-pushing the unchanged render payload.
2. **Given** an incoming data batch is processed, **When** the app transforms the batch, **Then** it does so without redundant duplicate passes over the same records.
3. **Given** the churn reductions are in effect, **When** vehicles move on screen, **Then** animation remains smooth and visually unchanged compared to before.

---

### Edge Cases

- **Unsupported measurement context**: The detailed per-type memory breakdown requires a specific secure/cross-origin-isolated browser context. When that context is unavailable, the measurement must degrade gracefully and report that the breakdown is unavailable rather than erroring.
- **Basemap swap after route de-duplication**: Removing redundant route copies must not break the ability to redraw all routes after a basemap style change — the one workflow the redundant copy existed to support.
- **Map never becomes ready**: If the map never signals readiness (bad style URL, graphics-context failure, or mobile background-tab throttling), buffered incoming batches must not accumulate without bound on the runtime heap for the life of the session.
- **Vehicle briefly drops out and returns**: Any work to drop stale tracked vehicles must tolerate a vehicle that disappears from the feed for a short interval and returns, without producing a visible teleport in place of smooth animation.
- **Debug flag left enabled**: Enabling the diagnostic debug flag must restore verbose output without otherwise altering app behavior, and must default to off in production.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST provide an in-app, on-demand memory measurement that returns a per-category byte breakdown of resident memory, distinguishing at minimum the application runtime heap, graphics/canvas (map) memory, and a total.
- **FR-002**: The memory measurement MUST make the runtime-heap share versus the graphics/map share of the total distinguishable, so the ~1.2 GB footprint can be attributed to a specific heap.
- **FR-003**: When the browser context does not support the detailed breakdown, the measurement MUST report that the detailed breakdown is unavailable (with the reason) instead of failing silently or crashing.
- **FR-004**: The app MUST retain the route-shape data needed to redraw routes after a basemap change in fewer simultaneous resident copies than today, eliminating at least one of the redundant full copies of the route geometry.
- **FR-005**: After the route-data reduction, the app MUST still correctly re-render all routes when the user toggles the basemap style, with no missing or corrupted routes.
- **FR-006**: The production configuration MUST NOT run verbose debug-level diagnostic logging during normal operation; the default production log level MUST be informational/warning or higher.
- **FR-007**: Hot-path diagnostic output (per-batch and per-frame) MUST be gated behind an explicit debug flag that defaults to off in production and can be turned on for troubleshooting.
- **FR-008**: Actual warnings and errors MUST continue to be reported regardless of the debug flag state.
- **FR-009**: The app MUST avoid rebuilding and re-pushing the render payload on an animation frame when nothing visible has changed since the prior frame.
- **FR-010**: The app MUST eliminate redundant duplicate passes over the same incoming data batch when transforming it for rendering.
- **FR-011**: Buffered incoming data batches MUST NOT accumulate without bound on the runtime heap when the map never becomes ready; the buffer MUST be bounded (e.g., retain only the most recent batches) and/or recover via a readiness watchdog.
- **FR-012**: All memory-reduction changes MUST preserve existing visible behavior — route rendering, vehicle animation smoothness, audio, and basemap toggling MUST be unchanged from the user's perspective.
- **FR-013**: The reduced footprint MUST remain flat over a sustained session (it MUST NOT reintroduce unbounded growth over time).
- **FR-014**: The measurement capability and the reduction changes MUST apply to the production build, not only the development build (the prior investigation established prod and dev footprints are equal).

### Key Entities *(include if feature involves data)*

- **Memory Measurement Result**: A per-category breakdown of the browser tab's resident memory at a point in time — categories include at minimum runtime/application-code memory, graphics/canvas (map) memory, and a total — used to attribute the footprint to a specific heap.
- **Route Shape Data**: The set of transit route geometries (coordinate paths) required to draw and redraw routes on the map; the subject of de-duplication, currently held in multiple redundant resident copies.
- **Vehicle Render Payload**: The collection of vehicle features rebuilt and handed to the map on each animation frame; the subject of churn reduction (skip when unchanged).
- **Incoming Data Batch**: A periodic batch of vehicle/position records received over the live feed; the subject of redundant-pass reduction and of bounded buffering when the map is not ready.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A maintainer can obtain a per-category memory breakdown of the running app in under one minute using only the in-app measurement, with no external profiling tooling required.
- **SC-002**: After the breakdown is obtained, the team can state what share of the total footprint is the runtime heap versus graphics/map memory, resolving the prior investigation's one open question.
- **SC-003**: The steady-state resident memory footprint of the running app is measurably lower than the ~1.2 GB baseline after the reduction work, while remaining flat over a 30–60 minute live session.
- **SC-004**: Resident memory attributable to route-shape data is reduced by removing at least one full redundant copy, with zero visible change to rendered routes before or after a basemap toggle.
- **SC-005**: During normal production operation, no verbose per-batch or per-frame diagnostic messages are emitted to the browser console, while warnings and errors are still reported.
- **SC-006**: Vehicle animation smoothness and all on-screen behavior are indistinguishable from the prior build to a user, confirming the reductions are non-visible.

## Assumptions

- The reduction targets are derived from the prior investigation in `docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md`, whose heap-snapshot attribution (runtime/WASM heap dominates; map cache and route geometry measured small in the JS snapshot; footprint flat and equal in prod and dev) is accepted as the starting point.
- The work is frontend/client-only; no server, worker, or shared-contract changes are required.
- "Reduce the footprint" means lowering the flat steady-state resident number and its peak, not eliminating the structural runtime baseline (the application-runtime baseline is inherent and out of scope to remove).
- Deep runtime-size reductions via build-level changes (aggressive trimming / ahead-of-time compilation / lazy assembly loading) are out of scope for this feature; they are a separate, larger effort and the prior investigation deprioritized build flags because production and development footprints are already equal.
- The detailed per-type memory measurement depends on a secure/cross-origin-isolated browser context; where that is unavailable the feature still delivers a graceful, clearly-labeled fallback rather than the full breakdown.
- Stale-vehicle eviction is treated as robustness for very long sessions rather than the cause of the current flat number; if pursued, it must keep the animation layer and the checkpoint tracker's vehicle state consistent and tolerate brief feed dropouts.
- "No visible change" is judged by a maintainer comparing the app before and after on live data (route rendering, vehicle animation, audio, basemap toggle).
