# SignalR Real-Time Eventing System — Tasks

Each task is atomic and independently verifiable. Complete in phase order — each phase must build before the next begins.

---

## Phase 1 — Shared Library: New Types

### 1.1 — EventData: VehicleData
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/EventData/VehicleData.cs`
- [X] Define `sealed record VehicleData(string Id, string? Label, string? LicensePlate, OccupancyStatus? OccupancyStatus, int? OccupancyPercentage)`
- [X] Define `enum OccupancyStatus` with all 9 values matching GTFS-RT proto integers (Empty=0 … NotBoardable=8) in the same file

### 1.2 — EventData: PositionData
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/EventData/PositionData.cs`
- [X] Define `sealed record PositionData(float Latitude, float Longitude, float? Bearing, float? SpeedMetersPerSec, double? OdometerMeters, long? Timestamp, uint? CurrentStopSequence, string? CurrentStopId, VehicleStopStatus? CurrentStatus, CongestionLevel? CongestionLevel)`
- [X] Define `enum VehicleStopStatus { IncomingAt=0, StoppedAt=1, InTransitTo=2 }` in the same file
- [X] Define `enum CongestionLevel` with all 5 values (UnknownCongestionLevel=0 … SevereCongestion=4) in the same file

### 1.3 — EventData: TripData
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/EventData/TripData.cs`
- [X] Define `sealed record TripData(string? TripId, string? RouteId, int? DirectionId, string? StartTime, string? StartDate, TripScheduleRelationship? ScheduleRelationship)`
- [X] Define `enum TripScheduleRelationship` with 7 values matching proto integers (Scheduled=0, Unscheduled=2, Canceled=3, Replacement=5, Duplicated=6, Deleted=7, New=8) in the same file

### 1.4 — EventData: StopTimeData
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/EventData/StopTimeData.cs`
- [X] Define `sealed record StopTimeData(string? StopId, uint? StopSequence, long? ArrivalTime, int? ArrivalDelay, int? ArrivalUncertainty, long? DepartureTime, int? DepartureDelay, int? DepartureUncertainty, StopTimeScheduleRelationship? ScheduleRelationship)`
- [X] Define `enum StopTimeScheduleRelationship { Scheduled=0, Skipped=1, NoData=2, Unscheduled=3 }` in the same file

### 1.5 — EventData: AlertData
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/EventData/AlertData.cs`
- [X] Define `sealed record AlertData(string? HeaderText, string? DescriptionText, string? Url, AlertCause Cause, AlertEffect Effect, AlertSeverity Severity, long? ActiveFrom, long? ActiveUntil, IReadOnlyList<string> AffectedRouteIds, IReadOnlyList<string> AffectedStopIds)`
- [X] Define `enum AlertCause` with all 12 values matching proto integers (UnknownCause=1 … MedicalEmergency=12) in the same file
- [X] Define `enum AlertEffect` with all 11 values matching proto integers (NoService=1 … AccessibilityIssue=11) in the same file
- [X] Define `enum AlertSeverity { UnknownSeverity=1, Info=2, Warning=3, Severe=4 }` in the same file

### 1.6 — Events: ISignalREvent
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/ISignalREvent.cs`
- [X] Define `public interface ISignalREvent;` — no members, no base interface

### 1.7 — Events: EventEnvelope
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/EventEnvelope.cs`
- [X] Define `public sealed record EventEnvelope(string EventType, DateTimeOffset Timestamp, ISignalREvent Payload)`

### 1.8 — Events: EventEnvelopeConverter
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/EventEnvelopeConverter.cs`
- [X] Implement `sealed class EventEnvelopeConverter : JsonConverter<EventEnvelope>`
- [X] Static constructor: scan `typeof(EventEnvelope).Assembly` for all non-abstract classes implementing `ISignalREvent`; build `static readonly Dictionary<string, Type> _eventTypes` keyed by `t.Name`
- [X] `Write`: write `eventType` (string), `timestamp` (DateTimeOffset), `payload` serialized to its concrete type using `value.Payload.GetType()`
- [X] `Read`: parse with `JsonDocument.ParseValue`; read `eventType` string (throw `JsonException("Missing EventType property")` if null); look up in `_eventTypes` (throw `JsonException($"Unknown EventType: {eventType} (strict mode enabled)")` if missing); deserialize `payload` element to the resolved type; return new `EventEnvelope`

### 1.9 — Events: VehiclePositionUpdatedEvent
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/VehiclePositionUpdatedEvent.cs`
- [X] Define `public sealed record VehiclePositionUpdatedEvent(VehicleData Vehicle, PositionData Position, TripData? Trip) : ISignalREvent`

### 1.10 — Events: ArrivalPredictionEvent
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/ArrivalPredictionEvent.cs`
- [X] Define `public sealed record ArrivalPredictionEvent(TripData Trip, string? VehicleId, long? Timestamp, int? TripDelaySeconds, IReadOnlyList<StopTimeData> StopTimes) : ISignalREvent`

### 1.11 — Events: RouteAlertEvent
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/RouteAlertEvent.cs`
- [X] Define `public sealed record RouteAlertEvent(string FeedEntityId, AlertData Alert, bool IsActive) : ISignalREvent`

### 1.12 — Events: VehicleDepartedStopEvent
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/VehicleDepartedStopEvent.cs`
- [X] Define `public sealed record VehicleDepartedStopEvent(VehicleData Vehicle, TripData Trip, string DepartedStopId, uint DepartedStopSequence, string? NextStopId, uint? NextStopSequence, int? DepartureDelaySeconds) : ISignalREvent`

### 1.13 — Events: TripCompletedEvent
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/Events/TripCompletedEvent.cs`
- [X] Define `public sealed record TripCompletedEvent(TripData Trip, string? VehicleId, string TerminalStopId, uint? TerminalStopSequence, long? ActualDepartureTime, int? FinalDelaySeconds) : ISignalREvent`

### 1.14 — JsonSettings
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/JsonSettings.cs`
- [X] Define `static class JsonSettings` with `static readonly JsonSerializerOptions DefaultOptions` (camelCase, `WhenWritingNull`, `JsonStringEnumConverter`, `EventEnvelopeConverter`, `AllowNamedFloatingPointLiterals`, `WriteIndented=false`)
- [X] Implement `static void ApplyTo(JsonSerializerOptions target)`: copy `PropertyNamingPolicy`, `DefaultIgnoreCondition`, `NumberHandling`, `WriteIndented`; add each converter from `DefaultOptions.Converters`

### 1.15 — JsonFlattener
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/JsonFlattener.cs`
- [X] Implement `static class JsonFlattener` with `static Dictionary<string, object?> Flatten<T>(T? value)`
- [X] Serialize value with `JsonSettings.DefaultOptions`; traverse `JsonDocument` recursively; produce dot-notation keys; omit null values; handle Object, Array, String, Number, True, False kinds

### 1.16 — ITransitHubPublisher
- [X] Create `src/ChefKnifeStudios.TransitJazz.Shared/ITransitHubPublisher.cs`
- [X] Define `public interface ITransitHubPublisher { Task PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default); }`

### 1.17 — Verify Phase 1 builds
- [X] Run `dotnet build src/ChefKnifeStudios.TransitJazz.Shared/` — zero errors, zero warnings

---

## Phase 2 — Delete Legacy Infrastructure

### 2.1 — Clean up TestEndpoints.cs
- [X] Open `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/TestEndpoints.cs`
- [X] Remove the entire `Test.SignalR` `MapPost` endpoint block and its `ITransitJazzNotificationHelper` parameter
- [X] Remove `using` directives for `TransitJazzNotification` and `ITransitJazzNotificationHelper` if now unused
- [X] If `MapTestEndpoints` body is now empty, keep the method but return `builder` with no mapped routes (or remove the call from `Program.cs` in task 2.2)

### 2.2 — Clean up Server.WebAPI/Program.cs
- [X] Remove `builder.Services.AddSingleton<IUserIdProvider, PlayerIdProvider>()`
- [X] Remove `builder.Services.AddSingleton<ITransitJazzNotificationHelper, TransitJazzNotificationHelper>()`
- [X] Remove `builder.Services.AddSingleton<IPlayerConnectionTracker, PlayerConnectionTracker>()`
- [X] Remove `builder.Services.AddSignalR()` (will be re-added with config in Phase 4)
- [X] Remove `app.MapHub<SignalRNotificationHub>("/cks-notification")`
- [X] Remove all `using` directives that now resolve to deleted types

### 2.3 — Clean up Client.WebApp/Program.cs
- [X] Remove `builder.Services.AddScoped<ISignalRNotificationService, SignalRNotificationService>()`
- [X] Remove the corresponding `using` directive

### 2.4 — Delete legacy Shared files
- [X] Delete `src/ChefKnifeStudios.TransitJazz.Shared/DTOs/SignalR/TransitJazzNotification.cs`
- [X] Delete `src/ChefKnifeStudios.TransitJazz.Shared/Enums/TransitJazzNotificationType.cs`
- [X] Delete empty `DTOs/SignalR/` folder if now empty

### 2.5 — Delete legacy Server.WebAPI files
- [X] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/SignalRNotificationHub.cs`
- [X] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/TransitJazzNotificationHelper.cs`
- [X] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/PlayerConnectionTracker.cs`
- [X] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/PlayerIdProvider.cs`
- [X] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/IEventNotificationService.cs` (the empty server-side stub only)
- [X] Delete `src/Server/ChefKnifeStudios.TransitJazz.Server.Core/Interfaces/ITransitJazzNotificationHelper.cs`

### 2.6 — Delete legacy Client.Core files
- [X] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/SignalRNotificationService.cs` (contains `SignalRNotificationService`, `ISignalRNotificationService`, `TransitJazzNotificationHandler`)

### 2.7 — Verify Phase 2 builds
- [X] Run `dotnet build src/` — zero errors, zero warnings
- [X] Confirm `grep -r "TransitJazzNotification\|PlayerConnectionTracker\|PlayerIdProvider\|SignalRNotificationHub\|TransitJazzNotificationHelper" src/` returns no results

---

## Phase 3 — Server: New SignalR Hubs

### 3.1 — TransitHub
- [X] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/TransitHub.cs`
- [X] Define `public class TransitHub : Hub { }` — no methods, no constructor

### 3.2 — WorkerTransitHub
- [X] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/SignalR/WorkerTransitHub.cs`
- [X] Decorate with `[Authorize(Policy = "TransitDataPublisher")]`
- [X] Inject `IHubContext<TransitHub>` and `ILogger<WorkerTransitHub>` via constructor
- [X] Implement `async Task PublishBatch(List<EventEnvelope> batch)`: call `_clientHub.Clients.All.SendAsync("ReceiveBatch", batch)`; log `"Relayed {Count} events from worker"`

### 3.3 — Verify Phase 3 builds
- [X] Run `dotnet build src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/` — zero errors

---

## Phase 4 — Server: Program.cs & Auth

### 4.1 — Add Microsoft.Identity.Web package
- [X] Add `<PackageReference Include="Microsoft.Identity.Web" Version="3.8.2" />` to `ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj`

### 4.2 — Wire authentication and authorization
- [X] Add `builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))`
- [X] Add `builder.Services.AddAuthorization(options => options.AddPolicy("TransitDataPublisher", policy => { policy.RequireAuthenticatedUser(); policy.RequireClaim("roles", "TransitData.Publish"); }))`

### 4.3 — Wire SignalR with JsonSettings
- [X] Add `builder.Services.AddSignalR().AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions))`

### 4.4 — Add middleware and hub endpoints
- [X] Add `app.UseAuthentication()` before `app.UseAuthorization()` and before any `app.MapHub` calls
- [X] Add `app.UseAuthorization()` after `app.UseAuthentication()`
- [X] Add `app.MapHub<TransitHub>("/hubs/transit").AllowAnonymous()`
- [X] Add `app.MapHub<WorkerTransitHub>("/hubs/worker-transit").RequireAuthorization("TransitDataPublisher")`

### 4.5 — Add AzureAd config section
- [X] Add `AzureAd` block to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` with keys: `Instance`, `TenantId`, `ClientId`, `Audience` (placeholder values)

### 4.6 — Verify Phase 4 builds and auth is wired
- [X] Run `dotnet build src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/` — zero errors
- [X] Confirm `app.UseAuthentication()` appears before `app.UseAuthorization()` in `Program.cs` (code review)

---

## Phase 5 — Worker: New Infrastructure

### 5.1 — Add packages and project reference to worker .csproj
- [ ] Add `<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />` to `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj`
- [ ] Add `<PackageReference Include="Azure.Identity" Version="1.14.1" />` to worker .csproj
- [ ] Add `<PackageReference Include="GtfsRealtimeBindings" Version="0.0.4" />` to worker .csproj — **only here; Shared has no GTFS dependency**
- [ ] Add `<ProjectReference Include="..\..\..\..\ChefKnifeStudios.TransitJazz.Shared\ChefKnifeStudios.TransitJazz.Shared.csproj" />` to worker .csproj
- [ ] Verify relative path resolves: run `dotnet build` on worker project alone

### 5.2 — EventMapper (Worker project)
- [ ] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/EventMapper.cs`
- [ ] Namespace: `ChefKnifeStudios.TransitJazz.Server.DataWorker`
- [ ] Implement `static VehicleData ToVehicleData(VehicleDescriptor v, VehiclePosition vp)`: map `v.Id`, `v.Label`, `v.LicensePlate`; cast `vp.OccupancyStatus` to `OccupancyStatus?` and `vp.OccupancyPercentage` to `int?`
- [ ] Implement `static PositionData ToPositionData(Position p, VehiclePosition vp)`: map `p.Latitude`, `p.Longitude`, `p.Bearing`, `p.Speed` (as `SpeedMetersPerSec`), `p.Odometer` (as `OdometerMeters`); map `vp.Timestamp` (as `long?`), `vp.CurrentStopSequence`, `vp.StopId` (as `CurrentStopId`), `vp.CurrentStatus` cast to `VehicleStopStatus?`, `vp.CongestionLevel` cast to `CongestionLevel?`
- [ ] Implement `static TripData? ToTripData(TripDescriptor? t)`: return `null` if `t` is null; map `t.TripId`, `t.RouteId`, `t.DirectionId` (as `int?`), `t.StartTime`, `t.StartDate`, `t.ScheduleRelationship` cast to `TripScheduleRelationship?`
- [ ] Implement `static StopTimeData ToStopTimeData(TripUpdate.StopTimeUpdate stu)`: map `stu.StopId`, `stu.StopSequence`; null-safe map `stu.Arrival?.Time`, `stu.Arrival?.Delay`, `stu.Arrival?.Uncertainty`; null-safe map `stu.Departure?.Time`, `stu.Departure?.Delay`, `stu.Departure?.Uncertainty`; cast `stu.ScheduleRelationship` to `StopTimeScheduleRelationship?`
- [ ] Implement `static AlertData ToAlertData(Alert a)`: call `ResolveTranslation` for `HeaderText`, `DescriptionText`, `Url`; cast `a.Cause` to `AlertCause`, `a.Effect` to `AlertEffect`, `a.SeverityLevel` to `AlertSeverity`; use `a.ActivePeriod.FirstOrDefault()?.Start` (as `long?`) and `.End` (as `long?`) for `ActiveFrom`/`ActiveUntil`; extract non-null `route_id` values from `a.InformedEntity` into `AffectedRouteIds`; extract non-null `stop_id` values into `AffectedStopIds`
- [ ] Implement `static string? ResolveTranslation(TranslatedString? ts)`: return `null` if null or `ts.Translation` is empty; return first where `language == "en"`; else first where `string.IsNullOrEmpty(language)`; else `ts.Translation[0].Text`
- [ ] Implement `static bool IsAlertActive(Alert a, DateTimeOffset now)`: convert `now` to Unix seconds via `now.ToUnixTimeSeconds()`; return `true` if any `active_period` satisfies `(period.Start == 0 || period.Start <= nowSec) && (period.End == 0 || nowSec < period.End)`

### 5.3 — TokenProvider
- [ ] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/TokenProvider.cs`
- [ ] `sealed class TokenProvider` — inject `IConfiguration` via constructor
- [ ] Read `AzureAd:Scope` — throw `InvalidOperationException("Missing AzureAd:Scope")` if null
- [ ] Read `AzureAd:ManagedIdentityClientId` (nullable)
- [ ] Construct `DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId })`
- [ ] Implement `async Task<string> GetAccessTokenAsync(CancellationToken ct = default)`: call `_credential.GetTokenAsync(new TokenRequestContext(_scopes), ct)`; return `token.Token`

### 5.4 — SignalRHubPublisher
- [ ] Create `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/SignalRHubPublisher.cs`
- [ ] `sealed class SignalRHubPublisher : ITransitHubPublisher, IAsyncDisposable`
- [ ] Constructor: read `SignalR:HubUrl` — throw `InvalidOperationException("Missing SignalR:HubUrl")` if null; build `HubConnection` with `.WithUrl(hubUrl, opts => opts.AccessTokenProvider = () => tokenProvider.GetAccessTokenAsync())`, `.WithAutomaticReconnect()`, `.AddJsonProtocol(opts => JsonSettings.ApplyTo(opts.PayloadSerializerOptions))`
- [ ] Wire `_connection.Reconnecting` → `LogWarning("Hub connection lost, reconnecting... {ex}")`
- [ ] Wire `_connection.Reconnected` → `LogInformation("Reconnected to hub, connectionId={id}")`
- [ ] Wire `_connection.Closed` → `LogError("Hub connection closed permanently {ex}")`
- [ ] Implement `async Task StartAsync(CancellationToken ct = default)`: `await _connection.StartAsync(ct)`; log `"Connected to WorkerTransitHub"`
- [ ] Implement `async Task PublishBatchAsync(List<EventEnvelope> batch, CancellationToken ct = default)`: if `_connection.State != HubConnectionState.Connected`, log warning and return; else `await _connection.InvokeAsync("PublishBatch", batch, ct)`
- [ ] Implement `async ValueTask DisposeAsync()`: `await _connection.DisposeAsync()`

### 5.5 — Rewrite Worker.cs
- [ ] Replace existing `Worker` implementation with primary constructor `Worker(ITransitHubPublisher publisher, ILogger<Worker> logger)`
- [ ] Use `PeriodicTimer(TimeSpan.FromSeconds(15))` in `ExecuteAsync`
- [ ] Each tick: call `BuildBatchAsync(ct)` then `publisher.PublishBatchAsync(batch, ct)`; wrap in try/catch; log errors; do not rethrow
- [ ] Implement `private async Task<List<EventEnvelope>> BuildBatchAsync(CancellationToken ct)` — stub returning `new List<EventEnvelope>()` with a `// TODO: fetch MARTA GTFS-RT feed` comment
- [ ] Add worker-state fields for derived event detection: `ConcurrentDictionary<string, VehicleStopStatus> _priorStopStatus` (for `VehicleDepartedStop`); `HashSet<string> _completedTripIds` (for `TripCompleted` dedup)

### 5.6 — Rewrite Worker Program.cs
- [ ] Replace existing `Program.cs` with:
  - `builder.Services.AddSingleton<TokenProvider>()`
  - `builder.Services.AddSingleton<SignalRHubPublisher>()`
  - `builder.Services.AddSingleton<ITransitHubPublisher>(sp => sp.GetRequiredService<SignalRHubPublisher>())`
  - `builder.Services.AddHostedService<Worker>()`
  - Build host, resolve `SignalRHubPublisher`, call `await publisher.StartAsync()`, then `host.Run()`

### 5.7 — Add worker appsettings.json entries
- [ ] Add `AzureAd:Scope` key with placeholder value `"api://{api-client-id}/.default"`
- [ ] Add `AzureAd:ManagedIdentityClientId` key with value `null`
- [ ] Add `SignalR:HubUrl` key with value `"https://localhost:7269/hubs/worker-transit"`

### 5.8 — Verify Phase 5 builds
- [ ] Run `dotnet build src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/` — zero errors

---

## Phase 6 — Client: TransitHubClient

### 6.1 — Create TransitHubClient
- [ ] Create `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/TransitHubClient.cs`
- [ ] `sealed class TransitHubClient : IAsyncDisposable`
- [ ] Declare typed events: `event Action<VehiclePositionUpdatedEvent>? OnVehiclePositionUpdated`, `event Action<ArrivalPredictionEvent>? OnArrivalPrediction`, `event Action<RouteAlertEvent>? OnRouteAlert`, `event Action<VehicleDepartedStopEvent>? OnVehicleDepartedStop`, `event Action<TripCompletedEvent>? OnTripCompleted`
- [ ] Constructor: inject `IConfiguration` and `ILogger<TransitHubClient>`; read `AppSettings:SignalR:HubUrl` — throw `InvalidOperationException` if null; build `HubConnection` with `.WithUrl(hubUrl)`, `.WithAutomaticReconnect()`, `.AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions))`
- [ ] Register `_connection.On<List<EventEnvelope>>("ReceiveBatch", batch => { foreach envelope: try { switch on Payload type, invoke matching event } catch (Exception ex) { _logger.LogError(ex, "Failed dispatching envelope {EventType}", envelope.EventType) } })`
- [ ] Implement `public Task ConnectAsync() => _connection.StartAsync()`
- [ ] Implement `public ValueTask DisposeAsync() => _connection.DisposeAsync()`

### 6.2 — Register TransitHubClient in Client.WebApp
- [ ] Open `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs`
- [ ] Add `builder.Services.AddScoped<TransitHubClient>()`
- [ ] Add `using ChefKnifeStudios.TransitJazz.Client.Core.Services;`

### 6.3 — Add client hub URL config
- [ ] Add `AppSettings.SignalR.HubUrl` to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json` with value `"https://localhost:7269/hubs/transit"`

### 6.4 — Verify Phase 6 builds
- [ ] Run `dotnet build src/Client/` — zero errors

---

## Phase 7 — Full Build & Verification

### 7.1 — Full solution build
- [ ] Run `dotnet build` at solution root — zero errors, zero warnings across all projects

### 7.2 — Legacy type grep verification (SC-007)
- [ ] Run: `grep -r "TransitJazzNotification\|PlayerConnectionTracker\|PlayerIdProvider\|SignalRNotificationHub\|TransitJazzNotificationHelper\|ISignalRNotificationClient\|SignalRNotificationService" src/`
- [ ] Confirm: zero results

### 7.3 — JsonSettings.ApplyTo call count verification (SC-006)
- [ ] Confirm exactly one `JsonSettings.ApplyTo` call in `Server.WebAPI/Program.cs`
- [ ] Confirm exactly one `JsonSettings.ApplyTo` call in `SignalRHubPublisher.cs`
- [ ] Confirm exactly one `JsonSettings.ApplyTo` call in `TransitHubClient.cs`

### 7.4 — Unit test: EventEnvelopeConverter round-trip (SC-004)
- [ ] Serialize `EventEnvelope` with `VehiclePositionUpdatedEvent` payload using `JsonSettings.DefaultOptions`
- [ ] Deserialize the JSON back using `JsonSettings.DefaultOptions`
- [ ] Assert `result.Payload` is `VehiclePositionUpdatedEvent` with all properties equal to original

### 7.5 — Unit test: strict mode enforcement (SC-005)
- [ ] Deserialize JSON `{"eventType":"UnknownFutureEvent","timestamp":"...","payload":{}}` using `JsonSettings.DefaultOptions`
- [ ] Assert `JsonException` is thrown with message containing `"strict mode enabled"`

### 7.6 — Unit test: missing eventType throws (EC-005)
- [ ] Deserialize JSON `{"timestamp":"...","payload":{}}` (no `eventType` property)
- [ ] Assert `JsonException` is thrown with message containing `"Missing EventType property"`

### 7.7 — Unit test: assembly scanner finds all event types (EC-004)
- [ ] Access `EventEnvelopeConverter` internal type dictionary via reflection or test-only accessor
- [ ] Assert dictionary contains exactly 5 keys: `"VehiclePositionUpdatedEvent"`, `"ArrivalPredictionEvent"`, `"RouteAlertEvent"`, `"VehicleDepartedStopEvent"`, `"TripCompletedEvent"`

### 7.8 — Unit test: EventMapper.IsAlertActive (Worker project)
- [ ] Test target: `ChefKnifeStudios.TransitJazz.Server.DataWorker.EventMapper.IsAlertActive`
- [ ] Assert returns `true` when `now` is between `start` and `end` of an active period
- [ ] Assert returns `true` when `start` is 0/missing (−∞ lower bound)
- [ ] Assert returns `true` when `end` is 0/missing (+∞ upper bound)
- [ ] Assert returns `false` when `now` is before all active periods
- [ ] Assert returns `false` when alert has no active periods

### 7.9 — Unit test: EventMapper.ResolveTranslation (Worker project)
- [ ] Test target: `ChefKnifeStudios.TransitJazz.Server.DataWorker.EventMapper.ResolveTranslation`
- [ ] Assert returns English translation when `language == "en"` is present
- [ ] Assert falls back to untagged translation when no English entry exists
- [ ] Assert falls back to `translation[0].Text` when all entries have non-English language tags
- [ ] Assert returns `null` for null or empty `TranslatedString`

### 7.10 — Unit test: JsonFlattener
- [ ] Assert nested object flattens to dot-notation keys (e.g., `vehicle.id`)
- [ ] Assert null properties are omitted from output
- [ ] Assert arrays are collected as `object[]`
- [ ] Assert `null` input returns empty dictionary

### 7.11 — Integration test: auth enforcement (SC-003)
- [ ] Start `Server.WebAPI` in test host
- [ ] Assert `GET /hubs/worker-transit` (no token) → HTTP 401
- [ ] Assert `GET /hubs/worker-transit` (valid token, no `TransitData.Publish` role) → HTTP 403
- [ ] Assert `GET /hubs/transit` (no token) → HTTP 101 Switching Protocols

### 7.12 — Integration test: per-envelope failure isolation (EC-001)
- [ ] Send a `ReceiveBatch` containing one valid `VehiclePositionUpdatedEvent` envelope followed by a malformed envelope
- [ ] Assert the valid envelope is dispatched (`OnVehiclePositionUpdated` fires)
- [ ] Assert the `TransitHubClient` connection remains open after the malformed envelope
- [ ] Assert an error is logged for the malformed envelope
