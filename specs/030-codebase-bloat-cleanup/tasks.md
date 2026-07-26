---
description: "Task list for 030-codebase-bloat-cleanup"
---

# Tasks: Codebase Bloat Cleanup (030)

**Input**: Design documents from `/specs/030-codebase-bloat-cleanup/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, quickstart.md ✅

**Organization**: Tasks follow the 6 ordered cleanup batches from plan.md. Each batch is independently buildable. `dotnet build` is the acceptance gate after every batch.

No tests are requested — this is a pure cleanup (delete/edit, no new logic except the Haversine overload).

---

## Phase 1: Batch A — Superseded Infrastructure Files

**Goal**: Delete two Azure DevOps YAML pipeline files that are explicitly marked "SUPERSEDED" in their first line. GitHub Actions is the current CI/CD system. These are not compiled and carry zero build risk.

**Independent Test**: `ls deploy/` shows no `.yml` files. GitHub Actions workflows in `.github/workflows/` are untouched.

- [X] T001 [P] [US6] Delete `deploy/server-pipeline.yml` (marked SUPERSEDED, replaced by `.github/workflows/server.yml`)
- [X] T002 [P] [US6] Delete `deploy/client-pipeline.yml` (marked SUPERSEDED, replaced by `.github/workflows/client.yml`)

**Checkpoint**: `deploy/` directory contains no YAML files. Build unaffected (these are not referenced by the .sln).

---

## Phase 2: Batch B — Dead Source Files

**Goal**: Delete C# source files with zero callers (confirmed by grep in research.md), the unused Client.Core duplicate of `AudioPlayerJsInterop`, and the debug-only `SignalRTest.razor` page that is currently compiled into production with no `#if DEBUG` guard.

**Independent Test**: `dotnet build ChefKnifeStudios.TransitJazz.sln` passes with 0 errors after all B-batch deletions.

- [ ] T003 [US1] Delete `src/BusDataPoc/` directory entirely — not in .sln, contains ~80 MB build artifacts, zero production references
- [X] T004 [P] [US1] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/EventMapper.cs` — 119-line static class, zero callers confirmed
- [X] T005 [P] [US1] Delete `src/ChefKnifeStudios.TransitJazz.Shared/JsonFlattener.cs` — 53-line utility, zero callers confirmed
- [X] T006 [P] [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/EndpointsServices/Discard.cs` — 7-line singleton sentinel, zero callers confirmed
- [X] T007 [P] [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/JsInterop/AudioPlayerJsInterop.cs` — Client.Core duplicate; DI wires the Client.Shared version (`builder.Services.AddSingleton<IAudioPlayerJsInterop, AudioPlayerJsInterop>()` in Client.WebApp/Program.cs line 57 references the Shared copy)
- [X] T008 [US1] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/SignalRTest.razor` — debug test page compiled into production; verify no nav links point to `@page "/signalr-test"` before deleting (grep for `signalr-test` in .razor and .cs files)

**Checkpoint**: `dotnet build` → 0 errors. No `CS0246` (type not found) errors.

---

## Phase 3: Batch C — Unused NuGet Packages

**Goal**: Remove three NuGet packages that are declared in .csproj files but have zero runtime callers — their only call sites are commented-out lines in `Program.cs` and `Extensions.cs`. Remove the packages and delete the dead commented lines.

**Independent Test**: `dotnet restore && dotnet build` passes. Lockfile no longer references the three removed packages. No `using` statements for removed namespaces remain.

- [X] T009 [US2] Remove `<PackageReference Include="Microsoft.Identity.Web" Version="3.8.2" />` from `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj`
- [X] T010 [US2] Delete the three dead commented auth lines from `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`: `//app.UseAuthentication();` (line ~103), `//app.UseAuthorization();` (line ~104), `//.RequireAuthorization("TransitDataPublisher");` (line ~123)
- [X] T011 [P] [US2] Remove `<PackageReference Include="StackExchange.Redis" Version="2.9.17" />` from `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj` — verify no `using StackExchange.Redis` remains in any .cs file
- [X] T012 [P] [US2] Remove `<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />` from `src/Server/ChefKnifeStudios.TransitJazz.ServiceDefaults/ChefKnifeStudios.TransitJazz.ServiceDefaults.csproj` — verify no `UseAzureMonitor()` call or `using Azure.Monitor` remains

**Checkpoint**: `dotnet restore && dotnet build` → 0 errors.

---

## Phase 4: Batch D — Anti-Pattern Fixes

**Goal**: Fix six anti-patterns identified in the audit: commented security attribute, silent exception swallowing, unguarded disk-write in production, always-true stub method, Console.WriteLine calls, and debug print spam. No behavior changes — only code quality and operational correctness improvements.

**Independent Test**: `dotnet build` → 0 errors. Grep confirms zero `Console.WriteLine` calls in non-debug production paths. LogEventWorker bare `catch { }` blocks are gone.

- [X] T013 [P] [US5] Delete commented `//[Authorize(Policy = "TransitDataPublisher")]` line from `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/WorkerTransitHub.cs` line ~11 — stale comment with no intent; auth is separately disabled at middleware level
- [X] T014 [US5] Fix bare `catch { }` blocks in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/LogEventWorker.cs` at lines ~126 and ~149 — replace each with `catch (Exception ex) { _logger.LogWarning(ex, "LogEventWorker: unexpected exception."); }`. Leave `catch (OperationCanceledException) { }` blocks at ~120 and ~143 untouched (intentional cancellation handling)
- [X] T015 [US5] Add `#if DEBUG` / `#endif` preprocessor guard around the `WriteBatchToDiskAsync(...)` call site in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` lines ~529-546 — prevents debug JSON batch files from being written in production builds
- [X] T016 [US5] Delete `IsAllowedRoute()` method from `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` lines ~559-561 — always returns `true`; find all call sites (grep for `IsAllowedRoute`) and either inline `true` or remove the enclosing condition
- [X] T017 [US5] Replace 3 `Console.WriteLine` calls in `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/HttpService.cs` lines ~126, ~133, ~139 with `_logger.LogWarning(...)` / `_logger.LogError(...)` using the injected `ILogger<HttpService>` (add DI injection if not already present)
- [X] T018 [US5] Delete or convert 17 `Console.WriteLine` calls in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/Map.razor.Helper.cs` — delete debug noise; convert any meaningful diagnostics to `ILogger.LogDebug(...)` if a logger is available in that partial class context

**Checkpoint**: `dotnet build` → 0 errors. `grep -r "Console.WriteLine" src/ --include="*.cs"` → 0 matches (BusDataPoc already deleted in Batch B).

---

## Phase 5: Batch E — Haversine Deduplication

**Goal**: Add `DistanceMeters()` to the canonical `HaversineCalculator` in Shared, then delete the inline `HaversineMeters()` duplicate in `TransitMap.razor.cs` and update its callers. The JS haversine in `vehicle-animator.js` is intentionally kept (browser animation loop cannot call C# interop per-frame).

**Independent Test**: `dotnet build` → 0 errors. `grep -r "HaversineMeters" src/ --include="*.cs"` → 0 matches (definition gone, call sites updated). Route-following vehicle animation continues to work in the browser.

- [X] T019 [US4] Add `public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2) => DistanceKm(lat1, lon1, lat2, lon2) * 1000;` to `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/HaversineCalculator.cs`
- [X] T020 [US4] Delete the local `HaversineMeters()` method from `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` lines ~411-427; replace all call sites in that file with `HaversineCalculator.DistanceMeters(...)` — add `using ChefKnifeStudios.TransitJazz.Shared.Geospatial;` if not already present

**Checkpoint**: `dotnet build` → 0 errors. One `DistanceMeters` definition exists in `HaversineCalculator.cs`.

---

## Phase 6: Batch F — JavaScript Dead Stubs

**Goal**: Delete two dead JavaScript functions from `map-interop.js` — a deprecated `addRouteShapeFeature` wrapper (already shows a `console.warn`) and a no-op POC `toggleTraffic` stub. Confirm no C# callers exist before deleting.

**Independent Test**: App loads in browser, map renders, routes display and animate, vehicle positions update — no JS `TypeError: window.ChefMap.X is not a function` console errors.

- [X] T021 [US1] Grep C# source for `InvokeAsync.*addRouteShapeFeature` and `InvokeAsync.*toggleTraffic` to confirm zero callers, then delete both dead function blocks from `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js` (lines ~125-127 for `toggleTraffic`, lines ~557-559 for `addRouteShapeFeature`)

**Checkpoint**: Browser smoke test — map loads, route selection works, no JS console errors for missing functions.

---

## Phase 7: Polish & Final Verification

**Purpose**: Final build gate and smoke test across all primary app flows.

- [X] T022 Run `dotnet build ChefKnifeStudios.TransitJazz.sln` and confirm 0 errors, 0 new warnings introduced by the cleanup
- [X] T023 [P] Confirm `grep -r "Console.WriteLine" src/ --include="*.cs"` → 0 matches (only BusDataPoc remains, T003 pending)
- [X] T024 [P] Confirm `grep -rn "catch { }" src/ --include="*.cs"` → 0 matches in LogEventWorker.cs
- [ ] T025 Manual smoke test: start app locally, verify map load → route select → audio → settings blade → checkpoint flash all function correctly with no JS console errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Batch A)**: No dependencies — start immediately (no build impact)
- **Phase 2 (Batch B)**: No dependencies — file deletions only
- **Phase 3 (Batch C)**: Depends on Phase 2 (BusDataPoc deleted first, which removes its `Console.WriteLine` from grep baseline)
- **Phase 4 (Batch D)**: Can proceed after Phase 2 (dead files gone)
- **Phase 5 (Batch E)**: Independent — can run in parallel with D
- **Phase 6 (Batch F)**: Independent — JS changes don't affect dotnet build
- **Phase 7 (Polish)**: Depends on all prior phases complete

### User Story Dependencies

- **US6 (Phase 1)**: No dependencies
- **US1 (Phase 2)**: No dependencies — pure deletions
- **US2 (Phase 3)**: After Phase 2 recommended (clean baseline)
- **US5 (Phase 4)**: After Phase 2
- **US4 (Phase 5)**: Independent of US5
- **US1/F (Phase 6)**: After Phase 2 (confirms no C# callers)

### Parallel Opportunities

Within each batch, all tasks marked `[P]` can run simultaneously (they touch different files):
- T001 + T002 (delete two independent YAML files)
- T004 + T005 + T006 + T007 (delete four independent dead C# files)
- T011 + T012 (remove two independent packages from two different .csproj files)
- T013 + T014 (WorkerTransitHub vs LogEventWorker — different files)
- T019 + T020 can be done in one editing session (same feature)

---

## Parallel Execution Examples

### Batch B (Dead Files) — max parallelism

```
Simultaneously:
  T004: Delete EventMapper.cs
  T005: Delete JsonFlattener.cs
  T006: Delete Discard.cs
  T007: Delete AudioPlayerJsInterop.cs (Client.Core)
Then sequentially:
  T008: Delete SignalRTest.razor (requires grep check first)
```

### Batch D (Anti-Patterns) — partial parallelism

```
Simultaneously:
  T013: Delete commented [Authorize] in WorkerTransitHub.cs
  T014: Fix LogEventWorker.cs catch blocks
Then:
  T015: Guard WriteBatchToDiskAsync in Worker.cs
  T016: Delete IsAllowedRoute() in TransitMap.razor.cs
  T017: Fix HttpService.cs Console.WriteLine
  T018: Fix Map.razor.Helper.cs Console.WriteLine
```

---

## Implementation Strategy

### MVP First (Batch A + B only)

1. Complete Phase 1 + Phase 2 (Batches A and B)
2. `dotnet build` → green
3. **STOP and VALIDATE**: Dead code removed, build clean

### Full Cleanup (All Batches)

1. Phase 1: Delete pipeline YAMLs
2. Phase 2: Delete dead source files → build gate
3. Phase 3: Remove packages → restore + build gate
4. Phase 4: Fix anti-patterns → build gate + grep gate
5. Phase 5: Haversine dedup → build gate
6. Phase 6: JS stubs → browser smoke test
7. Phase 7: Final gates

---

## Notes

- `[P]` = touches a different file from its batch-mates; safe to run in parallel
- `[Story]` labels map to user stories in `spec.md` for traceability
- No test tasks — this is a cleanup; the acceptance gate is `dotnet build` + grep checks
- The JS haversine in `vehicle-animator.js` is intentionally NOT removed (see research.md)
- `audioPlayerJsInterop.js` is intentionally NOT removed (it is the active implementation file, not a stub)
- `mdc-overrides.css`, 5 JsInterop interfaces, and `Ardalis.Result` are intentionally deferred (see research.md)
