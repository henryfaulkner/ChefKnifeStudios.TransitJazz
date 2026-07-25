# Quickstart: Validating the Route Identity Naming Unification

This feature has no new runtime behavior to demo — validation is entirely
"did the rename land correctly and did nothing break." Steps:

## 1. Build

```powershell
dotnet build ChefKnifeStudios.MartaJazz.sln
```

Must succeed with zero errors. A missed rename site (e.g. a call site still
referencing the old `RouteId` property name on a renamed record) will fail to
compile — this is the primary safety net for an exhaustive rename.

## 2. Run existing tests

```powershell
dotnet test src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests
```

All existing tests MUST pass unmodified in their *assertions* (SC-003) —
only identifier names inside test code change to match the rename.

## 3. Grep-verify the rename is exhaustive (SC-001, SC-002)

```powershell
# Should return ONLY true-route_id sites (RouteShapeProperties.RouteId,
# GtfsEndpoints, GtfsStaticLoader, and the GTFS-RT wire model fields):
git grep -n "RouteId" -- src/ ':(exclude)src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI' ':(exclude)*GtfsRtModels.cs'

# Should return exactly ONE hit — the RouteShapeProperties.JoinKey definition:
git grep -n "RouteShortName ?? RouteId"
```

## 4. Manual smoke test (map still renders and animates correctly)

Start the app via the AppHost (`ChefKnifeStudios.MartaJazz.AppHost`) and
confirm in the browser:
- Routes render on the map (no blank/missing route layer — would indicate a
  broken `routeJoinKey` GeoJSON property mismatch between C# and JS).
- Vehicles animate along routes (would break if `vehicle-animator.js`'s
  `routeJoinKey` lookups don't match the geometry cache keys).
- Route-filter grid selection still scopes the bus count and tones (would
  break if `RouteItem.RouteJoinKey`/`SelectedRouteJoinKeys` wiring is
  inconsistent between `RouteFilterViewModel` and `TransitMap.razor.cs`).
- Checkpoint crossings still pulse and trigger tones (would break if
  `RouteCrossingRecord.RouteJoinKey` isn't threaded through to
  `PulseCheckpointAsync`/`TriggerNoteAsync`).

This is the same golden-path smoke test as any map/audio-affecting change —
no new scenarios are introduced, since FR-006 guarantees no behavior change.
