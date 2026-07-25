# Feature Specification: Route Identity Naming Unification

**Feature Branch**: `039-route-identity-naming`
**Created**: 2026-07-12
**Status**: Draft
**Input**: User description: "Route identity naming unification: the codebase currently reuses the property/parameter name \"RouteId\" (and \"routeId\") to mean three genuinely different values across layers, which the team has confirmed causes bugs: (1) the true GTFS static route_id (WebAPI GtfsStaticLoader, GtfsEndpoints.GetRouteShape, its {city}:{route_id}-keyed store), (2) GTFS-RT's Trip.RouteId wire field, whose actual value is short-name-shaped for MARTA/WMATA rather than the static route_id, and (3) an internal \"join key\" composite computed independently in multiple places via RouteShortName ?? RouteId (Worker.cs _routeIndex and related caches, VehicleState.RouteId, RouteNearestPointRecord/RouteCrossingRecord sent over SignalR, RouteItem.RouteId and _routeShapeCache/_routeShapes keys on the Client, RouteBlurbStore.GetForRoute). Only RouteShapeProperties (Shared/GtfsData/RouteShapeFeature.cs) correctly and unambiguously carries both real GTFS fields (RouteId + RouteShortName) sourced straight from routes.txt/trips.txt. Goal: left-shift this ambiguity by giving the composite/RT-sourced \"join key\" value a distinct, self-documenting name (e.g. RouteJoinKey) everywhere it currently masquerades as RouteId/routeId, and reserve RouteId to mean only the true GTFS static route_id. Introduce a single shared helper to replace the independently-reimplemented `??` fallback sites with one canonical computation. No behavior change intended: this is a rename + de-duplication of an existing fallback expression, not a new join strategy."

## User Scenarios & Testing *(mandatory)*

<!--
  This feature's "users" are the developers who read, write, and extend the
  TransitJazz codebase (TransitDataWorker, Client, Shared). The value delivered
  is a codebase where a property name tells you unambiguously what value it
  holds, eliminating a documented class of silent-misjoin bugs.
-->

### User Story 1 - A developer can trust what "RouteId" means (Priority: P1)

A developer reading or writing code in `Worker.cs`, the Client ViewModels, or a
SignalR event record sees a field or variable named `RouteId` and can rely on
it meaning the true GTFS static `route_id` — the same value the WebAPI's
`GtfsEndpoints.GetRouteShape` and `{city}:{route_id}`-keyed shape store use.
Any place that instead holds the `RouteShortName ?? RouteId` composite (or a
value straight off the GTFS-RT wire, which is short-name-shaped for
MARTA/WMATA) is named something else — e.g. `RouteJoinKey` — so its identity
is self-evident without having to trace back through the constitution or prior
fallback logic.

**Why this priority**: This is the core ask — the naming collision is the
actual root cause of the "sometimes they get miscontrued for one another" bug
risk the team flagged. Without this, every other improvement is cosmetic.

**Independent Test**: Can be fully tested by picking any file touched in this
feature (e.g. `Worker.cs`, `RouteItem`, `RouteNearestPointRecord`) and
confirming every occurrence of `RouteId`/`routeId` in it now correctly
distinguishes "true GTFS route_id" from "join key" by name alone, with no
remaining ambiguous dual-purpose field.

**Acceptance Scenarios**:

1. **Given** the renamed codebase, **When** a developer greps for `RouteId`
   across `TransitDataWorker`, `Shared`, and `Client`, **Then** every
   remaining hit is a value sourced from true GTFS static `route_id` data
   (`RouteShapeProperties.RouteId`, the WebAPI's `routeId` route parameter and
   `{city}:{route_id}` store), not the short-name-or-fallback composite.
2. **Given** the renamed codebase, **When** a developer greps for
   `RouteJoinKey` (or the chosen replacement name), **Then** every hit
   corresponds to a site that previously held the `RouteShortName ?? RouteId`
   composite or a raw GTFS-RT `Trip.RouteId` wire value being used as a lookup
   key against the Worker's route index.

---

### User Story 2 - One canonical join-key computation instead of four (Priority: P2)

A developer who needs to compute "the key used to correlate a vehicle/route
across GTFS-RT and the static route index" calls a single shared helper
instead of re-typing `RouteShortName ?? RouteId` by hand. Today this
expression is independently reimplemented in `Worker.cs`,
`TransitMap.razor.cs`, `ApplicationViewModel.cs`, and `RouteFilterViewModel.cs`
(the last of these three times in the same LINQ chain) — four+ chances for
the logic to drift out of sync.

**Why this priority**: Directly serves the "left-shift data transformation to
deter future bugs" goal — one call site means one place to fix if the
fallback rule ever needs to change (e.g. a new city needs a different
precedence), instead of four.

**Independent Test**: Can be tested by confirming all known fallback sites
(`Worker.cs` route-index construction, `TransitMap.razor.cs` route-shape
cache, `ApplicationViewModel.cs` route-shape cache, `RouteFilterViewModel.cs`
route-item projection) call the same shared helper/property rather than
re-deriving the expression inline.

**Acceptance Scenarios**:

1. **Given** a `RouteShapeProperties` instance with both `RouteId` and
   `RouteShortName` populated, **When** any of the four consuming call sites
   computes its join key, **Then** all four produce identical output by
   calling the same helper, not four independent expressions.
2. **Given** a `RouteShapeProperties` instance with `RouteShortName` null,
   **When** the shared helper computes the join key, **Then** it falls back
   to `RouteId`, matching today's documented behavior (Principle VI) exactly.

---

### User Story 3 - Documentation matches the code it describes (Priority: P3)

A developer reading the constitution's Principle VI, `docs/MULTI_CITY_TRANSIT_DESIGN.md`,
or `docs/WMATA_GTFS_COMPATIBILITY.md` sees the same terminology
(`RouteJoinKey` or equivalent) that appears in the code, rather than prose
that itself conflates "route_short_name" and "routeId" as synonyms.

**Why this priority**: Lower priority than the code change itself, but
necessary so the next developer doesn't re-introduce the ambiguity by trusting
stale docs over the (now-clearer) code.

**Independent Test**: Can be tested by confirming the constitution and the two
named docs use the new terminology consistently wherever they currently say
"routeId" while describing the short-name-or-fallback composite.

**Acceptance Scenarios**:

1. **Given** the updated constitution, **When** a developer reads Principle
   VI, **Then** it names the join-key concept with the same term used in code,
   distinct from its description of the true GTFS `route_id`.

---

### Edge Cases

- What happens when `RouteShortName` is null/absent for a route (some transit
  agencies omit it)? The shared helper MUST fall back to `RouteId`, preserving
  exactly today's `RouteShortName ?? RouteId` behavior — this feature changes
  names and call-site count, not the fallback semantics.
- What happens for a city configured with a `RailRouteIdMap` (e.g. WMATA
  remapping `"BLUE"` → `"B"`)? The remapped value is still a join-key value,
  not a true `route_id` — it must be named/typed consistently with other
  join-key values, and the remap step itself is unchanged.
- What happens at the WebAPI boundary (`GtfsEndpoints.GetRouteShape`, its
  `{city}:{route_id}`-keyed store)? These already correctly mean true GTFS
  `route_id` and are explicitly OUT of scope for renaming — a reviewer should
  be able to confirm no WebAPI-layer identifier was touched.
- What happens to the MapLibre GeoJSON feature property and its JS consumers
  (`map-interop.js`, `vehicle-animator.js`)? They currently receive an opaque
  string under the key `routeId` that is actually the join-key composite; the
  property name sent across the JS interop boundary must be updated so JS
  code isn't left holding the one remaining place where the old ambiguous
  name persists.
- What happens to existing persisted or in-flight data (SignalR event
  payloads, cached client state) during a deploy? This is a pure rename with
  no wire-format value change (the same string values flow through, only
  field/property names change) — no migration or versioning concern for data
  at rest, since nothing is persisted to a durable store under these field
  names.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every property, field, parameter, and local variable that
  currently holds the `RouteShortName ?? RouteId` composite value (or a raw
  GTFS-RT `Trip.RouteId` wire value used as a lookup key against that
  composite-keyed index) MUST be renamed to a distinct name (e.g.
  `RouteJoinKey`) that does not contain the substring "RouteId" in a way that
  implies it is the true GTFS static route_id.
- **FR-002**: The name `RouteId` (in any casing: `RouteId`, `routeId`,
  `route_id`) MUST, after this change, refer exclusively to the true GTFS
  static `route_id` value as sourced from `routes.txt`/`trips.txt` — i.e. the
  meaning already correctly used by `RouteShapeProperties.RouteId`, the WebAPI
  `GtfsEndpoints.GetRouteShape` parameter, and the `{city}:{route_id}`-keyed
  shape store.
- **FR-003**: A single shared computation (e.g. a property or method on
  `RouteShapeProperties`) MUST provide the `RouteShortName ?? RouteId`
  fallback logic, and all current independent reimplementations of that
  expression (in `Worker.cs`, `TransitMap.razor.cs`, `ApplicationViewModel.cs`,
  and `RouteFilterViewModel.cs`) MUST be replaced with calls to it.
- **FR-004**: The rename MUST cover the TransitDataWorker (route index and
  related per-route caches, per-vehicle route tracking, `RailRouteIdMap`
  interaction), Shared (SignalR event records carrying route identity),
  and Client (route-filter view models, route-shape caches, route-blurb
  lookup, MapLibre GeoJSON route identity property and its JS consumers).
- **FR-005**: The WebAPI's true-`route_id` usage (`GtfsEndpoints`,
  `GtfsStaticLoader`'s `{city}:{route_id}` store) MUST NOT be renamed or
  otherwise altered by this feature.
- **FR-006**: This feature MUST NOT change any runtime behavior, join
  semantics, fallback precedence, or wire-format values — it is limited to
  identifier naming and de-duplicating an existing expression into one shared
  call site.
- **FR-007**: The constitution's Principle VI and the two affected reference
  docs (`docs/MULTI_CITY_TRANSIT_DESIGN.md`, `docs/WMATA_GTFS_COMPATIBILITY.md`)
  MUST be updated to use the new terminology consistently with the renamed
  code.
- **FR-008**: After the rename, it MUST be possible to determine, from the
  name alone, whether any given "route identity" value in this codebase is a
  true GTFS `route_id` or a join-key composite — no remaining site should
  require reading surrounding logic to disambiguate.

### Key Entities

- **Route static identity (`RouteId`)**: The GTFS static `route_id` value from
  `routes.txt`/`trips.txt` (e.g. `"26932"`). Globally meaningful within a
  city's static feed; used by the WebAPI to key its route-shape store.
- **Route join key (new name, e.g. `RouteJoinKey`)**: The value actually used
  to correlate GTFS-RT real-time data against the static route index — today
  computed as `RouteShortName ?? RouteId`, and for MARTA/WMATA typically equal
  to the public-facing short name (e.g. `"74"`), not the static `route_id`.
  This is the value carried through `Worker.cs`'s route index/caches, SignalR
  event records, Client route caches/view models, and the MapLibre route
  feature identity.
- **`RouteShapeProperties`**: The existing shared type that already correctly
  and unambiguously carries both `RouteId` and `RouteShortName` as distinct
  GTFS-sourced fields; becomes the home for the new shared join-key helper.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero remaining code sites where a field/parameter/variable named
  with "RouteId" holds a value other than the true GTFS static `route_id`
  (verifiable by an exhaustive grep-and-classify pass over the renamed areas).
- **SC-002**: The `RouteShortName ?? RouteId` fallback expression appears in
  exactly one place in the codebase (the shared helper); all other call sites
  invoke it rather than re-deriving it.
- **SC-003**: All existing automated tests continue to pass unmodified in
  their assertions of behavior (only identifier names in test code change),
  confirming no behavioral regression from the rename.
- **SC-004**: A developer unfamiliar with the historical ambiguity can
  correctly state, for any given "route identity" variable in the renamed
  areas, whether it's a true `route_id` or a join key, using only the
  variable's name — verified by spot-checking a sample of renamed sites
  against their doc comments/usage with no additional context needed.

## Assumptions

- The chosen replacement name for the composite/join-key value is
  `RouteJoinKey`, matching the name proposed during scoping; it may be
  refined during planning if a clearer alternative emerges, but the intent
  (a name that reads as "correlation key," not "the GTFS id") is fixed.
- This is a rename-and-consolidate refactor, not a redesign: the existing
  `RouteShortName ?? RouteId` precedence, the `RailRouteIdMap` per-city remap
  mechanism, and all current join behavior are preserved exactly.
- The MapLibre/JS interop boundary's `routeId` GeoJSON property name changes
  as part of this feature (per FR-004), since it is one of the places the
  ambiguous name currently leaks into JS; this requires updating
  `map-interop.js`/`vehicle-animator.js` alongside the C# rename, but no new
  JS behavior is introduced.
- No new persisted storage (parquet/telemetry) carries route identity fields
  today (per the feature-038 telemetry denormalization audit), so there is no
  data-migration concern for this feature.
- Test files referencing the renamed identifiers (`Worker.cs`,
  `RouteFilterViewModel.cs`, event record tests, etc.) are updated as part of
  this feature's implementation, not left for a follow-up.
