using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

/// <summary>Bounded, single-reader persistence queue for immutable route-hour envelopes.</summary>
public sealed class RouteHourStatisticsWriter : BackgroundService, IRouteHourStatisticsSink
{
    readonly Channel<FinalizedRouteHourStatisticsBatch> _channel;
    readonly ICityRouteHourStatisticsStore _store;
    readonly RouteHourHistoryOptions _options;
    readonly ILogger<RouteHourStatisticsWriter> _logger;
    readonly CancellationTokenSource _drainCancellation = new();
    int _disposed;

    public RouteHourStatisticsWriter(ICityRouteHourStatisticsStore store, RouteHourHistoryOptions options,
        ILogger<RouteHourStatisticsWriter> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _channel = Channel.CreateBounded<FinalizedRouteHourStatisticsBatch>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public bool TryEnqueue(FinalizedRouteHourStatisticsBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Validate(_options.MaxRoutesPerCityHour);
        return _channel.Writer.TryWrite(batch);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Keep reading under a separate token so shutdown can close admission and drain first.
        await foreach (var batch in _channel.Reader.ReadAllAsync(_drainCancellation.Token))
            await WriteBatchAsync(batch, _drainCancellation.Token);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.ShutdownDrainSeconds));
        try
        {
            if (ExecuteTask is { } executeTask)
                await executeTask.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            _logger.LogWarning("Route-hour statistics writer drain ended at its configured deadline.");
            _drainCancellation.Cancel();
        }
        await base.StopAsync(deadline.Token);
    }

    async Task WriteBatchAsync(FinalizedRouteHourStatisticsBatch batch, CancellationToken drainToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(drainToken);
            attemptDeadline.CancelAfter(TimeSpan.FromSeconds(_options.WriteAttemptTimeoutSeconds));
            try
            {
                var entities = batch.Rows.Select(ToEntity).ToArray();
                var report = await _store.WriteAsync(entities, _options.MaxRowsPerCommand,
                    _options.CommandTimeoutSeconds, _options.MaxRoutesPerCityHour, attemptDeadline.Token);
                _logger.LogInformation("Route-hour envelope write completed for {City}; hour={HourStartUtc}, outcome={Outcome}, rows={Rows}, attempt={Attempt}.",
                    batch.CitySlug, batch.HourStartUtc, report.Outcome, report.Rows, attempt);
                return;
            }
            catch (OperationCanceledException) when (!drainToken.IsCancellationRequested && attempt < _options.MaxWriteAttempts)
            {
                _logger.LogWarning("Route-hour envelope write timed out for {City}; hour={HourStartUtc}, rows={Rows}, attempt={Attempt}.",
                    batch.CitySlug, batch.HourStartUtc, batch.RowCount, attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt), drainToken);
            }
            catch (Exception exception) when (attempt < _options.MaxWriteAttempts && IsTransient(exception))
            {
                _logger.LogWarning("Route-hour envelope write failed transiently for {City}; hour={HourStartUtc}, rows={Rows}, attempt={Attempt}, exceptionType={ExceptionType}.",
                    batch.CitySlug, batch.HourStartUtc, batch.RowCount, attempt, exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(attempt), drainToken);
            }
            catch (Exception exception)
            {
                _logger.LogError("Route-hour envelope was not persisted for {City}; hour={HourStartUtc}, rows={Rows}, attempts={Attempts}, exceptionType={ExceptionType}.",
                    batch.CitySlug, batch.HourStartUtc, batch.RowCount, attempt, exception.GetType().Name);
                return;
            }
        }
    }

    static bool IsTransient(Exception exception) => exception is TimeoutException
        || exception is NpgsqlException { IsTransient: true }
        || exception.InnerException is NpgsqlException { IsTransient: true };

    static CityRouteHourStatistic ToEntity(RouteHourStatisticRow row) => new()
    {
        CitySlug = row.CitySlug,
        RouteJoinKey = row.RouteJoinKey,
        HourStartUtc = row.HourStartUtc,
        RouteShortName = row.RouteShortName,
        StaticRouteId = row.StaticRouteId,
        Category = row.Category,
        RouteCatalogFingerprint = row.RouteCatalogFingerprint,
        CatalogChanged = row.CatalogChanged,
        DefinitionVersion = row.DefinitionVersion,
        CaptureRunId = row.CaptureRunId,
        CollectionStatus = row.CollectionStatus switch
        {
            RouteHourCoverageStatus.Complete => RouteHourCollectionStatus.Complete,
            RouteHourCoverageStatus.Partial => RouteHourCollectionStatus.Partial,
            RouteHourCoverageStatus.NoData => RouteHourCollectionStatus.NoData,
            _ => throw new ArgumentOutOfRangeException(nameof(row.CollectionStatus)),
        },
        HasConflict = row.HasConflict,
        IncompleteReasons = row.IncompleteReasons.ToArray(),
        HealthyCadenceLimitSeconds = row.HealthyCadenceLimitSeconds,
        ObservedCycleCount = row.ObservedCycleCount,
        ValidActiveSampleCount = row.ValidActiveSampleCount,
        ValidPublishCycleCount = row.ValidPublishCycleCount,
        FailedCycleCount = row.FailedCycleCount,
        FirstCycleUtc = row.FirstCycleUtc,
        LastCycleUtc = row.LastCycleUtc,
        MaxObservationGapSeconds = row.MaxObservationGapSeconds,
        StartBoundaryOk = row.StartBoundaryOk,
        EndBoundaryOk = row.EndBoundaryOk,
        ActiveVehicleCountSum = row.ActiveVehicleCountSum,
        PeakActiveVehicleCount = row.PeakActiveVehicleCount,
        DistinctActiveVehicleCount = row.DistinctActiveVehicleCount,
        VehicleObservationsProcessedCount = row.VehicleObservationsProcessedCount,
        StaleObservationsCount = row.StaleObservationsCount,
        DistanceMetersSum = row.DistanceMetersSum,
        DistanceIntervalCount = row.DistanceIntervalCount,
        DistanceRejectedCount = row.DistanceRejectedCount,
        CrossingsDetectedCount = row.CrossingsDetectedCount,
        CrossingsPublishedCount = row.CrossingsPublishedCount,
        CrossingsSuppressedFirstSeen = row.CrossingsSuppressedFirstSeen,
        CrossingsSuppressedDeltaLeqZero = row.CrossingsSuppressedDeltaLeqZero,
        CrossingsSuppressedTeleport = row.CrossingsSuppressedTeleport,
        CrossingsSuppressedTransfer = row.CrossingsSuppressedTransfer,
    };

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _drainCancellation.Cancel();
        _drainCancellation.Dispose();
        base.Dispose();
    }
}
