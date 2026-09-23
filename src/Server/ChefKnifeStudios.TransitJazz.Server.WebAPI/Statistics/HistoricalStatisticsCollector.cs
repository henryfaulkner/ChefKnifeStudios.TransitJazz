using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

/// <summary>Disabled-by-default hosted collector for bounded historical and recurring samples.</summary>
public sealed class HistoricalStatisticsCollector(
    IOptions<HistoricalStatisticsOptions> options,
    IHistoricalStatisticsSource source,
    ICityMinuteStatisticsStore store,
    ILogger<HistoricalStatisticsCollector> logger) : BackgroundService
{
    public async Task<StatisticsCollectionReport> CollectAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var currentOptions = options.Value;
        currentOptions.Validate();
        if (!currentOptions.Enabled)
            return StatisticsCollectionReport.Disabled(currentOptions.SourceDefinitionVersion, currentOptions.Cities);

        if (currentOptions.InitialBackfill)
        {
            if (currentOptions.BackfillStartUtc is null || currentOptions.BackfillEndUtc is null)
                return FailureReport(currentOptions, "initial-backfill-range-missing");
            return await CollectRangeAsync(currentOptions, currentOptions.BackfillStartUtc.Value, currentOptions.BackfillEndUtc.Value, cancellationToken);
        }

        var closedMinute = AlignToMinute(nowUtc).AddMinutes(-currentOptions.IngestionGraceMinutes);
        var start = closedMinute.AddMinutes(-currentOptions.OverlapMinutes);
        return await CollectRangeAsync(currentOptions, start, closedMinute, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentOptions = options.Value;
        if (!currentOptions.Enabled)
            return;

        currentOptions.Validate();
        if (currentOptions.InitialBackfill)
            await CollectAsync(DateTime.UtcNow, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(HistoricalStatisticsOptions.FixedCollectionIntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var report = await CollectRecurringAsync(DateTime.UtcNow, stoppingToken);
            if (!report.Succeeded)
                logger.LogWarning("Historical statistics collection did not succeed: created={Created}, discrepant={Discrepant}, failures={Failures}",
                    report.Created, report.Discrepant, report.Failures.Count);
        }
    }

    async Task<StatisticsCollectionReport> CollectRecurringAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var currentOptions = options.Value;
        var closedMinute = AlignToMinute(nowUtc).AddMinutes(-currentOptions.IngestionGraceMinutes);
        return await CollectRangeAsync(currentOptions, closedMinute.AddMinutes(-currentOptions.OverlapMinutes), closedMinute, cancellationToken);
    }

    async Task<StatisticsCollectionReport> CollectRangeAsync(
        HistoricalStatisticsOptions currentOptions,
        DateTime fromMinuteUtc,
        DateTime toMinuteUtc,
        CancellationToken cancellationToken)
    {
        var aggregate = new ReportAccumulator(currentOptions, fromMinuteUtc, toMinuteUtc);
        var chunkStart = fromMinuteUtc;
        while (chunkStart <= toMinuteUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkEnd = Min(chunkStart.AddHours(HistoricalStatisticsOptions.InitialChunkHours).AddMinutes(-1), toMinuteUtc);
            StatisticsSourceResult result;
            try
            {
                result = await source.QueryAsync(chunkStart, chunkEnd, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                aggregate.Failures.Add("metrics-source-failure");
                break;
            }

            aggregate.AddWarnings(result.Warnings);
            aggregate.RecordReturned(result.ReturnedStartUtc, result.ReturnedEndUtc);
            var rows = BuildGrid(currentOptions, chunkStart, chunkEnd, result.Rows, result.Warnings.Count > 0);
            aggregate.AddRows(rows);

            if (!currentOptions.DryRun)
            {
                try
                {
                    var write = await store.UpsertAsync(rows, cancellationToken);
                    aggregate.Created += write.Created;
                    aggregate.Unchanged += write.Unchanged;
                    aggregate.Filled += write.Filled;
                    aggregate.Discrepant += write.Discrepant;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    aggregate.Failures.Add("statistics-store-failure");
                    break;
                }
            }

            chunkStart = chunkEnd.AddMinutes(1);
        }

        if (currentOptions.DryRun)
            aggregate.Created = aggregate.ExpectedRows;
        return aggregate.Build();
    }

    static IReadOnlyList<CityMinuteStatistic> BuildGrid(
        HistoricalStatisticsOptions options,
        DateTime fromMinuteUtc,
        DateTime toMinuteUtc,
        IReadOnlyList<CityMinuteStatistic> sourceRows,
        bool hasWarnings)
    {
        var byKey = sourceRows.ToDictionary(row => (row.CitySlug, row.StatMinuteUtc));
        var rows = new List<CityMinuteStatistic>();
        for (var minute = fromMinuteUtc; minute <= toMinuteUtc; minute = minute.AddMinutes(1))
        {
            foreach (var city in options.Cities)
            {
                if (byKey.TryGetValue((city, minute), out var sourceRow))
                {
                    var row = sourceRow.Clone();
                    if (!string.Equals(row.SourceDefinitionVersion, options.SourceDefinitionVersion, StringComparison.Ordinal))
                        row.CollectionStatus = CollectionStatus.Discrepant;
                    if (hasWarnings && row.CollectionStatus == CollectionStatus.Complete)
                        row.CollectionStatus = CollectionStatus.Partial;
                    rows.Add(row);
                }
                else
                {
                    rows.Add(new CityMinuteStatistic
                    {
                        CitySlug = city,
                        StatMinuteUtc = minute,
                        SourceDefinitionVersion = options.SourceDefinitionVersion,
                        CollectionStatus = CollectionStatus.NoData,
                    });
                }
            }
        }

        return rows;
    }

    static DateTime AlignToMinute(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
    static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;

    static StatisticsCollectionReport FailureReport(HistoricalStatisticsOptions options, string failure) => new(
        options.SourceDefinitionVersion, options.DryRun, options.BackfillStartUtc, options.BackfillEndUtc,
        null, null, options.Cities, 0, 0, 0, 0, 0, 0, 0, [], [], [failure]);

    sealed class ReportAccumulator(HistoricalStatisticsOptions options, DateTime requestedStartUtc, DateTime requestedEndUtc)
    {
        public int Created;
        public int Unchanged;
        public int Filled;
        public int Discrepant;
        public int ExpectedRows;
        public readonly List<DateTime> Gaps = [];
        public readonly List<string> Warnings = [];
        public readonly List<string> Failures = [];
        public readonly List<DateTime> ReturnedMinutes = [];
        public int CompleteRows;
        public int PartialRows;
        public int NoDataRows;
        DateTime? returnedStart;
        DateTime? returnedEnd;

        public void AddRows(IReadOnlyList<CityMinuteStatistic> rows)
        {
            ExpectedRows += rows.Count;
            foreach (var row in rows)
            {
                switch (row.CollectionStatus)
                {
                    case CollectionStatus.Complete: CompleteRows++; break;
                    case CollectionStatus.Partial: PartialRows++; break;
                    case CollectionStatus.NoData: NoDataRows++; Gaps.Add(row.StatMinuteUtc); break;
                    case CollectionStatus.Discrepant: Discrepant++; Failures.Add("source-definition-discrepancy"); break;
                }
            }
        }

        public void AddWarnings(IEnumerable<string> warnings)
        {
            foreach (var warning in warnings)
                if (!Warnings.Contains(warning, StringComparer.Ordinal))
                    Warnings.Add(warning);
        }

        public void RecordReturned(DateTime start, DateTime end)
        {
            returnedStart = returnedStart is null || start < returnedStart ? start : returnedStart;
            returnedEnd = returnedEnd is null || end > returnedEnd ? end : returnedEnd;
        }

        public StatisticsCollectionReport Build() => new(
            options.SourceDefinitionVersion, options.DryRun, requestedStartUtc, requestedEndUtc,
            returnedStart, returnedEnd, options.Cities, Created, Unchanged, Filled, Discrepant,
            CompleteRows, PartialRows, NoDataRows, Gaps.Distinct().Order().ToArray(), Warnings, Failures);
    }
}
