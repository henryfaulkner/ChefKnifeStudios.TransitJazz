# Research: Codebase Bloat Cleanup (030)

**Date**: 2026-06-26  
**Source**: Live codebase audit — all findings verified against current file system and grep results.

---

## Finding 1: Dead Code — Verified Locations

**Decision**: Delete all items below; none have callers outside themselves.

| Item | Actual Path | Size | Callers |
|------|-------------|------|---------|
| BusDataPoc dir | `src/BusDataPoc/MartaJazz.Engine/` | ~80 MB (bin artifacts) | 0 (not in .sln) |
| EventMapper.cs | `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/EventMapper.cs` | 119 lines | 0 |
| JsonFlattener.cs | `src/ChefKnifeStudios.MartaJazz.Shared/JsonFlattener.cs` | 53 lines | 0 — NOT in Client.Shared as reported; it's in Shared |
| Discard.cs | `src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/EndpointsServices/Discard.cs` | 7 lines | 0 |
| AudioPlayerJsInterop.cs (Core) | `src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/JsInterop/AudioPlayerJsInterop.cs` | 54 lines | 0 — DI wires Client.Shared version |
| audioPlayerJsInterop.js | `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/audioPlayerJsInterop.js` | 4 lines | KEEP — used by Client.Shared AudioPlayerJsInterop |

**Correction from audit report**: `audioPlayerJsInterop.js` is NOT a no-op stub — it exports a real `play(soundUrl)` function used by the surviving Client.Shared implementation. Do not delete it.

**Rationale**: Zero callers confirmed by grep. No test mocks found.  
**Alternatives considered**: Keeping for "future use" — rejected (YAGNI, no pending feature references these).

---

## Finding 2: Dependency Bloat — Verified

| Package | .csproj Location | Actual Usage | Decision |
|---------|-----------------|--------------|----------|
| `Ardalis.Result` | WebAPI + Client.Core .csproj | 6 active callers (HttpService, TransitEndpointsService, GtfsEndpointsService, HttpServiceFactory, InMemoryKeyValueRepository, IKeyValueRepository) | **KEEP** — actively used; replacing with inline ServiceResult<T> is separate refactor with high blast radius |
| `Microsoft.Identity.Web` | WebAPI.csproj | 0 callers — `UseAuthentication()`, `UseAuthorization()`, `.RequireAuthorization()` all commented at Program.cs:103-104,123 | **REMOVE** package + delete commented lines |
| `StackExchange.Redis` | WebAPI.csproj | 0 callers in .cs files (only in .csproj) | **REMOVE** package |
| `Azure.Monitor.OpenTelemetry.AspNetCore` | ServiceDefaults.csproj | 0 callers in source (only in .csproj) | **REMOVE** package |

**Correction**: `Ardalis.Result` is actively used across 6 files — NOT bloat. Removed from deletion scope.  
**Rationale for keeping Ardalis.Result**: Replacing a 6-file API contract mid-cleanup would expand scope significantly and risk introducing regressions. It's a separate concern.

---

## Finding 3: Single-Implementation Interfaces — Verified

**Actual count in Client.Shared** (5 interfaces, not 24 as reported):
- `ITriggerPointGenerator` → `TriggerPointGenerator`
- `ICheckpointTrackerJsInterop` → `CheckpointTrackerJsInterop`
- `IOutsideClickJsInterop` → `OutsideClickJsInterop`
- `ITransitSynthJsInterop` → `TransitSynthJsInterop`
- `IViewportSizeJsInterop` → `ViewportSizeJsInterop`

Additionally in Client.Core: `IAudioPlayerJsInterop` → `AudioPlayerJsInterop` (Client.Shared version).

**Test mocks**: NONE found via grep for `Mock<I` across all test projects.

**Decision — DEFER**: The audit's "24 interfaces" count includes JsInterop abstractions that, while having single implementations now, exist to support the lazy-module-load idiom in Blazor (the `IJSObjectReference`-based lazy loading pattern). Removing these would require restructuring the JsInterop DI pattern and risk the module-isolation mechanism. This cleanup pass will defer interface removal to avoid unintended breakage. The `IEventArgs` finding was not confirmed (no such interface found in grep).

**Rationale for deferral**: No test coverage relies on mocks, but the JS interop interfaces serve as seams for the RCL module loading pattern. The risk/reward ratio is worse than other items in this pass.

---

## Finding 4: Duplicated Code — Verified

### Haversine (3 locations)
1. `src/ChefKnifeStudios.MartaJazz.Shared/Geospatial/HaversineCalculator.cs` — returns **km**, used by Worker.cs and RouteSnapper.cs
2. `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` lines 411-427 — local `HaversineMeters()` returning **meters**
3. `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/vehicle-animator.js` — JS haversine for vehicle animation

**Decision**: 
- Add `DistanceMeters()` overload (or wrapper) to `HaversineCalculator.cs` (returns meters = km × 1000)
- Delete inline `HaversineMeters()` from `TransitMap.razor.cs`, replace with `HaversineCalculator.DistanceMeters()`  
- JS version in `vehicle-animator.js` CANNOT call C# — keep the JS implementation (it runs in the browser animation loop, no C# bridge feasible)

**Rationale**: The JS version is structurally necessary (animation loop cannot cross interop boundary per-frame). The C# duplication in TransitMap.razor.cs is the real fix.

### JsonOptions.cs vs JsonSettings.cs
- `JsonOptions.cs`: basic (CamelCase + CaseInsensitive + EnumConverter) — in Client projects
- `JsonSettings.cs`: full (adds WhenWritingNull + NamedFloatingPoint + EventEnvelopeConverter + ApplyTo()) — in Server/Shared

**Decision**: These serve different purposes (client vs. server serialization concerns with different converter needs). DEFER consolidation — merging them risks changing serialization behavior for the EventEnvelopeConverter path. Audit report overstated overlap.

---

## Finding 5: Anti-Patterns — Verified

### Console.WriteLine (21 occurrences, 3 files)
1. `src/BusDataPoc/MartaJazz.Engine/Worker.cs` — 1 call (will be deleted with BusDataPoc)
2. `src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/HttpService.cs` — 3 calls (lines 126, 133, 139)
3. `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/Map.razor.Helper.cs` — 17 calls

**Decision**: In Blazor WASM, `ILogger<T>` writes to browser console — the calls are functionally equivalent but should use ILogger for consistency. Replace with `ILogger` in HttpService.cs (3 calls). For Map.razor.Helper.cs (17 calls), evaluate: many are likely tracing/debug — delete rather than convert to avoid excessive browser console spam.

### IsAllowedRoute() stub
`TransitMap.razor.cs` line 559-561: `static bool IsAllowedRoute(string routeKey) => true;`

**Decision**: Delete method, inline `true` at every call site (or delete the condition if the method's only caller is in a boolean guard).

### Empty catch blocks (LogEventWorker.cs)
- Lines 120, 143: `catch (OperationCanceledException) { }` — these are INTENTIONAL (cancellation is not an error)
- Lines 126, 149: `catch { }` — swallows all exceptions silently

**Decision**: Leave `OperationCanceledException` catches empty (correct pattern). Add `ILogger.LogWarning` to the bare `catch { }` blocks at lines 126 and 149.

### Commented [Authorize] (WorkerTransitHub.cs:11)
`//[Authorize(Policy = "TransitDataPublisher")]`

**Decision**: DELETE the comment. The hub is internal worker-to-server communication; the WebAPI's `UseAuthentication`/`UseAuthorization` are also commented out (see Finding 2). A stale commented attribute adds noise without conveying intent. If auth is revived it should be added deliberately.

### BatchDebugRecord.WriteBatchToDiskAsync
- Located in `Worker.cs` lines 529-546 (NOT a separate file)
- No runtime flag or `#if DEBUG` guard

**Decision**: Wrap call site(s) in `#if DEBUG` preprocessor directive. The method itself can remain; the guard prevents disk writes in production.

---

## Finding 6: JavaScript / CSS — Verified

### map-interop.js stubs
- `addRouteShapeFeature` (lines 557-559): already shows a `console.warn` — deprecated wrapper, safe to delete
- `toggleTraffic` (lines 125-127): no-op POC stub, safe to delete

**Decision**: Delete both. Verify no C# `[JSInvokable]` or `IJSRuntime.InvokeAsync` callers first.

### mdc-overrides.css
- File exists at `wwwroot/css/mdc-overrides.css`
- MatBlazor registered via `builder.Services.AddMatBlazor()` in Program.cs:68 (no explicit PackageReference found, but it is used at runtime)

**Decision**: DEFER — MatBlazor IS in use (DI registration confirmed). The CSS overrides may be needed. Do not delete.

### SignalRTest.razor
- Exists at `Client.WebApp/Pages/SignalRTest.razor`
- No `#if DEBUG` guard — compiled into production

**Decision**: Delete the file entirely (including its `JsonFlattener` dep, which has no other callers). If debug testing is needed, it can be re-created.

---

## Finding 7: Infrastructure — Verified

Two YAML files explicitly marked "SUPERSEDED" in line 1 comment:
- `deploy/server-pipeline.yml`
- `deploy/client-pipeline.yml`

GitHub Actions workflows are the current CI/CD. These have no active callers.

**Decision**: Delete both files.

---

## Finding 8: Large Static Files — Verified Paths

| File | Path | Size |
|------|------|------|
| `neighborhood_routes.json` | `tools/neighborhood-routes/neighborhood_routes.json` | 120 KB |
| `neighborhood_routes_full.json` | `tools/neighborhood-routes/neighborhood_routes_full.json` | 722 KB |

**Decision**: DEFER — files are in `tools/` not `wwwroot/`, so they do not bloat the WASM payload. They're developer tools, not shipped assets. Git LFS migration is a separate, lower-priority concern. Not in scope for this pass.

---

## Scope Summary

### IN SCOPE (this pass)
- Delete: `src/BusDataPoc/`, `EventMapper.cs`, `JsonFlattener.cs`, `Discard.cs`, `AudioPlayerJsInterop.cs` (Client.Core copy), `SignalRTest.razor`
- Remove packages: `Microsoft.Identity.Web`, `StackExchange.Redis`, `Azure.Monitor.OpenTelemetry.AspNetCore` + delete their dead commented call sites
- Fix: `Console.WriteLine` → `ILogger` in HttpService.cs; delete debug prints in Map.razor.Helper.cs
- Fix: Delete `IsAllowedRoute()`, inline `true` at call sites
- Fix: Add logging to bare `catch { }` blocks in LogEventWorker.cs:126,149
- Fix: Delete `//[Authorize]` comment from WorkerTransitHub.cs
- Fix: Add `#if DEBUG` guard to `WriteBatchToDiskAsync` call site in Worker.cs
- Fix: Delete `addRouteShapeFeature` and `toggleTraffic` stubs from map-interop.js
- Delete: `deploy/server-pipeline.yml`, `deploy/client-pipeline.yml`
- Add: `HaversineCalculator.DistanceMeters()`, replace inline duplicate in TransitMap.razor.cs

### DEFERRED (not this pass)
- Interface removal (5 JsInterop interfaces) — risk of breaking RCL module loading
- `Ardalis.Result` replacement — 6-file API contract change, separate task
- `JsonOptions.cs` / `JsonSettings.cs` consolidation — different serialization concerns
- `audioPlayerJsInterop.js` — NOT a no-op, keep
- `mdc-overrides.css` — MatBlazor still in use
- `neighborhood_routes*.json` Git LFS — files are in `tools/`, not shipped
- JS haversine in `vehicle-animator.js` — structurally necessary (animation loop)
