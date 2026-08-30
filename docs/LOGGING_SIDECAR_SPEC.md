> **RETIRED by specs/055-remove-parquet-sidecar** — the Parquet telemetry sidecar
> this document specifies was removed. Centralized structured logging (feature 054)
> and Grafana metrics are the two live observability surfaces. Kept as a historical
> record of what was built; do not implement from it.

# Logging Sidecar Service Overview

The Event and EventData model separation should be very, very similar to the one used for SignalR events within ChefKnifeStudios.TransitJazz.

Create this as a sidecar service within the ChefKnifeStudios.TransitJazz.Server.TransitDataWorker project. Consolidate all added files for logging to a Logging directory.

## Definitions

### EventReceivedEventHandler

```csharp
public delegate Task EventReceivedEventHandler(object sender, IEventArgs e);
```

### IEventNotificationService

```csharp
public interface IEventNotificationService
{
    event EventReceivedEventHandler? EventReceived;
    void PostEvent(object sender, IEventArgs args);
}
```

### IEventArgs

```csharp
public interface IEventArgs { }
```

## EventNotificationService

```csharp
public class EventNotificationService : IEventNotificationService
{
    public event EventReceivedEventHandler? EventReceived;
    
    public void PostEvent(object sender, IEventArgs args)
    {
        EventReceived?.Invoke(sender, args);
    }
}
```

## LogEventArgs

```csharp
public class LogEventArgs : IEventArgs { }
```

## LogEventWorker

```csharp
public class LogEventWorker : IDisposable
{
    readonly IEventNotificationService _eventNotificationService;
    readonly Channel<IEventArgs> _channel;
    readonly ILoggingService _sink;

    public LogEventWorker(IEventNotificationService eventNotificationService, ILoggingService sink)
    {
        _eventNotificationService = eventNotificationService;
        _sink = sink;
        _channel = Channel.CreateBounded<IEventArgs>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _eventNotificationService.EventReceived += HandleEventReceived;
    }

    public void Dispose()
    {
        _logEventNotificationService.EventReceived -= HandleEventReceived;
    }

    Task HandleEventReceived(object sender, IEventArgs e)
    {
        switch (e)
        {
            case LogEventArgs logEventArgs:
                _channel.Writer.TryWrite(e);
                break;
        }
        return Task.CompletedTask;
    }

    async Task ConsumeAsync()
    {
        // This loop runs forever in the background
        await foreach (var message in _channel.Reader.ReadAllAsync())
        {
            try { await sink.ProcessAndLogAsync(message); }
            catch { Console.WriteLine($"Logging failed: {ex.Message}"); }
        }
    }
}
```

## Event Schemas

Each schema should have a Decision enum, which is logged as the string nameof().

### Snap

**Route data**
- Route number
- Route position

**Bus data**
- Bus number
- Bus position
- Bus speed
- Bus bearing

**Position delta**
- Timestamp
- CycleId

### Lerp

**Prior Route data**
- Route data

**Prior Bus data**
- Bus data

**Bus Delta data**
- Position delta
- Speed delta
- Bearing delta
- Time delta
- CycleId

### Cycle

- CycleId
- CycleStartTime
- CycleEndTime
- CycleExecutionSeconds
- BusesProcessed
- BusesMoved
- BusesUnchanged
- BusesStationary
- BusesStale
- BusesSkippedNoRouteId
- BusesSkippedUnknownRoute
- FeedHeaderTs
- DuplicateFeed
- LastUpdateCacheSize
- VehicleStateCacheSize

## Telemetry Metrics

Post telemetry about the logger cache size and other metrics to ensure the logger is running well in production. Decide whether this data should be written to the Cycle event or created as a separate event.
