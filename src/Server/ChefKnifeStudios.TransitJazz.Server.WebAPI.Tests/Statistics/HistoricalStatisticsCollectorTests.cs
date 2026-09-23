using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
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
    public async Task InitialBackfillUsesSequentialSixHourChunksAndDryRunSuppressesWrites()
    {
        var source = new FakeSource();
        var store = new FakeStore();
        var options = ValidOptions(enabled: true);
        options.InitialBackfill = true;
        options.DryRun = true;
        options.BackfillStartUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        options.BackfillEndUtc = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var collector = CreateCollector(options, source, store);

        var report = await collector.CollectAsync(options.BackfillEndUtc.Value);

        Assert.Equal(3, source.Calls.Count);
        Assert.Equal((options.BackfillStartUtc.Value, new DateTime(2026, 9, 20, 5, 59, 0, DateTimeKind.Utc)), source.Calls[0]);
        Assert.Equal((new DateTime(2026, 9, 20, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 20, 11, 59, 0, DateTimeKind.Utc)), source.Calls[1]);
        Assert.Equal((new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), options.BackfillEndUtc.Value), source.Calls[2]);
        Assert.Equal(0, store.WriteCalls);
        Assert.Equal(721, report.Created);
        Assert.Equal(options.BackfillStartUtc, report.RequestedStartUtc);
        Assert.Equal(options.BackfillEndUtc, report.RequestedEndUtc);
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

    static HistoricalStatisticsCollector CreateCollector(HistoricalStatisticsOptions options, FakeSource source, FakeStore store) =>
        new(Options.Create(options), source, store, NullLogger<HistoricalStatisticsCollector>.Instance);

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

        public Task<StatisticsSourceResult> QueryAsync(DateTime fromMinuteUtc, DateTime toMinuteUtc, CancellationToken cancellationToken = default)
        {
            Calls.Add((fromMinuteUtc, toMinuteUtc));
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

        public Task<StatisticsWriteReport> UpsertAsync(IReadOnlyCollection<CityMinuteStatistic> rows, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            return Task.FromResult(new StatisticsWriteReport(rows.Count, 0, 0, 0));
        }

        public Task<IReadOnlyList<CityMinuteStatistic>> ReadAsync(string citySlug, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CityMinuteStatistic>>([]);
    }
}
