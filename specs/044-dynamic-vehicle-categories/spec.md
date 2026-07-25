# Feature Specification: Dynamic Per-City Vehicle Categories

**Feature Branch**: `044-dynamic-vehicle-categories`  
**Created**: 2026-07-18  
**Status**: Draft  
**Input**: User description: "docs\DYNAMIC_VEHICLE_CATEGORY_DESIGN_DOCUMENT.md"

## Overview

Today every transit city in the app sorts its vehicles into exactly two fixed buckets — **Bus** and **Rail** — using one classification rule shared by all cities. This forces every kind of vehicle into one of those two labels. When Toronto (TTC) was onboarded, its **streetcars** had nowhere to live: they could only appear mislabeled as "Rail," giving riders no way to filter for streetcars on their own or see how many streetcars are running.

This feature replaces the fixed two-bucket scheme with an **open-ended, per-city set of vehicle categories**. A city can declare as many categories as its real fleet needs (for example `bus`, `rail`, `streetcar`, `ferry`), and those categories flow through everywhere a rider sees or filters vehicles: the map, the route filter panel, and the running-vehicle count labels. Cities that don't declare anything keep behaving exactly as they do today.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Streetcars appear as their own category in Toronto (Priority: P1)

A rider exploring Toronto transit wants to see, filter, and hear streetcars as a distinct kind of vehicle — separate from subways/rail and buses — because streetcars are a defining part of Toronto's transit identity.

**Why this priority**: This is the concrete problem that triggered the feature. Without it, Toronto is shipped with streetcars indistinguishable from rail, which is misleading and was the deferred gap left by the Toronto onboarding. Delivering just this story makes Toronto correct and demonstrates the whole capability end to end.

**Independent Test**: Open the app on Toronto, confirm a dedicated "Streetcar" filter section appears in the route filter panel, confirm a "streetcars running" count row appears in the running-vehicle label when streetcars are active, and confirm selecting/clearing that section filters only streetcar routes.

**Acceptance Scenarios**:

1. **Given** the app is showing Toronto and streetcar routes exist, **When** the rider opens the route filter panel, **Then** a distinct Streetcar section is shown alongside Rail and Bus sections.
2. **Given** streetcars are currently in service, **When** the rider views the running-vehicle count label, **Then** a "streetcars running" row shows the current streetcar count separately from bus and rail counts.
3. **Given** the rider selects only the Streetcar section, **When** the map updates, **Then** only streetcar routes/vehicles remain highlighted/active and other categories are deselected.
4. **Given** the rider views the categories in the filter panel and count label, **When** multiple categories are present, **Then** rail-family categories are ordered ahead of buses (streetcars and rail before buses), matching the natural transit prominence.

---

### User Story 2 - Existing cities are completely unchanged (Priority: P1)

A rider using one of the already-supported cities (Atlanta/MARTA, Washington/WMATA, Boston/MBTA, New York/NYMTA) sees the exact same Bus and Rail categories, in the same order, with the same labels and counts as before this feature.

**Why this priority**: The change touches shared, city-agnostic code. A regression here would degrade every existing city at once. Guaranteeing zero behavior change for cities that declare no categories is a non-negotiable safety property and must ship with (and be verified alongside) Story 1.

**Independent Test**: On each existing city, confirm the route filter panel still shows exactly a Rail section then a Bus section, the running-count label still reads "trains running" / "buses running" with correct counts, and no new or missing sections appear.

**Acceptance Scenarios**:

1. **Given** a city that declares no custom categories, **When** the rider opens the route filter panel, **Then** they see exactly a Rail section followed by a Bus section, identical to today.
2. **Given** rail vehicles are in service in such a city, **When** the rider views the running-count label, **Then** the rail row still reads "trains running" and the bus row still reads "buses running" with the same counts as before.
3. **Given** the existing cities are loaded, **When** their vehicles are classified, **Then** rail-family vehicle types map to Rail and everything else maps to Bus, exactly as today.

---

### User Story 3 - Unmatched vehicles become visible instead of silently mislabeled (Priority: P2)

When a live vehicle cannot be matched to a known route, it is shown in a distinct **Unknown** category rather than being silently counted as a bus — so a rider (and the operator) can see that some vehicles are unaccounted for, rather than that noise inflating the bus count.

**Why this priority**: Improves data honesty and observability and comes "for free" once the category machinery is dynamic, but it is not required to solve the core streetcar problem, so it is secondary to Stories 1 and 2.

**Independent Test**: Force or simulate a vehicle whose route is not in the current route set, and confirm it appears under an "Unknown" category (its own filter section and count row) rather than adding to the bus count.

**Acceptance Scenarios**:

1. **Given** a live vehicle whose route cannot be matched to any known route, **When** categories are computed, **Then** that vehicle is assigned to an "Unknown" category, not "Bus."
2. **Given** at least one unmatched vehicle exists, **When** the rider views the filter panel and count label, **Then** an Unknown section and count row are visible and countable.

---

### User Story 4 - A new city can define new categories through configuration (Priority: P3)

An operator onboarding a future city can declare that city's own set of vehicle categories (mapping each of the city's vehicle types to a category name of their choosing) without needing changes to the shared classification code.

**Why this priority**: This is the general capability the feature unlocks, but only Toronto needs a real category configuration on day one; broader reuse is future-facing, so it is the lowest priority to demonstrate now.

**Independent Test**: Provide a category configuration for a test/sample city mapping its vehicle types to two or more named categories, load that city, and confirm those categories appear as filter sections and count rows.

**Acceptance Scenarios**:

1. **Given** a city with a declared mapping from its vehicle types to category names, **When** the city loads, **Then** each declared category appears as its own filter section and count row (when it has active vehicles/routes).
2. **Given** a city's declared mapping does not cover a vehicle type that later appears in its live data, **When** that vehicle type is encountered, **Then** it is assigned to the Bus category and the occurrence is recorded (logged) rather than causing the city to fail to load.
3. **Given** a newly introduced category has no polished display label or styling yet, **When** it is shown, **Then** it renders with neutral/base styling and a readable fallback label and count sentence, never blank or broken.

### Edge Cases

- **City with no category configuration**: Falls back to today's rule — rail-family vehicle types become Rail, everything else becomes Bus (Story 2).
- **Configured city encounters an unlisted vehicle type**: That vehicle type defaults to Bus and the event is logged; the city keeps loading (Story 4, scenario 2).
- **Vehicle cannot be matched to any route**: Assigned to the Unknown category (Story 3).
- **Two different vehicle types map to the same category**: They collapse into a single section/count for that category, ordered by the more prominent (rail-ward) of the two types.
- **A category has no polished label / no polished count noun / no custom styling**: Renders with a readable fallback (raw category name for the label, a generic "N running" sentence, and neutral styling) — never blank or broken.
- **A category has no active vehicles/routes at the moment**: Its section and count row are hidden (empty categories are not shown), matching today's "section shows only when non-empty" behavior.
- **Category display order across cities**: Rail-family categories consistently sort ahead of buses; for existing cities this preserves the current Rail-then-Bus order exactly.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow each city to declare its own open-ended set of vehicle categories, rather than restricting all cities to a single fixed pair of categories.
- **FR-002**: The system MUST classify each route into a category based on the city's declared configuration, mapping the route's vehicle type to the configured category name.
- **FR-003**: For any city that declares no category configuration, the system MUST classify routes using today's existing rule (rail-family vehicle types → Rail, all others → Bus), producing no change in behavior for currently supported cities.
- **FR-004**: When a configured city encounters a vehicle type not covered by its configuration, the system MUST assign it to the Bus category, record the occurrence (log), and continue loading the city (it MUST NOT fail the city's data load).
- **FR-005**: When a live vehicle cannot be matched to any known route, the system MUST assign it to an "Unknown" category rather than to Bus.
- **FR-006**: The route filter panel MUST render one section per category that has routes, driven by the city's actual categories rather than a fixed Bus/Rail pair.
- **FR-007**: The running-vehicle count label MUST render one count row per category that has active vehicles, driven by the city's actual categories.
- **FR-008**: Riders MUST be able to select-all and clear-selection for any category section, and the panel MUST indicate when a category has an active selection — for every category, not only Bus and Rail.
- **FR-009**: The system MUST order categories consistently so that rail-family categories appear ahead of buses; for the four existing cities this MUST reproduce today's exact Rail-then-Bus order.
- **FR-010**: Each category's display label MUST come from the app's existing localization mechanism (keyed by the category name); when a label is missing, the system MUST fall back to a readable value (the raw category name) rather than showing blank or throwing.
- **FR-011**: Each category's running-count sentence MUST use a per-category phrase when available (e.g. "trains running", "buses running", "streetcars running"); when a per-category phrase is missing, the system MUST fall back to a generic "N running" sentence rather than showing blank.
- **FR-012**: The pre-change running-count copy MUST be preserved exactly for existing categories (rail shows "trains running", bus shows "buses running").
- **FR-013**: A category with no custom visual styling MUST render with neutral/base styling; the presence or absence of custom styling MUST NOT break rendering.
- **FR-014**: The category assigned to each vehicle MUST travel through the real-time vehicle data so the client can display and filter by it; the client MUST NOT need to independently re-derive categories.
- **FR-015**: The client MUST NOT hardcode any fixed list of category names; it MUST accept and render whatever categories a city provides, so that adding a category for a city does not require a client code change to that fixed list.
- **FR-016**: Vehicle category configuration MUST be authored in one place (WebAPI configuration); the data-processing worker MUST require no new configuration and MUST receive categories transitively from the already-shared route data.
- **FR-017**: On the map, rail-family vehicle markers MUST be visually distinguishable from other vehicle markers by size (this corrects a latent defect where the intended rail marker sizing never actually applied). Non-rail categories share a common marker size; per-category marker sizing is out of scope.
- **FR-018**: The running-vehicle count label MUST update reactively and correctly when category counts change (no stale counts after vehicles enter/leave service).
- **FR-019**: Category configuration and classification MUST be per-city with no city hardcoded into the shared classification logic (the shared classifier remains city-agnostic; per-city behavior comes only from configuration).

### Key Entities *(include if feature involves data)*

- **Vehicle Category**: A named grouping of vehicles a rider can see, filter, and count (e.g. `bus`, `rail`, `streetcar`, `ferry`, `unknown`). Open-ended per city. Carries: a stable name (used for grouping, filtering, and matching a localized label and a count phrase), a display label, and a running-count phrase.
- **City Category Configuration**: A per-city mapping from the city's vehicle types to category names. Optional — absent means "use the default rail/bus rule." Authored once, in WebAPI configuration.
- **Route (category-relevant view)**: A transit route now carries the category it belongs to and its underlying vehicle type (the latter used only to determine display ordering). Categories are assigned to routes at load time.
- **Running Vehicle**: A live vehicle that, when displayed, is associated with a category (via its matched route, or "Unknown" if unmatched) and contributes to that category's running count.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In Toronto, streetcars appear as a distinct category — their own filter section, their own running-count row, and their own selectable/clearable filter — separate from both Rail and Bus.
- **SC-002**: For all four existing cities, the route filter panel, category order, section labels, and running-count copy are unchanged from before this feature (verified against the pre-change behavior).
- **SC-003**: A city that declares no category configuration requires zero configuration edits to keep working correctly.
- **SC-004**: Adding a new category for a city (once its label/phrase exist) requires no change to any fixed list of category names in shared or client code.
- **SC-005**: Categories always render in a consistent order with rail-family categories ahead of buses, deterministically, across cities and reloads.
- **SC-006**: A vehicle that cannot be matched to a route is visibly countable under an Unknown category and is never counted as a bus.
- **SC-007**: A category lacking a polished label, count phrase, or styling still renders readably (fallbacks apply) and never appears blank, broken, or crashes the panel/label.
- **SC-008**: Running-vehicle counts update within one data refresh cycle when vehicles enter or leave service, with no stale category counts.
- **SC-009**: On the map, rail-family vehicle markers are visibly larger than non-rail markers (the intended distinction now takes effect).

## Assumptions

- **Category names are lowercase, whitespace-free identifiers** (e.g. `streetcar`), authored by the same people who maintain city configuration; category-name validation (enforcing casing/format) is deferred and out of scope.
- **New category labels and count phrases are English-only** for this change, consistent with the project's existing pattern of deferring additional locales; the localization mechanism itself is reused unchanged.
- **Only Toronto needs an explicit category configuration on day one.** The other four cities intentionally declare nothing and rely on the default rule.
- **Per-category audio/voicing is out of scope.** Instruments are assigned per route, not per category; introducing a streetcar category requires no audio changes. "Dedicated streetcar voicing" remains a separate future feature.
- **The map keeps a binary rail-sized-vs-not marker distinction.** Giving every category its own marker size is out of scope.
- **The change is deployed as a coordinated release** across the server, worker, and client, consistent with the project's existing discipline for real-time data-format changes; there is no dual-format transition period.
- **Unifying the two existing configuration-reading paths** (WebAPI's and the worker's) is out of scope; both continue to coexist.
- **Migrating existing cities onto explicit category configuration** is out of scope; they get the new behavior for free via the default rule.
