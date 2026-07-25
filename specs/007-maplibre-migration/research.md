# Phase 0 Research: MapLibre Migration

**Feature**: 007-maplibre-migration | **Date**: 2026-05-18

The migration is mechanical — most decisions were settled in POC 006 and recorded in `specs/006-maplibre-poc/decision.md`. Only two open items needed research before Phase 1 design.

---

## R1 — Does removing `Azure.Identity` / `Azure.Core` from `Server.WebAPI` break anything outside `MapsEndpoints.cs`?

**Question**: `MapsEndpoints.cs` is the only file in `Server.WebAPI` that imports `Azure.Identity` and `Azure.Core` (for `DefaultAzureCredential` and `TokenRequestContext`). After deleting that file, will dangling references remain anywhere in the WebAPI project, or in any other project?

**Decision**: No csproj edits required. The migration deletes `MapsEndpoints.cs` and removes the `.MapMapsEndpoints()` call from `Program.cs:124`; that is sufficient.

**Findings**:

- `Server.WebAPI.csproj` does **not** list `Azure.Identity` or `Azure.Core` as direct `PackageReference` entries. The types are pulled transitively (likely via `Microsoft.Identity.Web` or another dependency).
- A repo-wide grep for `Azure.Identity`, `Azure.Core`, and `DefaultAzureCredential` returns three files:
  - `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/MapsEndpoints.cs` — being deleted by this feature
  - `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/TokenProvider.cs` — out of scope; Worker authenticates to its own dependencies, unrelated to Azure Maps
  - `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj` — the Worker's own package reference, untouched
- After `MapsEndpoints.cs` deletion, there is no remaining direct use of `Azure.Identity`/`Azure.Core` in `Server.WebAPI`. The transitive reference remains in the dependency graph but consumes no compile-time symbols from `Server.WebAPI`.

**Implication**: One file deleted, one line removed from `Program.cs`. No csproj surgery required. The Server.WebAPI build will be slightly smaller because dead-code elimination removes the unused Azure-Identity surface from the final image, but the csproj graph is unchanged.

---

## R2 — Is `wwwroot/css/azure-maps-styles.css` safe to delete in full?

**Question**: The file contains an empty `body { }` rule and a single `.job-site-pin` class. If `.job-site-pin` is used elsewhere (any Razor component, any other CSS, any JS), the file or rule must be preserved.

**Decision**: Delete the entire file. The `<link>` tag for it in `index.html` is removed in the same edit.

**Findings**:

- Repo-wide grep for `job-site-pin` returns exactly one match: the CSS file itself defining the class. No `.razor`, `.cs`, `.js`, or other CSS file references it.
- The class was originally written for `AzureMapsTest.razor`'s pin styling (`id = "job-site-{vehicleId}"`, `pinIcon = "stop-pin-red"`), but the deletion of `AzureMapsTest.razor*` removes the last potential caller.
- The `body { }` rule is empty and contributes nothing.

**Implication**: The whole file deletes safely. No renaming, no rule-preservation step required.

---

## Summary of Phase 0 Decisions

1. **Csproj edits**: None. Deleting `MapsEndpoints.cs` and its `Program.cs` call site is sufficient.
2. **CSS cleanup**: Delete `wwwroot/css/azure-maps-styles.css` in full and remove its `<link>` from `index.html`.

No NEEDS CLARIFICATION items remain.
