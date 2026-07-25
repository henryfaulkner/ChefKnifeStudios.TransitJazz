# Feature Specification: MapLibre Migration

**Feature Branch**: `007-maplibre-migration`
**Created**: 2026-05-18
**Status**: Draft
**Input**: Replace Azure Maps entirely with MapLibre GL JS + MapTiler, and clean up the POC files. Decision record: `specs/006-maplibre-poc/decision.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Transit Map Uses New Provider (Priority: P1)

A visitor to the site opens the transit map and sees live MARTA vehicle positions animated on the map — exactly as before — but the map is now powered by MapTiler vector tiles instead of Azure Maps. The experience is indistinguishable from the visitor's perspective; only the underlying provider has changed.

**Why this priority**: This is the entire point of the migration. Everything else (cleanup, documentation) is secondary to the production map working correctly on the new provider.

**Independent Test**: Start the full local stack, navigate to `/transit-map`, confirm base map tiles render, vehicle markers appear and animate, route lines display, and clicking a marker or the map body fires the expected handler — all without any Azure Maps network calls appearing in the DevTools Network panel.

**Acceptance Scenarios**:

1. **Given** the app is running, **When** a visitor navigates to `/transit-map`, **Then** MapTiler vector tiles render as the base map and no Azure Maps CDN resources are requested.
2. **Given** live SignalR data is flowing, **When** vehicle position batches arrive, **Then** vehicle markers appear on the map and animate smoothly along their routes.
3. **Given** the map is displaying vehicles, **When** a visitor clicks a vehicle marker, **Then** the marker-click handler fires; **When** the visitor clicks an empty area, **Then** the map-body-click handler fires.
4. **Given** route geometry has loaded, **When** the map is viewed, **Then** MARTA route lines are drawn on the map in their correct colors.

---

### User Story 2 - No Dead Code Remains (Priority: P2)

A developer opening the codebase for the first time sees a single map component (`Map.razor`) backed by a single animator (`vehicle-animator.js`) and a single interop file (`azure-maps-interop.js` is gone). There are no POC pages, no parallel implementations, no commented-out Azure Maps references, and no Azure Maps dependencies in `index.html`. The constitution accurately describes the auth model in use.

**Why this priority**: Dead code actively misleads future developers and inflates page load by loading an unused SDK. It should be removed in the same PR as the migration so the repo never ships with both SDKs loaded simultaneously.

**Independent Test**: Search the repository for any reference to `azure-maps`, `atlas.microsoft.com`, `ChefMap` (the old namespace), `MapLibreTest`, or `AzureMapsTest`; zero results should remain in production-path files. The Azure Maps auth endpoint is gone from the server. The constitution's Principle II accurately describes MapTiler's auth model.

**Acceptance Scenarios**:

1. **Given** the migration is complete, **When** the app loads in a browser, **Then** only MapLibre GL JS and MapTiler CDN resources appear in the Network panel — no Azure Maps CDN requests.
2. **Given** the migration is complete, **When** a developer searches the codebase for `ChefMap` (the old JS namespace), **Then** no results appear in any production file.
3. **Given** the migration is complete, **When** a developer opens `Components/`, **Then** exactly one map component exists: `Map.razor` (backed by MapLibre).
4. **Given** the migration is complete, **When** a developer reads the project constitution, **Then** Principle II accurately describes MapTiler's URL-restricted public key as the auth model in use.

---

### Edge Cases

- What happens if the MapTiler API key placeholder is still present in `appsettings.json` when the Azure Maps config is removed? The map must fail visibly (blank tiles, console error) rather than silently — so the developer notices immediately.
- What happens to the `AzureMapsTest.razor` page (if it still exists) — is it in scope for deletion or left as historical artifact?
- If any other page or component in the app currently references `Map.razor`'s public API, renaming it must not break those callers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The production transit map page (`/transit-map`) MUST render correctly using MapLibre GL JS and MapTiler tiles after the migration, with no regression in vehicle animation, route display, or click handling.
- **FR-002**: The `MapLibre.razor` component and its code-behind files MUST be renamed to `Map.razor` (and `.cs`, `.Helper.cs`) so the component name matches the production role it now fills.
- **FR-003**: `TransitMap.razor.cs` MUST be updated to reference the renamed component type; no other behavioral changes to that page are in scope.
- **FR-004**: The Azure Maps interop file (`azure-maps-interop.js`) and the Azure Maps vehicle animator (`vehicle-animator.js`) MUST be deleted.
- **FR-005**: The original `Map.razor`, `Map.razor.cs`, and `Map.razor.Helper.cs` (the Azure Maps-backed versions) MUST be deleted.
- **FR-006**: The Azure Maps CDN stylesheet `<link>` and script `<script>` tags MUST be removed from `index.html`; the MapLibre GL JS CDN tags MUST remain.
- **FR-007**: The Azure Maps auth endpoint (`GetMapsAuthToken` in `MapsEndpoints.cs`) and any Azure Maps configuration it depends on (client ID in `appsettings.json`, related DI registrations) MUST be removed from the server.
- **FR-008**: The POC page (`MapLibreTest.razor` and its code-behind) MUST be deleted; it served its purpose and must not appear in the production app's navigation or routing.
- **FR-009**: The `AzureMapsTest.razor` page (if present) MUST be deleted or confirmed already absent; it is an Azure Maps artifact with no production purpose.
- **FR-010**: The project constitution's Principle II MUST be updated to accurately describe MapTiler's URL-restricted public key as the auth model, replacing the Azure Maps-specific wording.
- **FR-011**: After the migration, the app MUST build and run without errors or warnings introduced by the migration itself.

### Key Entities

- **Map component**: The single Blazor component responsible for rendering the interactive map. After migration, this is the renamed `MapLibre.razor` — same public API, new backing provider.
- **MapTiler API key**: A URL-restricted public key stored in `appsettings.json`. Remains in place; the Azure Maps client ID entry is removed alongside it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The transit map page loads and displays MapTiler vector tiles with zero Azure Maps CDN network requests — verifiable via DevTools Network panel with no `atlas.microsoft.com` entries.
- **SC-002**: Vehicle markers appear and animate on the production map page within the same time window as before the migration (no animation regression detectable by observation).
- **SC-003**: A repository-wide search for `atlas.microsoft.com`, `ChefMap` (old JS namespace), `mapAccClientId`, and `MapLibreTest` returns zero results in production-path files (source, wwwroot, and server config — excluding git history and spec artifacts).
- **SC-004**: The app builds with zero errors and zero new warnings after the migration.
- **SC-005**: The project constitution's Principle II contains no reference to Azure Maps as the current auth model.

## Assumptions

- The `AzureMapsTest.razor` page exists in the repo and is in scope for deletion; if it is already absent, that item is a no-op.
- No other pages or components in the codebase reference `Map.razor`'s public API beyond `TransitMap.razor.cs` — the rename is a contained change.
- The Azure Maps client ID in `appsettings.json` is not used by anything other than the map auth flow being removed; removing it does not affect other services.
- The MapTiler API key is already present and URL-restricted in `appsettings.json` from the POC work; no new account setup is required.
- The `maplibre-interop.js` and `maplibre-vehicle-animator.js` files retain their current names (they are already production-ready from the POC); only the Blazor component and the old Azure Maps files need renaming/deletion.
- The `perf-observer.js` file remains in `wwwroot/js/` and in `index.html`; it is a shared utility with no provider dependency.
- The `css/azure-maps-styles.css` local stylesheet (if it contains only Azure Maps overrides) is in scope for deletion alongside the Azure Maps CDN link.
