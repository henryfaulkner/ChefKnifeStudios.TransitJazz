# Feature Specification: Codebase Bloat Cleanup

**Feature Branch**: `030-codebase-bloat-cleanup`  
**Created**: 2026-06-26  
**Status**: Draft  
**Input**: User description: "clear the codebase bloat based on this document, C:\Projects\ChefKnifeStudios.TransitJazz\bloat-reports\20260626.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Remove Dead Code and Unreachable Files (Priority: P1)

A developer working in the codebase wants to eliminate all files and code paths with zero callers — including the unused POC directory, dead utility classes, no-op JS stubs, and the duplicate JsInterop implementation — so that the project surface area accurately reflects what the app actually uses.

**Why this priority**: Dead code is the highest-value removal: it reduces confusion about what is authoritative, lowers build times, and eliminates the risk of accidentally resurrecting a stale path. These items have no callers and carry no migration cost.

**Independent Test**: Delete each item in the dead-code category, run the build, and confirm zero compilation errors and no runtime regressions in the app's core flows (map load, route selection, audio, settings blade).

**Acceptance Scenarios**:

1. **Given** `apps/BusDataPoc/` exists in the repo, **When** the cleanup is applied, **Then** the directory is removed and no project or solution file references it.
2. **Given** `EventMapper.cs`, `JsonFlattener.cs`, `Discard.cs`, the duplicate `AudioPlayerJsInterop.cs`, and the no-op `audioPlayerJsInterop.js` stub exist, **When** the cleanup is applied, **Then** each file is deleted and the build succeeds with no unresolved references.

---

### User Story 2 - Remove Unused and Half-Installed Dependencies (Priority: P2)

A developer auditing the project's dependency graph wants all NuGet packages that are either commented out, never called, or only partially wired in to be removed — and their corresponding dead commented-out call sites deleted — so the project's dependencies reflect actual runtime requirements.

**Why this priority**: Unused dependencies inflate restore time, surface area for CVE exposure, and confusion about what the app actually uses. Commented-out wiring is ambiguous: it signals intent that was never completed and misleads future readers.

**Independent Test**: Remove each identified package reference, delete or uncomment the dead call sites, restore packages, build, and confirm the app starts and operates normally.

**Acceptance Scenarios**:

1. **Given** `Ardalis.Result` is referenced in the project, **When** it is replaced by an inline `ServiceResult<T>` type and the package reference removed, **Then** all existing callers compile and behavior is unchanged.
2. **Given** commented-out `Microsoft.Identity.Web`, `StackExchange.Redis`, and `Azure.Monitor.OpenTelemetry.AspNetCore` call sites exist in `Program.cs` / `Extensions.cs`, **When** those lines are deleted and the packages removed, **Then** the project builds cleanly.

---

### User Story 3 - Eliminate Single-Implementation Interfaces (Priority: P3)

A developer reading service registration code wants every `I*` interface that has exactly one implementation and is never mocked in tests to be removed — with DI registrations updated to use the concrete class directly — so that the indirection cost is eliminated without losing any capability.

**Why this priority**: 24 YAGNI interfaces create friction without benefit: they double the files to navigate, make rename refactors harder, and provide no testability benefit when no mock exists. Removing them is mechanical and low-risk once confirmed no tests rely on them.

**Independent Test**: Remove each identified interface, update DI registration and injection sites to the concrete type, build and run — confirm that dependency injection resolves correctly and the app functions normally.

**Acceptance Scenarios**:

1. **Given** `ITransitEndpointsService` is injected in components, **When** the interface is deleted and injection sites updated to `TransitEndpointsService`, **Then** the app builds and all dependent components resolve their dependencies at runtime.
2. **Given** `IEventArgs` is defined in two places, **When** both definitions are removed and all usages updated to the BCL `EventArgs`, **Then** the build succeeds with no remaining `IEventArgs` references.

---

### User Story 4 - Resolve Duplicated Logic (Priority: P3)

A developer tracing a distance calculation wants a single authoritative implementation of Haversine distance (and of `JsonOptions`) that all callers use, with the duplicate inline versions removed, so there is no risk of the copies diverging.

**Why this priority**: Duplicated logic — especially math — is a correctness hazard. Picking one canonical source and deleting the others eliminates the maintenance burden.

**Independent Test**: Identify which Haversine implementation is the canonical one, update all callers (C# inline + JS), delete the duplicates, and confirm the map and distance-dependent features (trigger points, checkpoint flash) work correctly.

**Acceptance Scenarios**:

1. **Given** Haversine logic exists in three locations (shared util, `TransitMap.razor.cs`, `vehicle-animator.js`), **When** the cleanup is applied, **Then** exactly one authoritative implementation remains and the other two locations reference it.
2. **Given** `JsonOptions.cs` and `JsonSettings.cs` both provide `PropertyNameCaseInsensitive` JSON settings, **When** one is deleted and callers updated, **Then** the build succeeds and JSON deserialization behavior is unchanged.

---

### User Story 5 - Fix Anti-Patterns and Security Gaps (Priority: P2)

A developer reviewing the codebase wants `Console.WriteLine` debug prints replaced with `ILogger<T>` calls (or deleted), empty catch blocks either removed or logged, the always-true `IsAllowedRoute()` method removed, commented-out `[Authorize]` attributes resolved, and `BatchDebugRecord.WriteBatchToDiskAsync` guarded behind a debug flag — so that the production app does not leak debug output or silently swallow exceptions.

**Why this priority**: The `Console.WriteLine` calls appear in production output; empty catch blocks hide exceptions; the unguarded disk-write method can produce unexpected side effects in production. These are correctness and operational hygiene issues, not just style.

**Independent Test**: After each fix, build and verify: no `Console.WriteLine` calls remain outside `#if DEBUG` guards; catch blocks either log or are removed; `IsAllowedRoute()` is gone; `[Authorize]` is either active or the line deleted; disk write is guarded.

**Acceptance Scenarios**:

1. **Given** 22 `Console.WriteLine` calls exist across production files, **When** cleanup is applied, **Then** zero `Console.WriteLine` calls remain in non-debug-guarded production code paths.
2. **Given** `LogEventWorker.cs` has empty catch blocks at lines 120, 126, 143, 149, **When** cleanup is applied, **Then** each catch either logs the exception with `ILogger` or is removed entirely.
3. **Given** `IsAllowedRoute()` always returns `true`, **When** cleanup is applied, **Then** the method is deleted and its callers are updated to inline `true` or the condition is removed.

---

### User Story 6 - Remove Superseded Infrastructure Files (Priority: P1)

A developer browsing the repo wants superseded Azure DevOps YAML pipeline files to be deleted so the CI/CD directory only contains active pipeline definitions.

**Why this priority**: Superseded pipeline files create confusion about which pipeline is authoritative and can accidentally be re-enabled. They carry no recovery value once marked superseded.

**Independent Test**: Delete the two superseded YAML files, confirm no active pipeline references them, and verify the active pipeline continues to succeed.

**Acceptance Scenarios**:

1. **Given** 2 Azure DevOps YAML files marked "SUPERSEDED" exist in the repo, **When** the cleanup is applied, **Then** both files are deleted and no active pipeline configuration references them.

---

### User Story 7 - Address Large Static Data Files (Priority: P3)

A developer cloning the repo or reviewing bundle size wants the two large static JSON files (`neighborhood_routes_full.json` at 722 KB and `neighborhood_routes.json` at 120 KB) either moved to Git LFS or replaced with API-fetched data at startup — so that the repo clone and build artifact sizes are reduced.

**Why this priority**: Large static JSON in source control bloats every clone and makes the WASM payload heavier. This is lower priority than correctness items but has compounding impact.

**Independent Test**: Move files to Git LFS (or convert to API fetch), clone a fresh repo, confirm file sizes are not fully materialized in the working tree, and confirm the features that read these files continue to function.

**Acceptance Scenarios**:

1. **Given** `neighborhood_routes_full.json` (722 KB) is committed as a regular git object, **When** cleanup is applied, **Then** it is tracked by Git LFS (pointer in tree) or replaced with a runtime API call, and neighborhood-related features work correctly.
2. **Given** `neighborhood_routes.json` (120 KB) is committed as a regular git object, **When** cleanup is applied, **Then** it is tracked by Git LFS or replaced with a runtime API call.

---

### Edge Cases

- Removing an interface that IS used in a test mock must NOT proceed until the test is updated or the test's mock strategy is reconsidered.
- `SignalRTest.razor` references `JsonFlattener.cs` — both must be removed together or the build will fail; they are treated as a unit.
- `mdc-overrides.css` should only be deleted after confirming MatBlazor is no longer referenced in the project.
- `AudioPlayerJsInterop` exists in both `Client.Core` and `Client.Shared` — the canonical location must be identified before deleting either copy, and all injection sites updated to the surviving one.
- The `[Authorize]` comment on `WorkerTransitHub.cs` must be resolved intentionally (not just deleted) — the decision to leave the hub unprotected must be explicit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The project MUST build successfully (zero compile errors) after all cleanup steps are applied.
- **FR-002**: All dead-code files identified in the bloat report MUST be deleted from the repository.
- **FR-003**: The `apps/BusDataPoc/` directory MUST be removed and no solution or project file may reference it.
- **FR-004**: Duplicate implementations (AudioPlayerJsInterop, IEventArgs, Haversine, JsonOptions/JsonSettings) MUST be collapsed to a single canonical version with all callers updated.
- **FR-005**: All 24 single-implementation interfaces MUST be removed; DI registrations MUST use concrete types directly.
- **FR-006**: Zero `Console.WriteLine` calls MUST remain in production (non-`#if DEBUG`) code paths after cleanup.
- **FR-007**: Empty catch blocks in `LogEventWorker.cs` MUST be replaced with structured logging or removed entirely.
- **FR-008**: `IsAllowedRoute()` MUST be deleted; call sites MUST inline `true` or remove the condition.
- **FR-009**: Superseded Azure DevOps YAML files MUST be deleted.
- **FR-010**: Unused NuGet packages (`Ardalis.Result` dependency removed or replaced inline; `Microsoft.Identity.Web`, `StackExchange.Redis`, `Azure.Monitor.OpenTelemetry.AspNetCore` removed) MUST no longer appear in project files.
- **FR-011**: Dead JavaScript (the `addRouteShapeFeature` wrapper, `toggleTraffic` stub) MUST be deleted from `map-interop.js`.
- **FR-012**: `BatchDebugRecord.WriteBatchToDiskAsync` MUST be guarded with a `#if DEBUG` preprocessor directive or a runtime configuration flag so it does not execute in production.
- **FR-013**: The app's primary user flows (map load, route selection, audio soundscape, settings blade, checkpoint flash) MUST continue to function correctly after all cleanup steps.

### Key Entities

- **Bloat Report**: The audit document at `bloat-reports/20260626.md` — the authoritative list of items to remove; cleanup scope is bounded by this document.
- **Canonical Implementation**: For each duplicated item, the single surviving copy agreed upon before deletion of the others.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The project build produces zero compilation errors and zero new warnings after all cleanup steps are complete.
- **SC-002**: The total number of tracked source files decreases by at least 10 files (dead code, superseded pipelines, no-op stubs).
- **SC-003**: Zero `Console.WriteLine` calls remain in non-debug-guarded production source files.
- **SC-004**: The count of `I*` interface files in `Client.Core` and `Client.Shared` drops to zero for interfaces with a single implementation.
- **SC-005**: All primary app flows (map load, route select, audio, settings, checkpoint) pass manual smoke-test after cleanup.
- **SC-006**: Git LFS tracking (or API-fetch replacement) reduces the blob size committed for the two large JSON files.

## Assumptions

- All items in the bloat report are in scope; no items have been intentionally retained for a pending feature.
- The surviving `AudioPlayerJsInterop` will be the one in `Client.Shared` (co-located with the rest of the client shared services), but this must be verified against actual injection sites before deletion.
- `Ardalis.Result` replacement with an inline `ServiceResult<T>` is limited to the callers identified at the time of cleanup; no new callers will be introduced during this work.
- `mdc-overrides.css` will only be deleted if MatBlazor is confirmed absent from the project at time of execution.
- The `[Authorize]` resolution on `WorkerTransitHub.cs` is a security decision: removing the comment (and leaving the hub unprotected) is acceptable only if the hub is not accessible from the public network; this must be confirmed before deletion.
- No automated test suite currently mocks any of the 24 single-implementation interfaces; if mocks are found, those interfaces are excluded from this cleanup pass.
- Large JSON files moving to Git LFS requires Git LFS to be installed and configured in the repository; if not available, the alternative is conversion to API fetch at startup.
