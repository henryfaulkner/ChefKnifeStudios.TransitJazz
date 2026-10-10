using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class HistoricalStatisticsCollectorTests
{
    [Fact]
    public async Task DisabledCollectorDoesNotQueryOrWrite()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var collector = CreateCollector(ValidOptions(enabled: false), source, store);

        var report = await collector.CollectAsync(new DateTime(2026, 9, 20, 15, 10, 31, DateTimeKind.Utc));

        Assert.Empty(source.Calls);
        Assert.Equal(0, store.WriteCalls);
        Assert.False(report.Succeeded is false);
    }

    [Fact]
    public async Task RecurringCollectionUsesTwoMinuteGraceAndFiveMinuteOverlapForEveryCity()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var options = ValidOptions(enabled: true);
        options.Cities = ["atlanta", "boston"];
        options.DryRun = true;
        var collector = CreateCollector(options, source, store);

        var report = await collector.CollectAsync(new DateTime(2026, 9, 20, 15, 10, 31, DateTimeKind.Utc));

        Assert.Equal((new DateTime(2026, 9, 20, 15, 3, 0, DateTimeKind.Utc), new DateTime(2026, 9, 20, 15, 8, 0, DateTimeKind.Utc)), Assert.Single(source.Calls));
        Assert.Equal(12, report.Created);
        Assert.Equal(6, report.PartialRows);
        Assert.Equal(6, report.NoDataRows);
        Assert.Equal(0, store.WriteCalls);
    }

    [Fact]
    public async Task AppliedSevenCityCollectionWritesOnlyTheRecurringWindow()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var options = ValidOptions(enabled: true);
        options.Cities = ["atlanta", "washington-dc", "boston", "new-york-city", "toronto", "philadelphia", "denver"];
        options.DryRun = false;
        var collector = CreateCollector(options, source, store);

        var report = await collector.CollectAsync(new DateTime(2026, 9, 20, 15, 10, 31, DateTimeKind.Utc));

        Assert.Single(source.Calls);
        Assert.Equal(1, store.WriteCalls);
        Assert.Equal(42, store.WrittenBatchSizes.Sum());
        Assert.All(store.WrittenBatchSizes, size => Assert.InRange(size, 1, CityMinuteStatisticsStore.MaxBatchRows));
        Assert.Equal(42, report.Created);
    }

    [Fact]
    public async Task SourceFailureLogsFieldAndReasonWithoutSensitiveExceptionMessage()
    {
        var logger = new CaptureLogger();
        var source = new FakeSource
        {
            Failure = new StatisticsSourceException(
                "https://metrics.example/private?token=private-token-value",
                "metrics-source-duplicate-sample",
                "last_cycled_unix_seconds"),
        };
        var collector = CreateCollector(ValidOptions(enabled: true), source, new FakeStore(), logger);

        var report = await collector.CollectAsync(new DateTime(2026, 9, 20, 15, 10, 31, DateTimeKind.Utc));

        Assert.Contains("metrics-source-duplicate-sample", report.Failures);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("last_cycled_unix_seconds", entry.Message, StringComparison.Ordinal);
        Assert.Contains("metrics-source-duplicate-sample", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token-value", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("metrics.example", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task UnexpectedSourceFailureLogsExceptionTypeWithoutSensitiveMessage()
    {
        var logger = new CaptureLogger();
        var source = new FakeSource { Failure = new HttpRequestException("private-token-value") };
        var collector = CreateCollector(ValidOptions(enabled: true), source, new FakeStore(), logger);

        var report = await collector.CollectAsync(new DateTime(2026, 9, 20, 15, 10, 31, DateTimeKind.Utc));

        Assert.Contains("metrics-source-failure", report.Failures);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("HttpRequestException", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token-value", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
    }

    static HistoricalStatisticsCollector CreateCollector(
        HistoricalStatisticsOptions options,
        FakeSource source,
        FakeStore store,
        ILogger<HistoricalStatisticsCollector>? logger = null) =>
        new(Options.Create(options), source, store, logger ?? NullLogger<HistoricalStatisticsCollector>.Instance);

    static HistoricalStatisticsOptions ValidOptions(bool enabled) => new()
    {
        Enabled = enabled,
        SourceEndpoint = enabled ? "https://metrics.example/api/v1/query_range" : string.Empty,
        ReaderAuthorization = enabled ? "Basic reader" : string.Empty,
        Cities = ["atlanta"],
    };

    sealed class FakeSource : IHistoricalStatisticsSource
    {
        public List<(DateTime Start, DateTime End)> Calls { get; } = [];
        public Exception? Failure { get; init; }

        public Task<StatisticsSourceResult> QueryAsync(DateTime fromMinuteUtc, DateTime toMinuteUtc, CancellationToken cancellationToken = default)
        {
            Calls.Add((fromMinuteUtc, toMinuteUtc));
            if (Failure is not null)
                throw Failure;
            var rows = new List<CityMinuteStatistic>();
            for (var minute = fromMinuteUtc; minute <= toMinuteUtc; minute = minute.AddMinutes(1))
            {
                rows.Add(new CityMinuteStatistic
                {
                    CitySlug = "atlanta",
                    StatMinuteUtc = minute,
                    CollectionStatus = CollectionStatus.Partial,
                    VehiclesProcessed = 0,
                });
            }
            return Task.FromResult(new StatisticsSourceResult(rows, fromMinuteUtc, toMinuteUtc, []));
        }
    }

    sealed class FakeStore : ICityMinuteStatisticsStore
    {
        public int WriteCalls { get; private set; }
        public List<int> WrittenBatchSizes { get; } = [];

        public Task<StatisticsWriteReport> UpsertAsync(IReadOnlyCollection<CityMinuteStatistic> rows, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            WrittenBatchSizes.Add(rows.Count);
            return Task.FromResult(new StatisticsWriteReport(rows.Count, 0, 0, 0));
        }

        public Task<IReadOnlyList<CityMinuteStatistic>> ReadAsync(string citySlug, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CityMinuteStatistic>>([]);
    }

    sealed class CaptureLogger : ILogger<HistoricalStatisticsCollector>
    {
        public List<(string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception));
    }
}
