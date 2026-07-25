# Feature Specification: Solution Scaffold - Full-Stack Aspire Project

**Feature Branch**: `002-solution-scaffold`  
**Created**: 2026-05-04  
**Status**: Draft  
**Input**: User description: "Create a full-stack .NET 10 Aspire solution matching the PokerAttack folder structure 1:1. Blazor WASM frontend (Core/Shared/WebApp tiers), ASP.NET Core WebAPI with Clean Architecture, SignalR, Scalar, cross-domain Shared project, Docker, and separate client/server deployment pipelines."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Solution Builds and Runs via Aspire (Priority: P1)

A developer clones the TransitJazz repo, opens the solution, and runs the Aspire AppHost. The AppHost orchestrates both the Server.WebAPI and Client.WebApp projects. The WebAPI starts and serves the Scalar API documentation page. The Blazor WASM client loads in the browser and renders the MatBlazor-themed layout with dark/light mode support.

**Why this priority**: Without a buildable, runnable solution, no other feature can be developed or tested. This is the foundation.

**Independent Test**: Run `dotnet build` on `ChefKnifeStudios.TransitJazz.sln` from `src/` and confirm zero errors. Run the AppHost and confirm both the API and client start with health checks passing.

**Acceptance Scenarios**:

1. **Given** the solution is cloned, **When** `dotnet build` is run from `src/`, **Then** all 9 projects compile with zero errors and zero warnings.
2. **Given** the AppHost is started, **When** the developer navigates to the API's `/scalar/v1` endpoint, **Then** the Scalar API reference page loads with the "TransitJazz API" title.
3. **Given** the AppHost is started, **When** the developer navigates to the client URL, **Then** the Blazor WASM app loads showing the MatBlazor-themed layout with the correct PokerAttack color scheme.
4. **Given** the AppHost is started, **When** both services are running, **Then** the `/health` endpoints on both client and API return healthy status.

---

### User Story 2 - Server API Endpoint Group Pattern Works (Priority: P1)

A developer adds a new Minimal API endpoint group to the server. The TestEndpoints group is already wired up and demonstrates the pattern: a static class with a `Map{Name}Endpoints` extension method, using routes defined in the cross-domain Shared project's `TransitJazzApiEndpoints` class. The developer can call the test endpoint via Scalar and receive a successful response.

**Why this priority**: The endpoint group pattern is the primary way all API functionality will be added. It must work correctly from day one.

**Independent Test**: Start the API, navigate to Scalar, and invoke the `POST /test/signalr` endpoint. Verify it returns a 200 OK.

**Acceptance Scenarios**:

1. **Given** the API is running, **When** the developer opens Scalar, **Then** the TestEndpoints group appears with its endpoints documented.
2. **Given** the API is running, **When** a POST request is sent to the test SignalR endpoint, **Then** a success result is returned.
3. **Given** a developer creates a new endpoint group class following the TestEndpoints pattern, **When** they chain `.Map{Name}Endpoints()` in Program.cs, **Then** the new endpoints appear in Scalar automatically.

---

### User Story 3 - SignalR Hub Connects Client to Server (Priority: P1)

A developer starts both client and server. The Blazor WASM client's `SignalRNotificationService` connects to the server's `SignalRNotificationHub` at `/cks-notification`. The connection is established with automatic reconnect enabled. The server's `PlayerConnectionTracker` registers the connection.

**Why this priority**: SignalR is the primary real-time communication channel. Without it, the app cannot push updates to clients.

**Independent Test**: Start the AppHost, open the client in a browser, trigger `SignalRNotificationService.InitAsync()`, and verify the hub connection state is `Connected`.

**Acceptance Scenarios**:

1. **Given** the server is running, **When** the client calls `InitAsync` with a user ID, **Then** a SignalR connection is established to `/cks-notification`.
2. **Given** a connected client, **When** the server broadcasts a notification, **Then** the client receives it via the `HandleNotificationReceived` event.
3. **Given** a connected client, **When** the connection drops, **Then** automatic reconnection is attempted.
4. **Given** a client connects, **When** the connection is established, **Then** the `PlayerConnectionTracker` registers the connection ID against the user ID.

---

### User Story 4 - Client Event Bus Enables Local Eventing (Priority: P2)

The Blazor client uses the `EventNotificationService` as a local event bus. Components can publish events (e.g., theme changes) and other components can subscribe to receive them. The MainLayout subscribes to theme change events to toggle between light and dark MatBlazor themes.

**Why this priority**: The event bus enables decoupled component communication. Theme switching demonstrates it works end-to-end.

**Independent Test**: Load the client, trigger a theme change event, and verify the MatBlazor theme toggles between light and dark mode.

**Acceptance Scenarios**:

1. **Given** the client is loaded with the light theme, **When** a `ThemeChangedEventArgs` event is posted with `IsDarkMode = true`, **Then** the layout switches to the dark theme using PokerAttack's dark color constants.
2. **Given** a component subscribes to `EventReceived`, **When** any `IEventArgs` is posted, **Then** the subscriber receives the event.

---

### User Story 5 - HTTP Service Factory Creates Named Clients (Priority: P2)

The client's `HttpServiceFactory` creates `HttpService` instances backed by named `HttpClient`s configured from `appsettings.json`. Each `HttpService` wraps HTTP operations (GET, POST, PUT, PATCH, DELETE) with the `Ardalis.Result` pattern, providing consistent error handling across all endpoint service calls.

**Why this priority**: All API communication from the client flows through this factory. It must work before any endpoint services can be implemented.

**Independent Test**: Configure an external API in `appsettings.json`, resolve `IHttpServiceFactory`, call `Create("TransitJazzAPI")`, and verify the returned `HttpService` can make a GET request to the test endpoint.

**Acceptance Scenarios**:

1. **Given** `appsettings.json` defines an external API named "TransitJazzAPI", **When** the app starts, **Then** an `HttpClient` is registered with the correct base URI.
2. **Given** an `IHttpServiceFactory` is resolved, **When** `Create("TransitJazzAPI")` is called, **Then** an `IHttpService` is returned that can perform HTTP operations.
3. **Given** an `IHttpService`, **When** `GetAsync<T>` is called and the server returns 200, **Then** a `Result<T>.Success` is returned with the deserialized body.
4. **Given** an `IHttpService`, **When** `GetAsync<T>` is called and the server returns 404, **Then** a `Result<T>.NotFound()` is returned.

---

### User Story 6 - Docker Build Produces Server Container Image (Priority: P2)

A developer runs `docker compose build` from the repo root. The server WebAPI Dockerfile builds a multi-stage image: SDK build, publish, and aspnet runtime. The resulting image exposes port 8080 and can be run as a standalone container.

**Why this priority**: Containerization is required for deployment to Azure Container Apps. The Dockerfile must work before CI/CD pipelines can be built.

**Independent Test**: Run `docker compose build` and verify the image is created. Run the container with `docker run -p 5268:8080` and verify the API responds.

**Acceptance Scenarios**:

1. **Given** the solution source code, **When** `docker compose build` is run, **Then** the `chefknifestudios.transitjazz.server.webapi` image is built successfully.
2. **Given** the built image, **When** a container is started, **Then** the API responds to HTTP requests on port 8080.

---

### User Story 7 - Deployment Pipelines Are Configured (Priority: P3)

The `deploy/` directory contains Azure DevOps YAML pipelines for separate client and server deployments. The client pipeline publishes the Blazor WASM app and deploys to Azure Static Web Apps. The server pipeline builds Docker images, pushes to ACR, and deploys to Azure Container Apps.

**Why this priority**: Pipelines are needed for production deployment but not for local development. They can be configured after all features work locally.

**Independent Test**: Validate YAML syntax. Verify pipeline variable references match expected Azure resource names.

**Acceptance Scenarios**:

1. **Given** the `deploy/client-pipeline.yml` exists, **When** it is parsed, **Then** it defines stages for BuildAndPublish and DeployProd targeting the TransitJazz client.
2. **Given** the `deploy/server-pipeline.yml` exists, **When** it is parsed, **Then** it defines stages for BuildAndPublish and DeployProd targeting the TransitJazz server.
---

### Edge Cases

- What happens when `appsettings.json` has no ExternalApis configured? The app should start without registering any HttpClients and log a warning.
- What happens when the SignalR hub URL is unreachable? The `SignalRNotificationService` should log the error and set `_hubConnection` to null without crashing the app.
- What happens when `docker compose build` is run without the .NET 10 SDK image available? Docker should pull the image automatically from MCR.
- What happens when the Aspire AppHost starts but one service fails? The other service should still start, and the Aspire dashboard should show the failed service's status.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Solution Structure
- **FR-001**: Solution MUST contain exactly 9 projects: AppHost, ServiceDefaults, Shared, Client.Core, Client.Shared, Client.WebApp, Server.WebAPI, Server.BL, Server.Core, Server.Infrastructure. Server.Data is explicitly omitted - this project does not require data persistence.
- **FR-002**: Solution MUST use the `ChefKnifeStudios.TransitJazz` namespace prefix for all projects.
- **FR-003**: Solution MUST organize projects into `Client` and `Server` solution folders matching PokerAttack's layout.
- **FR-004**: All projects MUST target `net10.0`.
- **FR-005**: The `.sln` file MUST reside at `src/ChefKnifeStudios.TransitJazz.sln`.

#### Aspire Orchestration
- **FR-006**: The AppHost MUST register `Server.WebAPI` as `"apiservice"` with an HTTP health check at `/health`.
- **FR-007**: The AppHost MUST register `Client.WebApp` as `"webfrontend"` with external HTTP endpoints, a health check at `/health`, a reference to the API service, and a `WaitFor` dependency on the API service.
- **FR-008**: The ServiceDefaults project MUST provide OpenTelemetry, health checks, service discovery, and HTTP resilience via an `AddServiceDefaults` extension method.

#### Blazor WASM Client (3-Tier)
- **FR-009**: `Client.WebApp` MUST be a Blazor WebAssembly application (SDK: `Microsoft.NET.Sdk.BlazorWebAssembly`).
- **FR-010**: `Client.Shared` MUST be a Razor Class Library (SDK: `Microsoft.NET.Sdk.Razor`) containing shared components, services, and view models.
- **FR-011**: `Client.Core` MUST be a Class Library containing `HttpServiceFactory`, `HttpService`, `SignalRNotificationService`, `EventNotificationService`, and `AppSettings`.
- **FR-012**: The client MUST use MatBlazor for UI components with a `MatThemeProvider` wrapping the layout.
- **FR-013**: The client MUST use PokerAttack's exact color constants (Light and Dark themes) from `ColorConstants.cs`.
- **FR-014**: The client MUST register a `SignalRNotificationService` that connects to the server's hub at `/cks-notification` with automatic reconnect.
- **FR-015**: The client MUST register an `EventNotificationService` for local pub/sub eventing.
- **FR-016**: The `MainLayout.razor` MUST subscribe to theme change events and toggle between Light and Dark MatBlazor themes.
- **FR-017**: The client MUST bind `AppSettings` from `appsettings.json` and register named `HttpClient`s for each configured external API.
- **FR-018**: The client MUST include `Blazored.LocalStorage` for client-side persistence.
- **FR-019**: The `index.html` MUST load MatBlazor CSS/JS, custom CSS files (`variables.css`, `mdc-overrides.css`, `app.css`), and the Blazor WebAssembly framework script.

#### ASP.NET Core Server (Clean Architecture)
- **FR-020**: `Server.WebAPI` MUST be an ASP.NET Core Web API (SDK: `Microsoft.NET.Sdk.Web`) that serves as both API and SignalR Hub host.
- **FR-021**: `Server.BL` MUST contain business logic services and reference `Server.Core`.
- **FR-023**: `Server.Core` MUST contain domain interfaces and models, referencing only the `Shared` project.
- **FR-024**: `Server.Infrastructure` MUST contain external service integrations (Redis via StackExchange.Redis), referencing `Server.Core`.
- **FR-025**: The WebAPI Program.cs MUST call `AddServiceDefaults()` for Aspire integration.
- **FR-026**: The WebAPI MUST configure Scalar API documentation with Solarized theme, Classic layout, and dark mode enabled.
- **FR-027**: The WebAPI MUST configure CORS to allow localhost development origins and production domains.
- **FR-028**: The WebAPI MUST register a `SignalRNotificationHub` mapped to `/cks-notification`.
- **FR-029**: The WebAPI MUST use the Minimal API endpoint group pattern with chainable `Map{Name}Endpoints()` extension methods.
- **FR-030**: The WebAPI MUST include a `TestEndpoints` group as a working example of the endpoint pattern.
- **FR-031**: The WebAPI MUST register `IUserIdProvider` as `PlayerIdProvider` that reads `playerId` from the query string.
- **FR-032**: The WebAPI MUST register `IPlayerConnectionTracker` as `PlayerConnectionTracker` for multi-tab connection tracking.
- **FR-033**: The WebAPI MUST include a logging pattern using Azure Monitor / Application Insights, with OpenTelemetry exporting traces, metrics, and logs to an Azure Log Analytics Workspace. The connection string MUST be configurable via `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable or appsettings.

#### Cross-Domain Shared Project
- **FR-034**: The `Shared` project MUST be a plain Class Library with no external NuGet dependencies.
- **FR-035**: The `Shared` project MUST contain a `TransitJazzApiEndpoints` class with nested static classes defining API route constants.
- **FR-036**: The `Shared` project MUST contain placeholder directories for `DTOs/`, `Enums/`, and shared constants.
- **FR-037**: The `Shared` project MUST contain a `FeatureFlagService` with an `IFeatureFlagService` interface.
- **FR-038**: The `Shared` project MUST contain a `JsonOptions` utility class for consistent JSON serialization settings.

#### Containerization
- **FR-039**: The server WebAPI MUST have a multi-stage Dockerfile: .NET 10 SDK build, publish, and aspnet:10.0 runtime, exposing port 8080.
- **FR-040**: The repo root MUST have a `docker-compose.yml` defining the `chefknifestudios.transitjazz.server.webapi` service with environment-variable-driven registry and tag. No data/migration service is needed.

#### Deployment Pipelines
- **FR-041**: The `deploy/` directory MUST contain `client-pipeline.yml` for Azure DevOps, building and deploying the Blazor WASM app to Azure Static Web Apps.
- **FR-042**: The `deploy/` directory MUST contain `server-pipeline.yml` for Azure DevOps, building Docker images, pushing to ACR, and deploying to Azure Container Apps. No DB migration step is needed.


#### Project Reference Graph
- **FR-044**: Project references MUST follow this dependency graph:
  - `AppHost` → `Client.WebApp`, `Server.WebAPI`
  - `Client.WebApp` → `Client.Shared`
  - `Client.Shared` → `Client.Core`
  - `Client.Core` → `Shared`
  - `Server.WebAPI` → `ServiceDefaults`, `Server.BL`, `Server.Infrastructure`
  - `Server.BL` → `Server.Core`
  - `Server.Core` → `Shared`
  - `Server.Infrastructure` → `Server.Core`

### Key Entities

- **AppSettings**: Configuration class binding external API definitions (Name, BaseUri, AuthenticationRequired, AddHttpClient) and feature flags from `appsettings.json`.
- **TransitJazzApiEndpoints**: Static class hierarchy defining all API route constants, consumed by both client endpoint services and server endpoint groups.
- **TransitJazzNotification**: SignalR notification DTO transmitted between hub and clients, carrying a notification type enum and a message payload.
- **ColorConstants**: Static class containing Material Design 3 color values for Light and Dark themes, used by `MainLayout.razor`'s `MatThemeProvider`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `dotnet build` on `src/ChefKnifeStudios.TransitJazz.sln` completes with 0 errors and 0 warnings.
- **SC-002**: The Aspire AppHost starts both services within 30 seconds, with both `/health` endpoints returning healthy.
- **SC-003**: The Scalar API reference page loads at `/scalar/v1` with all TestEndpoints documented.
- **SC-004**: The Blazor WASM client loads in the browser within 5 seconds and renders the MatBlazor-themed layout.
- **SC-005**: A SignalR connection from the client to the server hub is established within 3 seconds of calling `InitAsync`.
- **SC-006**: `docker compose build` completes successfully, producing a runnable container image.
- **SC-007**: The folder structure of the TransitJazz `src/` directory matches the PokerAttack `src/` directory at the project level, minus the omitted `Server.Data` project.
- **SC-008**: Both deployment pipeline YAML files pass syntax validation.
- **SC-009**: The project reference graph matches the specified dependency chain with no circular references.
- **SC-010**: The color scheme (ColorConstants) matches PokerAttack's exact hex values for all Light and Dark theme properties.

---

## Assumptions

- The developer has .NET 10 SDK installed locally.
- Docker Desktop is installed for container builds.
- Azure DevOps is used for CI/CD (pipelines are templates that require Azure resource configuration).
- The Application Insights connection string in configuration will be a placeholder that the developer replaces with their real Azure Log Analytics Workspace connection string.
- The `Server.BL` project includes placeholder service interfaces but no business logic implementations - those will be added domain-by-domain.
- The `Server.Infrastructure` project includes the Redis package reference but no implementations - those will be added as needed.
- CORS origins in the server's Program.cs will include localhost ports matching the Aspire configuration plus a placeholder production domain.
- The client `appsettings.json` will include placeholder API URLs that work with Aspire's service discovery during local development.
- No authentication/authorization is implemented in this scaffold - it will be added in a future feature per the constitution's Azure Maps auth function pattern.
