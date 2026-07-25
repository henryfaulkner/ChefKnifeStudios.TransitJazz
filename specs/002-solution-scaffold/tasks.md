# Tasks: Solution Scaffold - Full-Stack Aspire Project

**Input**: Design documents from `/specs/002-solution-scaffold/`
**Prerequisites**: plan.md (required), spec.md (required for user stories)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Solution Foundation (FR-001 through FR-005)

**Purpose**: Create the .sln file and all 9 .csproj files with correct SDKs, target frameworks, NuGet packages, and project references. The solution must compile with zero errors.

- [X] T001 [P] Create `src/ChefKnifeStudios.TransitJazz.Shared/ChefKnifeStudios.TransitJazz.Shared.csproj` — SDK: `Microsoft.NET.Sdk`, net10.0, no NuGet dependencies
- [X] T002 [P] Create `src/ChefKnifeStudios.TransitJazz.ServiceDefaults/ChefKnifeStudios.TransitJazz.ServiceDefaults.csproj` — SDK: `Microsoft.NET.Sdk`, net10.0, `IsAspireSharedProject=true`, FrameworkReference `Microsoft.AspNetCore.App`, NuGet: `Microsoft.Extensions.Http.Resilience` 10.0.0, `Microsoft.Extensions.ServiceDiscovery` 10.0.0, OpenTelemetry packages (1.14.0), `Azure.Monitor.OpenTelemetry.AspNetCore`
- [X] T003 [P] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.Core/ChefKnifeStudios.TransitJazz.Server.Core.csproj` — SDK: `Microsoft.NET.Sdk`, net10.0, NuGet: `Ardalis.Result` 10.1.0, ProjectRef: Shared
- [X] T004 [P] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.Infrastructure/ChefKnifeStudios.TransitJazz.Server.Infrastructure.csproj` — SDK: `Microsoft.NET.Sdk`, net10.0, NuGet: `StackExchange.Redis` 2.9.17, ProjectRef: Server.Core
- [X] T005 [P] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.BL/ChefKnifeStudios.TransitJazz.Server.BL.csproj` — SDK: `Microsoft.NET.Sdk`, net10.0, NuGet: `Ardalis.Result` 10.1.0, `Microsoft.Extensions.Hosting` 10.0.0, ProjectRef: Server.Core
- [X] T006 [P] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj` — SDK: `Microsoft.NET.Sdk.Web`, net10.0, NuGet: `Ardalis.Result` 10.1.0, `Microsoft.AspNetCore.OpenApi` 10.0.0, `Scalar.AspNetCore` 2.12.41, ProjectRef: ServiceDefaults, Server.BL, Server.Infrastructure
- [X] T007 [P] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/ChefKnifeStudios.TransitJazz.Client.Core.csproj` — SDK: `Microsoft.NET.Sdk`, net10.0, NuGet: `Ardalis.Result` 10.1.0, `Microsoft.AspNetCore.SignalR.Client` 10.0.0, `Microsoft.Extensions.Configuration.Binder` 10.0.1, `Microsoft.AspNetCore.Components.WebAssembly` 10.0.0, ProjectRef: Shared
- [X] T008 [P] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ChefKnifeStudios.TransitJazz.Client.Shared.csproj` — SDK: `Microsoft.NET.Sdk.Razor`, net10.0, RootNamespace: `ChefKnifeStudios.TransitJazz.Client.Shared`, SupportedPlatform: browser, NuGet: `Blazored.LocalStorage` 4.5.0, `CommunityToolkit.Mvvm` 8.4.0, `MatBlazor` 2.10.0, `Microsoft.AspNetCore.Components.Web` 10.0.0, ProjectRef: Client.Core
- [X] T009 [P] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj` — SDK: `Microsoft.NET.Sdk.BlazorWebAssembly`, net10.0, RootNamespace: `ChefKnifeStudios.TransitJazz.Client.WebApp`, SupportedPlatform: browser, NuGet: `Microsoft.AspNetCore.Components.WebAssembly` 10.0.0, `Microsoft.AspNetCore.Components.WebAssembly.DevServer` 10.0.0 (PrivateAssets=all), `Microsoft.Extensions.Http` 10.0.0, ProjectRef: Client.Shared
- [X] T010 [P] Create `src/ChefKnifeStudios.TransitJazz.AppHost/ChefKnifeStudios.TransitJazz.AppHost.csproj` — SDK: `Microsoft.NET.Sdk` + `Aspire.AppHost.Sdk` 13.0.0, OutputType: Exe, net10.0, NuGet: `Aspire.Hosting.AppHost` 13.0.0, ProjectRef: Client.WebApp, Server.WebAPI
- [X] T011 Create `src/ChefKnifeStudios.TransitJazz.sln` — Visual Studio solution file with all 9 projects, solution folders `Client` (containing Client.Core, Client.Shared, Client.WebApp) and `Server` (containing Server.WebAPI, Server.BL, Server.Core, Server.Infrastructure), plus AppHost, ServiceDefaults, and Shared at root level
- [X] T012 Add minimal placeholder files so all projects compile: empty `Class1.cs` or equivalent stub in each project that has no source files yet. Run `dotnet build src/ChefKnifeStudios.TransitJazz.sln` and verify 0 errors.

**Checkpoint**: Solution compiles. All 9 projects build successfully. Project reference graph matches FR-044.

---

## Phase 2: Aspire + Shared Contracts (FR-006 through FR-008, FR-034 through FR-038)

**Purpose**: Wire up Aspire orchestration and create the cross-domain Shared contracts that both client and server depend on. This phase MUST complete before any user story work.

### Aspire Orchestration

- [X] T013 [US1] Create `src/ChefKnifeStudios.TransitJazz.AppHost/AppHost.cs` — Register `apiservice` (Server.WebAPI) with `.WithHttpHealthCheck("/health")`, register `webfrontend` (Client.WebApp) with `.WithExternalHttpEndpoints()`, `.WithHttpHealthCheck("/health")`, `.WithReference(apiService)`, `.WaitFor(apiService)`. Port from PokerAttack's AppHost.cs with namespace changes.
- [X] T014 [US1] Create `src/ChefKnifeStudios.TransitJazz.ServiceDefaults/Extensions.cs` — Port PokerAttack's Extensions.cs verbatim with namespace `Microsoft.Extensions.Hosting`. Include `AddServiceDefaults`, `ConfigureOpenTelemetry`, `AddOpenTelemetryExporters` (enable `UseAzureMonitor()` when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set), `AddDefaultHealthChecks`, `MapDefaultEndpoints`. Add NuGet: `Azure.Monitor.OpenTelemetry.AspNetCore`.

### Shared Project Contracts

- [X] T015 [P] Create `src/ChefKnifeStudios.TransitJazz.Shared/Enums/FeatureFlags.cs` — Enum `FeatureFlags` with placeholder value `Placeholder = 0`
- [X] T016 [P] Create `src/ChefKnifeStudios.TransitJazz.Shared/Enums/TransitJazzNotificationType.cs` — Enum `TransitJazzNotificationType` with value `Test = 0`
- [X] T017 [P] Create `src/ChefKnifeStudios.TransitJazz.Shared/DTOs/SignalR/TransitJazzNotification.cs` — Record: `public record TransitJazzNotification(TransitJazzNotificationType Type, string Message)`
- [X] T018 [P] Create `src/ChefKnifeStudios.TransitJazz.Shared/TransitJazzApiEndpoints.cs` — Static class with nested `Test` class: `public const string SignalR = "/test/signalr";`
- [X] T019 [P] Create `src/ChefKnifeStudios.TransitJazz.Shared/FeatureFlagService.cs` — Port from PokerAttack: `IFeatureFlagService` interface with `IsEnabled(FeatureFlags flag)` method + `FeatureFlagService` implementation using `Dictionary<FeatureFlags, bool>`
- [X] T020 [P] Create `src/ChefKnifeStudios.TransitJazz.Shared/JsonOptions.cs` — Static class with `Get()` method returning `JsonSerializerOptions` configured for camelCase, case-insensitive deserialization

**Checkpoint**: Aspire AppHost compiles. Shared project has all contracts needed by client and server. Remove any placeholder stubs from T012 that are no longer needed.

---

## Phase 3: US1 — Solution Builds and Runs via Aspire (Priority: P1)

**Goal**: Both server and client start via the Aspire AppHost with health checks passing and Scalar docs loading.

**Independent Test**: Run AppHost, verify both `/health` endpoints return healthy, navigate to `/scalar/v1`.

### Server.Core (FR-023)

- [X] T021 [P] [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.Core/Interfaces/ITransitJazzNotificationHelper.cs` — Interface with `Task BroadcastToAllAsync(TransitJazzNotification notification, CancellationToken ct = default)`, `Task BroadcastToGroupAsync(string groupId, TransitJazzNotification notification, CancellationToken ct = default)`, `string GetGroupName(string id)`
- [X] T022 [P] [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.Core/Interfaces/IKeyValueRepository.cs` — Generic interface `IKeyValueRepository<T>` with `Task<Result<T>> GetAsync(string key, CancellationToken ct = default)`, `Task<Result<Dictionary<string, T>>> GetAllAsync(CancellationToken ct = default)`, `Task<Result> SetAsync(string key, T value, CancellationToken ct = default)`, `Task<Result> DeleteAsync(string key, CancellationToken ct = default)`

### Server.Infrastructure (FR-024)

- [X] T023 [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.Infrastructure/InMemoryKeyValueRepository.cs` — `ConcurrentDictionary`-backed implementation of `IKeyValueRepository<T>` returning `Result.Success`/`Result.NotFound` as appropriate

### Server.BL (FR-021)

- [X] T024 [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.BL/Services/EventNotificationService.cs` — Server-side event bus. Port from PokerAttack's server `EventNotificationService` with fire-and-forget handler invocation, invocation list capture, and `ILogger` error handling. Use namespace `ChefKnifeStudios.TransitJazz.Server.BL.Services`.

### Server.WebAPI (FR-020, FR-025 through FR-033)

- [X] T025 [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/PlayerIdProvider.cs` — Port from PokerAttack: `IUserIdProvider` reading `playerId` from query string
- [X] T026 [P] [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/PlayerConnectionTracker.cs` — Port from PokerAttack: `IPlayerConnectionTracker` interface + `PlayerConnectionTracker` with `ConcurrentDictionary<string, HashSet<string>>`, methods: `Add`, `Remove`, `GetConnections`
- [X] T027 [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/TransitJazzNotificationHelper.cs` — Implementation of `ITransitJazzNotificationHelper` using `IHubContext<SignalRNotificationHub, ISignalRNotificationClient>`. Methods: `BroadcastToAllAsync` (calls `Clients.All`), `BroadcastToGroupAsync` (calls `Clients.Group`), `GetGroupName` (returns `"group-{id}"`)
- [X] T028 [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/SignalRNotificationHub.cs` — Simplified hub: `ISignalRNotificationClient` interface with `Task ReceiveTransitJazzNotification(TransitJazzNotification notification, CancellationToken ct = default)`. `[AllowAnonymous]` hub with `OnConnectedAsync` (register via `IPlayerConnectionTracker`), `OnDisconnectedAsync` (unregister), `JoinGroupAsync(string groupId)`, `LeaveGroupAsync(string groupId)`, `BroadcastNotification(TransitJazzNotification notification)`
- [X] T029 [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` — Logging config (Default: Debug, Microsoft.AspNetCore: Warning), `AllowedHosts: "*"`, placeholder `APPLICATIONINSIGHTS_CONNECTION_STRING` comment
- [X] T030 [P] [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.Development.json` — Development logging overrides
- [X] T031 [US1] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` — Full startup: `AddServiceDefaults()`, `AddProblemDetails()`, `AddOpenApi()`, `AddCors()` (localhost:7150, localhost:5186, localhost:7333), `ConfigureHttpJsonOptions` (use `JsonOptions.Get()`), `AddSignalR()`, register `IUserIdProvider`→`PlayerIdProvider`, `ITransitJazzNotificationHelper`→`TransitJazzNotificationHelper`, `IPlayerConnectionTracker`→`PlayerConnectionTracker`, `IKeyValueRepository<>`→`InMemoryKeyValueRepository<>`, `IEventNotificationService`→server `EventNotificationService`, `IFeatureFlagService` from config. Middleware: `UseExceptionHandler()`, `MapOpenApi().AllowAnonymous()`, `MapScalarApiReference` (Title: "TransitJazz API", Theme: Solarized, Layout: Classic, DarkMode: true, HiddenClients: true, ClientButton: false, DefaultHttpClient: JavaScript/Axios), `UseCors()`, `MapHub<SignalRNotificationHub>("/cks-notification")`, `MapDefaultEndpoints()`

**Checkpoint**: Run `dotnet run --project src/ChefKnifeStudios.TransitJazz.AppHost`. Both services start. `/health` returns healthy. `/scalar/v1` loads the API docs page.

---

## Phase 4: US2 — Server API Endpoint Group Pattern (Priority: P1)

**Goal**: TestEndpoints group is wired up and callable via Scalar.

**Independent Test**: POST to `/test/signalr` via Scalar returns 200 OK.

- [X] T032 [US2] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/TestEndpoints.cs` — Static class with `MapTestEndpoints(this IEndpointRouteBuilder builder)` extension method. Create group with `.MapGroup(string.Empty).WithName("Test").WithTags("Test")`. Map `POST` to `TransitJazzApiEndpoints.Test.SignalR` that accepts a request body, calls `ITransitJazzNotificationHelper.BroadcastToGroupAsync`, returns `Result.Success()`. Include `.Produces<>` and `.WithName()` metadata.
- [X] T033 [US2] Wire `MapTestEndpoints()` into `Program.cs` — Add `app.MapTestEndpoints()` after CORS middleware and before `MapDefaultEndpoints()`. Verify endpoint appears in Scalar.

**Checkpoint**: Start API, open `/scalar/v1`, invoke POST `/test/signalr`, receive 200 OK.

---

## Phase 5: US3 — SignalR Hub Connects Client to Server (Priority: P1)

**Goal**: Client's SignalRNotificationService connects to server hub at `/cks-notification`.

**Independent Test**: Start AppHost, open client, call `InitAsync`, verify connection established.

### Client.Core Services

- [X] T034 [P] [US3] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Enums/APIs.cs` — Enum `APIs` with values `TransitJazzSignalR`, `TransitJazzAPI`
- [X] T035 [P] [US3] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/AppSettings.cs` — Port from PokerAttack: `AppSettings` class with `List<WebAPI> ExternalApis` and `Dictionary<FeatureFlags, bool> FeatureFlags`. Nested `WebAPI` class with `Name`, `BaseUri`, `AuthenticationRequired`, `AddHttpClient` properties. Update enum namespace to `ChefKnifeStudios.TransitJazz.Shared.Enums`.
- [X] T036 [US3] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/EventNotificationService.cs` — Port from PokerAttack client: `EventReceivedEventHandler` delegate, `IEventNotificationService` interface with `event EventReceived` and `PostEvent` method, `IEventArgs` interface, `EventNotificationService` implementation
- [X] T037 [US3] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/SignalRNotificationService.cs` — Simplified port from PokerAttack. `TransitJazzNotificationHandler` delegate. `ISignalRNotificationService` interface with: `event HandleNotificationReceived`, `InitAsync(string userId)`, `JoinGroupAsync(string groupId)`, `LeaveGroupAsync(string groupId)`. Implementation: read `TransitJazzSignalR` base URI from config, build `HubConnection` to `/cks-notification?playerId={userId}` with `WithAutomaticReconnect()`, register `On<TransitJazzNotification>("ReceiveTransitJazzNotification")` handler. Include `Dispose`, `CloseConnection`, `EnsureConnectedAsync` helpers.

**Checkpoint**: Client.Core compiles. SignalR service can be injected and connect to the server hub.

---

## Phase 6: US4 — Client Event Bus + US5 — HTTP Service Factory (Priority: P2)

**Goal**: Local eventing works (theme toggle). HTTP service factory creates named clients with Ardalis.Result wrapping.

**Independent Test**: Post a `ThemeChangedEventArgs` event, observe theme toggle. Resolve `IHttpServiceFactory`, create a client, hit test endpoint.

### HTTP Services (US5)

- [X] T038 [P] [US5] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/HttpServiceFactory.cs` — Port from PokerAttack: `IHttpServiceFactory` interface with `IHttpService Create(string name)`, `HttpServiceFactory` implementation taking `Func<string, HttpClient>` delegate
- [X] T039 [P] [US5] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/HttpService.cs` — Port from PokerAttack: `IHttpService` interface with `GetAsync<T>`, `PostAsync<X,Y>`, `PostAsync<Y>` (FormUrlEncoded), `PutAsync<X,Y>`, `PatchAsync<X,Y>`, `DeleteAsync<T>`. `HttpService` implementation with `Ardalis.Result` wrapping, `JsonOptions.Get()` for serialization, `HandleResponse<T>` mapping status codes to Result types

### Client.Shared (US4, FR-010, FR-012, FR-013)

- [X] T040 [P] [US4] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Constants/ColorConstants.cs` — Exact copy of PokerAttack's `ColorConstants.cs` with namespace changed to `ChefKnifeStudios.TransitJazz.Client.Shared.Constants`. Include `LightNotUsed`, `DarkNotUsed` (Material 3 baseline), `Light` (Deep Blue primary), `Dark` (Pastel Blue primary) — all hex values identical.
- [X] T041 [P] [US4] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/EventArgs/ThemeChangedEventArgs.cs` — Class implementing `IEventArgs` (from Client.Core) with `bool IsDarkMode` property
- [X] T042 [P] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/_Imports.razor` — `@using Microsoft.AspNetCore.Components.Web` and `@using MatBlazor`

**Checkpoint**: Client.Core and Client.Shared compile. All service interfaces and implementations are in place.

---

## Phase 7: US1/US3/US4 — Client WebApp Host (Priority: P1)

**Goal**: Blazor WASM app loads in browser with MatBlazor-themed layout, connects to API via SignalR, supports theme toggling.

**Independent Test**: Start AppHost, open client URL, see themed layout. Verify SignalR connects via browser console logs.

- [X] T043 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json` — `AppSettings.ExternalApis`: `TransitJazzSignalR` (BaseUri: localhost placeholder, AddHttpClient: false), `TransitJazzAPI` (BaseUri: localhost placeholder, AuthenticationRequired: false, AddHttpClient: true). `AppSettings.FeatureFlags`: empty. Logging: Default Debug, Microsoft.AspNetCore Warning.
- [X] T044 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/css/variables.css` — CSS custom properties file with color variable placeholders matching ColorConstants
- [X] T045 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/css/mdc-overrides.css` — Minimal MatBlazor/MDC component style overrides
- [X] T046 [P] [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/css/app.css` — Base application styles: body defaults, `#blazor-error-ui` styling, loading indicator
- [X] T047 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/index.html` — HTML entry point: MatBlazor CSS (`_content/MatBlazor/dist/matBlazor.css`), MatBlazor JS (`_content/MatBlazor/dist/matBlazor.js`), links to `css/variables.css`, `css/mdc-overrides.css`, `css/app.css`, scoped CSS link, `<div id="app">Loading...</div>`, blazor-error-ui div, `_framework/blazor.webassembly.js`
- [X] T048 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/_Imports.razor` — Standard Blazor usings: `System.Net.Http`, `System.Net.Http.Json`, `Microsoft.AspNetCore.Components.Forms/Routing/Web/Web.Virtualization/WebAssembly.Http`, `Microsoft.JSInterop`, `ChefKnifeStudios.TransitJazz.Client.Shared.Constants`, `ChefKnifeStudios.TransitJazz.Client.WebApp`, `ChefKnifeStudios.TransitJazz.Client.WebApp.Layout`, `MatBlazor`
- [X] T049 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/App.razor` — Port from PokerAttack: `<Router>` with `Found`/`NotFound` handling, `DefaultLayout="@typeof(MainLayout)"`
- [X] T050 [US4] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Layout/MainLayout.razor` — Port from PokerAttack: `@inherits LayoutComponentBase`, `@implements IDisposable`, inject `IEventNotificationService`. `MatThemeProvider` wrapping `@Body` + `MatToastContainer`. Static `LightTheme`/`DarkTheme` `MatTheme` instances using `ColorConstants.Light.*`/`ColorConstants.Dark.*`. Subscribe to `EventReceived` in `OnInitialized`, handle `ThemeChangedEventArgs` to toggle theme, `Dispose` unsubscribes.
- [X] T051 [US1] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs` — Blazor WASM startup: bind `AppSettings` from config, register named `HttpClient`s from `ExternalApis` where `AddHttpClient=true`, register `IHttpServiceFactory` (via `HttpServiceFactory` with `IHttpClientFactory` delegate), register `IFeatureFlagService` from `appSettings.FeatureFlags`, register `IEventNotificationService`→`EventNotificationService` (singleton), register `ISignalRNotificationService`→`SignalRNotificationService` (scoped), `AddMatBlazor()`, `AddMatToaster()` (Position: BottomRight, PreventDuplicates: true, NewestOnTop: true, ShowCloseButton: true, ShowProgressBar: true), `AddBlazoredLocalStorage()`, `builder.Build().RunAsync()`

**Checkpoint**: Start AppHost. Client loads in browser with MatBlazor-themed layout using PokerAttack colors. SignalR connection logs appear in browser console. Theme can be toggled programmatically.

---

## Phase 8: US6 — Docker Containerization (Priority: P2)

**Goal**: `docker compose build` produces a runnable server container image.

**Independent Test**: Build and run container, verify API responds on port 8080.

- [X] T052 [US6] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Dockerfile` — Multi-stage: `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`, `WORKDIR /src`, `COPY ["./src", "/src"]`, `WORKDIR /src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI`, `RUN dotnet build -c Release`, publish stage, `FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final`, `EXPOSE 8080`, `ENTRYPOINT ["dotnet", "ChefKnifeStudios.TransitJazz.Server.WebAPI.dll"]`
- [X] T053 [US6] Create `docker-compose.yml` (repo root) — Single service `chefknifestudios.transitjazz.server.webapi`, build context `.`, dockerfile `./src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Dockerfile`, image `${CONTAINER_REGISTRY:-localhost:5000}/chefknifestudios.transitjazz.server.webapi:${CONTAINER_TAG:-latest}`

**Checkpoint**: `docker compose build` succeeds. `docker run -d -p 5268:8080 <image>` starts API that responds to requests.

---

## Phase 9: US7 — Deployment Pipelines (Priority: P3)

**Goal**: Azure DevOps YAML pipelines for separate client and server deployment.

**Independent Test**: YAML parses without syntax errors. Variable references are internally consistent.

- [X] T054 [P] [US7] Create `deploy/client-pipeline.yml` — Azure DevOps YAML adapted from PokerAttack: name format `$(SourceBranchName)-chefknifestudios-transitjazz-client-$(Date:yyyyMMdd).$(Rev:r)`, trigger on main, ubuntu-latest pool. Stage BuildAndPublish: `UseDotNet@2` (10.x), `NuGetAuthenticate@1`, `dotnet workload install wasm-tools`, `DotNetCoreCLI@2` publish `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj`, `PublishPipelineArtifact@1`. Stage DeployProd: deployment to Azure Static Web Apps using variable group `Prod-TransitJazz`, `ExtractFiles@1`, update `staticwebapp.config.json` Blazor-Environment header, `AzureStaticWebApp@0` with `$(TransitJazzWebDeploymentToken)`.
- [X] T055 [P] [US7] Create `deploy/server-pipeline.yml` — Azure DevOps YAML adapted from PokerAttack: name format `$(SourceBranchName)-chefknifestudios-transitjazz-server-$(Date:yyyyMMdd).$(Rev:r)`, trigger on main, ubuntu-latest pool. Variables: `azure-subscription-service-connection: TransitJazzSC`, `container-registry: chefknife.azurecr.io`, `api-container-name: chefknifestudios.transitjazz.server.webapi`. Stage BuildAndPublish: Docker login, `docker compose build`, push API image. Stage DeployProd: `AzureContainerApps@1` deploy to `transit-jazz-api` in `transit-jazz-rg`. No migration step. No E2E test stage.

**Checkpoint**: Both YAML files parse without errors. All variable references are consistent.

---

## Phase 10: Cleanup & Verification

**Purpose**: Remove scaffolding stubs, verify all success criteria, final build check.

- [X] T056 Remove any remaining placeholder/stub files (empty `Class1.cs` files from T012 that are no longer needed)
- [X] T057 Run `dotnet build src/ChefKnifeStudios.TransitJazz.sln` — verify 0 errors, 0 warnings (SC-001)
- [ ] T058 Start AppHost — verify both services start with `/health` returning healthy (SC-002)
- [ ] T059 Verify `/scalar/v1` loads with TestEndpoints documented (SC-003)
- [ ] T060 Verify client loads in browser with MatBlazor-themed layout (SC-004)
- [X] T061 Verify project reference graph has no circular dependencies (SC-009)
- [X] T062 Verify ColorConstants hex values match PokerAttack exactly (SC-010)

**Checkpoint**: All success criteria pass. Solution is ready for feature development.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Foundation)**: No dependencies — start immediately. All T001-T010 are parallel.
- **Phase 2 (Aspire + Shared)**: Depends on Phase 1. T015-T020 are parallel. T013-T014 are sequential.
- **Phase 3 (US1 Server Layers)**: Depends on Phase 2. T021-T022 are parallel. Then T023, T024, T025-T026 (parallel), T027, T028, T029-T030 (parallel), T031.
- **Phase 4 (US2 Endpoints)**: Depends on Phase 3 (needs Program.cs and hub).
- **Phase 5 (US3 Client SignalR)**: Depends on Phase 2 (needs Shared contracts). Can run in parallel with Phase 3/4.
- **Phase 6 (US4/US5 Client Services)**: Depends on Phase 5 (needs Client.Core services). T038-T042 are parallel.
- **Phase 7 (Client WebApp)**: Depends on Phase 3 (server running) + Phase 6 (client services).
- **Phase 8 (Docker)**: Depends on Phase 3 (server compiles).
- **Phase 9 (Pipelines)**: No code dependencies. T054-T055 are parallel. Can run anytime after Phase 1.
- **Phase 10 (Cleanup)**: Depends on all prior phases.

### Parallel Opportunities

```
Phase 1: All .csproj files (T001-T010) in parallel
Phase 2: All Shared files (T015-T020) in parallel
Phase 3 + Phase 5: Server layers and Client.Core can build simultaneously
Phase 8 + Phase 9: Docker and Pipelines can run in parallel
```

---

## Notes

- [P] tasks = different files, no dependencies between them
- [Story] label maps task to specific user story for traceability
- Commit after each phase completes
- All file paths are relative to repo root `C:\Projects\ChefKnifeStudios.TransitJazz`
- Port from PokerAttack means: copy source, change all `PokerAttack` namespaces to `TransitJazz`, remove domain-specific logic
