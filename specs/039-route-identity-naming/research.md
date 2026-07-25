# Phase 0 Research: Route Identity Naming Unification

## R1: Should the GTFS-RT wire model's `Trip.RouteId` field be renamed?

**Decision**: No. `TripDescriptor.RouteId` and `EntitySelector.RouteId` (`GtfsRtModels.cs`) keep their names.

**Rationale**: These are protobuf-mapped fields whose names mirror the external GTFS-RT Realtime spec, which genuinely calls the field `route_id` on the wire — even though its *value* happens to be short-name-shaped for MARTA/WMATA. Renaming a wire-model field to something project-specific (e.g. `RouteJoinKey`) would misrepresent the external contract and make it harder to cross-reference against the GTFS-RT spec or other feeds where `Trip.RouteId` genuinely does carry the static `route_id`. The ambiguity this feature fixes is in *our* code's naming of *derived/consumed* values, not in third-party wire models we deserialize as-is.

**Alternatives considered**:
- Rename the wire field too, for total consistency → rejected: breaks the "this struct mirrors an external spec" contract and would need a translation layer immediately after deserialization anyway, which is exactly what `Worker.cs:361` (`string? routeId = entity.Vehicle.Trip?.RouteId;`) already does — the translation point is where the rename takes effect (the local variable becomes `routeJoinKey`).
- Leave wire field ambiguous and only rename derived values → this is the chosen approach; it draws the boundary at "our code" vs. "third-party deserialization target."

## R2: Should `RailRouteIdMap` (config key name) be renamed?

**Decision**: No. The `RailRouteIdMap` config property name (`CityConfig.cs`, `appsettings.json` `RailRouteIdMap` blocks) and `GtfsRtCity.ApplyRailRouteIdMap` method name are unchanged.

**Rationale**: This mechanism remaps GTFS-RT wire values (`Trip.RouteId`, e.g. WMATA's `"BLUE"`) to other GTFS-RT-shaped wire values (`"B"`) *before* the worker ever reads them as a join key — it operates entirely within "wire value space," consistent with R1's decision to leave the wire model's naming alone. Renaming it would suggest it operates on the new `RouteJoinKey` concept, which it doesn't (it runs upstream of that translation point, mutating the deserialized protobuf object in place).

**Alternatives considered**: Rename to `RailRouteJoinKeyMap` → rejected per the same reasoning as R1; this map's inputs and outputs are both wire-shaped `Trip.RouteId` values, not the internal join key.

## R3: Exhaustive site inventory — confirming nothing was missed

**Decision**: The inventory in `data-model.md` is treated as authoritative, built by cross-referencing the original Explore-agent audit against direct `Grep` verification of every file it named, plus one file the audit's summary omitted (`TransitMap.razor.cs`'s local `CrossingEventDto(string VehicleId, string RouteId, ...)` record at line 594, and `RoutePoint` in `Shared/Geospatial/RoutePoint.cs`).

**Rationale**: Read-verified via direct `Grep`/`Read` tool calls (not agent-reported) against:
- `Worker.cs` (all `_routeIndex`/`_routeMode`/`_routeCumDist`/`_routeTriggerPoints`/local `routeId` sites — lines 31-37, 75-82, 169-181, 211, 227-265, 297-303, 348-580, 609, 702-703)
- `VehicleState.cs` (record param + XML doc, line 22)
- `RouteNearestPointBatchEvent.cs` / `RouteCrossingBatchEvent.cs` (record fields, lines 33 / 11)
- `RoutePoint.cs` (record struct field, line 3)
- `RouteFilterViewModel.cs` (`RouteItem.RouteId`, `SelectedRouteId(s)`, `HoveredRouteId`, 3x inline `??` — lines 16, 32-34, 226-232, 248-288)
- `ApplicationViewModel.cs` (cache key + log message, lines 26, 131, 134)
- `RouteBlurbStore.cs` (`GetForRoute` param, lines 9, 26-32)
- `TransitMap.razor.cs` (`_routeShapeCache`, `ConfigureTrackerForRouteAsync`, `CrossingEventDto`, log templates — lines 67, 417, 432-461, 490-541, 576-580, 594)
- `map-interop.js` (GeoJSON property + MapLibre match expressions + cache keys — lines 289-525)
- `vehicle-animator.js` (`state.routeId`/`rec.routeId` — lines 101-459)
- `GtfsRtCity.cs` (`ApplyRailRouteIdMap` — confirmed out of scope per R2, lines 37, 60-68)

**Alternatives considered**: Trust the agent audit's summary as-is → rejected; direct verification found `CrossingEventDto` and `RoutePoint`, two sites the audit's prose summary didn't explicitly enumerate (though its "every RouteId site" grep-based conclusion implicitly covered them). Direct verification also confirms exact line numbers for `data-model.md`, which the audit's high-level summary didn't always pin down.

## R4: Naming — `RouteJoinKey` vs. alternatives

**Decision**: `RouteJoinKey` (confirmed, matches spec.md Assumptions).

**Rationale**: Reads unambiguously as "the key used to join/correlate," distinct from "the GTFS id." Alternatives like `RouteShortNameOrId` are more literal but tie the name to the *current* fallback implementation (`ShortName ?? Id`) rather than its *purpose* — if a future city needed a three-way fallback or a different precedence, `RouteShortNameOrId` would become misleading while `RouteJoinKey` would not.

**Alternatives considered**:
- `RouteShortNameOrId` — rejected: over-specifies the implementation in the name.
- `RouteKey` — rejected: too generic: could be misread as "any key associated with a route" (e.g. a dictionary key unrelated to GTFS-RT correlation), losing the "this is specifically the join key" signal.
- `RouteDisplayId` — rejected: the value is used for correlation/lookup, not display (display is `RouteShortName`, unaffected by this feature).
