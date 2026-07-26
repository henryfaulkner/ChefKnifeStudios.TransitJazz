# Blast Radius: Blazor WASM → Blazor Server

**Audit type:** ponytail-audit (repo-wide, over-engineering / complexity only)
**Scope question:** total blast radius of shifting from Blazor WebAssembly to Blazor Server.
**Date:** 2026-06-28
**Boundaries:** Correctness bugs, security holes, and performance are out of scope and routed to a normal review pass. This report lists findings; it applies nothing.

---

## Summary

The shift is **not localized**. It touches both client projects' DI/host, the SignalR service, the HttpService stack, the WebAPI CORS + origin model, the entire client CI/CD path, and forces every JS-interop call onto the circuit.

The single highest-value deletion is the **cross-origin indirection** (CORS + `ExternalApis` BaseUri + `HttpServiceFactory`), which exists *solely* to bridge two origins that WASM forced apart. Server hosting collapses client and API into one origin and that whole layer evaporates.

**net: ~250 lines, -2 deps possible** (`Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.AspNetCore.Components.WebAssembly.DevServer`; `Blazored.LocalStorage` stays but its sync usage is invalidated).

---

## Findings (ranked, biggest cut first)

### 1. `delete:` cross-origin SignalR/HTTP indirection
The `marta-jazz-dev-ca-server` cross-origin SignalR/HTTP indirection. Server hosting collapses client + API into one origin — the whole `AppSettings:ExternalApis` BaseUri config, the absolute-vs-relative URI dance, and CORS exist *only* because WASM runs on a separate Static Web App origin.
`src/Client/.../Core/Services/SignalRNotificationService.cs:48-74`, `src/Client/.../WebApp/Program.cs:28-45`, `src/Client/.../WebApp/wwwroot/appsettings.json:3-16`

### 2. `delete:` CORS policy "Default"
CORS policy "Default" with its 5 hardcoded localhost origins + `AllowCredentials`. Same-origin Server hosting needs no CORS at all. Cut the `AddCors`/`UseCors` block entirely.
`src/Server/.../WebAPI/Program.cs:30-39, 134`

### 3. `delete:` `HttpServiceFactory` + named-client-per-API
`HttpServiceFactory` + named-`HttpClient`-per-API + the `AddHttpClient` flag on each `ExternalApis` entry. Server-side a single typed/injected `HttpClient` (or direct in-process calls to the same app's endpoints) replaces the browser-`HttpClient`-by-base-address factory. The whole `IHttpServiceFactory` indirection is a WASM artifact.
`src/Client/.../Core/Services/HttpServiceFactory.cs`, `src/Client/.../WebApp/Program.cs:29-45`, `src/Client/.../Core/AppSettings.cs:10-16`

### 4. `native:` WASM host primitives
`IWebAssemblyHostEnvironment` / `WebAssemblyHostBuilder` / `RootComponents.Add<App>("#app")`. Server uses `WebApplication.CreateBuilder` + `MapRazorComponents<App>()` (or the classic `_Host.cshtml`), and `NavigationManager.BaseUri` is always absolute — the relative-URI fallback in SignalR init disappears. The platform gives you the host.
`src/Client/.../WebApp/Program.cs:24-26`, `src/Client/.../Core/Services/SignalRNotificationService.cs:8,28-30,71-73`

### 5. `delete:` WASM heap cap + RAM investigation
The `EmccMaximumHeapSize` cap plus the whole browser-RAM / `MemoryProbe` investigation. The ~1.2 GB client RAM is .NET WASM linear memory — it *evaporates* on Server (the runtime moves to the host). The heap-cap workaround, the cap-tuning commit, and the memory-probe tooling become dead weight.
`src/Client/.../WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj:6`; memory notes `project_browser_ram_wasm_heap`, `project_memory_probe_instrumentation`

### 6. `delete:` Static Web App deploy pipeline
`client.yml` Static Web App pipeline + `wasm-tools` workload + `staticwebapp.config.json` + the `Blazor-Environment` header injection. Server deploys as one container (reuse `server.yml` + the existing `WebAPI/Dockerfile`); the SWA artifact path, the `skip_app_build` upload, and the runtime env-header trick all go.
`.github/workflows/client.yml`, `staticwebapp.config.json`

### 7. `yagni:` warm-cache REST snapshot + pending-batch replay
The `GetLastBatch` warm-cache snapshot + `_pendingBatches` accumulation + the `JsonSettings.ApplyTo` round-trip it exists to support. It paints buses before the first SignalR batch on a cold WASM load; with a Server circuit the hub is connected before first render, so the pre-render snapshot/replay machinery is largely redundant. Re-evaluate — likely shrinkable.
`src/Client/.../Core/Services/HttpService.cs:33-37`, `src/Client/.../WebApp/Pages/TransitMap.razor.cs:54-60`

### 8. `shrink:` SignalR connection builder
SignalR `HubConnectionBuilder` against an absolute external URL → in Server, components can inject the hub context directly, or connect to a relative `/hubs/transit`. The `Uri.IsWellFormedUriString` branch, `hostEnvironment.BaseAddress`, and `TrimEnd('/')` juggling all collapse to one relative path.
`src/Client/.../Core/Services/SignalRNotificationService.cs:46-89`

### 9. `native:` synchronous local storage
`Blazored.LocalStorage` (sync) for the Settings JSON blob. Synchronous local storage works *because* WASM runs in-browser; on a Server circuit all JS-interop (including localStorage) is async over the wire — `SettingsService`'s sync Blazored calls must become async regardless, so the "sync Blazored" choice from feature 016 is invalidated by the move.
`src/Client/.../WebApp/Program.cs:80`, `src/Client/.../Shared/Services/SettingsService.cs`

---

## Caveats (blast-radius surface, not cuts)

These are correctness/perf concerns, out of audit scope, routed to a normal review pass — but they define the real risk of the migration.

- **All 12+ JS-interop modules become network round-trips.** `transit-synth.js`, `map-interop.js`, `vehicle-animator.js`, `checkpoint-tracker.js`, etc. run in-process under WASM; under Server every `InvokeAsync`/`InvokeVoidAsync` crosses the SignalR circuit. High-frequency calls (per-vehicle `TriggerNoteAsync`, animation ticks) will have latency/chattiness problems. **This is the largest behavioral risk.**
- **`AppSettings` config relocates server-side.** `builder.Configuration.GetSection("AppSettings")` is loaded from `wwwroot/appsettings.json` (publicly fetched) under WASM. On Server it moves server-side — including `MapTiler:ApiKey`, currently shipped to the browser anyway, so no secret-posture *gain* unless intentionally relocated.

---

## Dependency / project impact

| Item | WASM today | Under Server |
|---|---|---|
| `Microsoft.AspNetCore.Components.WebAssembly` | required (Core + WebApp) | removable |
| `Microsoft.AspNetCore.Components.WebAssembly.DevServer` | required (WebApp) | removable |
| `Blazored.LocalStorage` | sync usage | stays; usage must go async |
| `Microsoft.Extensions.Http` / `HttpClientFactory` | per-API named clients | collapses to in-process / single client |
| CORS middleware | required | removable |
| `client.yml` + Static Web App | required | replaced by container deploy |
