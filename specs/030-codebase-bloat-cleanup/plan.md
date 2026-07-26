# Implementation Plan: Codebase Bloat Cleanup

**Branch**: `030-codebase-bloat-cleanup` | **Date**: 2026-06-26 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/030-codebase-bloat-cleanup/spec.md`

---

## Summary

Remove verified dead code, unused dependencies, anti-pattern debug prints, and superseded infrastructure files identified in the 2026-06-26 bloat audit (`bloat-reports/20260626.md`). All findings were re-verified against the live codebase before scoping. The cleanup is organized into discrete, independently safe batches that keep the build green throughout. See `research.md` for per-item verification details and deferral rationale.

---

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 (all projects); JavaScript (ES2020, browser); CSS  
**Primary Dependencies**: Blazor WASM, ASP.NET Core, MapLibre GL JS, Tone.js  
**Storage**: In-memory KV (server), Azure Blob (telemetry sidecar)  
**Testing**: No automated test suite targets these areas; manual smoke-test after each batch  
**Target Platform**: Browser (WASM) + Azure Container Apps (server worker)  
**Project Type**: Blazor WASM SPA + ASP.NET Core WebAPI + .NET Worker Service  
**Performance Goals**: No regressions in map render, vehicle animation, or audio  
**Constraints**: Build must remain green at end of every batch; no behavior changes  
**Scale/Scope**: ~20 file deletions, ~4 package removals, ~30 line edits

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
|-----------|------------|--------|
| **I. Decoupled Cloud Architecture** | No architecture changes; cleanup is purely within existing project boundaries | ✅ PASS |
| **II. No Frontend Secrets** | No secrets touched; `audioPlayerJsInterop.js` (which carries no secrets) is kept | ✅ PASS |
| **III. Two-Pass RT Pipeline** | `EventMapper.cs` (dead, no callers) deleted; Worker.cs V1/V2 passes untouched | ✅ PASS |
| **IV. OpenTelemetry Observability** | `Azure.Monitor.OpenTelemetry.AspNetCore` removed — it was declared but never wired. Base OTEL packages in `ServiceDefaults.csproj` are retained. Empty catch blocks get `ILogger` calls (improves observability) | ✅ PASS |
| **V. Azure DevOps CI/CD** | Superseded Azure DevOps YAMLs deleted; GitHub Actions workflows (current CI) untouched | ✅ PASS |
| **VI. GTFS ID Mapping** | No data pipeline changes | ✅ PASS |
| **VII. OpenStreetMap-Based Cartography** | Dead JS stubs in `map-interop.js` deleted; active map interop untouched | ✅ PASS |
| **VIII. Generative Transit Music** | Audio interop cleanup removes only the unused Client.Core duplicate; Client.Shared `AudioPlayerJsInterop` + JS file retained | ✅ PASS |
| **IX. Persistent Multi-Selection** | No filter or selection logic changed | ✅ PASS |
| **X. Zoom-Adaptive Controls** | No control layout changes | ✅ PASS |
| **XI. Snappy, Reversible Overlays** | No overlay changes | ✅ PASS |
| **XII. Internationalized, Settings-Driven** | No settings or localization changes | ✅ PASS |

**Constitution verdict**: All gates pass. No violations to justify.

---

## Project Structure

### Documentation (this feature)

```text
specs/030-codebase-bloat-cleanup/
├── plan.md              ← this file
├── research.md          ← Phase 0 output (verified codebase facts)
├── data-model.md        ← N/A (no new entities; cleanup only)
├── quickstart.md        ← Phase 1 output
├── contracts/           ← N/A (no new external interfaces)
└── tasks.md             ← Phase 2 output (/speckit-tasks)
```

### Source Code Affected

```text
src/
├── BusDataPoc/                                             ← DELETE entire directory
├── ChefKnifeStudios.TransitJazz.Shared/
│   ├── Geospatial/HaversineCalculator.cs                  ← ADD DistanceMeters() overload
│   └── JsonFlattener.cs                                   ← DELETE
│
├── Server/
│   ├── ChefKnifeStudios.TransitJazz.Server.WebAPI/
│   │   ├── Program.cs                                     ← DELETE commented auth lines
│   │   ├── SignalR/WorkerTransitHub.cs                    ← DELETE commented [Authorize]
│   │   └── ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj ← REMOVE 2 packages
│   │
│   ├── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
│   │   ├── EventMapper.cs                                 ← DELETE
│   │   ├── Worker.cs                                      ← ADD #if DEBUG guard
│   │   └── Logging/LogEventWorker.cs                      ← FIX bare catch blocks
│   │
│   └── ChefKnifeStudios.TransitJazz.ServiceDefaults/
│       └── *.csproj                                       ← REMOVE 1 package
│
└── Client/
    ├── ChefKnifeStudios.TransitJazz.Client.Core/
    │   ├── Services/EndpointsServices/Discard.cs           ← DELETE
    │   └── Services/JsInterop/AudioPlayerJsInterop.cs      ← DELETE (Core duplicate)
    │
    ├── ChefKnifeStudios.TransitJazz.Client.Shared/
    │   └── Services/HttpService.cs                         ← REPLACE Console.WriteLine with ILogger
    │
    └── ChefKnifeStudios.TransitJazz.Client.WebApp/
        ├── Pages/
        │   ├── TransitMap.razor.cs                         ← DELETE IsAllowedRoute(), DELETE HaversineMeters()
        │   ├── Map.razor.Helper.cs                         ← DELETE Console.WriteLine calls
        │   └── SignalRTest.razor                           ← DELETE file
        └── wwwroot/js/
            └── map-interop.js                              ← DELETE 2 dead stubs

deploy/
├── server-pipeline.yml                                     ← DELETE
└── client-pipeline.yml                                     ← DELETE
```

---

## Batch Execution Plan

Batches are ordered so each one leaves the build green. A `dotnet build` after each batch is the acceptance gate.

---

### Batch A — Infrastructure Files (No Build Impact)

Safe to delete first because these files are not compiled.

**A1** — Delete superseded Azure DevOps pipeline YAMLs  
- `deploy/server-pipeline.yml`
- `deploy/client-pipeline.yml`

**Verification**: `ls deploy/` shows no YAML files remain. GitHub Actions workflows unchanged.

---

### Batch B — Dead Source Files

Delete files with zero callers. No callers means no downstream compilation errors.

**B1** — Delete `src/BusDataPoc/` (entire directory)  
- Contains `MartaJazz.Engine/` (.NET console POC, not in .sln)  
- Removes ~80 MB build artifact cache

**B2** — Delete dead C# files  
- `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/EventMapper.cs`  
- `src/ChefKnifeStudios.TransitJazz.Shared/JsonFlattener.cs`  
- `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/EndpointsServices/Discard.cs`  
- `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/JsInterop/AudioPlayerJsInterop.cs`

**B3** — Delete debug test page  
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/SignalRTest.razor`  
- Also check for `@page "/signalr-test"` route — confirm no navigation links point to it

**Verification**: `dotnet build` passes. No `CS0246` (type not found) or `CS0103` (name not found) errors.

---

### Batch C — Unused NuGet Packages

Remove package declarations + delete their dead commented-out call sites.

**C1** — WebAPI.csproj: remove `Microsoft.Identity.Web` (3.8.2)  
- Also delete from `Program.cs`:
  - Line 103: `//app.UseAuthentication();`
  - Line 104: `//app.UseAuthorization();`
  - Line 123: `//.RequireAuthorization("TransitDataPublisher");`

**C2** — WebAPI.csproj: remove `StackExchange.Redis` (2.9.17)  
- Verify no using statements for `StackExchange.Redis` remain in any .cs file

**C3** — ServiceDefaults.csproj: remove `Azure.Monitor.OpenTelemetry.AspNetCore` (1.4.0)  
- Verify no `using Azure.Monitor.OpenTelemetry` or `UseAzureMonitor()` calls remain

**Verification**: `dotnet restore && dotnet build` passes. Package count in lockfile reduced by 3+.

---

### Batch D — Anti-Pattern Fixes

Code edits with no behavior change.

**D1** — WorkerTransitHub.cs: delete commented `[Authorize]` line  
- File: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/WorkerTransitHub.cs`  
- Delete line: `//[Authorize(Policy = "TransitDataPublisher")]`

**D2** — LogEventWorker.cs: fix bare catch blocks  
- File: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/LogEventWorker.cs`  
- Lines 126 and 149: replace `catch { }` with `catch (Exception ex) { _logger.LogWarning(ex, "LogEventWorker: unexpected exception."); }`  
- Leave `catch (OperationCanceledException) { }` blocks at lines 120 and 143 as-is (intentional)

**D3** — Worker.cs: guard `WriteBatchToDiskAsync` call site  
- File: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`  
- Locate the call to `WriteBatchToDiskAsync(...)` and wrap: `#if DEBUG ... await WriteBatchToDiskAsync(...) ... #endif`

**D4** — TransitMap.razor.cs: delete `IsAllowedRoute()`  
- File: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`  
- Delete the method at lines 559-561
- Find all callers: replace `IsAllowedRoute(x)` → `true` (or delete the conditional entirely if the guard is `if (IsAllowedRoute(...))`)

**D5** — HttpService.cs: replace `Console.WriteLine` with `ILogger`  
- File: `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/HttpService.cs`  
- Replace 3 `Console.WriteLine(...)` calls (lines 126, 133, 139) with `_logger.LogWarning(...)` / `_logger.LogError(...)`  
- Ensure `ILogger<HttpService>` is already injected (likely is; add if not)

**D6** — Map.razor.Helper.cs: delete debug Console.WriteLine calls  
- File: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/Map.razor.Helper.cs`  
- Review all 17 `Console.WriteLine` calls: determine which are useful diagnostics vs. debug noise  
- Delete noise; convert meaningful ones to `ILogger.LogDebug(...)` if a logger is available in context

**Verification**: `dotnet build` passes. Grep confirms zero `Console.WriteLine` in production (non-BusDataPoc) source. Zero bare `catch { }` blocks in LogEventWorker.

---

### Batch E — Haversine Deduplication

**E1** — Add `DistanceMeters()` to `HaversineCalculator`  
- File: `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/HaversineCalculator.cs`  
- Add: `public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2) => DistanceKm(lat1, lon1, lat2, lon2) * 1000;`

**E2** — Remove inline `HaversineMeters()` from TransitMap.razor.cs  
- File: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`  
- Delete local `HaversineMeters()` method (lines 411-427)  
- Replace call sites with `HaversineCalculator.DistanceMeters(...)`  
- Add `using ChefKnifeStudios.TransitJazz.Shared.Geospatial;` if not present

**Verification**: `dotnet build` passes. Grep confirms one C# `HaversineMeters` definition (in HaversineCalculator.cs only, named `DistanceMeters`). JS haversine in `vehicle-animator.js` is intentionally unchanged (browser animation loop; no C# bridge feasible).

---

### Batch F — JavaScript Dead Stubs

**F1** — Delete stubs from `map-interop.js`  
- File: `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`  
- Delete `addRouteShapeFeature` function (lines 557-559, deprecated wrapper with `console.warn`)  
- Delete `toggleTraffic` function (lines 125-127, no-op POC stub)
- First grep C# files for `InvokeAsync.*addRouteShapeFeature` and `InvokeAsync.*toggleTraffic` to confirm no active callers

**Verification**: App loads, map renders, routes display, vehicle animation works (no JS console errors for missing functions).

---

## Deferred Items

The following were evaluated but excluded from this pass. Document here for future reference.

| Item | Why Deferred |
|------|-------------|
| 5 JsInterop interfaces (ITriggerPointGenerator, etc.) | Interface-per-RCL-module is the Blazor lazy-load seam; removal needs careful DI restructure |
| `Ardalis.Result` replacement | 6-file API contract; separate refactor with meaningful blast radius |
| `JsonOptions.cs` / `JsonSettings.cs` consolidation | Serve different serialization concerns (client vs. server+EventEnvelope converter) |
| `audioPlayerJsInterop.js` deletion | NOT a no-op; actively used by Client.Shared AudioPlayerJsInterop |
| `mdc-overrides.css` | MatBlazor is in active DI registration (AddMatBlazor()); CSS may be required |
| JS haversine in `vehicle-animator.js` | Animation loop cannot call C# interop; structurally necessary |
| `neighborhood_routes*.json` Git LFS | Files are in `tools/`, not WASM payload; low priority |

---

## Complexity Tracking

No constitution violations. No complexity tracking required.
