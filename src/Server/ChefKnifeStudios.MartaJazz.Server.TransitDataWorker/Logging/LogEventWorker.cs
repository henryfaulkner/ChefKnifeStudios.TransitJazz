using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Hosted service that owns the bounded telemetry channel. Subscribes to
/// <see cref="IEventNotificationService.EventReceived"/>, drains events into
/// <see cref="ILoggingService"/>, and flushes on a periodic timer plus best-effort on stop.
/// </summary>
public sealed class LogEventWorker : IHostedService
{
    readonly IEventNotificationService _notifications;
    readonly ILoggingService _sink;
    readonly ILogger<LogEventWorker> _logger;
    readonly LoggingOptions _options;
    readonly Channel<IEventArgs> _channel;

    long _droppedRecords;
    Task? _consumerTask;
    CancellationTokenSource? _cts;

    /// <summary>Estimated number of events waiting in the channel.</summary>
    public int BufferOccupancy => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <summary>Total events dropped by DropWrite overflow since process start.</summary>
    public long DroppedRecords => Interlocked.Read(ref _droppedRecords);

    public LogEventWorker(
        IEventNotificationService notifications,
        ILoggingService sink,
        IOptions<LoggingOptions> options,
        ILogger<LogEventWorker> logger)
    {
        _notifications = notifications;
        _sink = sink;
        _options = options.Value;
        _logger = logger;

        _channel = Channel.CreateBounded<IEventArgs>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Logging sidecar disabled (Logging:Telemetry:Enabled=false).");
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _notifications.EventReceived += HandleEventReceived;
        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);

        _logger.LogInformation("Logging sidecar started. ChannelCapacity={Cap}, FlushInterval={Interval}s",
            _options.ChannelCapacity, _options.FlushIntervalSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;

        _notifications.EventReceived -= HandleEventReceived;
        _channel.Writer.TryComplete();

        if (_consumerTask != null)
        {
            try { await _consumerTask.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                _logger.LogWarning("Logging sidecar consumer did not drain within timeout; flushing what we have.");
            }
        }

        // Best-effort final flush
        try { await _sink.FlushAsync(cancellationToken); }
        catch (Exception ex) { _logger.LogError(ex, "Best-effort final flush failed on shutdown."); }

        _cts?.Cancel();
        _cts?.Dispose();
        _logger.LogInformation("Logging sidecar stopped. TotalDropped={Dropped}", DroppedRecords);
    }

    void HandleEventReceived(object sender, IEventArgs e)
    {
        // DropWrite mode silently drops incoming items when full and TryWrite returns true.
        // Count drops by checking channel occupancy before writing.
        bool wasFull = _channel.Reader.CanCount && _channel.Reader.Count >= _options.ChannelCapacity;
        _channel.Writer.TryWrite(e);

        if (wasFull)
        {
            long dropped = Interlocked.Increment(ref _droppedRecords);
            if (dropped % 100 == 1)
                _logger.LogWarning("Sidecar channel full — event dropped. TotalDropped={Dropped}", dropped);
        }
    }

    async Task ConsumeAsync(CancellationToken ct)
    {
        var flushInterval = TimeSpan.FromSeconds(_options.FlushIntervalSeconds);
        using var timer = new PeriodicTimer(flushInterval);

        var flushTask = FlushOnTimerAsync(timer, ct);

        try
        {
            await foreach (var e in _channel.Reader.ReadAllAsync(ct))
            {
                try { _sink.Accumulate(e); }
                catch (Exception ex) { _logger.LogError(ex, "Sidecar Accumulate error — event skipped."); }
            }
        }
        catch (OperationCanceledException) { }

        // Drain remaining items after channel is completed
        while (_channel.Reader.TryRead(out var e))
        {
            try { _sink.Accumulate(e); }
            catch (Exception ex) { _logger.LogWarning(ex, "LogEventWorker: unexpected exception."); }
        }

        await CancelTimerGracefully(flushTask);
    }

    async Task FlushOnTimerAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                LogSidecarHealth();
                try { await _sink.FlushAsync(ct); }
                catch (Exception ex) { _logger.LogError(ex, "Periodic flush error."); }
            }
        }
        catch (OperationCanceledException) { }
    }

    static async Task CancelTimerGracefully(Task flushTask)
    {
        try { await flushTask; }
        catch (OperationCanceledException) { }
    }

    void LogSidecarHealth()
    {
        _logger.LogInformation(
            "Sidecar health: BufferOccupancy={Occupancy}, DroppedRecords={Dropped}, PersistFailures={Failures}",
            BufferOccupancy, DroppedRecords, _sink.PersistFailures);
    }
}
