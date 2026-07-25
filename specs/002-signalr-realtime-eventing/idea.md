# SignalR Real-Time Eventing System — Full Specification

## 1. Overview

This spec defines a high-performance, low-boilerplate real-time event system for a transit tracking application (MARTA GTFS bus routes). An ASP.NET Core background worker produces domain events (bus position updates, route alerts, arrival predictions) and pushes them to Blazor clients over SignalR. Both server and client are C#.

### Core Design Goals
1. **Performance**: Minimize per-message overhead for high-frequency updates (400+ bus position updates per worker run) via batching and reduced serialization steps.
2. **Code Simplicity**: Eliminate unnecessary components (Hub methods, manual type switching, duplicated serialization config) to reduce maintenance burden.
3. **Minimal Code Radius**: Adding new event types requires touching only 1 file (the shared event record) with no client/server changes.
4. **Schema Consistency**: Shared types between server and client prevent drift, with automatic polymorphic serialization via assembly scanning.
5. **Strict Correctness**: Unknown event types throw explicit errors (strict mode) instead of silently ignoring, preventing silent failures.

### Domain Context (MARTA GTFS)
The system models a real-time bus tracking system with:
- **Vehicles**: Transit buses with GPS, identified by vehicle ID
- **Routes**: Named bus routes (e.g., Route 110 — Peachtree St)
- **Stops**: Physical stops along a route with scheduled/predicted arrival times
- **Alerts**: Service disruptions (detours, delays, cancellations)

---

## 2. Design Principles
1. **Shared Types**: All event records, sub-data records, and serialization utilities live in a single shared library referenced by both server and client. No hand-written DTOs on either side.
2. **Flat Record Hierarchy**: Events are `sealed record` types that implement a marker interface only. No base classes, no intermediate interfaces. Sub-data records are composed into events as properties.
3. **Static Pure Mappers**: Domain-to-sub-data mapping uses static pure functions with no side effects or dependencies.
4. **Automatic Polymorphic Serialization**: An `EventEnvelopeConverter` scans the shared assembly for all event types at startup, eliminating manual type switching or discriminator bookkeeping.
5. **Batched Delivery**: Worker batches all events per run into a single `List<EventEnvelope>` and sends 1 `SendAsync` call, reducing per-message overhead.
6. **Single Serialization Config**: A `JsonSettings` class defines canonical serializer options, with a helper to apply settings to SignalR (eliminating 3x config duplication).
7. **Strict Event Validation**: Unknown `EventType` values throw `JsonException` instead of silently ignoring.
8. **Minimal Hub**: The SignalR Hub class has no methods (only defines the client connection endpoint). Workers use `IHubContext` to push events, never invoking Hub methods directly.

---

## 3. Project Structure
All shared types live in a `Shared/` folder referenced by both server and client projects (via project reference or `.shproj`):

```
YourSolution/
├── Shared/
│   ├── Events/
│   │   ├── ISignalREvent.cs
│   │   ├── EventEnvelope.cs
│   │   ├── EventEnvelopeConverter.cs
│   │   ├── VehiclePositionUpdatedEvent.cs
│   │   ├── ArrivalPredictionEvent.cs
│   │   ├── RouteAlertEvent.cs
│   │   ├── VehicleDepartedStopEvent.cs
│   │   └── TripCompletedEvent.cs
│   ├── EventData/
│   │   ├── VehicleData.cs
│   │   ├── PositionData.cs
│   │   ├── RouteData.cs
│   │   ├── StopData.cs
│   │   ├── PredictionData.cs
│   │   └── AlertData.cs
│   ├── EventMapper.cs
│   ├── JsonSettings.cs
│   └── JsonFlattener.cs
├── Server/
│   ├── Hubs/
│   │   └── TransitHub.cs (empty, no methods)
│   ├── Services/
│   │   └── VehicleTrackingService.cs (background worker that publishes events)
│   └── Program.cs (hub registration, JSON config)
└── Client/
    ├── Services/
    │   └── TransitHubClient.cs (typed wrapper around HubConnection)
    └── Pages/
        └── LiveMap.razor (consumes events)
```

---

## 4. Shared Library Components

### 4.1 `ISignalREvent` — Marker Interface
A no-member interface implemented by every concrete event record to enable type constraints and assembly scanning.

```csharp
// Shared/Events/ISignalREvent.cs
namespace YourApp.Shared.Events;

/// <summary>
/// Marker interface implemented by every concrete event record.
/// Carries no members. Exists to enable type scanning and generic constraints.
/// </summary>
public interface ISignalREvent;
```

**Rules**:
- No properties, no methods, no default implementations
- Every concrete event directly implements `ISignalREvent` (no intermediate interfaces or base classes)

---

### 4.2 `EventEnvelope` — Wire Wrapper
A single record that carries all events over the SignalR wire, with a string discriminator and direct `ISignalREvent` payload (no intermediate `JsonElement`).

```csharp
// Shared/Events/EventEnvelope.cs
using System.Text.Json;

namespace YourApp.Shared.Events;

/// <summary>
/// The single type that crosses the SignalR wire for all events.
/// Uses a string discriminator (EventType) to identify the concrete payload type,
/// and carries the payload directly as ISignalREvent to eliminate intermediate serialization.
/// </summary>
/// <param name="EventType">
/// Discriminator string, set via nameof(ConcreteEventRecord) on the server.
/// Matched by EventEnvelopeConverter to deserialize the correct concrete type.
/// </param>
/// <param name="Timestamp">
/// UTC timestamp of when the event was produced on the server.
/// </param>
/// <param name="Payload">
/// The concrete event record implementing ISignalREvent.
/// Serialized/deserialized automatically via EventEnvelopeConverter.
/// </param>
public sealed record EventEnvelope(
    string EventType,
    DateTimeOffset Timestamp,
    ISignalREvent Payload);
```

---

### 4.3 `EventEnvelopeConverter` — Automatic Polymorphic Serialization
A custom `JsonConverter<EventEnvelope>` that handles serialization/deserialization of the polymorphic `Payload` property via assembly scanning. Scans the assembly containing `EventEnvelope` at startup to build a mapping of `EventType` strings to concrete types.

```csharp
// Shared/Events/EventEnvelopeConverter.cs
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YourApp.Shared.Events;

/// <summary>
/// Custom JsonConverter for EventEnvelope that automatically handles polymorphic
/// serialization of the Payload property (ISignalREvent) via assembly scanning.
/// 
/// Strict mode: Throws JsonException if an unknown EventType is encountered
/// during deserialization.
/// </summary>
public sealed class EventEnvelopeConverter : JsonConverter<EventEnvelope>
{
    private static readonly Dictionary<string, Type> _eventTypes;

    static EventEnvelopeConverter()
    {
        // Scan the assembly containing EventEnvelope for all ISignalREvent implementations
        var assembly = typeof(EventEnvelope).Assembly;
        _eventTypes = assembly.GetTypes()
            .Where(t => typeof(ISignalREvent).IsAssignableFrom(t) 
                && t.IsClass 
                && !t.IsAbstract)
            .ToDictionary(t => t.Name, t => t);
    }

    public override EventEnvelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var eventType = root.GetProperty("eventType").GetString() 
            ?? throw new JsonException("Missing EventType property");
        var timestamp = root.GetProperty("timestamp").GetDateTimeOffset();
        var payloadElement = root.GetProperty("payload");

        if (!_eventTypes.TryGetValue(eventType, out var payloadType))
            throw new JsonException($"Unknown EventType: {eventType} (strict mode enabled)");

        var payload = (ISignalREvent)payloadElement.Deserialize(payloadType, options)!;
        return new EventEnvelope(eventType, timestamp, payload);
    }

    public override void Write(Utf8JsonWriter writer, EventEnvelope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("eventType", value.EventType);
        writer.WriteString("timestamp", value.Timestamp);
        writer.WritePropertyName("payload");
        JsonSerializer.Serialize(writer, value.Payload, value.Payload.GetType(), options);
        writer.WriteEndObject();
    }
}
```

**Behavior**:
- **Startup**: Scans the assembly containing `EventEnvelope` for all non-abstract classes implementing `ISignalREvent`, builds a `nameof(ConcreteType) → Type` mapping.
- **Serialization**: Writes `EventType`, `Timestamp`, and serializes `Payload` to its concrete type.
- **Deserialization**: Reads `EventType`, looks up the concrete type, deserializes `Payload` directly. Throws `JsonException` for unknown `EventType` (strict mode).

---

### 4.4 `JsonSettings` — Single Serialization Source of Truth
Defines canonical `JsonSerializerOptions` and provides a helper to apply settings to SignalR, eliminating duplicated config.

```csharp
// Shared/JsonSettings.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YourApp.Shared;

/// <summary>
/// Canonical serialization settings for the entire eventing system.
/// Single source of truth to prevent server/client config drift.
/// </summary>
public static class JsonSettings
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new EventEnvelopeConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = false
    };

    /// <summary>
    /// Applies canonical settings to a target JsonSerializerOptions (used for SignalR config).
    /// Eliminates 3x config duplication between server, client, and shared library.
    /// </summary>
    public static void ApplyTo(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = DefaultOptions.PropertyNamingPolicy;
        options.DefaultIgnoreCondition = DefaultOptions.DefaultIgnoreCondition;
        foreach (var converter in DefaultOptions.Converters) 
            options.Converters.Add(converter);
        options.NumberHandling = DefaultOptions.NumberHandling;
        options.WriteIndented = DefaultOptions.WriteIndented;
    }
}
```

---

### 4.5 `JsonFlattener` — Debug Logging Only
Retained for debug logging of event payloads. Explicitly excluded from the hot-path eventing flow.

```csharp
// Shared/JsonFlattener.cs
using System.Collections.Generic;
using System.Text.Json;

namespace YourApp.Shared;

/// <summary>
/// Serialization utilities for non-hot-path use cases (debug logging only).
/// Flattens nested objects into dot-notation key-value pairs for logging.
/// </summary>
public static class JsonFlattener
{
    /// <summary>
    /// Serializes a value to JSON and flattens into a single-level dictionary
    /// with dot-notation keys for nested objects. Omits null properties.
    /// </summary>
    public static Dictionary<string, object?> Flatten<T>(T? value)
    {
        if (value is null)
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        var json = JsonSerializer.Serialize(value, JsonSettings.DefaultOptions);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Null)
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        Traverse(doc.RootElement, string.Empty, result);
        return result;
    }

    static void Traverse(JsonElement element, string prefix, Dictionary<string, object?> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    Traverse(prop.Value, key, result);
                }
                break;
            case JsonValueKind.Array:
                if (element.GetArrayLength() == 0) return;
                var arr = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    arr.Add(ExtractValue(item));
                result[prefix] = arr.ToArray();
                break;
            case JsonValueKind.String:
                result[prefix] = element.GetString();
                break;
            case JsonValueKind.True:
                result[prefix] = true;
                break;
            case JsonValueKind.False:
                result[prefix] = false;
                break;
            case JsonValueKind.Null:
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var i)) result[prefix] = i;
                else if (element.TryGetDouble(out var d)) result[prefix] = d;
                else result[prefix] = element.GetRawText();
                break;
        }
    }

    static object? ExtractValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Number when element.TryGetInt32(out var i) => i,
        JsonValueKind.Number when element.TryGetDouble(out var d) => d,
        _ => element.GetRawText()
    };
}
```

---

### 4.6 Sub-Data Records
Small, focused `sealed record` types composed into concrete events. No inheritance, no interfaces, no behavior.

```csharp
// Shared/EventData/VehicleData.cs
namespace YourApp.Shared.EventData;

public sealed record VehicleData(
    string VehicleId,
    string Label,
    string? LicensePlate);
```

```csharp
// Shared/EventData/PositionData.cs
namespace YourApp.Shared.EventData;

public sealed record PositionData(
    double Latitude,
    double Longitude,
    double? Bearing,
    double SpeedMph,
    double? Odometer);
```

```csharp
// Shared/EventData/RouteData.cs
namespace YourApp.Shared.EventData;

public sealed record RouteData(
    string RouteId,
    string RouteShortName,
    string RouteLongName,
    int? DirectionId);
```

```csharp
// Shared/EventData/StopData.cs
namespace YourApp.Shared.EventData;

public sealed record StopData(
    string StopId,
    string StopName,
    int StopSequence,
    double Latitude,
    double Longitude);
```

```csharp
// Shared/EventData/PredictionData.cs
using System;

namespace YourApp.Shared.EventData;

public sealed record PredictionData(
    DateTimeOffset ScheduledArrival,
    DateTimeOffset? PredictedArrival,
    int? DelaySeconds,
    bool IsRealTime);
```

```csharp
// Shared/EventData/AlertData.cs
using System;

namespace YourApp.Shared.EventData;

public sealed record AlertData(
    string AlertId,
    string Severity,
    string HeaderText,
    string DescriptionText,
    DateTimeOffset? ActiveFrom,
    DateTimeOffset? ActiveUntil);
```

---

### 4.7 Concrete Event Records
`Sealed record` types implementing `ISignalREvent`, composing sub-data records as properties.

```csharp
// Shared/Events/VehiclePositionUpdatedEvent.cs
using YourApp.Shared.EventData;

namespace YourApp.Shared.Events;

public sealed record VehiclePositionUpdatedEvent(
    VehicleData Vehicle,
    PositionData Position,
    RouteData? Route,
    string? TripId,
    int? CurrentStopSequence,
    string? CurrentStatus
) : ISignalREvent;
```

```csharp
// Shared/Events/ArrivalPredictionEvent.cs
using System.Collections.Generic;
using YourApp.Shared.EventData;

namespace YourApp.Shared.Events;

public sealed record ArrivalPredictionEvent(
    VehicleData Vehicle,
    RouteData Route,
    string TripId,
    IReadOnlyList<StopPrediction> Predictions
) : ISignalREvent;

public sealed record StopPrediction(StopData Stop, PredictionData Prediction);
```

```csharp
// Shared/Events/RouteAlertEvent.cs
using System.Collections.Generic;
using YourApp.Shared.EventData;

namespace YourApp.Shared.Events;

public sealed record RouteAlertEvent(
    AlertData Alert,
    IReadOnlyList<string> AffectedRouteIds,
    IReadOnlyList<string> AffectedStopIds,
    bool IsResolved
) : ISignalREvent;
```

```csharp
// Shared/Events/VehicleDepartedStopEvent.cs
using YourApp.Shared.EventData;

namespace YourApp.Shared.Events;

public sealed record VehicleDepartedStopEvent(
    VehicleData Vehicle,
    RouteData Route,
    StopData DepartedStop,
    StopData? NextStop,
    string TripId,
    int? DepartureDelaySeconds
) : ISignalREvent;
```

```csharp
// Shared/Events/TripCompletedEvent.cs
using System;
using YourApp.Shared.EventData;

namespace YourApp.Shared.Events;

public sealed record TripCompletedEvent(
    VehicleData Vehicle,
    RouteData Route,
    string TripId,
    StopData TerminalStop,
    double TripDurationMinutes,
    double? ScheduledDurationMinutes,
    DateTimeOffset CompletedAt
) : ISignalREvent;
```

---

### 4.8 `EventMapper` — Static Domain-to-SubData Mapper
Pure static functions that transform domain objects into sub-data records. No state, no I/O, no dependencies.

```csharp
// Shared/EventMapper.cs
using System;
using YourApp.Shared.EventData;

namespace YourApp.Shared;

public static class EventMapper
{
    public static VehicleData ToVehicleData(object vehicle) 
        => throw new NotImplementedException("Replace with domain-specific implementation");

    public static PositionData ToPositionData(object position) 
        => throw new NotImplementedException("Replace with domain-specific implementation");

    public static RouteData? ToRouteData(object? route) 
        => throw new NotImplementedException("Replace with domain-specific implementation");

    public static StopData ToStopData(object stop, int stopSequence) 
        => throw new NotImplementedException("Replace with domain-specific implementation");

    public static PredictionData ToPredictionData(
        DateTimeOffset scheduledArrival,
        DateTimeOffset? predictedArrival,
        bool isRealTime)
    {
        int? delaySeconds = predictedArrival.HasValue
            ? (int)(predictedArrival.Value - scheduledArrival).TotalSeconds
            : null;
        return new PredictionData(scheduledArrival, predictedArrival, delaySeconds, isRealTime);
    }

    public static AlertData ToAlertData(object alert) 
        => throw new NotImplementedException("Replace with domain-specific implementation");
}
```

---

## 5. Server Implementation

### 5.1 `TransitHub` — Empty Hub Class
No methods. Exists only to define the SignalR endpoint for client connections.

```csharp
// Server/Hubs/TransitHub.cs
using Microsoft.AspNetCore.SignalR;

namespace YourApp.Server.Hubs;

public class TransitHub : Hub { }
```

---

### 5.2 `Program.cs` — Configuration
Registers SignalR with shared JSON settings and maps the hub endpoint.

```csharp
// Server/Program.cs
using Microsoft.AspNetCore.SignalR;
using YourApp.Server.Hubs;
using YourApp.Shared;

var builder = WebApplication.CreateBuilder(args);

// Register SignalR with shared serialization settings
builder.Services.AddSignalR()
    .AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions));

// Register background worker
builder.Services.AddHostedService<VehicleTrackingService>();

var app = builder.Build();

// Map SignalR endpoint (clients connect here)
app.MapHub<TransitHub>("/hubs/transit");

app.Run();
```

---

### 5.3 `VehicleTrackingService` — Background Worker
Polls GTFS-RT feed, batches 400+ bus updates into a single list, and sends 1 `SendAsync` call. Optional debug logging uses `JsonFlattener`.

```csharp
// Server/Services/VehicleTrackingService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using YourApp.Server.Hubs;
using YourApp.Shared;
using YourApp.Shared.Events;

namespace YourApp.Server.Services;

public class VehicleTrackingService : BackgroundService
{
    private readonly IHubContext<TransitHub> _hubContext;
    private readonly ILogger<VehicleTrackingService> _logger;

    public VehicleTrackingService(IHubContext<TransitHub> hubContext, ILogger<VehicleTrackingService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = new List<EventEnvelope>();
            
            // Simulate processing 400 bus updates per run
            foreach (var busUpdate in GetBusUpdates()) // Replace with actual GTFS-RT polling
            {
                // Step 1: Map domain objects to sub-data records
                var vehicleData = EventMapper.ToVehicleData(busUpdate.Vehicle);
                var positionData = EventMapper.ToPositionData(busUpdate.Position);
                var routeData = EventMapper.ToRouteData(busUpdate.Route);

                // Step 2: Compose concrete event
                var evt = new VehiclePositionUpdatedEvent(
                    Vehicle: vehicleData,
                    Position: positionData,
                    Route: routeData,
                    TripId: busUpdate.TripId,
                    CurrentStopSequence: busUpdate.CurrentStopSequence,
                    CurrentStatus: busUpdate.CurrentStatus);

                // Step 3: Add to batch
                batch.Add(new EventEnvelope(
                    EventType: nameof(VehiclePositionUpdatedEvent),
                    Timestamp: DateTimeOffset.UtcNow,
                    Payload: evt));

                // Optional debug logging (non-hot-path)
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var flat = JsonFlattener.Flatten(evt);
                    _logger.LogDebug("Produced event {EventType}: {Flattened}", evt.GetType().Name, flat);
                }
            }

            // Send 1 batch to all clients (no per-route filtering)
            if (batch.Count > 0)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveBatch", batch, stoppingToken);
                _logger.LogInformation("Sent batch of {Count} events to all clients", batch.Count);
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    // Replace with actual GTFS-RT feed polling
    private IEnumerable<object> GetBusUpdates() => Enumerable.Empty<object>();
}
```

---

## 6. Client Implementation (Blazor)

### 6.1 `TransitHubClient` — Simplified Typed Wrapper
No manual deserialization. Uses batch handler, automatically deserialized payloads via `EventEnvelopeConverter`.

```csharp
// Client/Services/TransitHubClient.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using YourApp.Shared;
using YourApp.Shared.Events;

namespace YourApp.Client.Services;

public sealed class TransitHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public event Action<VehiclePositionUpdatedEvent>? OnVehiclePositionUpdated;
    public event Action<RouteAlertEvent>? OnRouteAlert;
    // Add other event Action<T> properties as needed

    public TransitHubClient(string hubUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions))
            .Build();

        // Single batch handler — payloads are already concrete types
        _connection.On<List<EventEnvelope>>("ReceiveBatch", batch =>
        {
            foreach (var envelope in batch)
            {
                switch (envelope.Payload)
                {
                    case VehiclePositionUpdatedEvent e:
                        OnVehiclePositionUpdated?.Invoke(e);
                        break;
                    case RouteAlertEvent e:
                        OnRouteAlert?.Invoke(e);
                        break;
                    // Add other event types as needed
                }
            }
        });
    }

    public async Task ConnectAsync() => await _connection.StartAsync();
    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
```

---

### 6.2 Blazor Page Consumption
Minimal code-behind, subscribes to events, updates UI.

```razor
@* Client/Pages/LiveMap.razor *@
@page "/live"
@implements IAsyncDisposable
@inject TransitHubClient Transit

<h3>Live Bus Positions</h3>

@foreach (var bus in _busPositions.Values)
{
    <div>@bus.Vehicle.Label — @bus.Position.Latitude, @bus.Position.Longitude — @bus.Position.SpeedMph mph</div>
}

@code {
    private readonly Dictionary<string, VehiclePositionUpdatedEvent> _busPositions = new();

    protected override async Task OnInitializedAsync()
    {
        Transit.OnVehiclePositionUpdated += HandlePosition;
        await Transit.ConnectAsync();
    }

    private void HandlePosition(VehiclePositionUpdatedEvent e)
    {
        _busPositions[e.Vehicle.VehicleId] = e;
        InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        Transit.OnVehiclePositionUpdated -= HandlePosition;
    }
}
```

---

## 7. Serialization Contract
- **Canonical Settings**: `JsonSettings.DefaultOptions` is the only source of truth. All serialization/deserialization uses these options.
- **EventEnvelopeConverter**: Registered in `DefaultOptions.Converters`, handles polymorphic `Payload` automatically.
- **Strict Mode**: Unknown `EventType` values throw `JsonException` during deserialization, preventing silent failures.
- **Wire Format Example**:
```json
{
  "eventType": "VehiclePositionUpdatedEvent",
  "timestamp": "2026-05-04T14:30:00Z",
  "payload": {
    "vehicle": { "vehicleId": "bus_1042", "label": "Bus 1042" },
    "position": { "latitude": 33.749, "longitude": -84.388, "speedMph": 22.5 },
    "route": { "routeId": "110", "routeShortName": "110" },
    "tripId": "trip_5589",
    "currentStopSequence": 12,
    "currentStatus": "IN_TRANSIT_TO"
  }
}
```

---

## 8. Performance Metrics (400 Bus Updates per Worker Run)
| Metric | Value |
|--------|-------|
| Total `SendAsync` Calls | 1 (batched) |
| Serialization Steps per Event | 1 (automatic via converter) |
| Deserialization Steps per Event | 0 (automatic via converter) |
| Files Touched for New Event | 1 (Shared/Events/NewEvent.cs) |
| Per-Message Overhead | Eliminated (batched) |

---

## 9. Worker → Hub Communication (Azure AD Authorized SignalR Client)

### 9.1 Architecture

```
┌─────────────────────────┐                          ┌─────────────────────────┐
│  TransitDataWorker      │                          │   Server.WebAPI         │
│  (App Service / ACA)    │                          │                         │
│                         │  1. Acquire token via    │                         │
│  ┌───────────────────┐  │     client credentials   │  ┌──────────────────┐  │
│  │ Azure.Identity    │──┼──► Azure AD ──────────►  │  │ JWT Bearer Auth  │  │
│  │ (DefaultAzureCred)│  │                          │  │ (validates token)│  │
│  └───────────────────┘  │                          │  └──────────────────┘  │
│                         │  2. HubConnection with   │                         │
│  ┌───────────────────┐  │     Bearer token         │  ┌──────────────────┐  │
│  │ HubConnection     │──┼──────────────────────►   │  │ WorkerTransitHub │  │
│  │ (SignalR client)  │  │  calls "PublishBatch"    │  │ [Authorize]      │  │
│  └───────────────────┘  │                          │  └────────┬─────────┘  │
└─────────────────────────┘                          │           │            │
                                                     │           │ IHubContext │
                                                     │           ▼            │
                                                     │  ┌──────────────────┐  │
                                                     │  │ SignalR clients   │  │
                                                     │  │ (ReceiveBatch)   │  │
                                                     │  └──────────────────┘  │
                                                     └─────────────────────────┘
```

The worker is a **client** of the hub, not a host. It connects like any Blazor client, calls a server-side hub method, and the hub rebroadcasts to all real clients. A dedicated worker hub endpoint (`/hubs/worker-transit`) is authorized via Azure AD app role, keeping the client-facing hub anonymous.

---

### 9.2 Azure AD Setup (Two App Registrations)

**1. API App Registration** (`TransitJazz-API`):
- Expose an API → Set App ID URI: `api://{api-client-id}`
- App Roles → Add:
  ```json
  {
    "allowedMemberTypes": ["Application"],
    "displayName": "Transit Data Publisher",
    "value": "TransitData.Publish",
    "id": "<generate-guid>"
  }
  ```

**2. Worker App Registration** (`TransitJazz-Worker`):
- API Permissions → Add `TransitJazz-API` → Application permission → `TransitData.Publish`
- Grant admin consent
- For local dev: create a client secret
- For Azure hosting: assign a Managed Identity and federate it to this app registration (no secrets needed)

---

### 9.3 Server.WebAPI: Worker Hub with Authorization

A dedicated hub for the worker, separate from the client-facing hub. Receives batches and relays to clients via `IHubContext`.

```csharp
// Server.WebAPI/SignalR/WorkerTransitHub.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using YourApp.Shared.Events;

namespace YourApp.Server.WebAPI.SignalR;

[Authorize(Policy = "TransitDataPublisher")]
public class WorkerTransitHub : Hub
{
    private readonly IHubContext<SignalRNotificationHub> _clientHub;
    private readonly ILogger<WorkerTransitHub> _logger;

    public WorkerTransitHub(
        IHubContext<SignalRNotificationHub> clientHub,
        ILogger<WorkerTransitHub> logger)
    {
        _clientHub = clientHub;
        _logger = logger;
    }

    public async Task PublishBatch(List<EventEnvelope> batch)
    {
        await _clientHub.Clients.All.SendAsync("ReceiveBatch", batch);
        _logger.LogInformation("Relayed batch of {Count} events from worker", batch.Count);
    }
}
```

---

### 9.4 Server.WebAPI: Authentication & Authorization Config

```csharp
// Server.WebAPI/Program.cs — additions
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

// Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Authorization policy: requires the app role assigned to the worker
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TransitDataPublisher", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("roles", "TransitData.Publish");
    });
});

// ... existing service registrations ...

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Client-facing hub (anonymous)
app.MapHub<SignalRNotificationHub>("/cks-notification").AllowAnonymous();

// Worker hub (requires TransitDataPublisher role)
app.MapHub<WorkerTransitHub>("/hubs/worker-transit")
    .RequireAuthorization("TransitDataPublisher");
```

**`appsettings.json` (WebAPI):**
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "{your-tenant-id}",
    "ClientId": "{api-app-registration-client-id}",
    "Audience": "api://{api-app-registration-client-id}"
  }
}
```

---

### 9.5 Shared: Publisher Interface

Lives in the shared library so both worker and WebAPI can reference it without circular dependencies.

```csharp
// Shared/ITransitHubPublisher.cs
using YourApp.Shared.Events;

namespace YourApp.Shared;

public interface ITransitHubPublisher
{
    Task PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default);
}
```

---

### 9.6 Worker: Token Acquisition

Uses `Azure.Identity` which handles both Managed Identity (in Azure) and client credentials (local dev) through `DefaultAzureCredential`'s fallback chain.

```csharp
// Server.TransitDataWorker/TokenProvider.cs
using Azure.Core;
using Azure.Identity;

namespace ChefKnifeStudios.TransitJazz.Server.DataWorker;

public sealed class TokenProvider
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;

    public TokenProvider(IConfiguration configuration)
    {
        _scopes = [configuration["AzureAd:Scope"]
            ?? throw new InvalidOperationException("Missing AzureAd:Scope")];

        // DefaultAzureCredential tries in order:
        // 1. Environment variables (CI/CD)
        // 2. Managed Identity (Azure App Service / Container Apps)
        // 3. Azure CLI (local dev)
        // 4. Visual Studio credential (local dev)
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = configuration["AzureAd:ManagedIdentityClientId"]
        });
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(_scopes), ct);
        return token.Token;
    }
}
```

**Why `DefaultAzureCredential` over raw MSAL:**
- In Azure: automatically uses Managed Identity (no secrets to rotate)
- In local dev: falls through to Azure CLI or Visual Studio credentials
- Single code path for all environments

---

### 9.7 Worker: Hub Publisher with Auth

```csharp
// Server.TransitDataWorker/SignalRHubPublisher.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using YourApp.Shared;
using YourApp.Shared.Events;

namespace ChefKnifeStudios.TransitJazz.Server.DataWorker;

public sealed class SignalRHubPublisher : ITransitHubPublisher, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<SignalRHubPublisher> _logger;

    public SignalRHubPublisher(
        IConfiguration configuration,
        TokenProvider tokenProvider,
        ILogger<SignalRHubPublisher> logger)
    {
        _logger = logger;
        var hubUrl = configuration["SignalR:HubUrl"]
            ?? throw new InvalidOperationException("Missing SignalR:HubUrl");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => tokenProvider.GetAccessTokenAsync();
            })
            .WithAutomaticReconnect()
            .AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions))
            .Build();

        _connection.Reconnecting += ex =>
        {
            _logger.LogWarning(ex, "Hub connection lost, reconnecting...");
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _logger.LogInformation("Reconnected to hub with connectionId {Id}", connectionId);
            return Task.CompletedTask;
        };

        _connection.Closed += ex =>
        {
            _logger.LogError(ex, "Hub connection closed permanently");
            return Task.CompletedTask;
        };
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _connection.StartAsync(ct);
        _logger.LogInformation("Connected to WorkerTransitHub");
    }

    public async Task PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default)
    {
        if (_connection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Hub not connected (state: {State}), dropping batch of {Count}",
                _connection.State, batch.Count);
            return;
        }

        await _connection.InvokeAsync("PublishBatch", batch, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
```

---

### 9.8 Worker: Program.cs & Worker.cs

```csharp
// Server.TransitDataWorker/Program.cs
using ChefKnifeStudios.TransitJazz.Server.DataWorker;
using YourApp.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddSingleton<SignalRHubPublisher>();
builder.Services.AddSingleton<ITransitHubPublisher>(sp => sp.GetRequiredService<SignalRHubPublisher>());
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

var publisher = host.Services.GetRequiredService<SignalRHubPublisher>();
await publisher.StartAsync();

host.Run();
```

```csharp
// Server.TransitDataWorker/Worker.cs
using YourApp.Shared;
using YourApp.Shared.Events;
using YourApp.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Server.DataWorker;

public class Worker(
    ITransitHubPublisher publisher,
    IHttpClientFactory httpClientFactory,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var batch = await BuildBatchAsync(stoppingToken);
                if (batch.Count > 0)
                {
                    await publisher.PublishBatchAsync(batch, stoppingToken);
                    logger.LogInformation("Published {Count} events", batch.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tick failed");
            }
        }
    }

    private async Task<List<EventEnvelope>> BuildBatchAsync(CancellationToken ct)
    {
        // Fetch GTFS-RT, map to events (same as BusDataPoc pattern)
        var batch = new List<EventEnvelope>();
        // ... populate from MARTA feed ...
        return batch;
    }
}
```

**`appsettings.json` (Worker):**
```json
{
  "AzureAd": {
    "Scope": "api://{api-app-registration-client-id}/.default",
    "ManagedIdentityClientId": null
  },
  "SignalR": {
    "HubUrl": "https://your-api-host/hubs/worker-transit"
  }
}
```

For local dev, set `ManagedIdentityClientId` to null and authenticate via `az login`. In Azure, set it to the user-assigned managed identity's client ID (or leave null for system-assigned).

---

### 9.9 Design Decisions

| Decision | Rationale |
|----------|-----------|
| Separate hub endpoint (`/hubs/worker-transit`) | Client hub stays anonymous; no auth regression for Blazor users |
| App Role claim (`TransitData.Publish`) | More restrictive than just validating appId — role must be explicitly granted via Azure AD |
| `DefaultAzureCredential` | Single code path works for local dev (Azure CLI), CI (environment vars), and production (Managed Identity) |
| `AccessTokenProvider` delegate | SignalR client calls it before each connection/reconnection — handles token refresh automatically |
| Relay via `IHubContext<SignalRNotificationHub>` | Worker hub receives → broadcasts through client hub. Clean separation of ingress and egress |
| Managed Identity over client secrets | No secrets to rotate, no credential leaks, Azure-native identity |

---

### 9.10 Azure Deployment Notes

**App Service:**
- Enable system-assigned managed identity in Identity blade
- Assign the `TransitData.Publish` app role to the managed identity:
  ```bash
  az ad app show --id {api-app-id} --query "appRoles"
  az rest --method POST \
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/{worker-sp-id}/appRoleAssignments" \
    --body '{"principalId":"{worker-sp-id}","resourceId":"{api-sp-id}","appRoleId":"{role-guid}"}'
  ```

**Container Apps:**
- Same Managed Identity approach; assign via Bicep/ARM or CLI
- Internal traffic between containers in the same Container App Environment bypasses public DNS

---

## 10. Adding a New Event Type
Only 1 file needs to be created/modified:
1. Create a new `sealed record` in `Shared/Events/` implementing `ISignalREvent` (compose sub-data records as properties)
2. The `EventEnvelopeConverter` automatically scans the new type at startup (no registration needed)
3. Add a case to the client's `TransitHubClient` batch handler (optional, only if the client needs to consume the event)

No server-side changes are required. No serialization config updates are needed.

---

## 11. Testing Strategy
1. **EventMapper Tests**: Test each static mapper method with known domain objects, assert all sub-data record properties.
2. **Serialization Round-Trip Tests**: Serialize `EventEnvelope` with concrete payload, deserialize, assert payload is the correct type with correct values.
3. **Strict Mode Tests**: Assert `JsonException` is thrown when deserializing an unknown `EventType`.
4. **Performance Tests**: Assert 400-event batch sends complete in <50ms.
5. **Integration Tests**: End-to-end test with worker producing events, client receiving and dispatching correctly.
6. **Auth Tests**: Verify unauthenticated connections to `/hubs/worker-transit` are rejected with 401. Verify connections without `TransitData.Publish` role are rejected with 403.

---

## 12. Validation Checklist
- [ ] All event records and `EventEnvelope` are in the same assembly (for automatic scanning)
- [ ] `JsonSettings.ApplyTo` is used for all SignalR configuration (no duplicated config)
- [ ] `TransitHub` has no methods
- [ ] Worker uses `IHubContext.Clients.All` (no groups, no filtering)
- [ ] `JsonFlattener.Flatten` is only used for debug logging (not hot-path)
- [ ] Unknown `EventType` throws `JsonException` (strict mode)
- [ ] New events require touching only 1 file in the shared library
- [ ] Azure AD app registrations created (API + Worker)
- [ ] `TransitData.Publish` app role defined on API registration
- [ ] Worker app registration granted `TransitData.Publish` permission with admin consent
- [ ] `/hubs/worker-transit` requires `TransitDataPublisher` authorization policy
- [ ] `/cks-notification` remains `AllowAnonymous` for Blazor clients
- [ ] Managed Identity configured for Azure-hosted worker (no client secrets in production)
