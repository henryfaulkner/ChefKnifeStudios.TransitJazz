# Implementation Plan: Solution Scaffold - Full-Stack Aspire Project

**Branch**: `002-solution-scaffold` | **Date**: 2026-05-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-solution-scaffold/spec.md`

## Summary

Create a 9-project .NET 10 Aspire-orchestrated solution matching the PokerAttack project structure (minus Server.Data). The scaffold includes a 3-tier Blazor WASM client (Core/Shared/WebApp) with MatBlazor theming, SignalR client, and EventNotificationService; an ASP.NET Core WebAPI with Clean Architecture (WebAPI/BL/Core/Infrastructure), Minimal API endpoint groups, SignalR Hub, and Scalar docs; a cross-domain Shared project; Docker containerization; and Azure DevOps deployment pipelines. Logging consolidates into Azure Log Analytics via OpenTelemetry/Application Insights.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**:
- Aspire AppHost SDK 13.0.0, ServiceDefaults (OpenTelemetry, Resilience, Service Discovery)
- MatBlazor 2.10.0, Blazored.LocalStorage 4.5.0, CommunityToolkit.Mvvm 8.4.0
- Ardalis.Result 10.1.0, Microsoft.AspNetCore.SignalR.Client 10.0.0
- Scalar.AspNetCore 2.12.41, StackExchange.Redis 2.9.17
- Azure.Monitor.OpenTelemetry.AspNetCore (for Application Insights OTEL export)

**Storage**: None (Server.Data omitted by design)  
**Testing**: Manual verification via Aspire AppHost + Scalar; Playwright E2E in pipelines  
**Target Platform**: Blazor WASM (browser) + ASP.NET Core (Linux container / Azure Container Apps)  
**Project Type**: Full-stack web application (real-time)  
**Performance Goals**: Aspire health checks pass within 30s; SignalR connects within 3s  
**Constraints**: No data persistence layer; no authentication in scaffold  
**Scale/Scope**: 9 .NET projects, ~60 source files, 3 deployment pipelines

## Constitution Check

*GATE: Must pass before implementation.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | PASS | Blazor WASM frontend + ASP.NET Core backend, independently deployable, SignalR + HTTPS communication |
| II. No Frontend Secrets | PASS | No secrets in client code; appsettings contains only public URIs and feature flags |
| III. Real-time Data Processing Pipeline | DEFERRED | Background worker / MARTA GTFS polling is a future feature, not part of scaffold |
| IV. OpenTelemetry Observability | PASS | ServiceDefaults provides OTEL metrics/tracing; FR-033 adds Azure Monitor/App Insights export to Log Analytics Workspace |
| V. Azure DevOps CI/CD Pipeline | PASS | Separate client/server pipelines in `deploy/`; WASM → Azure Static Web Apps, Docker → ACR → Azure Container Apps |

## Project Structure

### Documentation (this feature)

```text
specs/002-solution-scaffold/
├── spec.md              # Requirements specification
└── plan.md              # This file
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.TransitJazz.sln
│
├── ChefKnifeStudios.TransitJazz.AppHost/
│   ├── AppHost.cs
│   └── ChefKnifeStudios.TransitJazz.AppHost.csproj
│
├── ChefKnifeStudios.TransitJazz.ServiceDefaults/
│   ├── Extensions.cs
│   └── ChefKnifeStudios.TransitJazz.ServiceDefaults.csproj
│
├── ChefKnifeStudios.TransitJazz.Shared/
│   ├── ChefKnifeStudios.TransitJazz.Shared.csproj
│   ├── TransitJazzApiEndpoints.cs
│   ├── FeatureFlagService.cs
│   ├── JsonOptions.cs
│   ├── DTOs/
│   │   └── SignalR/
│   │       └── TransitJazzNotification.cs
│   └── Enums/
│       ├── FeatureFlags.cs
│       └── TransitJazzNotificationType.cs
│
├── Client/
│   ├── ChefKnifeStudios.TransitJazz.Client.Core/
│   │   ├── ChefKnifeStudios.TransitJazz.Client.Core.csproj
│   │   ├── AppSettings.cs
│   │   ├── Enums/
│   │   │   └── APIs.cs
│   │   └── Services/
│   │       ├── EventNotificationService.cs
│   │       ├── HttpService.cs
│   │       ├── HttpServiceFactory.cs
│   │       └── SignalRNotificationService.cs
│   │
│   ├── ChefKnifeStudios.TransitJazz.Client.Shared/
│   │   ├── ChefKnifeStudios.TransitJazz.Client.Shared.csproj
│   │   ├── _Imports.razor
│   │   ├── Constants/
│   │   │   └── ColorConstants.cs
│   │   ├── Components/
│   │   │   └── (placeholder directory)
│   │   ├── EventArgs/
│   │   │   └── ThemeChangedEventArgs.cs
│   │   ├── Services/
│   │   │   └── (placeholder directory for SettingsService, etc.)
│   │   └── ViewModels/
│   │       └── (placeholder directory)
│   │
│   └── ChefKnifeStudios.TransitJazz.Client.WebApp/
│       ├── ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
│       ├── App.razor
│       ├── Program.cs
│       ├── _Imports.razor
│       ├── Layout/
│       │   └── MainLayout.razor
│       └── wwwroot/
│           ├── index.html
│           ├── appsettings.json
│           └── css/
│               ├── variables.css
│               ├── mdc-overrides.css
│               └── app.css
│
└── Server/
    ├── ChefKnifeStudios.TransitJazz.Server.WebAPI/
    │   ├── ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj
    │   ├── Dockerfile
    │   ├── Program.cs
    │   ├── appsettings.json
    │   ├── appsettings.Development.json
    │   ├── EndpointGroups/
    │   │   └── TestEndpoints.cs
    │   └── SignalR/
    │       ├── SignalRNotificationHub.cs
    │       ├── PlayerIdProvider.cs
    │       ├── PlayerConnectionTracker.cs
    │       └── TransitJazzNotificationHelper.cs
    │
    ├── ChefKnifeStudios.TransitJazz.Server.BL/
    │   ├── ChefKnifeStudios.TransitJazz.Server.BL.csproj
    │   └── Services/
    │       └── EventNotificationService.cs
    │
    ├── ChefKnifeStudios.TransitJazz.Server.Core/
    │   ├── ChefKnifeStudios.TransitJazz.Server.Core.csproj
    │   ├── Interfaces/
    │   │   ├── ITransitJazzNotificationHelper.cs
    │   │   └── IKeyValueRepository.cs
    │   └── Models/
    │       └── (placeholder directory)
    │
    └── ChefKnifeStudios.TransitJazz.Server.Infrastructure/
        ├── ChefKnifeStudios.TransitJazz.Server.Infrastructure.csproj
        └── InMemoryKeyValueRepository.cs

deploy/
├── client-pipeline.yml
└── server-pipeline.yml

docker-compose.yml
```

**Structure Decision**: Mirrors the PokerAttack project structure 1:1 with the deliberate omission of `Server.Data`. All 9 projects share the same tiered organization: infrastructure (AppHost, ServiceDefaults), cross-domain (Shared), client tiers (Core → Shared → WebApp), and server tiers (Core → BL, Infrastructure → WebAPI).

## Implementation Phases

### Phase 1: Solution Foundation (FR-001 through FR-005, FR-044)

Create the `.sln` file and all 9 `.csproj` files with correct SDKs, target frameworks, NuGet dependencies, and project references. Verify `dotnet build` succeeds with zero errors.

**Files created:**
- `src/ChefKnifeStudios.TransitJazz.sln`
- All 9 `.csproj` files with dependencies listed below

**Project SDK and dependency mapping:**

| Project | SDK | Key NuGet Packages |
|---------|-----|--------------------|
| AppHost | `Microsoft.NET.Sdk` + `Aspire.AppHost.Sdk` 13.0.0 | `Aspire.Hosting.AppHost` 13.0.0 |
| ServiceDefaults | `Microsoft.NET.Sdk` | `Microsoft.Extensions.Http.Resilience` 10.0.0, `Microsoft.Extensions.ServiceDiscovery` 10.0.0, OpenTelemetry packages (1.13-1.14), `Azure.Monitor.OpenTelemetry.AspNetCore` |
| Shared | `Microsoft.NET.Sdk` | (none) |
| Client.Core | `Microsoft.NET.Sdk` | `Ardalis.Result` 10.1.0, `Microsoft.AspNetCore.SignalR.Client` 10.0.0, `Microsoft.Extensions.Configuration.Binder` 10.0.1, `Microsoft.AspNetCore.Components.WebAssembly` 10.0.0 |
| Client.Shared | `Microsoft.NET.Sdk.Razor` | `Blazored.LocalStorage` 4.5.0, `CommunityToolkit.Mvvm` 8.4.0, `MatBlazor` 2.10.0, `Microsoft.AspNetCore.Components.Web` 10.0.0 |
| Client.WebApp | `Microsoft.NET.Sdk.BlazorWebAssembly` | `Microsoft.AspNetCore.Components.WebAssembly` 10.0.0, `Microsoft.Extensions.Http` 10.0.0 |
| Server.WebAPI | `Microsoft.NET.Sdk.Web` | `Ardalis.Result` 10.1.0, `Microsoft.AspNetCore.OpenApi` 10.0.0, `Scalar.AspNetCore` 2.12.41 |
| Server.BL | `Microsoft.NET.Sdk` | `Ardalis.Result` 10.1.0, `Microsoft.Extensions.Hosting` 10.0.0 |
| Server.Core | `Microsoft.NET.Sdk` | `Ardalis.Result` 10.1.0 |
| Server.Infrastructure | `Microsoft.NET.Sdk` | `StackExchange.Redis` 2.9.17 |

**Project reference chain (FR-044):**
```
AppHost → Client.WebApp, Server.WebAPI
Client.WebApp → Client.Shared
Client.Shared → Client.Core
Client.Core → Shared
Server.WebAPI → ServiceDefaults, Server.BL, Server.Infrastructure
Server.BL → Server.Core
Server.Core → Shared
Server.Infrastructure → Server.Core
```

**Verification**: `dotnet build src/ChefKnifeStudios.TransitJazz.sln` → 0 errors, 0 warnings.

---

### Phase 2: Aspire Orchestration (FR-006 through FR-008)

Wire up the Aspire AppHost and ServiceDefaults so both client and server projects start via the Aspire dashboard with health checks.

**Files:**
- `src/ChefKnifeStudios.TransitJazz.AppHost/AppHost.cs` — Register `apiservice` and `webfrontend` with health checks, references, and WaitFor.
- `src/ChefKnifeStudios.TransitJazz.ServiceDefaults/Extensions.cs` — Port the PokerAttack `Extensions.cs` verbatim with namespace change. Enable the Azure Monitor OTEL exporter (uncomment the `UseAzureMonitor()` block from PokerAttack's template) to satisfy FR-033/Constitution Principle IV.

**Key design decisions:**
- The ServiceDefaults `AddOpenTelemetryExporters` method checks for `APPLICATIONINSIGHTS_CONNECTION_STRING` and calls `UseAzureMonitor()` when present. This replaces Sentry throughout the stack.
- Health endpoints (`/health`, `/alive`) are mapped only in development via `MapDefaultEndpoints()`.

**Verification**: Run AppHost, see both services green on the Aspire dashboard, hit `/health` on both.

---

### Phase 3: Cross-Domain Shared Project (FR-034 through FR-038)

Create the Shared project's domain contracts that both client and server depend on.

**Files:**
- `TransitJazzApiEndpoints.cs` — Static class with nested `Test` class defining route constants (e.g., `public const string SignalR = "/test/signalr";`). Additional endpoint groups added as empty nested classes for extensibility.
- `FeatureFlagService.cs` — `IFeatureFlagService` interface + `FeatureFlagService` implementation (port from PokerAttack).
- `JsonOptions.cs` — Static `Get()` method returning `JsonSerializerOptions` with camelCase, case-insensitive, and any needed converters.
- `DTOs/SignalR/TransitJazzNotification.cs` — Record with `TransitJazzNotificationType Type` and `string Message`.
- `Enums/FeatureFlags.cs` — Enum with placeholder values.
- `Enums/TransitJazzNotificationType.cs` — Enum with `Test` value.

**Verification**: Projects referencing Shared compile without errors.

---

### Phase 4: Server Clean Architecture Layers (FR-020 through FR-032)

Build the server from the inside out: Core interfaces, then Infrastructure implementations, then BL services, then WebAPI host.

#### Phase 4a: Server.Core (FR-023)

**Files:**
- `Interfaces/ITransitJazzNotificationHelper.cs` — Interface with `BroadcastToAllAsync`, `BroadcastToGroupAsync`, `GetGroupName` methods.
- `Interfaces/IKeyValueRepository.cs` — Generic `IKeyValueRepository<T>` interface with Get/GetAll/Set/Delete async methods returning `Result<T>`.
- `Models/` — Empty directory placeholder.

#### Phase 4b: Server.Infrastructure (FR-024)

**Files:**
- `InMemoryKeyValueRepository.cs` — `ConcurrentDictionary`-backed implementation of `IKeyValueRepository<T>`.

#### Phase 4c: Server.BL (FR-021)

**Files:**
- `Services/EventNotificationService.cs` — Server-side event bus (port from PokerAttack's server EventNotificationService with fire-and-forget handler invocation and logging).

#### Phase 4d: Server.WebAPI (FR-020, FR-025 through FR-033)

**Files:**
- `Program.cs` — Full startup:
  1. `AddServiceDefaults()` (Aspire + OTEL)
  2. `AddProblemDetails()`
  3. `AddOpenApi()`
  4. `AddCors()` with localhost origins + placeholder production domain
  5. `AddSignalR()` + register `PlayerIdProvider`, `TransitJazzNotificationHelper`, `PlayerConnectionTracker`
  6. Register `IKeyValueRepository<>` as `InMemoryKeyValueRepository<>`
  7. Register `IEventNotificationService`
  8. Register `IFeatureFlagService` from config
  9. Middleware: `UseExceptionHandler`, `MapOpenApi`, `MapScalarApiReference` (Solarized/Classic/DarkMode), `UseCors`, `MapHub<SignalRNotificationHub>("/cks-notification")`, `MapTestEndpoints()`, `MapDefaultEndpoints`

- `appsettings.json` — Logging config with `APPLICATIONINSIGHTS_CONNECTION_STRING` placeholder. No Sentry section.
- `appsettings.Development.json` — Debug-level logging overrides.

- `SignalR/SignalRNotificationHub.cs` — Simplified hub with `OnConnectedAsync`/`OnDisconnectedAsync` tracking, group join/leave, and a `BroadcastNotification` method. Domain-specific game methods from PokerAttack are omitted.
- `SignalR/PlayerIdProvider.cs` — Reads `playerId` from query string (port from PokerAttack).
- `SignalR/PlayerConnectionTracker.cs` — Thread-safe connection tracking (port from PokerAttack).
- `SignalR/TransitJazzNotificationHelper.cs` — Implementation of `ITransitJazzNotificationHelper` using `IHubContext<SignalRNotificationHub>`.

- `EndpointGroups/TestEndpoints.cs` — Test endpoint group with a `POST /test/signalr` endpoint that broadcasts a notification via the hub helper, demonstrating the Minimal API group pattern.

- `Dockerfile` — Multi-stage build (SDK 10.0 → publish → aspnet 10.0, expose 8080).

**Verification**: Start WebAPI, navigate to `/scalar/v1`, invoke test endpoint, see 200 response.

---

### Phase 5: Client 3-Tier Stack (FR-009 through FR-019)

Build the client from the inside out: Core services, then Shared components, then WebApp host.

#### Phase 5a: Client.Core (FR-011, FR-014, FR-015, FR-017)

**Files:**
- `AppSettings.cs` — Configuration class with `ExternalApis` list and `FeatureFlags` dictionary (port from PokerAttack, replace enum namespace).
- `Enums/APIs.cs` — Enum with `TransitJazzSignalR`, `TransitJazzAPI`.
- `Services/EventNotificationService.cs` — Client-side event bus (port from PokerAttack client).
- `Services/HttpService.cs` — HTTP wrapper with Ardalis.Result (port from PokerAttack).
- `Services/HttpServiceFactory.cs` — Named HttpClient factory (port from PokerAttack).
- `Services/SignalRNotificationService.cs` — SignalR client connecting to `/cks-notification` with auto-reconnect. Simplified from PokerAttack: only `InitAsync`, `JoinGroupAsync`, `LeaveGroupAsync`, and `HandleNotificationReceived` event. Domain-specific methods (PlayHand, Discard, etc.) are omitted.

#### Phase 5b: Client.Shared (FR-010, FR-012, FR-013, FR-016)

**Files:**
- `Constants/ColorConstants.cs` — Exact copy of PokerAttack's color values (Light and Dark nested classes with all Material Design 3 properties).
- `EventArgs/ThemeChangedEventArgs.cs` — `IEventArgs` implementation with `bool IsDarkMode`.
- `_Imports.razor` — `@using Microsoft.AspNetCore.Components.Web` and `@using MatBlazor`.
- `Components/` — Empty placeholder directory.
- `Services/` — Empty placeholder directory.
- `ViewModels/` — Empty placeholder directory.

#### Phase 5c: Client.WebApp (FR-009, FR-016, FR-017, FR-018, FR-019)

**Files:**
- `Program.cs` — Blazor WASM startup:
  1. Bind `AppSettings` from configuration
  2. Register named `HttpClient`s from `ExternalApis` config
  3. Register `IHttpServiceFactory`, `IFeatureFlagService`, `IEventNotificationService`, `ISignalRNotificationService`
  4. `AddMatBlazor()` + `AddMatToaster()` (same config as PokerAttack)
  5. `AddBlazoredLocalStorage()`
  6. Configure logging (no Sentry — OTEL/Azure Monitor handles it server-side; client logs stay in browser console)

- `App.razor` — Standard router with MainLayout (port from PokerAttack).
- `Layout/MainLayout.razor` — MatThemeProvider with Light/Dark theme toggle via EventNotificationService subscription (port from PokerAttack, replace namespaces).
- `_Imports.razor` — Standard Blazor usings + MatBlazor + TransitJazz namespaces.

- `wwwroot/index.html` — MatBlazor CSS/JS, custom CSS links, Blazor WASM script. No Driver.js (tour feature not needed in scaffold).
- `wwwroot/appsettings.json` — ExternalApis config with `TransitJazzSignalR` and `TransitJazzAPI` entries using localhost placeholder URLs. FeatureFlags section with empty defaults.
- `wwwroot/css/variables.css` — CSS custom properties for theming (empty scaffold with color variable placeholders).
- `wwwroot/css/mdc-overrides.css` — MatBlazor/MDC component overrides (minimal scaffold).
- `wwwroot/css/app.css` — Base application styles (body, error UI, loading indicator).

**Verification**: Start AppHost, open client URL, see MatBlazor-themed layout. Toggle theme via programmatic event.

---

### Phase 6: Containerization (FR-039, FR-040)

**Files:**
- `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Dockerfile` — Multi-stage: `dotnet/sdk:10.0` build, publish, `dotnet/aspnet:10.0` runtime, expose 8080.
- `docker-compose.yml` (repo root) — Single service `chefknifestudios.transitjazz.server.webapi` with `CONTAINER_REGISTRY` and `CONTAINER_TAG` env vars.

**Verification**: `docker compose build` succeeds. `docker run -p 5268:8080` starts the API.

---

### Phase 7: Deployment Pipelines (FR-041, FR-042)

**Files:**
- `deploy/client-pipeline.yml` — Azure DevOps YAML: BuildAndPublish (dotnet publish Blazor WASM) → DeployProd (Azure Static Web Apps). Adapted from PokerAttack with TransitJazz naming.
- `deploy/server-pipeline.yml` — Azure DevOps YAML: BuildAndPublish (docker compose build + push to ACR) → DeployProd (deploy to Azure Container Apps, no migration step). Adapted from PokerAttack with TransitJazz naming, migration step removed, and no E2E test stage.

**Key differences from PokerAttack pipelines:**
- Service connection names use `TransitJazz` prefix
- Variable group names use `Prod-TransitJazz`
- Container app names use `transit-jazz-api`
- Resource group uses `transit-jazz-rg`
- No migration container or DB migration step in server pipeline
- No E2E test stage in any pipeline
- Azure subscription service connection placeholder: `TransitJazzSC`

**Verification**: YAML parses without syntax errors. Variable references are internally consistent.

---

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| 9 projects for a scaffold | Matches the proven PokerAttack architecture 1:1. Future TransitJazz features (MARTA polling, music engine, Azure Maps) slot into the existing layers without restructuring. | A 3-project solution (Client, Server, Shared) would require restructuring when business logic, infrastructure, and core domain separation become necessary. |
| OpenTelemetry + Azure Monitor in scaffold | Constitution Principle IV mandates OTEL to Azure Log Analytics from day one. Adding it later means retrofitting every service. | Console logging alone would violate the constitution and require rework. |

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| MatBlazor 2.10.0 compatibility with .NET 10 | Medium | High — client won't render | Test MatBlazor rendering during Phase 5c. If incompatible, pin a known-working version or use MatBlazor nightly builds. |
| Azure.Monitor.OpenTelemetry.AspNetCore version mismatch with OTEL packages | Low | Medium — build errors | Pin all OTEL packages to compatible version set. ServiceDefaults already uses 1.13-1.14 range. |
| Aspire 13.0.0 SDK availability for .NET 10 | Low | High — AppHost won't build | Verify SDK version exists on NuGet before starting Phase 1. Fall back to latest stable if 13.0.0 is preview-only. |
