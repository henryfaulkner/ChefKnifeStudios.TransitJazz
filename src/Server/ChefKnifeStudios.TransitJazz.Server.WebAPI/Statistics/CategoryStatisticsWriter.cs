using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed class CategoryStatisticsWriter : BackgroundService, ICategoryStatisticsSink
{
    readonly Channel<FinalizedCategoryStatisticsBatch> _channel;
    readonly CityCategoryStatisticsStore _store;
    readonly CityCategoryInsightsOptions _options;
    readonly ILogger<CategoryStatisticsWriter> _logger;
    readonly CancellationTokenSource _drainCancellation = new();

    public CategoryStatisticsWriter(CityCategoryStatisticsStore store, CityCategoryInsightsOptions options,
        ILogger<CategoryStatisticsWriter> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _channel = Channel.CreateBounded<FinalizedCategoryStatisticsBatch>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public bool TryEnqueue(FinalizedCategoryStatisticsBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.RowCount == 0) return true;
        batch.Validate();
        return _channel.Writer.TryWrite(batch);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // BackgroundService.StopAsync cancels its token as soon as shutdown begins. Read
        // from a separate token so StopAsync can close admission and drain before canceling.
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
            _logger.LogWarning("Category statistics writer drain ended at its configured deadline.");
            _drainCancellation.Cancel();
        }
        // A dependency may ignore cancellation. The host must still finish stopping at
        // this deadline; passing the expired token prevents an unbounded second wait.
        await base.StopAsync(deadline.Token);
    }

    async Task WriteBatchAsync(FinalizedCategoryStatisticsBatch batch, CancellationToken cancellationToken)
    {
        var rows = batch.Minutes.Select(x => (IsHour: false, Row: x)).Concat(batch.Hours.Select(x => (IsHour: true, Row: x)))
            .OrderBy(x => x.IsHour).ThenBy(x => x.Row.WindowStartUtc).ThenBy(x => x.Row.Category, StringComparer.Ordinal).ToArray();
        for (var offset = 0; offset < rows.Length; offset += _options.MaxBatchRows)
        {
            var slice = rows.Skip(offset).Take(_options.MaxBatchRows).ToArray();
            var minutes = slice.Where(x => !x.IsHour).Select(x => ToMinute(x.Row)).ToArray();
            var hours = slice.Where(x => x.IsHour).Select(x => ToHour(x.Row)).ToArray();
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var report = await _store.WriteAsync(minutes, hours, _options.CommandTimeoutSeconds, cancellationToken);
                    if (report.BackingMinutesUnavailable > 0 && attempt < _options.MaxWriteAttempts)
                    {
                        _logger.LogWarning("Category statistics complete-hour candidates are waiting for durable minutes for {City}; attempt={Attempt}, rows={Rows}, inserted={Inserted}, unchanged={Unchanged}, conflicts={Conflicts}, unavailableHours={UnavailableHours}.",
                            batch.CitySlug, attempt, slice.Length, report.Inserted, report.Unchanged, report.Conflicts, report.BackingMinutesUnavailable);
                        await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                        continue;
                    }
                    if (report.BackingMinutesUnavailable > 0)
                        _logger.LogWarning("Category statistics complete-hour candidates remain unverified for {City}; attempts={Attempts}, unavailableHours={UnavailableHours}.",
                            batch.CitySlug, attempt, report.BackingMinutesUnavailable);
                    _logger.LogInformation("Category statistics write completed for {City}; inserted={Inserted}, unchanged={Unchanged}, conflicts={Conflicts}, backingMinutesUnavailable={BackingMinutesUnavailable}.",
                        batch.CitySlug, report.Inserted, report.Unchanged, report.Conflicts, report.BackingMinutesUnavailable);
                    break;
                }
                catch (Exception ex) when (attempt < _options.MaxWriteAttempts && IsTransient(ex))
                {
                    _logger.LogWarning("Category statistics write attempt failed for {City}; attempt={Attempt}, rows={Rows}, exceptionType={ExceptionType}.",
                        batch.CitySlug, attempt, slice.Length, ex.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Category statistics rows were not persisted for {City}; attempts={Attempts}, rows={Rows}, exceptionType={ExceptionType}.",
                        batch.CitySlug, attempt, slice.Length, ex.GetType().Name);
                    break;
                }
            }
        }
    }

    static bool IsTransient(Exception exception) => exception is TimeoutException
        || exception is Npgsql.NpgsqlException { IsTransient: true }
        || exception.InnerException is Npgsql.NpgsqlException { IsTransient: true };

    static CityCategoryMinuteStatistic ToMinute(CategoryStatisticRow x) => new()
    {
        CitySlug = x.CitySlug, Category = x.Category, StatMinuteUtc = x.WindowStartUtc, DefinitionVersion = x.DefinitionVersion,
        CollectionStatus = Map(x.Status), HealthyCadenceLimitSeconds = x.HealthyCadenceLimitSeconds,
        ObservedCycleCount = x.ObservedCycleCount, ValidActiveSampleCount = x.ValidActiveSampleCount,
        ValidPublishCycleCount = x.ValidPublishCycleCount, FailedCycleCount = x.FailedCycleCount,
        FirstCycleUtc = x.FirstCycleUtc, LastCycleUtc = x.LastCycleUtc, MaxObservationGapSeconds = x.MaxObservationGapSeconds,
        ActiveVehicleCountSum = x.ActiveVehicleCountSum, DistanceMetersSum = x.DistanceMetersSum,
        DistanceIntervalCount = x.DistanceIntervalCount, DistanceRejectedCount = x.DistanceRejectedCount,
        CrossingsPublishedCount = x.CrossingsPublishedCount,
    };

    static CityCategoryHourStatistic ToHour(CategoryStatisticRow x) => new()
    {
        CitySlug = x.CitySlug, Category = x.Category, HourStartUtc = x.WindowStartUtc, DefinitionVersion = x.DefinitionVersion,
        CollectionStatus = Map(x.Status), HealthyCadenceLimitSeconds = x.HealthyCadenceLimitSeconds,
        ObservedCycleCount = x.ObservedCycleCount, ValidActiveSampleCount = x.ValidActiveSampleCount,
        ValidPublishCycleCount = x.ValidPublishCycleCount, FailedCycleCount = x.FailedCycleCount,
        FirstCycleUtc = x.FirstCycleUtc, LastCycleUtc = x.LastCycleUtc, MaxObservationGapSeconds = x.MaxObservationGapSeconds,
        ActiveVehicleCountSum = x.ActiveVehicleCountSum, DistanceMetersSum = x.DistanceMetersSum,
        DistanceIntervalCount = x.DistanceIntervalCount, DistanceRejectedCount = x.DistanceRejectedCount,
        CrossingsPublishedCount = x.CrossingsPublishedCount, DistinctActiveVehicleCount = x.DistinctActiveVehicleCount,
        CoveredMinutes = x.CoveredMinutes,
    };

    static CategoryCollectionStatus Map(CategoryCoverageStatus status) => status switch
    {
        CategoryCoverageStatus.Complete => CategoryCollectionStatus.Complete,
        CategoryCoverageStatus.Partial => CategoryCollectionStatus.Partial,
        CategoryCoverageStatus.NoData => CategoryCollectionStatus.NoData,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public override void Dispose()
    {
        _drainCancellation.Cancel();
        _drainCancellation.Dispose();
        base.Dispose();
    }
}
