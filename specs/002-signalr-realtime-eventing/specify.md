# SignalR Real-Time Eventing System — Specification

## Overview

Replace the existing SignalR notification infrastructure with a high-performance, polymorphic, domain-typed real-time event delivery system for the MARTA GTFS bus tracking domain. The current `TransitJazzNotification(Type, Message)` DTO, `ISignalRNotificationClient`, `TransitJazzNotificationHelper`, `PlayerConnectionTracker`, `PlayerIdProvider`, and related types are deleted and replaced entirely. Breaking all existing consumers is acceptable. The replacement system uses typed event records, automatic polymorphic JSON serialization via assembly scanning, batched delivery, and an authorized worker-to-hub publish path using Azure AD.

---

## User Stories

### US-001 — Receive Typed Bus Position Updates
**As a** Blazor client user,  
**I want** to receive live bus position updates as typed C# records,  
**So that** I can render GPS positions, speed, and route info without manual JSON parsing.

**Acceptance Criteria:**
- Client receives a `List<EventEnvelope>` batch on a single `ReceiveBatch` message
- Each envelope's `Payload` is deserialized to the correct concrete type automatically
- Client can pattern-match on `envelope.Payload` type without casting, string comparison, or JSON re-parsing

---

### US-002 — Receive Route Alerts and Arrival Predictions
**As a** Blazor client user,  
**I want** to receive `RouteAlertEvent` and `ArrivalPredictionEvent` records in the same batch stream,  
**So that** service disruptions and predicted arrivals are surfaced in real time alongside position updates.

**Acceptance Criteria:**
- `RouteAlertEvent` and `ArrivalPredictionEvent` arrive in the same `ReceiveBatch` handler
- Each event type is deserialized to its concrete C# type automatically
- Unknown event types throw `JsonException` (strict mode) — no silent skipping

---

### US-003 — Add New Event Types with Minimal Effort
**As a** developer,  
**I want** adding a new transit event to require touching only one file,  
**So that** the eventing system stays maintainable as the domain grows.

**Acceptance Criteria:**
- Creating a new `sealed record` in `Shared/Events/` implementing `ISignalREvent` is sufficient for the server to serialize and the client to receive it
- No changes required to `EventEnvelopeConverter`, `JsonSettings`, or any `Program.cs`
- Client adds one `case` to the batch handler switch to consume the new type (optional — the envelope is still delivered)

---

### US-004 — Worker Publishes Batches via Authorized Hub Endpoint
**As a** background data worker,  
**I want** to push event batches to the SignalR hub using Azure AD authentication,  
**So that** only authorized services can inject data into the real-time stream.

**Acceptance Criteria:**
- Worker connects to `/hubs/worker-transit` with a Bearer token acquired via `DefaultAzureCredential`
- Hub validates the `TransitData.Publish` app role before accepting the connection
- Unauthenticated connections to `/hubs/worker-transit` are rejected with HTTP 401
- Connections missing the app role are rejected with HTTP 403
- `/hubs/transit` (Blazor client hub) is `AllowAnonymous`
- Worker calls `PublishBatch` on the worker hub; the hub relays via `IHubContext<TransitHub>`

---

### US-005 — Single Serialization Configuration
**As a** developer,  
**I want** serialization options defined once and applied everywhere,  
**So that** JSON format never drifts between server, worker, and client.

**Acceptance Criteria:**
- `JsonSettings` in the Shared library is the sole definition of `JsonSerializerOptions` for SignalR
- Server `Program.cs`, worker `SignalRHubPublisher`, and client `TransitHubClient` all call `JsonSettings.ApplyTo(options.PayloadSerializerOptions)` — no inline options
- `JsonOptions` (HTTP endpoints) is unrelated and untouched

---

## Functional Requirements

### FR-001 — Delete Legacy Notification Infrastructure
Delete the following types entirely — no preservation, no deprecation wrappers:
- `TransitJazzNotification` (Shared/DTOs/SignalR/)
- `TransitJazzNotificationType` enum (Shared/Enums/)
- `ISignalRNotificationClient` (Server.WebAPI/SignalR/)
- `SignalRNotificationHub` (Server.WebAPI/SignalR/)
- `TransitJazzNotificationHelper` + `ITransitJazzNotificationHelper` (Server.WebAPI/SignalR/)
- `PlayerConnectionTracker` + `IPlayerConnectionTracker` (Server.WebAPI/SignalR/)
- `PlayerIdProvider` (Server.WebAPI/SignalR/)
- `IEventNotificationService` stub (Server.WebAPI/) — the empty stub only; `EventNotificationService` and `IEventNotificationService` in `Client.Core/Services/` are unrelated and must not be touched
- `SignalRNotificationService` (Client.Core/Services/)
- `ISignalRNotificationService` (Client.Core/Services/)
- `TransitJazzNotificationHandler` delegate (Client.Core/Services/)
- Any registration of these types in `Program.cs` files

### FR-002 — Shared Event Records
- `ISignalREvent`: no-member marker interface in `ChefKnifeStudios.TransitJazz.Shared`
- Five concrete `sealed record` types implementing `ISignalREvent` (no base class, no intermediate interface):
  - `VehiclePositionUpdatedEvent`
  - `ArrivalPredictionEvent`
  - `RouteAlertEvent`
  - `VehicleDepartedStopEvent`
  - `TripCompletedEvent`
- Six sub-data `sealed record` types (no interfaces, no behavior):
  - `VehicleData`, `PositionData`, `RouteData`, `StopData`, `PredictionData`, `AlertData`
- All event and sub-data records live in the same assembly as `EventEnvelope` (required for scanner)

### FR-003 — EventEnvelope Wire Format
- `sealed record EventEnvelope(string EventType, DateTimeOffset Timestamp, ISignalREvent Payload)`
- `EventType` set via `nameof(ConcreteEventRecord)` on the producer
- Wire format: camelCase JSON, no nulls written, no indentation

### FR-004 — Automatic Polymorphic Serialization
- `EventEnvelopeConverter : JsonConverter<EventEnvelope>` scans the Shared assembly at static init
- Scans for all non-abstract classes implementing `ISignalREvent`; builds `Dictionary<string, Type>` keyed by `t.Name`
- Serialization: writes `eventType`, `timestamp`, serializes `payload` to its concrete type
- Deserialization: reads `eventType`, looks up concrete type, deserializes `payload` directly — no intermediate `JsonElement`
- Strict mode: unknown `eventType` throws `JsonException` with "strict mode enabled" in the message; missing `eventType` property throws `JsonException("Missing EventType property")`

### FR-005 — JsonSettings
- `static class JsonSettings` in Shared with `DefaultOptions: JsonSerializerOptions` (camelCase, `WhenWritingNull`, `JsonStringEnumConverter`, `EventEnvelopeConverter`, `AllowNamedFloatingPointLiterals`, `WriteIndented = false`)
- `static void ApplyTo(JsonSerializerOptions target)` copies all settings to a target instance
- All SignalR configuration points call `JsonSettings.ApplyTo` — no other options construction

### FR-006 — JsonFlattener (Debug Only)
- `static class JsonFlattener` with `Flatten<T>(T? value) → Dictionary<string, object?>`
- Flattens nested JSON to dot-notation key-value pairs; omits null properties
- Called only inside `if (logger.IsEnabled(LogLevel.Debug))` guards; never on the hot path

### FR-007 — Static EventMapper
- `static class EventMapper` with pure static methods: `ToVehicleData`, `ToPositionData`, `ToRouteData`, `ToStopData`, `ToPredictionData`, `ToAlertData`
- `ToPredictionData` computes `DelaySeconds = (int)(predictedArrival - scheduledArrival).TotalSeconds` when `predictedArrival` is non-null
- All other methods are stubs (`throw new NotImplementedException`) pending GTFS-RT domain integration

### FR-008 — TransitHub (Server — Client-Facing)
- `public class TransitHub : Hub` with no hub methods — defines the Blazor client connection endpoint only
- Registered at `/hubs/transit` with `AllowAnonymous`
- Workers push to clients via `IHubContext<TransitHub>`, never calling hub methods directly from the server

### FR-009 — WorkerTransitHub (Server — Worker-Facing)
- `public class WorkerTransitHub : Hub` decorated with `[Authorize(Policy = "TransitDataPublisher")]`
- Single hub method: `async Task PublishBatch(List<EventEnvelope> batch)`
- Relays batch to all Blazor clients via `IHubContext<TransitHub>.Clients.All.SendAsync("ReceiveBatch", batch)`
- Registered at `/hubs/worker-transit` with `.RequireAuthorization("TransitDataPublisher")`

### FR-010 — Azure AD Authorization (Server WebAPI)
- Add JWT Bearer authentication via `Microsoft.Identity.Web`, configured from `AzureAd` appsettings section
- Authorization policy `"TransitDataPublisher"`: requires authenticated user + claim `roles` = `TransitData.Publish`
- `app.UseAuthentication()` and `app.UseAuthorization()` inserted before all hub mappings
- SignalR registered with `JsonSettings.ApplyTo` applied to `PayloadSerializerOptions`

### FR-011 — ITransitHubPublisher Interface (Shared)
- `interface ITransitHubPublisher { Task PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default); }`
- Lives in Shared to avoid circular project references

### FR-012 — TokenProvider (Worker)
- `sealed class TokenProvider` reads `AzureAd:Scope` (required, throws if missing) and `AzureAd:ManagedIdentityClientId` (optional)
- Uses `DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = ... })`
- Exposes `Task<string> GetAccessTokenAsync(CancellationToken ct = default)`

### FR-013 — SignalRHubPublisher (Worker)
- `sealed class SignalRHubPublisher : ITransitHubPublisher, IAsyncDisposable`
- Reads `SignalR:HubUrl` from config; throws `InvalidOperationException` if missing
- Builds `HubConnection` with `AccessTokenProvider` delegate → `tokenProvider.GetAccessTokenAsync()`
- Calls `JsonSettings.ApplyTo` on SignalR JSON protocol options
- Logs reconnecting / reconnected / closed lifecycle events
- `StartAsync(CancellationToken ct)`: calls `_connection.StartAsync(ct)`, logs success
- `PublishBatchAsync`: if not `Connected`, logs warning and returns (drop); else `InvokeAsync("PublishBatch", batch, ct)`

### FR-014 — TransitHubClient (Client)
- Replaces `SignalRNotificationService` entirely
- `sealed class TransitHubClient : IAsyncDisposable`
- Builds `HubConnection` with `JsonSettings.ApplyTo` applied; reads hub URL from `IConfiguration`
- Registers a single `"ReceiveBatch"` handler on `List<EventEnvelope>` that iterates envelopes individually; each envelope dispatch is wrapped in its own try/catch — a deserialization or dispatch failure on one envelope is logged and skipped; remaining envelopes in the batch continue processing; the connection is never terminated by a bad envelope
- Exposes typed events: `event Action<VehiclePositionUpdatedEvent>? OnVehiclePositionUpdated`, etc.
- `ConnectAsync()` / `DisposeAsync()`
- Registered as `scoped` in the Blazor client DI container

### FR-015 — Worker Registration and Startup
- Worker `Program.cs` registers: `TokenProvider` (singleton), `SignalRHubPublisher` (singleton), `ITransitHubPublisher` resolved from the same `SignalRHubPublisher` singleton, `Worker` as hosted service
- `SignalRHubPublisher.StartAsync()` called before `host.Run()`
- `Worker` uses `PeriodicTimer(TimeSpan.FromSeconds(15))` and calls `publisher.PublishBatchAsync(batch, ct)` each tick
- Errors within a tick are caught, logged, and do not terminate the host

### FR-016 — Server WebAPI Program.cs Cleanup
- Remove all registrations of deleted types (FR-001)
- Add SignalR registration with `JsonSettings.ApplyTo`
- Add authentication + authorization middleware and policy
- Map `TransitHub` at `/hubs/transit` (`AllowAnonymous`)
- Map `WorkerTransitHub` at `/hubs/worker-transit` (`.RequireAuthorization("TransitDataPublisher")`)

---

## Non-Functional Requirements

### NFR-001 — Performance
- 400+ bus position updates batched into a single `List<EventEnvelope>` per 15-second worker tick
- Single `InvokeAsync` from worker to hub; single `SendAsync` from hub to all clients — no per-event network calls
- `EventEnvelopeConverter` type dictionary built exactly once at static init via `static EventEnvelopeConverter()`; never rebuilt at runtime
- `JsonFlattener` never executes on the batch send/receive hot path

### NFR-002 — Schema Consistency
- Shared assembly is the single source of truth for all event records, sub-data records, and serialization settings
- No hand-written DTOs on either the client or server side for transit events

### NFR-003 — Security
- Worker hub endpoint requires Azure AD app role; Blazor client hub is anonymous
- No client secrets in production appsettings — Managed Identity only in Azure
- `DefaultAzureCredential` handles token acquisition for local dev (Azure CLI), CI (env vars), and production (Managed Identity) via a single code path

### NFR-004 — Maintainability
- Adding a new event type: create one `sealed record` file in Shared/Events/ — zero other changes required
- No manual type registration, discriminator bookkeeping, or serialization config changes ever needed

---

## Edge Cases

### EC-001 — Per-Envelope Failure Isolation
Each envelope in the `ReceiveBatch` handler is dispatched individually inside its own try/catch. A `JsonException`, dispatch error, or subscriber exception on one envelope is logged (including the raw envelope data) and skipped; remaining envelopes in the same batch continue processing. The SignalR connection is never terminated by a bad envelope. This is a hard requirement, not optional defensive code.

### EC-002 — Hub Not Connected at Publish Time
Worker calls `PublishBatchAsync` while `HubConnectionState != Connected` (e.g., mid-reconnect). Drop behavior (log warning, return) is acceptable for position updates since the next tick provides fresh data. Alert and prediction events have the same ephemeral treatment for now; a retry queue is out of scope.

### EC-003 — Token Expiry During Long-Running Session
`AccessTokenProvider` is called by the SignalR client before each connection/reconnection. `DefaultAzureCredential.GetTokenAsync` caches tokens and refreshes automatically before expiry — no manual refresh logic is required.

### EC-004 — Assembly Scanner Finds No Event Types
If `ISignalREvent` implementations are moved to a different assembly than `EventEnvelope`, the scanner builds an empty dictionary and all deserialization throws `JsonException`. An integration test (SC-004) must assert that the converter's type dictionary is non-empty at startup.

### EC-005 — Missing EventType Property in Wire JSON
A malformed message with no `eventType` property throws `JsonException("Missing EventType property")`. The SignalR connection is not terminated; the error propagates to the registered message handler and should be caught and logged there (see EC-001).

---

## Success Criteria

### SC-001 — Batch Throughput
A single worker tick producing 400 `VehiclePositionUpdatedEvent` envelopes completes `PublishBatchAsync` in under 50ms under local network conditions.

### SC-002 — Type Safety
A Blazor component can pattern-match `envelope.Payload` to `VehiclePositionUpdatedEvent` with no cast, no string comparison, and no JSON re-parsing.

### SC-003 — Auth Enforcement
A WebSocket upgrade to `/hubs/worker-transit` without a token returns HTTP 401. An upgrade with a valid token lacking the `TransitData.Publish` role returns HTTP 403.

### SC-004 — Round-Trip Fidelity
Serializing an `EventEnvelope` with a `VehiclePositionUpdatedEvent` payload and deserializing it produces an object structurally equal to the original, including all nullable sub-data fields.

### SC-005 — Strict Mode Enforcement
Deserializing an `EventEnvelope` with `"eventType": "UnknownFutureEvent"` throws `JsonException` whose message contains "strict mode enabled".

### SC-006 — Config Deduplication
Each of server `Program.cs`, worker `Program.cs`, and client `TransitHubClient` calls `JsonSettings.ApplyTo` exactly once for SignalR configuration. No other `JsonSerializerOptions` are constructed for SignalR anywhere.

### SC-007 — Legacy Types Absent
The codebase contains no references to `TransitJazzNotification`, `TransitJazzNotificationType`, `ISignalRNotificationClient`, `SignalRNotificationHub`, `PlayerConnectionTracker`, `PlayerIdProvider`, or `TransitJazzNotificationHelper` after implementation.

---

## Out of Scope

- Per-route or per-group event filtering (worker broadcasts to all clients, no groups)
- Persistent message queuing or replay (all events are ephemeral; drop on disconnect)
- GTFS-RT feed polling implementation (stubs in `EventMapper`; domain integration is a separate feature)
- Azure AD app registration provisioning (manual setup documented in idea.md §9.2)
- Managed Identity Azure role assignment via Bicep/ARM (deployment concern)
- LiveMap.razor or any Blazor page implementation (separate UI spec)
