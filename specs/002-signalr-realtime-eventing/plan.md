# SignalR Real-Time Eventing System — Implementation Plan

## Tech Stack

| Concern | Choice | Reason |
|---------|--------|--------|
| Real-time transport | ASP.NET Core SignalR (already in use) | No change |
| Serialization | `System.Text.Json` + custom `JsonConverter<EventEnvelope>` | Zero allocations vs Newtonsoft; assembly-scan polymorphism |
| Auth (server) | `Microsoft.Identity.Web` JWT Bearer | Standard for Azure AD app roles |
| Auth (worker) | `Azure.Identity` `DefaultAzureCredential` | Single code path: local dev → CI → Managed Identity |
| Target framework | net10.0 (matches all existing projects) | No change |

---

## Architecture Overview

```
┌─────────────────────────────┐        ┌──────────────────────────────────┐
│  TransitDataWorker          │        │  Server.WebAPI                   │
│                             │        │                                  │
│  Worker (PeriodicTimer 15s) │        │  WorkerTransitHub [Authorize]    │
│    └─ BuildBatchAsync()     │        │    └─ PublishBatch(batch)        │
│         └─ EventEnvelope[]  │──JWT──►│         └─ IHubContext<TransitHub│
│                             │        │              .SendAsync(          │
│  SignalRHubPublisher        │        │               "ReceiveBatch",     │
│    └─ InvokeAsync(          │        │                batch)            │
│        "PublishBatch",batch)│        │                                  │
│                             │        │  TransitHub [AllowAnonymous]      │
│  TokenProvider              │        │    (no methods — connection only) │
│    └─ DefaultAzureCredential│        └──────────────┬───────────────────┘
└─────────────────────────────┘                       │ WebSocket
                                                      │ "ReceiveBatch"
                                        ┌─────────────▼───────────────────┐
                                        │  Client.Core                    │
                                        │                                 │
                                        │  TransitHubClient               │
                                        │    On("ReceiveBatch", batch =>  │
                                        │      foreach envelope           │
                                        │        try { dispatch }         │
                                        │        catch { log, skip })     │
                                        │                                 │
                                        │    OnVehiclePositionUpdated     │
                                        │    OnRouteAlert                 │
                                        │    OnArrivalPrediction          │
                                        │    OnVehicleDepartedStop        │
                                        │    OnTripCompleted              │
                                        └─────────────────────────────────┘
```

---

## Project Reference Changes

### `ChefKnifeStudios.TransitJazz.Shared.csproj`
- Add package: `Microsoft.AspNetCore.SignalR.Client` (needed by `ITransitHubPublisher` and for `JsonSettings` to reference SignalR JSON protocol types — or keep it package-free and use only `System.Text.Json`)
- No new project references needed — Shared is already referenced by all downstream projects

> **Note**: `JsonSettings.ApplyTo` takes a plain `JsonSerializerOptions` — no SignalR assembly reference needed in Shared. Keep Shared dependency-free.

### `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj`
Add packages:
- `Microsoft.AspNetCore.SignalR.Client` — HubConnection
- `Azure.Identity` — DefaultAzureCredential
Add project reference:
- `ChefKnifeStudios.TransitJazz.Shared`

### `ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj`
Add packages:
- `Microsoft.Identity.Web` — JWT Bearer auth for Azure AD

### `ChefKnifeStudios.TransitJazz.Client.Core.csproj`
- Already has `Microsoft.AspNetCore.SignalR.Client` — no new packages needed
- No project reference changes

---

## Implementation Phases

### Phase 1 — Shared Library: New Types

**Files to create** (all in `src/ChefKnifeStudios.TransitJazz.Shared/`):

| File | Type | FR |
|------|------|----|
| `Events/ISignalREvent.cs` | Marker interface | FR-002 |
| `Events/EventEnvelope.cs` | Wire wrapper record | FR-003 |
| `Events/EventEnvelopeConverter.cs` | Custom JsonConverter | FR-004 |
| `Events/VehiclePositionUpdatedEvent.cs` | Concrete event record | FR-002 |
| `Events/ArrivalPredictionEvent.cs` | Concrete event record | FR-002 |
| `Events/RouteAlertEvent.cs` | Concrete event record | FR-002 |
| `Events/VehicleDepartedStopEvent.cs` | Concrete event record | FR-002 |
| `Events/TripCompletedEvent.cs` | Concrete event record | FR-002 |
| `EventData/VehicleData.cs` | Sub-data record | FR-002 |
| `EventData/PositionData.cs` | Sub-data record | FR-002 |
| `EventData/TripData.cs` | Sub-data record | FR-002 |
| `EventData/StopTimeData.cs` | Sub-data record | FR-002 |
| `EventData/AlertData.cs` | Sub-data record | FR-002 |
| `JsonSettings.cs` | Serialization config | FR-005 |
| `JsonFlattener.cs` | Debug logging util | FR-006 |
| `ITransitHubPublisher.cs` | Publisher interface | FR-011 |

---

#### Complete Model Definitions

All types are derived directly from the GTFS-RT protobuf schema (`gtfs-realtime.proto`). Fields map 1:1 to proto fields. Proto `optional float` → `float?`, proto `required float` → `float`, proto `uint32`/`uint64` → `long?` (JSON-safe), proto enums → C# enums with matching value names.

---

##### EventData Records

**`VehicleData.cs`**
Maps from `VehicleDescriptor` + `VehiclePosition.occupancy_status` + `VehiclePosition.occupancy_percentage`.
```csharp
namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record VehicleData(
    string Id,               // VehicleDescriptor.id
    string? Label,           // VehicleDescriptor.label
    string? LicensePlate,    // VehicleDescriptor.license_plate
    OccupancyStatus? OccupancyStatus,    // VehiclePosition.occupancy_status
    int? OccupancyPercentage             // VehiclePosition.occupancy_percentage
);

public enum OccupancyStatus
{
    Empty = 0,
    ManySeatsAvailable = 1,
    FewSeatsAvailable = 2,
    StandingRoomOnly = 3,
    CrushedStandingRoomOnly = 4,
    Full = 5,
    NotAcceptingPassengers = 6,
    NoDataAvailable = 7,
    NotBoardable = 8
}
```

**`PositionData.cs`**
Maps from `Position` + `VehiclePosition.current_stop_sequence` + `VehiclePosition.stop_id` + `VehiclePosition.current_status` + `VehiclePosition.congestion_level` + `VehiclePosition.timestamp`.
```csharp
namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record PositionData(
    float Latitude,           // Position.latitude (required)
    float Longitude,          // Position.longitude (required)
    float? Bearing,           // Position.bearing (degrees clockwise from North)
    float? SpeedMetersPerSec, // Position.speed (m/s from proto; display conversion is UI concern)
    double? OdometerMeters,   // Position.odometer
    long? Timestamp,          // VehiclePosition.timestamp (Unix seconds)
    uint? CurrentStopSequence,// VehiclePosition.current_stop_sequence
    string? CurrentStopId,    // VehiclePosition.stop_id
    VehicleStopStatus? CurrentStatus,    // VehiclePosition.current_status
    CongestionLevel? CongestionLevel     // VehiclePosition.congestion_level
);

public enum VehicleStopStatus
{
    IncomingAt = 0,
    StoppedAt = 1,
    InTransitTo = 2
}

public enum CongestionLevel
{
    UnknownCongestionLevel = 0,
    RunningSmoothly = 1,
    StopAndGo = 2,
    Congestion = 3,
    SevereCongestion = 4
}
```

**`TripData.cs`**
Maps from `TripDescriptor`. Replaces the generic `RouteData` placeholder — MARTA's GTFS-RT `VehiclePosition.trip` carries route_id, trip_id, direction, and schedule state, not a static route record.
```csharp
namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record TripData(
    string? TripId,           // TripDescriptor.trip_id
    string? RouteId,          // TripDescriptor.route_id
    int? DirectionId,         // TripDescriptor.direction_id (0 = outbound, 1 = inbound)
    string? StartTime,        // TripDescriptor.start_time ("HH:mm:ss" format)
    string? StartDate,        // TripDescriptor.start_date ("YYYYMMDD" format)
    TripScheduleRelationship? ScheduleRelationship  // TripDescriptor.schedule_relationship
);

public enum TripScheduleRelationship
{
    Scheduled = 0,
    Unscheduled = 2,
    Canceled = 3,
    Replacement = 5,
    Duplicated = 6,
    Deleted = 7,
    New = 8
}
```

**`StopTimeData.cs`**
Maps from `TripUpdate.StopTimeUpdate` + its nested `StopTimeEvent` fields. Used by `ArrivalPredictionEvent`.
```csharp
namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record StopTimeData(
    string? StopId,           // StopTimeUpdate.stop_id
    uint? StopSequence,       // StopTimeUpdate.stop_sequence
    long? ArrivalTime,        // StopTimeUpdate.arrival.time (Unix seconds)
    int? ArrivalDelay,        // StopTimeUpdate.arrival.delay (seconds, + = late)
    int? ArrivalUncertainty,  // StopTimeUpdate.arrival.uncertainty
    long? DepartureTime,      // StopTimeUpdate.departure.time (Unix seconds)
    int? DepartureDelay,      // StopTimeUpdate.departure.delay (seconds, + = late)
    int? DepartureUncertainty,// StopTimeUpdate.departure.uncertainty
    StopTimeScheduleRelationship? ScheduleRelationship  // StopTimeUpdate.schedule_relationship
);

public enum StopTimeScheduleRelationship
{
    Scheduled = 0,
    Skipped = 1,
    NoData = 2,
    Unscheduled = 3
}
```

**`AlertData.cs`**
Maps from `Alert`. `TranslatedString` is resolved to the first available English or untagged translation as a plain `string`.
```csharp
namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record AlertData(
    string? HeaderText,       // Alert.header_text (first English/untagged translation)
    string? DescriptionText,  // Alert.description_text
    string? Url,              // Alert.url
    AlertCause Cause,         // Alert.cause
    AlertEffect Effect,       // Alert.effect
    AlertSeverity Severity,   // Alert.severity_level
    long? ActiveFrom,         // Alert.active_period[0].start (Unix seconds)
    long? ActiveUntil,        // Alert.active_period[0].end (Unix seconds)
    IReadOnlyList<string> AffectedRouteIds,  // Alert.informed_entity[].route_id (non-null)
    IReadOnlyList<string> AffectedStopIds    // Alert.informed_entity[].stop_id (non-null)
);

public enum AlertCause
{
    UnknownCause = 1, OtherCause = 2, TechnicalProblem = 3, Strike = 4,
    Demonstration = 5, Accident = 6, Holiday = 7, Weather = 8,
    Maintenance = 9, Construction = 10, PoliceActivity = 11, MedicalEmergency = 12
}

public enum AlertEffect
{
    NoService = 1, ReducedService = 2, SignificantDelays = 3, Detour = 4,
    AdditionalService = 5, ModifiedService = 6, OtherEffect = 7,
    UnknownEffect = 8, StopMoved = 9, NoEffect = 10, AccessibilityIssue = 11
}

public enum AlertSeverity
{
    UnknownSeverity = 1, Info = 2, Warning = 3, Severe = 4
}
```

---

##### Event Records

**`VehiclePositionUpdatedEvent.cs`**
Produced once per moved bus per 15-second tick. The POC's `EventMessage` confirms only moved buses are emitted.
```csharp
using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

// Emitted for each bus whose position changed since the last tick.
// Source: FeedEntity.vehicle (VehiclePosition) where Position != null.
public sealed record VehiclePositionUpdatedEvent(
    VehicleData Vehicle,      // VehicleDescriptor fields
    PositionData Position,    // Position + VehiclePosition status fields
    TripData? Trip            // TripDescriptor (null if vehicle not on a trip)
) : ISignalREvent;
```

**`ArrivalPredictionEvent.cs`**
Produced from `FeedEntity.trip_update` entities. One event per trip update, carrying all stop time predictions for that trip.
```csharp
using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

// Emitted for each TripUpdate entity in the GTFS-RT feed.
// Source: FeedEntity.trip_update where stop_time_update is non-empty.
public sealed record ArrivalPredictionEvent(
    TripData Trip,                              // TripUpdate.trip
    string? VehicleId,                          // TripUpdate.vehicle.id
    long? Timestamp,                            // TripUpdate.timestamp (Unix seconds)
    int? TripDelaySeconds,                      // TripUpdate.delay (trip-level delay)
    IReadOnlyList<StopTimeData> StopTimes       // TripUpdate.stop_time_update[]
) : ISignalREvent;
```

**`RouteAlertEvent.cs`**
Produced from `FeedEntity.alert` entities.
```csharp
using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

// Emitted for each Alert entity in the GTFS-RT feed.
// Source: FeedEntity.alert.
public sealed record RouteAlertEvent(
    string FeedEntityId,      // FeedEntity.id (unique alert identifier)
    AlertData Alert,          // Alert fields
    bool IsActive             // true when DateTimeOffset.UtcNow is within any active_period
) : ISignalREvent;
```

**`VehicleDepartedStopEvent.cs`**
Derived event — emitted when a bus transitions from `StoppedAt` to `InTransitTo` between ticks. The worker detects this by comparing `VehicleStopStatus` against cached prior state.
```csharp
using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

// Emitted when a vehicle transitions from StoppedAt → InTransitTo between ticks.
// Requires worker-side state tracking (prior VehicleStopStatus per vehicle).
public sealed record VehicleDepartedStopEvent(
    VehicleData Vehicle,
    TripData Trip,
    string DepartedStopId,        // The stop_id the vehicle just left
    uint DepartedStopSequence,    // The stop_sequence of the departed stop
    string? NextStopId,           // VehiclePosition.stop_id after the transition
    uint? NextStopSequence,       // VehiclePosition.current_stop_sequence after transition
    int? DepartureDelaySeconds    // From matching StopTimeUpdate.departure.delay if available
) : ISignalREvent;
```

**`TripCompletedEvent.cs`**
Derived event — emitted when a `TripUpdate` has its last `StopTimeUpdate` in the past and the `TripDescriptor.schedule_relationship` is not `Canceled`/`Deleted`. Worker detects by comparing last stop departure time against `DateTimeOffset.UtcNow`.
```csharp
using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

// Emitted when a TripUpdate's final stop departure time has passed,
// indicating the trip has completed. Worker detects via StopTimeUpdate.departure.time.
public sealed record TripCompletedEvent(
    TripData Trip,
    string? VehicleId,            // TripUpdate.vehicle.id
    string TerminalStopId,        // Last StopTimeUpdate.stop_id
    uint? TerminalStopSequence,   // Last StopTimeUpdate.stop_sequence
    long? ActualDepartureTime,    // Last StopTimeUpdate.departure.time (Unix seconds)
    int? FinalDelaySeconds        // Last StopTimeUpdate.departure.delay
) : ISignalREvent;
```

---

#### EventMapper — Lives in Worker project, not Shared

`EventMapper` is defined in `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker` (not Shared) so that `GtfsRealtimeBindings` / `protobuf-net` are a Worker-only dependency. Shared remains package-free. See Phase 5 for the full implementation detail.

---

#### `EventEnvelopeConverter` static constructor — assembly scan pattern:
```csharp
static EventEnvelopeConverter()
{
    _eventTypes = typeof(EventEnvelope).Assembly
        .GetTypes()
        .Where(t => typeof(ISignalREvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
        .ToDictionary(t => t.Name);
}
```

#### `JsonSettings.ApplyTo` — copy pattern:
```csharp
public static void ApplyTo(JsonSerializerOptions target)
{
    target.PropertyNamingPolicy = DefaultOptions.PropertyNamingPolicy;
    target.DefaultIgnoreCondition = DefaultOptions.DefaultIgnoreCondition;
    target.NumberHandling = DefaultOptions.NumberHandling;
    target.WriteIndented = DefaultOptions.WriteIndented;
    foreach (var converter in DefaultOptions.Converters)
        target.Converters.Add(converter);
}
```

---

### Phase 2 — Delete Legacy Infrastructure

**Files to delete entirely:**

| File | Location |
|------|----------|
| `DTOs/SignalR/TransitJazzNotification.cs` | Shared |
| `Enums/TransitJazzNotificationType.cs` | Shared |
| `SignalR/SignalRNotificationHub.cs` | Server.WebAPI |
| `SignalR/TransitJazzNotificationHelper.cs` | Server.WebAPI |
| `SignalR/PlayerConnectionTracker.cs` | Server.WebAPI |
| `SignalR/PlayerIdProvider.cs` | Server.WebAPI |
| `IEventNotificationService.cs` (stub) | Server.WebAPI |
| `Services/SignalRNotificationService.cs` | Client.Core |

**Files to clean up (not delete):**

| File | Change |
|------|--------|
| `EndpointGroups/TestEndpoints.cs` | Remove the `Test.SignalR` POST endpoint and its `ITransitJazzNotificationHelper` dependency entirely — the test notification concept is gone |
| `TransitJazzApiEndpoints.cs` | Remove the `Test.SignalR` constant if it exists |
| `Server.WebAPI/Program.cs` | Remove registrations: `IUserIdProvider`, `ITransitJazzNotificationHelper`, `IPlayerConnectionTracker`; remove `AddSignalR()` (will be re-added with config in Phase 4) |
| `Client.WebApp/Program.cs` | Remove `ISignalRNotificationService` / `SignalRNotificationService` scoped registration |

> **Do not touch**: `Client.Core/Services/EventNotificationService.cs` and `IEventNotificationService` — entirely separate concern.

---

### Phase 3 — Server: New SignalR Hubs

**Files to create** (in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/`):

**`TransitHub.cs`** (FR-008):
```csharp
namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

public class TransitHub : Hub { }
```

**`WorkerTransitHub.cs`** (FR-009):
```csharp
[Authorize(Policy = "TransitDataPublisher")]
public class WorkerTransitHub : Hub
{
    private readonly IHubContext<TransitHub> _clientHub;
    private readonly ILogger<WorkerTransitHub> _logger;

    public WorkerTransitHub(IHubContext<TransitHub> clientHub, ILogger<WorkerTransitHub> logger)
    { ... }

    public async Task PublishBatch(List<EventEnvelope> batch)
    {
        await _clientHub.Clients.All.SendAsync("ReceiveBatch", batch);
        _logger.LogInformation("Relayed {Count} events from worker", batch.Count);
    }
}
```

---

### Phase 4 — Server: Program.cs Rewrite

Replace `builder.Services.AddSignalR()` and all deleted-type registrations with:

```csharp
// Auth
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
    options.AddPolicy("TransitDataPublisher", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("roles", "TransitData.Publish");
    }));

// SignalR with shared JSON settings
builder.Services.AddSignalR()
    .AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions));
```

Middleware order (must be in this sequence):
```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<TransitHub>("/hubs/transit").AllowAnonymous();
app.MapHub<WorkerTransitHub>("/hubs/worker-transit").RequireAuthorization("TransitDataPublisher");
```

`appsettings.json` additions:
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "",
    "ClientId": "",
    "Audience": "api://"
  }
}
```

---

### Phase 5 — Worker: New Infrastructure

**Files to create** (in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/`):

**`EventMapper.cs`** (FR-007) — Worker-only; depends on `GtfsRealtimeBindings`. Pure static methods mapping proto types → Shared event data records. See task 5.2 for full method signatures.

**`TokenProvider.cs`** (FR-012):
- Reads `AzureAd:Scope` — throws `InvalidOperationException` if null
- Reads `AzureAd:ManagedIdentityClientId` — nullable, passed to `DefaultAzureCredentialOptions`
- `GetAccessTokenAsync` → `_credential.GetTokenAsync(new TokenRequestContext(_scopes), ct).Token`

**`SignalRHubPublisher.cs`** (FR-013):
- Constructor builds `HubConnection` with `.WithUrl(hubUrl, opts => opts.AccessTokenProvider = () => tokenProvider.GetAccessTokenAsync())`, `.WithAutomaticReconnect()`, `.AddJsonProtocol(opts => JsonSettings.ApplyTo(opts.PayloadSerializerOptions))`
- Reconnecting/Reconnected/Closed event handlers — log only
- `StartAsync` — `await _connection.StartAsync(ct)`
- `PublishBatchAsync` — guard on `HubConnectionState.Connected`, log+drop if not; else `InvokeAsync("PublishBatch", batch, ct)`
- `DisposeAsync` — `await _connection.DisposeAsync()`

**Rewrite `Worker.cs`** (FR-015):
- Primary constructor: `Worker(ITransitHubPublisher publisher, ILogger<Worker> logger)`
- `PeriodicTimer` at 15s; each tick calls `BuildBatchAsync` then `publisher.PublishBatchAsync`
- `BuildBatchAsync` returns stub `List<EventEnvelope>` (empty for now)
- Tick errors caught, logged, host continues

**Rewrite `Program.cs`** (FR-015):
```csharp
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddSingleton<SignalRHubPublisher>();
builder.Services.AddSingleton<ITransitHubPublisher>(sp => sp.GetRequiredService<SignalRHubPublisher>());
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.Services.GetRequiredService<SignalRHubPublisher>().StartAsync();
host.Run();
```

**`appsettings.json`** additions:
```json
{
  "AzureAd": {
    "Scope": "api://{api-client-id}/.default",
    "ManagedIdentityClientId": null
  },
  "SignalR": {
    "HubUrl": "https://localhost:7269/hubs/worker-transit"
  }
}
```

**.csproj** additions:
```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
<PackageReference Include="Azure.Identity" Version="1.14.1" />
<ProjectReference Include="..\..\..\..\ChefKnifeStudios.TransitJazz.Shared\ChefKnifeStudios.TransitJazz.Shared.csproj" />
```

---

### Phase 6 — Client: TransitHubClient

**File to create**: `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/TransitHubClient.cs` (FR-014)

```csharp
public sealed class TransitHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<TransitHubClient> _logger;

    public event Action<VehiclePositionUpdatedEvent>? OnVehiclePositionUpdated;
    public event Action<ArrivalPredictionEvent>? OnArrivalPrediction;
    public event Action<RouteAlertEvent>? OnRouteAlert;
    public event Action<VehicleDepartedStopEvent>? OnVehicleDepartedStop;
    public event Action<TripCompletedEvent>? OnTripCompleted;

    public TransitHubClient(IConfiguration configuration, ILogger<TransitHubClient> logger)
    {
        _logger = logger;
        var hubUrl = configuration["AppSettings:SignalR:HubUrl"]
            ?? throw new InvalidOperationException("Missing AppSettings:SignalR:HubUrl");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions))
            .Build();

        _connection.On<List<EventEnvelope>>("ReceiveBatch", batch =>
        {
            foreach (var envelope in batch)
            {
                try
                {
                    switch (envelope.Payload)
                    {
                        case VehiclePositionUpdatedEvent e: OnVehiclePositionUpdated?.Invoke(e); break;
                        case ArrivalPredictionEvent e:      OnArrivalPrediction?.Invoke(e);      break;
                        case RouteAlertEvent e:             OnRouteAlert?.Invoke(e);             break;
                        case VehicleDepartedStopEvent e:    OnVehicleDepartedStop?.Invoke(e);    break;
                        case TripCompletedEvent e:          OnTripCompleted?.Invoke(e);          break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed dispatching envelope {EventType}", envelope.EventType);
                }
            }
        });
    }

    public Task ConnectAsync() => _connection.StartAsync();
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
```

**Rewrite `Client.WebApp/Program.cs`** — remove `ISignalRNotificationService` registration; add:
```csharp
builder.Services.AddScoped<TransitHubClient>();
```

**`appsettings.json`** for client (in `Client.WebApp/wwwroot/`):
```json
{
  "AppSettings": {
    "SignalR": {
      "HubUrl": "https://localhost:7269/hubs/transit"
    }
  }
}
```

---

### Phase 7 — NuGet Package Updates

| Project | Package | Action |
|---------|---------|--------|
| Server.WebAPI | `Microsoft.Identity.Web` | Add |
| Server.TransitDataWorker | `Microsoft.AspNetCore.SignalR.Client` | Add |
| Server.TransitDataWorker | `Azure.Identity` | Add |
| Server.TransitDataWorker | Shared project reference | Add |

---

## Implementation Order

The phases must be implemented in this sequence to avoid broken builds at each step:

```
Phase 1 (Shared new types)
  → Phase 2 (Delete legacy — Shared types deleted first, then server/client consumers)
    → Phase 3 (Server hubs — depend on Shared event types)
      → Phase 4 (Server Program.cs — depends on new hubs + auth packages)
        → Phase 5 (Worker — depends on Shared ITransitHubPublisher + JsonSettings)
          → Phase 6 (Client — depends on Shared event types + JsonSettings)
            → Phase 7 (Package versions pinned, build verified)
```

Within Phase 2, delete in this sub-order to avoid dangling references at each save:
1. `TestEndpoints.cs` cleanup (removes `ITransitJazzNotificationHelper` dependency)
2. `Server.WebAPI/Program.cs` cleanup (removes registrations)
3. `Client.WebApp/Program.cs` cleanup (removes `SignalRNotificationService` registration)
4. Delete the 8 files listed in Phase 2

---

## Configuration Reference

### Server.WebAPI `appsettings.json`
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "{tenant-id}",
    "ClientId": "{api-app-registration-client-id}",
    "Audience": "api://{api-app-registration-client-id}"
  }
}
```

### Worker `appsettings.json`
```json
{
  "AzureAd": {
    "Scope": "api://{api-app-registration-client-id}/.default",
    "ManagedIdentityClientId": null
  },
  "SignalR": {
    "HubUrl": "https://localhost:7269/hubs/worker-transit"
  }
}
```

### Client `wwwroot/appsettings.json`
```json
{
  "AppSettings": {
    "SignalR": {
      "HubUrl": "https://localhost:7269/hubs/transit"
    }
  }
}
```

---

## Testing Strategy

### Unit Tests
| Test | Covers |
|------|--------|
| `EventEnvelopeConverter` round-trip with each concrete event type | SC-004, FR-004 |
| `EventEnvelopeConverter` throws on unknown `eventType` with "strict mode enabled" | SC-005, FR-004 |
| `EventEnvelopeConverter` throws on missing `eventType` property | EC-005 |
| `EventMapper.ToPredictionData` delay calculation — positive, negative, null predicted | FR-007 |
| `JsonFlattener.Flatten` with nested nulls, arrays, primitives | FR-006 |
| Assembly scan finds all 5 event types (non-empty dictionary assertion) | EC-004 |

### Integration Tests
| Test | Covers |
|------|--------|
| `GET /hubs/worker-transit` without token → 401 | SC-003, FR-010 |
| `GET /hubs/worker-transit` with valid token, no role → 403 | SC-003, FR-010 |
| `GET /hubs/transit` without token → 101 Switching Protocols | FR-008 |
| Worker → hub → client end-to-end batch delivery | SC-001, US-001 |

### Verification Checklist
- `grep -r "TransitJazzNotification\|PlayerConnectionTracker\|PlayerIdProvider\|SignalRNotificationHub" src/` returns no results (SC-007)
- `JsonSettings.ApplyTo` call count per project = 1 each (SC-006)
- Build succeeds with no warnings on `net10.0`
