using System.Collections.Concurrent;
using System.Reflection;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayCacheTests
{
    static readonly DateTimeOffset Start = new(2026, 1, 2, 12, 2, 30, TimeSpan.Zero);

    [Fact]
    public async Task ManualClockControlsTaskDelayMonotonicallyAndCleansUpTimers()
    {
        var clock = new StatsOverlayTestTimeProvider(Start);
        using var cancellation = new CancellationTokenSource();
        var delay = Task.Delay(TimeSpan.FromSeconds(5), clock, cancellation.Token);

        Assert.Equal(1, clock.ActiveTimerCount);
        clock.AdvanceUtc(TimeSpan.FromHours(1));
        Assert.False(delay.IsCompleted);
        clock.AdvanceMonotonic(TimeSpan.FromSeconds(4));
        Assert.False(delay.IsCompleted);
        clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        await delay.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, clock.ActiveTimerCount);

        using var canceled = new CancellationTokenSource();
        var canceledDelay = Task.Delay(TimeSpan.FromMinutes(1), clock, canceled.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledDelay);
        Assert.Equal(0, clock.ActiveTimerCount);

        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = clock.CreateTimer(_ => fired.TrySetResult(), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        Assert.True(timer.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan));
        clock.AdvanceMonotonic(TimeSpan.FromSeconds(2));
        Assert.False(fired.Task.IsCompleted);
        clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await timer.DisposeAsync();
        Assert.Equal(0, clock.ActiveTimerCount);

        var disposedFired = false;
        var disposedTimer = clock.CreateTimer(_ => disposedFired = true, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        disposedTimer.Dispose();
        clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        Assert.False(disposedFired);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task UnfilteredSnapshotHasAbsoluteExpiryAndConfigurationCutoffKeys()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new CountingQuery();
        var options = Options("atlanta");
        var service = new StatsOverlayService(query, options, clock);
        var request = AllRequest();

        var first = await service.ReadAsync(request, CancellationToken.None);
        var boundary = new DateTimeOffset(first.NextRefreshAtUtc!.Value, TimeSpan.Zero);
        clock.Set(boundary.AddTicks(-1));
        var cached = await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(first.GeneratedAtUtc, cached.GeneratedAtUtc);
        Assert.Equal(1, query.Calls);
        Assert.Equal(1, GetCacheCount(service));

        clock.Set(boundary);
        var expired = await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(2, query.Calls);
        Assert.NotEqual(first.GeneratedAtUtc, expired.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));

        options.Cities[0].HealthyCadenceLimitSeconds = 20;
        await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(3, query.Calls);
        Assert.Equal(1, GetCacheCount(service));

        clock.Set(new DateTimeOffset(expired.NextRefreshAtUtc!.Value, TimeSpan.Zero));
        await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(4, query.Calls);

        clock.Set(new DateTimeOffset(2026, 1, 3, 0, 2, 30, TimeSpan.Zero));
        await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(5, query.Calls);
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task SnapshotExpiresImmediatelyBeforeAtAndAfterItsOriginalRefreshBoundary()
    {
        var clock = new MutableTimeProvider(Start);
        var options = Options("atlanta");
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, options, clock);
        var first = await service.ReadAsync(AllRequest(), CancellationToken.None);
        var boundary = new DateTimeOffset(first.NextRefreshAtUtc!.Value, TimeSpan.Zero);

        clock.Set(boundary.AddTicks(-1));
        var before = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(first.GeneratedAtUtc, before.GeneratedAtUtc);
        Assert.Equal(1, query.Calls);

        clock.Set(boundary);
        var at = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.NotEqual(first.GeneratedAtUtc, at.GeneratedAtUtc);
        Assert.Equal(2, query.Calls);

        clock.Advance(TimeSpan.FromTicks(1));
        var after = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(at.GeneratedAtUtc, after.GeneratedAtUtc);
        Assert.Equal(2, query.Calls);
    }

    [Fact]
    public async Task SlowFillCrossingOriginalBoundaryReturnsOriginalMetadataWithoutRetention()
    {
        var clock = new MutableTimeProvider(Start);
        var options = Options("atlanta");
        var query = new BlockingQuery();
        var service = new StatsOverlayService(query, options, clock);
        var fill = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var boundary = StatsOverlayRefreshPolicy.Resolve(Start, options.RefreshIntervalMinutes,
            options.IngestionGraceMinutes).NextRefreshAtUtc;
        clock.Set(new DateTimeOffset(boundary, TimeSpan.Zero).AddTicks(1));
        query.Release.TrySetResult();

        var crossed = await fill;
        Assert.Equal(Start.UtcDateTime, crossed.GeneratedAtUtc);
        Assert.Equal(boundary, crossed.NextRefreshAtUtc);
        Assert.Equal(0, GetCacheCount(service));
        var followup = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.NotEqual(crossed.GeneratedAtUtc, followup.GeneratedAtUtc);
        Assert.Equal(2, query.Calls);
    }

    [Fact]
    public async Task ClockRollbackFlushesRetainedEvidenceAndMonotonicAgeStillExpiresIt()
    {
        var clock = new StatsOverlayTestTimeProvider(Start);
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, Options("atlanta", "boston"), clock);
        var first = await service.ReadAsync(AllRequest(), CancellationToken.None);
        await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);

        clock.SetUtcNow(Start.AddSeconds(-1));
        await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);
        Assert.Equal(3, query.Calls);
        await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(4, query.Calls);

        var latest = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(4, query.Calls);
        var lifetime = new DateTimeOffset(latest.NextRefreshAtUtc!.Value, TimeSpan.Zero)
            - new DateTimeOffset(DateTime.SpecifyKind(latest.GeneratedAtUtc, DateTimeKind.Utc), TimeSpan.Zero);
        clock.AdvanceMonotonic(lifetime);
        await service.ReadAsync(AllRequest(), CancellationToken.None);

        Assert.Equal(5, query.Calls);
        Assert.Equal(1, GetCacheCount(service));
        Assert.NotEqual(default, first.GeneratedAtUtc);
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
    public async Task FilteredCountBudgetUsesLruAndDoesNotDisplaceBaselineSnapshots()
    {
        var query = new CountingQuery();
        var options = Options("atlanta");
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));
        await service.ReadAsync(AllRequest(), CancellationToken.None);
        await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);
        var filtered = Enumerable.Range(0, 8)
            .Select(index => CityRequest("atlanta", [$"route-{index:D2}"])).ToArray();
        foreach (var request in filtered)
            await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(10, GetCacheCount(service));

        await service.ReadAsync(filtered[0], CancellationToken.None);
        await service.ReadAsync(CityRequest("atlanta", ["route-08"]), CancellationToken.None);
        Assert.Equal(10, GetCacheCount(service));
        await service.ReadAsync(filtered[0], CancellationToken.None);
        await service.ReadAsync(filtered[1], CancellationToken.None);
        await service.ReadAsync(CityRequest("atlanta", ["route-08"]), CancellationToken.None);

        Assert.Equal(12, query.Calls);
        Assert.Equal(10, GetCacheCount(service));
        Assert.Equal("all", (await service.ReadAsync(AllRequest(), CancellationToken.None)).Selection.Scope);
        Assert.Equal("city", (await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None)).Selection.Scope);
        Assert.Equal(12, query.Calls);
    }

    [Fact]
    public async Task FilteredByteBudgetEnforcesExactFitOneByteOverAndByteOnlyPressure()
    {
        var probe = new StatsOverlayService(new LargeCatalogQuery(), Options("atlanta"), new MutableTimeProvider(Start));
        await probe.ReadAsync(CityRequest("atlanta", ["route-00"]), CancellationToken.None);
        var estimate = GetCacheEstimatedBytes(probe);
        Assert.InRange(estimate, 1024 * 1024, 64L * 1024 * 1024);

        var exactOptions = Options("atlanta");
        SetFilteredBudget(exactOptions, "MaximumFilteredCacheBytes", estimate);
        var exact = new StatsOverlayService(new LargeCatalogQuery(), exactOptions, new MutableTimeProvider(Start));
        await exact.ReadAsync(CityRequest("atlanta", ["route-00"]), CancellationToken.None);
        Assert.Equal(estimate, GetCacheEstimatedBytes(exact));
        Assert.Equal(1, GetCacheCount(exact));

        var oneByteShortOptions = Options("atlanta");
        SetFilteredBudget(oneByteShortOptions, "MaximumFilteredCacheBytes", estimate - 1);
        var oneByteShort = new StatsOverlayService(new LargeCatalogQuery(), oneByteShortOptions, new MutableTimeProvider(Start));
        await oneByteShort.ReadAsync(CityRequest("atlanta", ["route-00"]), CancellationToken.None);
        Assert.Equal(0, GetCacheCount(oneByteShort));

        var pressureOptions = Options("atlanta");
        SetFilteredBudget(pressureOptions, "MaximumFilteredCacheBytes", estimate * 2 - 1);
        var pressure = new StatsOverlayService(new LargeCatalogQuery(), pressureOptions, new MutableTimeProvider(Start));
        await pressure.ReadAsync(CityRequest("atlanta", ["route-00"]), CancellationToken.None);
        await pressure.ReadAsync(CityRequest("atlanta", ["route-01"]), CancellationToken.None);
        Assert.Equal(1, GetCacheCount(pressure));
        Assert.Equal(estimate, GetCacheEstimatedBytes(pressure));
    }

    [Fact]
    public async Task CombinedFilteredCountAndBytePressureKeepsBothBudgetsExact()
    {
        var probe = new StatsOverlayService(new LargeCatalogQuery(), Options("atlanta"), new MutableTimeProvider(Start));
        await probe.ReadAsync(CityRequest("atlanta", ["route-00"]), CancellationToken.None);
        var estimate = GetCacheEstimatedBytes(probe);
        var options = Options("atlanta");
        SetFilteredBudget(options, "MaximumFilteredCacheEntries", 2);
        SetFilteredBudget(options, "MaximumFilteredCacheBytes", estimate * 2);
        var query = new LargeCatalogQuery();
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));

        await service.ReadAsync(CityRequest("atlanta", ["route-00"]), CancellationToken.None);
        await service.ReadAsync(CityRequest("atlanta", ["route-01"]), CancellationToken.None);
        Assert.Equal(2, GetCacheCount(service));
        Assert.Equal(estimate * 2, GetCacheEstimatedBytes(service));
        await service.ReadAsync(CityRequest("atlanta", ["route-02"]), CancellationToken.None);

        Assert.Equal(3, query.Calls);
        Assert.Equal(2, GetCacheCount(service));
        Assert.Equal(estimate * 2, GetCacheEstimatedBytes(service));
    }

    [Fact]
    public async Task ExpiredFilteredEntriesAreRemovedBeforeSubsetAdmission()
    {
        var clock = new MutableTimeProvider(Start);
        var options = Options("atlanta");
        SetFilteredBudget(options, "MaximumFilteredCacheEntries", 1);
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, options, clock);
        var first = await service.ReadAsync(CityRequest("atlanta", ["route-old"]), CancellationToken.None);
        clock.Set(new DateTimeOffset(first.NextRefreshAtUtc!.Value, TimeSpan.Zero));

        await service.ReadAsync(CityRequest("atlanta", ["route-new"]), CancellationToken.None);

        Assert.Equal(2, query.Calls);
        Assert.Equal(1, GetCacheCount(service));
        Assert.Equal("route-new", Assert.Single(GetCachedFilteredRoutes(service)));
    }

    [Fact]
    public async Task FullBaselineCacheRejectsFilteredRetentionWithoutEvictingBaselineEvidence()
    {
        var cities = Enumerable.Range(0, 32).Select(index => $"city-{index:00}").ToArray();
        var options = Options(cities);
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));
        var baselines = cities.Select(city => CityRequest(city)).Append(AllRequest()).ToArray();
        foreach (var request in baselines)
            await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(33, GetCacheCount(service));

        await service.ReadAsync(CityRequest(cities[0], ["route-filtered"]), CancellationToken.None);
        foreach (var request in baselines)
            await service.ReadAsync(request, CancellationToken.None);
        Assert.Equal(34, query.Calls);

        await service.ReadAsync(CityRequest(cities[0], ["route-filtered"]), CancellationToken.None);
        Assert.Equal(35, query.Calls);
        Assert.Equal(33, GetCacheCount(service));
    }

    [Fact]
    public async Task BaselineAdmissionEvictsFilteredLruBeforeAnOlderBaseline()
    {
        var options = Options("atlanta");
        options.MaximumCacheEntries = 2;
        SetFilteredBudget(options, "MaximumFilteredCacheEntries", 1);
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));
        await service.ReadAsync(AllRequest(), CancellationToken.None);
        await service.ReadAsync(CityRequest("atlanta", ["route-filtered"]), CancellationToken.None);
        Assert.Equal(2, GetCacheCount(service));

        await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);
        Assert.Equal(3, query.Calls);
        Assert.Equal(2, GetCacheCount(service));
        await service.ReadAsync(AllRequest(), CancellationToken.None);
        await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);

        Assert.Equal(3, query.Calls);
        Assert.Equal(2, GetCacheCount(service));
        await service.ReadAsync(CityRequest("atlanta", ["route-filtered"]), CancellationToken.None);
        Assert.Equal(4, query.Calls);
    }

    [Fact]
    public async Task CacheEstimateIncludesCanonicalKeysAndEntryOverhead()
    {
        var options = Options("atlanta");
        var service = new StatsOverlayService(new LargeCatalogQuery(), options, new MutableTimeProvider(Start));
        var response = await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);
        var serializedBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            response, ChefKnifeStudios.TransitJazz.Shared.JsonSettings.DefaultOptions).Length;
        var cache = (System.Collections.IDictionary)typeof(StatsOverlayService)
            .GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        var item = (System.Collections.IDictionaryEnumerator)cache.GetEnumerator();
        Assert.True(item.MoveNext());
        var entry = item.Value!;
        var identityKey = (string)entry.GetType().GetProperty("IdentityKey")!.GetValue(entry)!;
        var expected = (long)serializedBytes * 8 + 4_096
            + System.Text.Encoding.UTF8.GetByteCount(identityKey)
            + System.Text.Encoding.UTF8.GetByteCount((string)item.Key)
            + 512;

        Assert.Equal(expected, GetCacheEstimatedBytes(service));
    }

    [Fact]
    public async Task ZeroFilteredRetentionStillCoalescesConcurrentExactRequests()
    {
        var query = new BlockingQuery();
        var options = Options("atlanta");
        SetFilteredBudget(options, "MaximumFilteredCacheEntries", 0);
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));
        var reads = Enumerable.Range(0, 3)
            .Select(_ => service.ReadAsync(CityRequest("atlanta", ["route-a"]), CancellationToken.None)).ToArray();
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, GetInFlightCount(service));
        query.Release.TrySetResult();
        await Task.WhenAll(reads);

        Assert.Equal(1, query.Calls);
        Assert.Equal(0, GetCacheCount(service));
        await service.ReadAsync(CityRequest("atlanta", ["route-a"]), CancellationToken.None);
        Assert.Equal(2, query.Calls);
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
    public async Task FirstAndAllWaiterCancellationLeaveSharedFillRunningForLaterJoiner()
    {
        var query = new BlockingQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), new MutableTimeProvider(Start));
        using var firstWaiter = new CancellationTokenSource();
        var first = service.ReadAsync(AllRequest(), firstWaiter.Token);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        firstWaiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(query.Canceled.Task.IsCompleted);

        using var secondWaiter = new CancellationTokenSource();
        using var thirdWaiter = new CancellationTokenSource();
        var second = service.ReadAsync(AllRequest(), secondWaiter.Token);
        var third = service.ReadAsync(AllRequest(), thirdWaiter.Token);
        secondWaiter.Cancel();
        thirdWaiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        Assert.False(query.Canceled.Task.IsCompleted);

        var laterJoiner = service.ReadAsync(AllRequest(), CancellationToken.None);
        query.Release.TrySetResult();
        var response = await laterJoiner;
        var warm = await service.ReadAsync(AllRequest(), CancellationToken.None);

        Assert.Equal("all", response.Selection.Scope);
        Assert.Equal(response.GeneratedAtUtc, warm.GeneratedAtUtc);
        Assert.Equal(1, query.Calls);
        Assert.False(query.Canceled.Task.IsCompleted);
    }

    [Fact]
    public async Task WholeReadDeadlineUsesMonotonicManualTimeAndDisposesItsTimer()
    {
        var clock = new StatsOverlayTestTimeProvider(Start);
        var query = new BlockingQuery();
        var options = Options("atlanta");
        options.ReadTimeoutSeconds = 5;
        var service = new StatsOverlayService(query, options, clock);
        var read = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, clock.ActiveTimerCount);

        clock.AdvanceMonotonic(TimeSpan.FromSeconds(5));
        if (!query.Tokens.Single().IsCancellationRequested)
        {
            query.Release.TrySetResult();
            try { await read; }
            catch (StatsOverlayRequestException) { }
            Assert.Fail("The whole-read deadline did not follow the manual monotonic clock.");
        }

        var failure = await Assert.ThrowsAsync<StatsOverlayRequestException>(() => read);
        Assert.Equal("read_timeout", failure.Code);
        Assert.Equal(0, GetCacheCount(service));
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task HostShutdownCancelsSharedQueryAndReleasesItsAdmission()
    {
        var query = new BlockingQuery();
        var options = Options("atlanta");
        var hostStopping = new CancellationTokenSource();
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start),
            hostStoppingToken: hostStopping.Token);
        var read = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        hostStopping.Cancel();
        if (!query.Tokens.Single().IsCancellationRequested)
        {
            query.Release.TrySetResult();
            try { await read; }
            catch (StatsOverlayRequestException) { }
            Assert.Fail("Host shutdown did not cancel the shared query.");
        }

        await Assert.ThrowsAnyAsync<Exception>(() => read);
        Assert.Equal(0, GetCacheCount(service));
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(options.MaximumConcurrentReads, GetAvailableReadSlots(service));
    }

    [Fact]
    public async Task ExpiredFillCleanupDoesNotRemoveANewerPromiseForTheSameSelection()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new TwoGateQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), clock);
        var older = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromHours(1));
        var newer = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, GetInFlightCount(service));

        query.ReleaseFirst.TrySetResult();
        var oldResponse = await older;
        Assert.Equal(1, GetInFlightCount(service));
        Assert.Equal(Start.UtcDateTime, oldResponse.GeneratedAtUtc);
        var joining = service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(2, query.Calls);

        query.ReleaseSecond.TrySetResult();
        var responses = await Task.WhenAll(newer, joining);
        Assert.All(responses, response => Assert.Equal(clock.GetUtcNow().UtcDateTime, response.GeneratedAtUtc));
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task NewerCompletionBeforeOlderFillCannotBeResurrected()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new TwoGateQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), clock);
        var older = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromHours(1));
        var newer = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        query.ReleaseSecond.TrySetResult();
        var newerResponse = await newer;
        Assert.Equal(clock.GetUtcNow().UtcDateTime, newerResponse.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));

        query.ReleaseFirst.TrySetResult();
        var olderResponse = await older;
        var warm = await service.ReadAsync(AllRequest(), CancellationToken.None);

        Assert.Equal(Start.UtcDateTime, olderResponse.GeneratedAtUtc);
        Assert.Equal(newerResponse.GeneratedAtUtc, warm.GeneratedAtUtc);
        Assert.Equal(2, query.Calls);
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task ResponseSizeRejectionReleasesAdmissionAndAllowsSmallerReplacement()
    {
        var query = new LargeCatalogQuery();
        var options = Options("atlanta");
        options.MaximumConcurrentReads = 1;
        options.MaximumResponseBytes = 1024;
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));

        var tooLarge = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(CityRequest("atlanta"), CancellationToken.None));
        Assert.Equal("response_too_large", tooLarge.Code);
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(1, GetAvailableReadSlots(service));

        options.MaximumResponseBytes = 8 * 1024 * 1024;
        var valid = await service.ReadAsync(CityRequest("atlanta"), CancellationToken.None);
        Assert.NotEmpty(valid.RouteOptions);
        Assert.Equal(2, query.Calls);
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(1, GetAvailableReadSlots(service));
    }

    [Fact]
    public async Task UnsafeProjectionRejectionReleasesAdmissionAndAllowsRetry()
    {
        var query = new UnsafeProjectionThenValidQuery();
        var options = Options("atlanta");
        options.MaximumConcurrentReads = 1;
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));

        var unsafeProjection = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(CityRequest("atlanta", ["route-unsafe"]), CancellationToken.None));
        Assert.Equal("scope_too_large", unsafeProjection.Code);
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(1, GetAvailableReadSlots(service));

        var valid = await service.ReadAsync(CityRequest("atlanta", ["route-unsafe"]), CancellationToken.None);
        Assert.Equal("city", valid.Selection.Scope);
        Assert.Equal(2, query.Calls);
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(1, GetAvailableReadSlots(service));
    }

    [Fact]
    public async Task AsynchronousFailureReleasesAdmissionAndAllowsRetry()
    {
        var query = new AsyncFailOnceQuery();
        var options = Options("atlanta");
        options.MaximumConcurrentReads = 1;
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));

        var failure = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(AllRequest(), CancellationToken.None));
        Assert.Equal("dependency_unavailable", failure.Code);
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(1, GetAvailableReadSlots(service));

        var retried = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal("all", retried.Selection.Scope);
        Assert.Equal(2, query.Calls);
        Assert.Equal(0, GetInFlightCount(service));
        Assert.Equal(1, GetAvailableReadSlots(service));
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
    public async Task DelayedFillRetainsUntilOriginalRefreshBoundary()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new BlockingQuery();
        var options = Options("atlanta");
        var service = new StatsOverlayService(query, options, clock);

        var delayed = service.ReadAsync(AllRequest(), CancellationToken.None);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(6));
        query.Release.TrySetResult();
        var first = await delayed;

        Assert.Equal(Start.UtcDateTime, first.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));

        var refreshed = await service.ReadAsync(AllRequest(), CancellationToken.None);
        Assert.Equal(1, query.Calls);
        Assert.Equal(first.GeneratedAtUtc, refreshed.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task FourDistinctFilteredScopesConsumeSeparateReadSlotsAndFifthIsRejectedImmediately()
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
    public async Task NormalizedFilteredBurstSharesOneColdReadAndWarmRequestUsesItsSnapshot()
    {
        var query = new BlockingQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), new MutableTimeProvider(Start));
        var requests = new[]
        {
            CityRequest("atlanta", ["route-b", "route-a"]),
            CityRequest("atlanta", ["route-a", "route-b"]),
            CityRequest("atlanta", ["route-a", "route-b", "route-a"])
        };

        var reads = requests.Select(request => service.ReadAsync(request, CancellationToken.None)).ToArray();
        var activeSharedFills = GetInFlightCount(service);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        query.Release.TrySetResult();
        var responses = await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(2));
        var warm = await service.ReadAsync(CityRequest("atlanta", ["route-b", "route-a"]), CancellationToken.None);

        Assert.Equal(1, activeSharedFills);
        Assert.Equal(1, query.Calls);
        Assert.All(responses, response => Assert.Equal(new[] { "route-a", "route-b" }, response.Selection.RouteJoinKeys));
        Assert.Equal(responses[0].GeneratedAtUtc, warm.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task StructuredSelectionIdentitySeparatesCaseRouteSetCityAndScope()
    {
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, Options("atlanta", "boston"), new MutableTimeProvider(Start));
        var selections = new[]
        {
            CityRequest("atlanta", ["route:A"]),
            CityRequest("atlanta", ["route:a"]),
            CityRequest("atlanta", ["route:a", "route:b"]),
            CityRequest("boston", ["route:a"]),
            AllRequest()
        };

        var responses = new List<StatsOverlayResponse>();
        foreach (var selection in selections)
            responses.Add(await service.ReadAsync(selection, CancellationToken.None));
        var warm = await service.ReadAsync(selections[0], CancellationToken.None);

        Assert.Equal(5, query.Calls);
        Assert.Equal("route:A", Assert.Single(responses[0].Selection.RouteJoinKeys));
        Assert.Equal("route:a", Assert.Single(responses[1].Selection.RouteJoinKeys));
        Assert.Equal(new[] { "route:a", "route:b" }, responses[2].Selection.RouteJoinKeys);
        Assert.Equal("boston", responses[3].Cities[0].CitySlug);
        Assert.Equal("all", responses[4].Selection.Scope);
        Assert.Equal(responses[0].GeneratedAtUtc, warm.GeneratedAtUtc);
        Assert.Equal(responses[0].RefreshWindowStartUtc, warm.RefreshWindowStartUtc);
        Assert.Equal(responses[0].NextRefreshAtUtc, warm.NextRefreshAtUtc);
        Assert.Equal(5, GetCacheCount(service));
    }

    [Fact]
    public async Task EachSinglePolicyChangeInvalidatesTheRetainedIdentity()
    {
        var query = new CountingQuery();
        var options = Options("atlanta");
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start));
        var request = CityRequest("atlanta");

        await service.ReadAsync(request, CancellationToken.None);
        options.MaximumCategories--;
        await service.ReadAsync(request, CancellationToken.None);
        options.RefreshIntervalMinutes = 30;
        await service.ReadAsync(request, CancellationToken.None);
        options.IngestionGraceMinutes++;
        await service.ReadAsync(request, CancellationToken.None);
        options.Cities[0].DisplayNameKey = "changed-city-name";
        await service.ReadAsync(request, CancellationToken.None);
        options.Cities[0].HealthyCadenceLimitSeconds++;
        await service.ReadAsync(request, CancellationToken.None);

        Assert.Equal(6, query.Calls);
        Assert.Equal(1, GetCacheCount(service));
    }

    [Fact]
    public async Task IndependentServiceInstancesRetainTheirOwnOriginalSnapshotMetadata()
    {
        var clock = new MutableTimeProvider(Start);
        var firstQuery = new CountingQuery();
        var secondQuery = new CountingQuery();
        var firstService = new StatsOverlayService(firstQuery, Options("atlanta"), clock);
        var secondService = new StatsOverlayService(secondQuery, Options("atlanta"), clock);
        var first = await firstService.ReadAsync(AllRequest(), CancellationToken.None);
        var second = await secondService.ReadAsync(AllRequest(), CancellationToken.None);
        var warm = await firstService.ReadAsync(AllRequest(), CancellationToken.None);

        Assert.Equal(1, firstQuery.Calls);
        Assert.Equal(1, secondQuery.Calls);
        Assert.Equal(first.GeneratedAtUtc, second.GeneratedAtUtc);
        Assert.Equal(first.CutoffUtc, second.CutoffUtc);
        Assert.Equal(first.RefreshWindowStartUtc, second.RefreshWindowStartUtc);
        Assert.Equal(first.NextRefreshAtUtc, second.NextRefreshAtUtc);
        Assert.Equal(first.GeneratedAtUtc, warm.GeneratedAtUtc);
        Assert.Equal(first.RefreshWindowStartUtc, warm.RefreshWindowStartUtc);
        Assert.Equal(first.NextRefreshAtUtc, warm.NextRefreshAtUtc);
    }

    [Fact]
    public async Task ReportedVehicleEnablementIsPartOfTheCacheIdentityEvenWithNoConfiguredSources()
    {
        var query = new CountingQuery();
        var options = Options("atlanta");
        var catalog = new ReportedVehicleSourceCatalog([]);
        var service = new StatsOverlayService(query, options, new MutableTimeProvider(Start), catalog);
        var request = CityRequest("atlanta");

        var disabled = await service.ReadAsync(request, CancellationToken.None);
        options.ReportedVehiclesEnabled = true;
        var enabled = await service.ReadAsync(request, CancellationToken.None);

        Assert.Equal(2, query.Calls);
        Assert.Contains("reported_reads_disabled", disabled.TodayReportedVehicles.Limitations);
        Assert.DoesNotContain("reported_reads_disabled", enabled.TodayReportedVehicles.Limitations);
    }

    [Fact]
    public async Task MatchingFilteredWaiterJoinsWhenAllReadSlotsAreOccupied()
    {
        var query = new BlockingQuery(expectedStarts: 4);
        var service = new StatsOverlayService(query, Options("atlanta"), new MutableTimeProvider(Start));
        var reads = Enumerable.Range(0, 4)
            .Select(index => service.ReadAsync(CityRequest("atlanta", [$"route-{index}"]), CancellationToken.None))
            .ToArray();
        await query.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var matching = service.ReadAsync(CityRequest("atlanta", ["route-0", "route-0"]), CancellationToken.None);
        var fifthDistinct = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(CityRequest("atlanta", ["route-4"]), CancellationToken.None));
        Assert.Equal("busy", fifthDistinct.Code);
        Assert.Equal(4, query.Calls);
        Assert.Equal(4, GetInFlightCount(service));

        query.Release.TrySetResult();
        var responses = await Task.WhenAll(reads.Append(matching));
        Assert.Equal(4, query.Calls);
        Assert.Equal(5, responses.Length);
        Assert.All(responses, response => Assert.Equal("city", response.Selection.Scope));
    }

    [Fact]
    public async Task IndependentFilteredReadStartsWhileAnotherQueryIsBlocked()
    {
        var query = new BlockingQuery(expectedStarts: 2);
        var service = new StatsOverlayService(query, Options("atlanta"), new MutableTimeProvider(Start));
        var first = service.ReadAsync(CityRequest("atlanta", ["route-a"]), CancellationToken.None);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = service.ReadAsync(CityRequest("atlanta", ["route-b"]), CancellationToken.None);

        await query.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, query.Calls);
        query.Release.TrySetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task ImmediateSuccessAndSynchronousFailureBothReleaseAdmissionForReuse()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new SwitchableQuery();
        var options = Options("atlanta");
        options.MaximumConcurrentReads = 1;
        var service = new StatsOverlayService(query, options, clock);

        var first = await service.ReadAsync(AllRequest(), CancellationToken.None);
        clock.Set(new DateTimeOffset(first.NextRefreshAtUtc!.Value, TimeSpan.Zero).AddTicks(1));
        query.Fail = true;
        var failure = await Assert.ThrowsAsync<StatsOverlayRequestException>(() =>
            service.ReadAsync(AllRequest(), CancellationToken.None));
        Assert.Equal("dependency_unavailable", failure.Code);
        query.Fail = false;
        var retried = await service.ReadAsync(AllRequest(), CancellationToken.None);

        Assert.Equal(3, query.Calls);
        Assert.NotEqual(first.GeneratedAtUtc, retried.GeneratedAtUtc);
        Assert.Equal(1, GetCacheCount(service));
        Assert.Equal(0, GetInFlightCount(service));
    }

    [Fact]
    public async Task ImmediateFilteredBurstRegistersPromiseBeforeSynchronousQueryCompletes()
    {
        var query = new CountingQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), new MutableTimeProvider(Start));
        var requests = Enumerable.Range(0, 8)
            .Select(_ => service.ReadAsync(CityRequest("atlanta", ["route-b", "route-a"]), CancellationToken.None))
            .ToArray();
        var responses = await Task.WhenAll(requests);

        Assert.Equal(1, query.Calls);
        Assert.Equal(1, GetCacheCount(service));
        Assert.All(responses, response => Assert.Equal(responses[0].GeneratedAtUtc, response.GeneratedAtUtc));
    }

    [Fact]
    public async Task ExpiredCacheFailureDoesNotReturnPreviousSnapshot()
    {
        var clock = new MutableTimeProvider(Start);
        var query = new SwitchableQuery();
        var service = new StatsOverlayService(query, Options("atlanta"), clock);
        var first = await service.ReadAsync(AllRequest(), CancellationToken.None);
        query.Fail = true;
        clock.Set(new DateTimeOffset(first.NextRefreshAtUtc!.Value, TimeSpan.Zero).AddTicks(1));

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
        SetFilteredBudget(options, "MaximumFilteredCacheBytes", 1024L * 1024);
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

    static void SetFilteredBudget(StatsOverlayOptions options, string propertyName, object value)
    {
        var property = typeof(StatsOverlayOptions).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(options, value);
    }

    static long GetCacheEstimatedBytes(StatsOverlayService service) =>
        (long)typeof(StatsOverlayService).GetField("cachedEstimatedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;

    static string[] GetCachedFilteredRoutes(StatsOverlayService service)
    {
        var entries = (System.Collections.IDictionary)typeof(StatsOverlayService)
            .GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        return entries.Values.Cast<object>()
            .Where(entry => (bool)entry.GetType().GetProperty("IsFiltered")!.GetValue(entry)!)
            .Select(entry => (string[])((StatsOverlayResponse)entry.GetType().GetProperty("Response")!
                .GetValue(entry)!).Selection.RouteJoinKeys.ToArray())
            .SelectMany(routes => routes).ToArray();
    }

    static int GetInFlightCount(StatsOverlayService service) =>
        ((System.Collections.IDictionary)typeof(StatsOverlayService).GetField("inFlight", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!).Count;

    static int GetAvailableReadSlots(StatsOverlayService service) =>
        ((SemaphoreSlim)typeof(StatsOverlayService).GetField("readSlots", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!).CurrentCount;

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
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentBag<CancellationToken> Tokens { get; } = [];

        public async Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref calls);
            if (count >= expectedStarts) AllStarted.TrySetResult();
            Tokens.Add(cancellationToken);
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return Empty(input);
            }
            finally
            {
                Completed.TrySetResult();
            }
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

    sealed class AsyncFailOnceQuery : IStatsOverlayQuery
    {
        int calls;
        public int Calls => Volatile.Read(ref calls);

        public async Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await Task.Yield();
                throw new InvalidOperationException("private async failure detail");
            }
            return Empty(input);
        }
    }

    sealed class UnsafeProjectionThenValidQuery : IStatsOverlayQuery
    {
        int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) > 1) return Task.FromResult(Empty(input));
            var hour = input.Hours.First(value => value.CitySlug == "atlanta" && value.IsToday).HourStartUtc;
            var result = Empty(input);
            result.RouteOptions.Add(new StatsOverlayRouteOption
            {
                CitySlug = "atlanta",
                RouteJoinKey = "route-unsafe",
                Label = "Unsafe route",
                CategoryKey = "bus",
                MetadataTimestampUtc = Start.UtcDateTime
            });
            result.RouteMeasures.Add(new StatsOverlayRouteMeasure
            {
                CitySlug = "atlanta",
                Window = "today",
                RouteJoinKey = "route-unsafe",
                DistanceKilometers = 1m,
                RouteVehicleHours = 9_007_199_254_740_992L,
                ActiveClockHours = 1,
                ActivitySampleNumerator = 1,
                SampleDenominator = 1,
                AvailableHours = 1,
                FirstHourStartUtc = hour,
                LastHourStartUtc = hour
            });
            return Task.FromResult(result);
        }
    }

    sealed class TwoGateQuery : IStatsOverlayQuery
    {
        int calls;
        public int Calls => Volatile.Read(ref calls);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            var started = call == 1 ? FirstStarted : SecondStarted;
            var release = call == 1 ? ReleaseFirst : ReleaseSecond;
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Empty(input);
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
