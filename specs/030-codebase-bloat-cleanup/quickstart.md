# Quickstart: Codebase Bloat Cleanup (030)

## TL;DR

Six independent, build-safe batches. Run `dotnet build` after each one. No behavior changes.

---

## Prerequisites

- Branch: `030-codebase-bloat-cleanup` (or work directly on `main`)
- Build green before you start: `dotnet build ChefKnifeStudios.TransitJazz.sln`

---

## Batch A — Superseded Pipeline Files (2 files, ~0 build risk)

```powershell
Remove-Item deploy/server-pipeline.yml
Remove-Item deploy/client-pipeline.yml
```

Verify: `ls deploy/` — no YAML files remain.

---

## Batch B — Dead Source Files (~6 files)

```powershell
# B1: Entire BusDataPoc directory (not in .sln)
Remove-Item -Recurse -Force src/BusDataPoc/

# B2: Dead C# files
Remove-Item src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/EventMapper.cs
Remove-Item src/ChefKnifeStudios.MartaJazz.Shared/JsonFlattener.cs
Remove-Item src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/EndpointsServices/Discard.cs
Remove-Item src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/JsInterop/AudioPlayerJsInterop.cs

# B3: Debug test page
Remove-Item src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/SignalRTest.razor
```

Gate: `dotnet build` → 0 errors.

---

## Batch C — Unused NuGet Packages (~3 package edits + ~4 commented lines deleted)

Edit `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/ChefKnifeStudios.MartaJazz.Server.WebAPI.csproj`:
- Remove `<PackageReference Include="Microsoft.Identity.Web" ... />`
- Remove `<PackageReference Include="StackExchange.Redis" ... />`

Edit `src/Server/ChefKnifeStudios.MartaJazz.ServiceDefaults/ChefKnifeStudios.MartaJazz.ServiceDefaults.csproj`:
- Remove `<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" ... />`

Edit `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/Program.cs`:
- Delete lines `//app.UseAuthentication();`, `//app.UseAuthorization();`, `//.RequireAuthorization("TransitDataPublisher");`

Gate: `dotnet restore && dotnet build` → 0 errors.

---

## Batch D — Anti-Pattern Fixes (~30 line edits)

**WorkerTransitHub.cs** — delete `//[Authorize(Policy = "TransitDataPublisher")]` line.

**LogEventWorker.cs** — replace bare catch blocks at lines ~126 and ~149:
```csharp
// BEFORE
catch { }

// AFTER
catch (Exception ex) { _logger.LogWarning(ex, "LogEventWorker: unexpected exception."); }
```
Leave `catch (OperationCanceledException) { }` at ~120 and ~143 as-is.

**Worker.cs** — guard `WriteBatchToDiskAsync` call:
```csharp
#if DEBUG
await WriteBatchToDiskAsync(batch, ct);
#endif
```

**TransitMap.razor.cs** — delete `IsAllowedRoute()` method; replace callers:
```csharp
// BEFORE (example guard)
if (IsAllowedRoute(routeKey)) { ... }

// AFTER
// (inline true — keep the block, remove the guard; or just remove the if entirely)
{ ... }
```

**HttpService.cs** — replace Console.WriteLine with ILogger:
```csharp
// BEFORE
Console.WriteLine($"Error: {message}");

// AFTER
_logger.LogWarning("HttpService error: {Message}", message);
```

**Map.razor.Helper.cs** — delete or convert 17 Console.WriteLine calls (see research.md for judgment guidance).

Gate: `dotnet build` → 0 errors. `grep -r "Console.WriteLine" src/` (excluding BusDataPoc) → 0 matches.

---

## Batch E — Haversine Deduplication (~15 line change)

**HaversineCalculator.cs** — add overload:
```csharp
public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    => DistanceKm(lat1, lon1, lat2, lon2) * 1000;
```

**TransitMap.razor.cs** — delete local `HaversineMeters()` method; replace callers:
```csharp
// BEFORE
var dist = HaversineMeters(lat1, lon1, lat2, lon2);

// AFTER
var dist = HaversineCalculator.DistanceMeters(lat1, lon1, lat2, lon2);
```

Gate: `dotnet build` → 0 errors.

---

## Batch F — JavaScript Dead Stubs

**map-interop.js** — first confirm no C# callers:
```powershell
grep -r "addRouteShapeFeature" src/ --include="*.cs"
grep -r "toggleTraffic" src/ --include="*.cs"
```
Both should return 0 matches. Then delete the two function blocks from map-interop.js.

Gate: Load app in browser, open map, select a route — no JS console errors.

---

## Final Smoke Test

After all batches:
1. `dotnet build` → 0 errors
2. Start app locally
3. Map loads and renders routes
4. Select a route — route highlights, blurb bar appears
5. Audio plays on checkpoint pass
6. Settings blade opens/closes
7. No JS console errors

---

## What Was NOT Changed

See `research.md` → "Deferred Items" for the full list and rationale.
