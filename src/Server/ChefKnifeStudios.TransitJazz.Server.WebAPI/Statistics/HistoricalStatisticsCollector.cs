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

/// <summary>Disabled-by-default hosted collector for recurring city-minute samples.</summary>
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
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(HistoricalStatisticsOptions.FixedCollectionIntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var report = await CollectAsync(DateTime.UtcNow, stoppingToken);
            LogReport(report);
        }
    }

    void LogReport(StatisticsCollectionReport report) =>
        logger.Log(report.Succeeded ? LogLevel.Information : LogLevel.Warning,
            "Historical statistics collection: succeeded={Succeeded}, dryRun={DryRun}, requestedStartUtc={RequestedStartUtc}, requestedEndUtc={RequestedEndUtc}, created={Created}, unchanged={Unchanged}, filled={Filled}, discrepant={Discrepant}, completeRows={CompleteRows}, partialRows={PartialRows}, noDataRows={NoDataRows}, warningCount={WarningCount}, failureCodes={FailureCodes}",
            report.Succeeded, report.DryRun, report.RequestedStartUtc, report.RequestedEndUtc,
            report.Created, report.Unchanged, report.Filled, report.Discrepant, report.CompleteRows,
            report.PartialRows, report.NoDataRows, report.Warnings.Count, string.Join(",", report.Failures.Distinct()));

    async Task<StatisticsCollectionReport> CollectRangeAsync(
        HistoricalStatisticsOptions currentOptions,
        DateTime fromMinuteUtc,
        DateTime toMinuteUtc,
        CancellationToken cancellationToken)
    {
        var aggregate = new ReportAccumulator(currentOptions, fromMinuteUtc, toMinuteUtc);
        cancellationToken.ThrowIfCancellationRequested();
        StatisticsSourceResult result;
        try
        {
            result = await source.QueryAsync(fromMinuteUtc, toMinuteUtc, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (StatisticsSourceException exception)
        {
            logger.LogWarning(
                "Historical statistics source failure: code={Code}, field={Field}, httpStatusCode={HttpStatusCode}, causeType={CauseType}, exceptionType={ExceptionType}, fromMinuteUtc={FromMinuteUtc}, toMinuteUtc={ToMinuteUtc}, exceptionStackTrace={ExceptionStackTrace}",
                exception.Code, exception.FieldName, exception.HttpStatusCode, exception.CauseType,
                exception.GetType().Name, fromMinuteUtc, toMinuteUtc, exception.StackTrace);
            aggregate.Failures.Add(exception.Code);
            return aggregate.Build();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Historical statistics source failure: code={Code}, exceptionType={ExceptionType}, fromMinuteUtc={FromMinuteUtc}, toMinuteUtc={ToMinuteUtc}, exceptionStackTrace={ExceptionStackTrace}",
                "metrics-source-failure", exception.GetType().Name, fromMinuteUtc, toMinuteUtc, exception.StackTrace);
            aggregate.Failures.Add("metrics-source-failure");
            return aggregate.Build();
        }

        aggregate.AddWarnings(result.Warnings);
        aggregate.RecordReturned(result.ReturnedStartUtc, result.ReturnedEndUtc);
        var rows = BuildGrid(currentOptions, fromMinuteUtc, toMinuteUtc, result.Rows, result.Warnings.Count > 0);
        aggregate.AddRows(rows);

        if (!currentOptions.DryRun)
        {
            try
            {
                foreach (var batch in rows.Chunk(CityMinuteStatisticsStore.MaxBatchRows))
                {
                    var write = await store.UpsertAsync(batch, cancellationToken);
                    aggregate.Created += write.Created;
                    aggregate.Unchanged += write.Unchanged;
                    aggregate.Filled += write.Filled;
                    aggregate.Discrepant += write.Discrepant;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Historical statistics store failure: code={Code}, exceptionType={ExceptionType}, fromMinuteUtc={FromMinuteUtc}, toMinuteUtc={ToMinuteUtc}, exceptionStackTrace={ExceptionStackTrace}",
                    "statistics-store-failure", exception.GetType().Name, fromMinuteUtc, toMinuteUtc, exception.StackTrace);
                aggregate.Failures.Add("statistics-store-failure");
                return aggregate.Build();
            }
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
