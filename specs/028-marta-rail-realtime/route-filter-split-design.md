# Design: RouteFilter Rail / Bus Split

**Feature branch**: `028-marta-rail-realtime`
**Date**: 2026-06-25
**Method**: Decision-tree interview (grill-me)

---

## Decision Tree

### Q1 — How does `RouteItem` know its transit mode?

**Options considered:**
- Add `TransitMode Mode` to `RouteItem`, derived from live `_railVehicleIds` (populated by incoming SignalR batches)
- Add `TransitMode Mode` to `RouteItem`, derived from GTFS shape data at route-load time

**Decision:** `TransitMode Mode { get; init; }` added to `RouteItem`, set in `BuildRouteItems()` from the `RouteShapeFeature` properties. No hardcoded rail route names. No dependency on live vehicle observation.

**Rationale:** Live `_railVehicleIds` only populates after the first SignalR batch arrives, so pills would briefly appear in the wrong section on cold start. Mode from the GTFS shape is known the moment `RoutesLoaded` fires — same moment pills appear — so rail pills are classified correctly from first paint.

---

### Q2 — What is the visual layout of the two sections?

**Options considered:**
- **A**: Rail pills on their own compact row above the bus grid, with thin section labels
- **B**: Single continuous grid with a full-width divider element between sections
- **C**: Two fully distinct stacked sections each with their own header and grid

**Decision: A** — Rail pills in their own flex row above the existing 6-column bus grid. A thin section label ("Rail" / "Buses") precedes each section.

**Rationale:** Rail has only 4 routes; a dedicated compact row suits the count. Bus grid is unchanged (`repeat(6, 1fr)`). No vertical space wasted on heavy section chrome.

---

### Q3 — When does the rail section appear?

**Options considered:**
- **A**: Always shown from startup (mode from GTFS shapes, same moment as bus pills)
- **B**: Appears only after the first rail vehicle is observed (mode from live `_railVehicleIds`)

**Decision: A** — matches bus behavior. Bus pills appear the moment `RoutesLoaded` fires from GTFS shape data. Rail pills follow the same path.

**Consequence:** Mode must come from `RouteShapeProperties` (server-side GTFS `route_type`), not from live vehicle observation. See Q1.

---

### Q4 — Is selection scoped per section or global?

**Options considered:**
- **A**: Global — selecting any pill dims all non-selected pills across both sections
- **B**: Section-scoped — selecting a rail pill only dims other rail pills; bus pills unaffected
- **C**: No cross-section dimming, but cross-section multi-select allowed

**Decision: A** — global selection pool. `SelectedRouteIds`, `HoveredRouteId`, and dimming logic are unchanged. The map filters by route regardless of mode.

**Rationale:** Forking the VM selection state for B/C is significant complexity for an edge case. Global dimming is unambiguous and requires zero changes to existing interaction logic.

---

### Q5 — Where do section header labels come from?

**Options considered:**
- `IStringLocalizer<RouteFilterResources>` (resx keys), consistent with all other labels in the component
- Hardcoded strings (proper nouns, unlikely to change)

**Decision:** Resx for both. Add `Rail` key (value `"Rail"`). Reuse existing `Buses` key (value `"Buses"`).

---

### Q6 — Where does the Clear button live?

**Options considered:**
- **A**: Stays in its own full-width controls row above both sections (current position)
- **B**: Moves above the Bus section only
- **C**: First cell in the Rail row

**Decision: A** — no change. The Clear button already lives in its own `.route-filters__controls` div outside the `@foreach`. Clear-all is a top-level action, not scoped to either mode.

---

### Q7 — What happens if a section has zero routes?

**Options considered:**
- **A**: Hide the section entirely (no label, no pills) when zero `RouteItem`s have that mode
- **B**: Always show both sections, even when empty

**Decision: A** — both sections hide when empty. Mirrors the `TransitRunningLabel` hide-when-zero pattern. No orphaned section label with nothing under it. Applies symmetrically to both Rail and Bus sections.

---

### Q8 — Does the Clear button hide when there is no selection?

**Options considered:**
- **A**: Always visible (current behavior)
- **B**: Hidden when `HasSelection` is false

**Decision: A** — no change. Stable layout; no reflow on selection state change.

---

## Agreed Spec

### Data model changes

| Layer | Change |
|---|---|
| `RouteShapeProperties` (Shared) | Add `TransitMode Mode` field |
| `GtfsStaticLoader.ParseRouteMetadata()` (Server) | Read `route_type` from `routes.txt`; map `1 → Rail`, all others `→ Bus` |
| `GtfsStaticLoader.BuildLineStringFeature()` (Server) | Include `mode` in serialized GeoJSON properties |
| `RouteItem` (Client Shared) | Add `TransitMode Mode { get; init; }` |
| `RouteFilterViewModel.BuildRouteItems()` (Client Shared) | Set `Mode` from `RouteShapeFeature.Properties.Mode` |
| `RouteFilterViewModel._railVehicleIds` (Client Shared) | Remove — mode now comes from shape data, not live observation |

### Component changes

| File | Change |
|---|---|
| `RouteFilters.razor` | Split `@foreach` into Rail section + Bus section, each with a label and hide-when-empty guard; Clear button stays in its existing controls row above both |
| `RouteFilterResources.resx` | Add `Rail = "Rail"` key |

### Layout structure

```
[ Clear ]                          ← full-width controls row, always visible

[ Rail ]                           ← section label, hidden if no rail routes
[ RED ][ GOLD ][ BLUE ][ GREEN ]   ← flex row

[ Buses ]                          ← section label, hidden if no bus routes
[ 1  ][ 3  ][ 6  ][ 12 ][ 15 ][ 19 ]   ← repeat(6, 1fr) grid
[ ... continued ...                ]
```

### Selection behavior

Global pool — no change to `SelectedRouteIds`, `HoveredRouteId`, or dimming logic. Selecting any pill dims all non-selected pills across both sections.
