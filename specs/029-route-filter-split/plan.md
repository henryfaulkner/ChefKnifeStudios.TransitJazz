# Implementation Plan: RouteFilter Rail / Bus Split

**Branch**: `028-marta-rail-realtime` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/029-route-filter-split/spec.md`
**Design source**: [route-filter-split-design.md](../028-marta-rail-realtime/route-filter-split-design.md)

## Summary

Split the route-filter grid into a labeled **Rail** section (RED/GOLD/BLUE/GREEN) above a labeled
**Buses** section. Each pill's mode comes from **static GTFS `route_type`** carried through the
shapes pipeline (Shared → Server → Client), so rail pills are classified correctly from the first
paint — never from live vehicle observation. Selection, dimming, and the Clear control are
unchanged (one global selection pool). Empty sections hide entirely. Frontend + a small shared/server
data-shape addition; no change to the selection interaction model.

## Technical Context

**Language/Version**: C# / .NET 10.0, Blazor WebAssembly
**Primary Dependencies**: MatBlazor, CommunityToolkit.Mvvm, MapLibre GL JS (unaffected), `IStringLocalizer`
**Storage**: In-memory `IKeyValueRepository<string>` (server route shapes) — no schema/DB
**Testing**: Manual quickstart (this codebase has no automated UI test suite; consistent with prior 015–017 features)
**Target Platform**: WASM in browser; ASP.NET Core WebAPI host
**Project Type**: Web (Blazor WASM client + ASP.NET Core server + shared library)
**Performance Goals**: No new per-frame work; classification is one enum read at route-build time
**Constraints**: Rail classification correct from first filter paint (no cold-start misplacement)
**Scale/Scope**: 4 rail routes + existing bus routes; ~6 files touched

### Reconciliation with the design doc (deltas found in actual code)

The design doc was written before reading current source. Grounded against the code:

- `TransitMode { Bus = 0, Rail = 1 }` **already exists** (in
  `Shared/Events/RouteNearestPointBatchEvent.cs`) and is already used by
  `RouteFilterViewModel` to split active counts via `_railVehicleIds`.
- `RouteItem` exposes `RouteId` (the short name), **not** `RouteShortName`; the design doc's
  "add `RouteShortName`" note is stale — only add `TransitMode Mode`.
- **Do NOT remove `_railVehicleIds`.** The design doc says remove it, but it currently feeds the
  active **count** split (`RecomputeActiveTransitCounts`), which is a separate concern from pill
  grouping. Removing it would break the rail/bus count. This feature only adds `Mode` for grouping;
  count logic stays.
- The GeoJSON properties are **hand-serialized** in `BuildLineStringFeature` (a `StringBuilder`),
  so `mode` must be appended there; client deserialization already applies `JsonStringEnumConverter`
  + camelCase (`HttpService` → `JsonSettings.ApplyTo`), so a `"mode":"Rail"` string round-trips to
  the enum with no extra config.

## Constitution Check

*GATE: Must pass before Phase 0. Re-checked after Phase 1.*

| Principle | Relevant? | Compliance |
|---|---|---|
| VI. GTFS ID Mapping | Yes | Mode derived from `routes.txt` `route_type`; join key (`route_short_name`) unchanged. ✅ |
| IX. Persistent Multi-Selection | Yes | Selection set, dimming, Select-all/Clear unchanged — purely a visual regroup of the same pills. Global pool preserved (Q4 = A). ✅ |
| X. Zoom-Adaptive Controls | Indirect | Grid anchor logic untouched; layout change is internal to the filter component. ✅ |
| XI. Snappy Overlays | No | No new overlays/animation. ✅ |
| XII. Internationalized Presentation | Yes | Section labels via `IStringLocalizer<RouteFilterResources>`; new `Rail` + `Buses` keys added to the single `RouteFilterResources.resx`. EN-only this pass (Spanish deferred, consistent with 015–017). ⚠️ partial-but-tracked, same as prior features. |

**Localization note**: resx already has `SettingBusesVisible="Buses"` (a *setting* label) and
`NumBusesRunning`. Neither is a section header. Add dedicated `Rail="Rail"` and `Buses="Buses"`
section-label keys rather than overloading a setting key (clearer intent, avoids coupling).

No violations. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/029-route-filter-split/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (GeoJSON property contract)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (files this feature touches)

```text
src/ChefKnifeStudios.TransitJazz.Shared/
└── GtfsData/RouteShapeFeature.cs            # add TransitMode Mode to RouteShapeProperties

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
└── GtfsStatic/GtfsStaticLoader.cs           # parse route_type → Mode; serialize "mode" in GeoJSON

src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
├── ViewModels/RouteFilterViewModel.cs       # RouteItem.Mode; set from Properties.Mode in BuildRouteItems
├── Components/RouteFilters.razor            # split @foreach into Rail + Bus sections, hide-when-empty
└── Resources/RouteFilterResources.resx      # add Rail + Buses section-label keys
```

**Structure Decision**: Existing 3-tier layout (Shared / Server.WebAPI / Client.Shared). The change
threads one enum field from the GTFS parse through to the view-model, plus a presentation regroup in
one component. `TransitMode` lives where it already is (no enum move).

## Complexity Tracking

No constitution violations — section intentionally empty.
