# Research: RouteFilter Rail / Bus Split

All open questions were resolved in the grill-me design doc
([route-filter-split-design.md](../028-marta-rail-realtime/route-filter-split-design.md), Q1–Q8).
This file records the resolutions plus the code-grounding deltas found while planning.

## Decision: Source of transit mode

- **Decision**: Mode comes from static GTFS `route_type` carried through `RouteShapeProperties`,
  set on `RouteItem` in `BuildRouteItems()`.
- **Rationale**: Pills appear the moment `RoutesLoaded` fires, before any SignalR batch. Live
  `_railVehicleIds` only populates after the first batch, so it would briefly misplace rail pills on
  cold start. Static mode is known at route-load time (design Q1/Q3; spec FR-002).
- **Alternatives considered**: Derive mode from live vehicle observation — rejected (cold-start
  flicker). Hardcode rail route names — rejected (brittle, design Q1).

## Decision: route_type mapping

- **Decision**: `route_type == 1 → Rail`, all other values → `Bus`. No hardcoded names (spec FR-010).
- **Rationale**: GTFS `route_type` 1 = "Subway/Metro" (MARTA heavy rail). MARTA's four heavy-rail
  lines are the only `route_type` 1 routes in the feed; everything else is bus.

## Decision: Layout (design Q2 = A)

- **Decision**: Rail pills in a compact flex row above the existing `repeat(6, 1fr)` bus grid; a thin
  section label precedes each section. Bus grid unchanged.
- **Rationale**: Only 4 rail routes — a dedicated row fits; no heavy section chrome.

## Decision: Selection scope (design Q4 = A)

- **Decision**: One global selection pool. `SelectedRouteIds`, `HoveredRouteId`, dimming unchanged.
- **Rationale**: Forking selection state per-section is large complexity for an edge case
  (spec FR-007/FR-008; constitution Principle IX preserved).

## Decision: Empty sections (design Q7 = A)

- **Decision**: Hide a section (label + pills) when it has zero routes, symmetric for both
  (spec FR-006).

## Decision: Clear button (design Q6/Q8 = A)

- **Decision**: No change — stays in its `.route-filters__controls` row above both sections, always
  visible (spec FR-009).

## Decision: Section labels (design Q5)

- **Decision**: resx keys via `IStringLocalizer<RouteFilterResources>`. Add `Rail="Rail"` and a
  dedicated `Buses="Buses"` section-label key.
- **Rationale**: Constitution XII — no hardcoded copy. The existing `SettingBusesVisible="Buses"` is
  a *settings* label, not a section header; a dedicated key keeps intent clear and decoupled.

## Code-grounding deltas (design doc was pre-source)

| Design doc said | Actual code | Plan resolution |
|---|---|---|
| Add `TransitMode Mode` to `RouteItem` | `RouteItem` uses `RouteId` (short name), no mode yet | Add `TransitMode Mode { get; init; }` |
| Remove `_railVehicleIds` | `_railVehicleIds` feeds the active **count** split, not grouping | **Keep it** — out of scope; removing breaks rail/bus counts |
| `route_type` mapping is "new" | `TransitMode` enum already exists in the RT event file | Reuse existing enum; no new type, no move |
| Serialize `mode` in GeoJSON | Properties are hand-serialized via `StringBuilder` | Append `"mode":"Rail"`/`"Bus"` string |
| Client deserialization | `HttpService` applies `JsonStringEnumConverter` + camelCase | String enum round-trips with zero extra config |

## Open questions

None. Spec has no `[NEEDS CLARIFICATION]` markers.
