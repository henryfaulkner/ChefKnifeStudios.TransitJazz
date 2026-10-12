using System.Collections.Concurrent;
using System.Reflection;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayCacheTests
{
    static readonly DateTimeOffset Start = new(2026, 1, 2, 12, 2, 30, TimeSpan.Zero);

    [Fact]
    public async Task UnfilteredSnapshotHasAbsoluteExpiryAndConfigurationCutoffKeys()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new CountingQuery();
        var options = Options("atlanta");
        var service = new StatsOverlayService(query, options, clock);
        var request = AllRequest();

        var first = await service.ReadAsync(request, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(30));
        var cached = await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(first.GeneratedAtUtc, cached.GeneratedAtUtc);
        Assert.Equal(1, query.Calls);
        Assert.Equal(1, GetCacheCount(service));

        clock.Advance(TimeSpan.FromSeconds(31));
        var expired = await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(2, query.Calls);
        Assert.NotEqual(first.GeneratedAtUtc, expired.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));

        options.Cities[0].HealthyCadenceLimitSeconds = 20;
        await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(3, query.Calls);
        Assert.Equal(1, GetCacheCount(service));

        clock.Set(new DateTimeOffset(2026, 1, 2, 13, 2, 30, TimeSpan.Zero));
        await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(4, query.Calls);

        clock.Set(new DateTimeOffset(2026, 1, 3, 0, 2, 30, TimeSpan.Zero));
        await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(5, query.Calls);
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task CacheStaysWithinThirtyThreeScopesAndAllUsesItsOwnSnapshot()
    {
        var cities = Enumerable.Range(0, 32).Select(index => $"city-{index:00}").ToArray();
        var options = Options(cities);
        var clock = new MutableTimeProvider(Start);
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, options, clock);

        foreach (var city in cities)
            await service.ReadAsync(CityRequest(city), CancellationToken.None);
        await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(33, GetCacheCount(service));
        Assert.True(GetCacheEstimatedBytes(service) <= options.MaximumCacheBytes);

        options.IngestionGraceMinutes = 3;
        foreach (var city in cities)
            await service.ReadAsync(CityRequest(city), CancellationToken.None);
        var all = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(33, GetCacheCount(service));
        Assert.True(GetCacheEstimatedBytes(service) <= options.MaximumCacheBytes);
        Assert.Equal(66, query.Calls);
        Assert.Equal("all", all.Selection.Scope);
        Assert.Contains(query.Scopes, scope => scope == "all");
    }

    [Fact]
    public async Task CoalescedUnfilteredFillDoesNotFollowOneWaitersCancellation()
    {
        var query = new BlockingQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), new MutableTimeProvider(Start));
        using var canceledWaiter = new CancellationTokenSource();
        var first = service.ReadAsync(AllRequest(), canceledWaiter.Token);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = service.ReadAsync(AllRequest(), CancellationToken.None);

        canceledWaiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        query.Release.TrySetResult();
        var response = await second;

        Assert.Equal("all", response.Selection.Scope);
        Assert.Equal(1, query.Calls);
    }

    [Fact]
    public async Task SharedFillDeadlineReleasesItsSlotAndDoesNotCacheFailure()
    {
        var query = new BlockingQuery();
        var options = Options("atlanta");
        options.ReadTimeoutSeconds = 1;
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));

        var failure = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(AllRequest(), CancellationToken.None));

        Assert.Equal("read_timeout", failure.Code);
        Assert.Equal(1, query.Calls);
        Assert.Equal(0, GetCacheCount(service));
        query.Release.TrySetResult();
        var retry = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal("all", retry.Selection.Scope);
        Assert.Equal(2, query.Calls);
    }

    [Fact]
    public async Task DelayedFillDoesNotExtendSnapshotTtlAtCacheAdmission()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new BlockingQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), clock);

        var delayed = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(60));
        query.Release.TrySetResult();
        var first = await delayed;

        Assert.Equal(Start.UtcDateTime, first.GeneratedAtUtc);
        Assert.Equal(0, GetCacheCount(service));

        var refreshed = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(2, query.Calls);
        Assert.NotEqual(first.GeneratedAtUtc, refreshed.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task FilteredScopesDoNotCoalesceAndFifthColdReadIsRejectedImmediately()
    {
        var query = new BlockingQuery(expectedStarts: 4);
        var options = Options("atlanta");
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));
        var requests = Enumerable.Range(0, 4).Select(index => CityRequest("atlanta", [$"route-{index}"])).ToArray();
        var reads = requests.Select(request => service.ReadAsync(request, CancellationToken.None)).ToArray();
        await query.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var busy = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(CityRequest("atlanta", ["route-4"]), CancellationToken.None));
        Assert.Equal("busy", busy.Code);
        Assert.Equal(4, query.Calls);
        query.Release.TrySetResult();
        await Task.WhenAll(reads);
    }

    [Fact]
    public async Task ExpiredCacheFailureDoesNotReturnPreviousSnapshot()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new SwitchableQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), clock);
        var first = await service.ReadAsync(AllRequest(), CancellationToken.None);
        query.Fail = true;
        clock.Advance(TimeSpan.FromSeconds(61));

        var failure = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(AllRequest(), CancellationToken.None));
        Assert.Equal("dependency_unavailable", failure.Code);
        Assert.NotEqual(default, first.GeneratedAtUtc);
        Assert.Equal(2, query.Calls);
    }

    [Fact]
    public async Task CacheAdmissionUsesConservativeObjectWeightRatherThanSerializedLength()
    {
        var query = new LargeCatalogQuery();
        var options = Options("atlanta");
        options.MaximumCacheBytes = 1024 * 1024;
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));

        var first = await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);
        var serializedBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            first, ChefKnifeStudios.TransitJazz.Shared.JsonSettings.DefaultOptions).Length;
        var second = await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);

        Assert.True(serializedBytes < options.MaximumCacheBytes);
        Assert.True(GetCacheEstimatedBytes(service) == 0);
        Assert.Equal(2, query.Calls);
        Assert.NotEmpty(second.RouteOptions);
    }

    static int GetCacheCount(StatsOverlayService service) =>
        ((System.Collections.IDictionary)typeof(StatsOverlayService).GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!).Count;

    static long GetCacheEstimatedBytes(StatsOverlayService service) =>
        (long)typeof(StatsOverlayService).GetField("cachedEstimatedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;

    static StatsOverlayOptions Options(params string[] slugs) => new()
    {
        Enabled = true,
        Cities = slugs.Select(slug => new StatsOverlayCityOptions
        {
            CitySlug = slug, DisplayNameKey = slug, TimeZoneId = "UTC", HealthyCadenceLimitSeconds = 30
        }).ToList()
    };

    static StatsOverlayRequest AllRequest() => new() { Scope = "all", RouteJoinKeys = [] };
    static StatsOverlayRequest CityRequest(string slug, string[]? routes = null) => new()
    {
        Scope = "city", CitySlug = slug, RouteJoinKeys = routes ?? []
    };

    static StatsOverlayQueryResult Empty(StatsOverlayQueryInput input)
    {
        var result = new StatsOverlayQueryResult();
        foreach (var hour in input.Hours)
            result.CohortHours.Add(new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug, HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday, IsLast24 = hour.IsLast24,
                RouteCount = 0, SelectedRouteCount = 0, IsEligible = false, Reason = "missing_cohort"
            });
        return result;
    }

    sealed class CountingQuery : IStatsOverlayQuery
    {
        public int Calls { get; private set; }
        public ConcurrentBag<string> Scopes { get; } = [];
        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            Scopes.Add(input.Scope);
            return Task.FromResult(Empty(input));
        }
    }

    sealed class BlockingQuery(int expectedStarts = 1) : IStatsOverlayQuery
    {
        int calls;
        public int Calls => Volatile.Read(ref calls);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref calls);
            if (count >= expectedStarts) AllStarted.TrySetResult();
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Empty(input);
        }
    }

    sealed class SwitchableQuery : IStatsOverlayQuery
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("private database diagnostic");
            return Task.FromResult(Empty(input));
        }
    }

    sealed class LargeCatalogQuery : IStatsOverlayQuery
    {
        public int Calls { get; private set; }
        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            var result = Empty(input);
            result.RouteOptions = Enumerable.Range(0, 48).Select(index => new StatsOverlayRouteOption
            {
                CitySlug = "atlanta",
                RouteJoinKey = $"route-{index}",
                Label = new string((char)('a' + index % 26), 4_096),
                CategoryKey = "bus",
                MetadataTimestampUtc = Start.UtcDateTime
            }).ToList();
            return Task.FromResult(result);
        }
    }

    sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        DateTimeOffset now = current;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan span) => now += span;
        public void Set(DateTimeOffset value) => now = value;
    }
}
