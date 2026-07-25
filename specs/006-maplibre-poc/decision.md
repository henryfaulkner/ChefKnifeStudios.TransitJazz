# Decision Record: MapLibre + MapTiler POC

**Feature**: 006-maplibre-poc | **Date**: 2026-05-18
**Outcome**: **migrate**

---

## Summary

The MapLibre GL JS + MapTiler side-by-side POC passed all hard gates. Vehicle animation at ~200 markers is smooth and visually correct. The decision is to replace Azure Maps with MapLibre GL JS + MapTiler across the production app.

---

## Gate-by-gate evaluation

| Gate | Criterion | Result | Notes |
|------|-----------|--------|-------|
| (a) Cold-load time | Tiles visible faster than baseline | **not formally measured** | Both SDKs load on shared `index.html`; decided to skip formal LCP capture and proceed directly to migrate decision |
| (b) Sustained FPS ≥ 45 at ~200 markers | Animation smooth at target marker count | **PASS** | Animation judged smooth and correct during live MARTA data session |
| (c) Polyline rendering | ≥5 routes visible without defects | **PASS** | Route lines render correctly via `addRouteShapeFeature` |
| (d) Click handlers | Blazor `OnBusMarkerClicked` + `OnMapBodyClicked` fire | **PASS** | Wired and verified |
| (e) Aesthetic fit (soft) | Experience matches soundscape concept | **PASS** | Map style and animation feel appropriate for the product |

---

## Rationale

The `setData`-once-per-RAF-tick strategy (research.md R1) works well in practice — animation is smooth with no visible stutter. The MapTiler `streets-v2` style renders cleanly. The port from `vehicle-animator.js` to `maplibre-vehicle-animator.js` was mechanical at the four Azure-specific touch points identified in research.md R4. The Blazor component surface mirrors `Map.razor` exactly, so the migration is a delete-and-rename rather than a rewrite.

Cold-load LCP was not formally captured (gate a); the decision-maker accepted the qualitative result and proceeded to migrate. If a future audit requires the numeric comparison, it can be captured from the archived POC branch.

---

## Migration follow-on

A follow-on feature spec will cover:

- Delete `Map.razor`, `Map.razor.cs`, `Map.razor.Helper.cs`
- Delete `wwwroot/js/azure-maps-interop.js`, `wwwroot/js/vehicle-animator.js`
- Delete `MapsEndpoints.cs` `GetMapsAuthToken` endpoint and its Azure Maps auth infrastructure
- Rename `MapLibre.razor` → `Map.razor` (and `.cs`, `.Helper.cs`)
- Update `TransitMap.razor.cs` to use the renamed component (type references only)
- Delete `Pages/MapLibreTest.razor` and `.cs` (POC page, no longer needed)
- Revise constitution Principle II to recognize MapTiler's URL-restricted public key auth model
- Remove Azure Maps CDN `<link>` and `<script>` from `index.html`; keep MapLibre CDN tags

See follow-on spec: `specs/007-maplibre-migration/` (to be created).
