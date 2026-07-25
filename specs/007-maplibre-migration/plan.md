# Implementation Plan: MapLibre Migration

**Branch**: `007-maplibre-migration` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/007-maplibre-migration/spec.md`

## Summary

Promote the POC's `MapLibre.razor` component to be the production map component, and delete every Azure Maps artifact left in the repo. The functional code is already proven from POC 006; this feature is a mechanical rename/delete/rewire pass plus a constitution amendment. No new behavior is introduced — the production map page (`/transit-map`) keeps the same UX, only the underlying provider changes.

The migration touches three layers: (1) the Blazor frontend (delete old `Map.razor*` and Azure interop JS, rename `MapLibre.razor*` → `Map.razor*` and its JS namespace, delete POC and Azure test pages, drop CDN tags from `index.html`); (2) the server (delete `MapsEndpoints.GetMapsAuthToken` and its Azure SDK dependencies, drop `AzureMaps` config); (3) the project constitution (Principle II rewritten for MapTiler's URL-restricted public-key model).

## Technical Context

**Language/Version**: C# / .NET 10.0 (Blazor WebAssembly, ASP.NET Core Minimal API), JavaScript ES2017+ (browser interop)
**Primary Dependencies (kept)**: MapLibre GL JS v4.x (CDN), MapTiler vector tiles, `Microsoft.JSInterop`, `Microsoft.AspNetCore.SignalR.Client`
**Primary Dependencies (removed)**: Azure Maps Web SDK (`atlas.min.js`, `atlas.min.css`), `Azure.Identity` and `Azure.Core` (only used by `GetMapsAuthToken`)
**Storage**: No storage changes. In-browser MapLibre GeoJSON sources persist as before.
**Testing**: Manual verification — start local stack, navigate to `/transit-map`, confirm vehicles animate, click handlers fire, no `atlas.microsoft.com` requests in DevTools Network. Repo-wide grep for the search-term list in spec SC-003.
**Target Platform**: Same as POC — modern WebGL-capable browser (Chrome/Edge/Firefox latest).
**Project Type**: Web frontend cleanup + thin server endpoint removal + governance amendment.
**Performance Goals**: No regression vs. POC's `MapLibreTest.razor` behavior (which already passed gates b/c/d).
**Constraints**: The migration must land in a single PR (rename + deletions are not safely separable — the JS namespace name `ChefMapLibre` becomes `ChefMap`, and both the new and old JS must not coexist with the same name).
**Scale/Scope**: ~10 files renamed, ~10 files deleted, ~5 files edited (server `Program.cs`, both `appsettings*.json`, `index.html`, `TransitMap.razor.cs`, `Map.razor.cs`/Helper after rename), constitution amended. No `Server.WebAPI.csproj` reference removal until verified (deferred to verification step).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | PASS | No architectural changes. SignalR, WebAPI, Worker, Static Web App boundaries unchanged. |
| II. No Frontend Secrets | **AMENDED BY THIS FEATURE** | This feature *is* the amendment. The current wording binds the no-secrets principle to Azure Maps specifically; the migration rewrites it to bind to MapTiler's URL-restricted public-key model. No secret remains in the frontend bundle — the URL-restricted key is the *compensating control* (key cannot be used by origins outside the project's domains). The amendment moves Principle II from "uses Azure Maps Auth Function" to "uses URL-restricted public key with origin enforcement." Constitution version bumps from 2.0.0 → 3.0.0 (MAJOR — Principle II is redefined). |
| III. Two-Pass Real-Time Data Processing Pipeline | PASS | Worker untouched. Frontend continues to consume `RouteNearestPointBatchEvent` and `VehiclePositionUpdatedEvent`. |
| IV. OpenTelemetry Observability | PASS | No observability surface changes. |
| V. Azure DevOps CI/CD Pipeline | PASS | Same WASM build, same Worker Docker image. The `Azure.Identity` and `Azure.Core` package removals are dependency hygiene; they reduce the WASM bundle and Server image slightly but do not change the artifact set. |
| VI. GTFS ID Mapping | PASS | `routeShortName` join key untouched on both ends. |

**Post-Phase-1 Re-check**: The amendment to Principle II is an explicit deliverable of this feature (see FR-010, the constitution edit). After Phase 1 design lands, the constitution edit is the gate that closes this row — until then, this row's status is "AMENDED BY THIS FEATURE" with the amendment scheduled as a task in `/speckit-tasks`.

## Project Structure

### Documentation (this feature)

```text
specs/007-maplibre-migration/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — research decisions (small: 2 items)
├── data-model.md        # Phase 1 — file-level rename/delete/edit catalog (no runtime data model changes)
├── quickstart.md        # Phase 1 — verification protocol
├── contracts/
│   └── constitution-amendment.md   # Phase 1 — the exact Principle II rewrite
├── checklists/
│   └── requirements.md  # already created
└── tasks.md             # Phase 2 output (`/speckit-tasks`, not this command)
```

### Source Code (repository root)

```text
src/
├── Client/
│   ├── ChefKnifeStudios.TransitJazz.Client.Shared/
│   │   └── Components/
│   │       ├── Map.razor                       # DELETED then RECREATED (was Azure, becomes MapLibre via rename)
│   │       ├── Map.razor.cs                    # DELETED then RECREATED
│   │       ├── Map.razor.Helper.cs             # DELETED then RECREATED
│   │       ├── MapLibre.razor                  # RENAMED → Map.razor
│   │       ├── MapLibre.razor.cs               # RENAMED → Map.razor.cs (type + namespace stays as Map)
│   │       └── MapLibre.razor.Helper.cs        # RENAMED → Map.razor.Helper.cs
│   │
│   └── ChefKnifeStudios.TransitJazz.Client.WebApp/
│       ├── Pages/
│       │   ├── TransitMap.razor                # unchanged
│       │   ├── TransitMap.razor.cs             # EDITED — only the [Inject] / type-name references shift back to `Map` (same name, new backing)
│       │   ├── AzureMapsTest.razor             # DELETED
│       │   ├── AzureMapsTest.razor.cs          # DELETED
│       │   ├── MapLibreTest.razor              # DELETED
│       │   └── MapLibreTest.razor.cs           # DELETED
│       │
│       └── wwwroot/
│           ├── index.html                      # EDITED — remove atlas CDN <link>/<script> + azure-maps-styles.css <link>; remove old /js/azure-maps-interop.js + /js/vehicle-animator.js script tags; rename /js/maplibre-interop.js script src → /js/map-interop.js (after JS rename); same for animator
│           ├── appsettings.json                # EDITED — remove "AzureMaps" block
│           ├── css/
│           │   └── azure-maps-styles.css       # DELETED (file body verified empty of non-Azure rules; only `.job-site-pin` remains and is unused after AzureMapsTest deletion)
│           └── js/
│               ├── azure-maps-interop.js       # DELETED
│               ├── vehicle-animator.js         # DELETED
│               ├── maplibre-interop.js         # RENAMED → map-interop.js; window.ChefMapLibre → window.ChefMap
│               ├── maplibre-vehicle-animator.js  # RENAMED → vehicle-animator.js (after the old one is deleted); window.ChefMapLibreAnimator → window.ChefMapAnimator
│               └── perf-observer.js            # KEPT — provider-agnostic; remove the `.start('baseline')` call in TransitMap.razor.cs since there is no baseline anymore (or keep it as a single-page perf marker)
│
└── Server/
    └── ChefKnifeStudios.TransitJazz.Server.WebAPI/
        ├── EndpointGroups/
        │   └── MapsEndpoints.cs                # DELETED (entire file — single endpoint, Azure-Maps-only)
        ├── Program.cs                          # EDITED — remove the `.MapMapsEndpoints()` call (line 124)
        └── appsettings.Development.json        # EDITED — remove "AzureMaps" block

src/ChefKnifeStudios.TransitJazz.Shared/
└── ApiEndpoints.cs                             # EDITED — remove the `public static class Maps` block

.specify/
└── memory/
    └── constitution.md                         # EDITED — Principle II rewritten; version bumped 2.0.0 → 3.0.0; Sync Impact Report at top updated; "Frontend (Blazor WASM)" tech-stack section's Azure Maps wording revised

(no other projects modified)
```

**Structure Decision**: All changes are renames, deletions, or single-line edits to existing files. No new source files are introduced. The MapLibre POC files already conform to the production conventions (same Blazor component shape, same JS interop pattern). The rename pass uses the existing POC artifacts verbatim under their final production names; the JS-namespace rename (`ChefMapLibre` → `ChefMap`) is a global find-and-replace within the two relevant JS files plus the Helper.cs file that calls them.

The migration cannot be split into pre/post-merge slices: `Map.razor` cannot exist as both an Azure and a MapLibre component simultaneously without breaking the `TransitMap.razor.cs` call site, and `window.ChefMap` cannot point to two different namespaces at once. One PR.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Constitution amendment (Principle II rewrite, version 2.0.0 → 3.0.0) | The current Principle II names Azure Maps as the auth-bearing provider. The migration replaces that provider entirely. Leaving the principle as-written would create a permanent governance lie — the constitution would describe an architecture that no longer exists. | Keeping the old wording and treating MapTiler as a "documented exception" (the path POC 006 took) was correct for a POC but wrong for the post-migration state. A permanent exception in the constitution would normalize drift. The MAJOR version bump is intentional: the principle is *redefined*, not extended. |

No other complexity tracking entries. Every other change is a rename or deletion of code that the spec explicitly requires be removed.

## Phase 0 Research

See [research.md](research.md). Two open items, both small:

1. Does removing `Azure.Identity` and `Azure.Core` from the Server.WebAPI csproj break anything outside `MapsEndpoints.cs`? (grep for other usages)
2. Is the `.job-site-pin` CSS class in `azure-maps-styles.css` used by anything other than `AzureMapsTest.razor` (which is also being deleted)? If unused, delete the file; if used, keep just the rule and rename the file.

## Phase 1 Design

### Data Model

See [data-model.md](data-model.md). There is no runtime data model in this feature — the entities are *files in the repo*. The data-model document is a catalog of every file touched (rename, delete, edit) with the exact change, so the task generator and reviewer can both verify completeness.

### Contracts

See [contracts/constitution-amendment.md](contracts/constitution-amendment.md). The single "interface contract" this feature exposes is the new wording of Principle II in the constitution. The contract file contains the exact replacement text, the Sync Impact Report block update, and the version bump.

### Quickstart

See [quickstart.md](quickstart.md). The verification protocol after the migration lands:

- App builds clean (zero new warnings)
- Navigate to `/transit-map`, confirm tiles + vehicles + routes render
- DevTools Network panel shows zero `atlas.microsoft.com` requests
- Repo grep returns zero hits for the SC-003 search-term list outside spec artifacts and git history
- Constitution Principle II contains no Azure Maps reference
- The five SC items from `spec.md` are checked off in order

### Agent Context

`CLAUDE.md`'s SPECKIT marker block will be updated to point to this plan (`specs/007-maplibre-migration/plan.md`) as the active feature plan once the plan is committed.
