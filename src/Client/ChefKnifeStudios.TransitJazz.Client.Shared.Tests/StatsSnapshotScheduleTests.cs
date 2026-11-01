using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using System.Net;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsSnapshotScheduleTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    static readonly StatsSelection City = new("city", "toronto", []);

    [Fact]
    public async Task FiveSecondJitterNeverStartsBeforeTheReturnedBoundaryAndSamplesOnce()
    {
        var harness = await StartStoreAsync(60, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        try
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(59));
            Assert.Single(harness.Endpoints.Calls);
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Single(harness.Endpoints.Calls);
            harness.Clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
            Assert.Single(harness.Endpoints.Calls);
            Assert.Equal(1, harness.Jitter.SamplesTaken);

            harness.Clock.Advance(TimeSpan.FromTicks(1));
            var refresh = await harness.Endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, harness.Endpoints.Calls.Count);
            Assert.Equal(1, harness.Jitter.SamplesTaken);

            var joined = harness.Store.EnsureRequestedAsync(City);
            refresh.Return(Success(harness.Clock, City, 60));
            await joined.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, harness.Jitter.SamplesTaken);
        }
        finally { await harness.Store.StopAsync(); }
    }

    [Fact]
    public async Task ZeroJitterRunsAtBoundaryAndDuplicateTriggersJoinTheSameRefresh()
    {
        var harness = await StartStoreAsync(60, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        try
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(59));
            Assert.Single(harness.Endpoints.Calls);
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            var refresh = await harness.Endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            var manual = harness.Store.EnsureRequestedAsync(City);
            await harness.Store.StartAsync("toronto");
            harness.Store.SetOverlaySelection(City);
            var duplicate = harness.Store.EnsureRequestedAsync(City);
            Assert.Equal(2, harness.Endpoints.Calls.Count);
            Assert.Equal(1, harness.Jitter.SamplesTaken);

            refresh.Return(Success(harness.Clock, City, 60));
            await Task.WhenAll(manual, duplicate).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, harness.Jitter.SamplesTaken);
            Assert.Equal(2, harness.Endpoints.Calls.Count);
        }
        finally { await harness.Store.StopAsync(); }
    }

    [Fact]
    public async Task NewFifteenMinuteMetadataReplacesThePriorHourlyBoundary()
    {
        var harness = await StartStoreAsync(60, TimeSpan.Zero, TimeSpan.Zero);
        try
        {
            harness.Clock.Advance(TimeSpan.FromHours(1));
            var hourlyRefresh = await harness.Endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            hourlyRefresh.Return(Success(harness.Clock, City, 15));
            await harness.Store.EnsureRequestedAsync(City).WaitAsync(TimeSpan.FromSeconds(2));

            harness.Clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1));
            Assert.Equal(2, harness.Endpoints.Calls.Count);
            harness.Clock.Advance(TimeSpan.FromTicks(1));
            _ = await harness.Endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(3, harness.Endpoints.Calls.Count);
            Assert.Equal(2, harness.Jitter.SamplesTaken);
        }
        finally { await harness.Store.StopAsync(); }
    }

    [Fact]
    public async Task BoundaryDuringBlockedHttpQueuesOnlyOneDesireForThatSelection()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var jitter = new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero, TimeSpan.Zero);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false, jitter: jitter);
        var filtered = new StatsSelection("city", "toronto", ["route:a"]);
        var scheduledDesire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StateChanged += (selection, state) =>
        {
            if (selection.Scope == "city" && selection.RouteJoinKeys.Count == 0 && state.RefreshConsumed)
                scheduledDesire.TrySetResult();
        };
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(City);
            var initial = store.EnsureRequestedAsync(City);
            var cityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            cityCall.Return(Success(clock, City, 60));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));

            var filteredWork = store.EnsureRequestedAsync(filtered);
            var filteredCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromHours(1));
            await scheduledDesire.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, endpoints.Calls.Count);

            var duplicateTrigger = store.EnsureRequestedAsync(City);
            filteredCall.Return(Success(clock, filtered, 60));
            var dueCityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(3, endpoints.Calls.Count);
            dueCityCall.Return(Success(clock, City, 60));
            await Task.WhenAll(filteredWork, duplicateTrigger).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(3, endpoints.Calls.Count);
            Assert.Equal(2, jitter.SamplesTaken);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task DisplayedFilteredRequestUsesLatestOnlyTwoHundredMillisecondDebounce()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false);
        var first = new StatsSelection("city", "toronto", ["route:a"]);
        var latest = new StatsSelection("city", "toronto", ["route:b"]);
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(first);
            var firstDesire = store.EnsureDisplayedSelectionAsync(first);
            Assert.Same(firstDesire, store.EnsureDisplayedSelectionAsync(first));
            clock.Advance(TimeSpan.FromMilliseconds(199));
            Assert.Empty(endpoints.Calls);

            store.SetOverlaySelection(latest);
            var latestDesire = store.EnsureDisplayedSelectionAsync(latest);
            clock.Advance(TimeSpan.FromMilliseconds(199));
            Assert.Empty(endpoints.Calls);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var call = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { "route:b" }, call.Request.RouteJoinKeys);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Single(endpoints.Calls);

            call.Return(Success(clock, latest, 60));
            await latestDesire.WaitAsync(TimeSpan.FromSeconds(2));
            try { await firstDesire.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { }
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task RepeatedFilteredDesiresDoNotStarveDueCityAndAllBaselines()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints
        {
            IgnoreCancellationForCall = call => call.Request.RouteJoinKeys is { } keys
                && keys.SequenceEqual(["route:a"], StringComparer.Ordinal)
        };
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: true,
            jitter: new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
        var all = new StatsSelection("all", null, []);
        var routeA = new StatsSelection("city", "toronto", ["route:a"]);
        var routeB = new StatsSelection("city", "toronto", ["route:b"]);
        var routeC = new StatsSelection("city", "toronto", ["route:c"]);
        var cityDue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allDue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StateChanged += (selection, state) =>
        {
            if (!state.RefreshConsumed) return;
            if (SelectionEquals(selection, City)) cityDue.TrySetResult();
            if (SelectionEquals(selection, all)) allDue.TrySetResult();
        };
        try
        {
            await store.StartAsync("toronto");
            var cityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            cityCall.Return(Success(clock, City, 15));

            var allCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            allCall.Return(Success(clock, all, 15));

            store.SetOverlaySelection(routeA);
            var routeAWork = store.EnsureRequestedAsync(routeA);
            var activeRouteA = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromMinutes(15));
            await Task.WhenAll(cityDue.Task, allDue.Task).WaitAsync(TimeSpan.FromSeconds(2));

            var cityRefresh = store.EnsureRequestedAsync(City);
            var allRefresh = store.EnsureRequestedAsync(all);
            store.SetOverlaySelection(routeB);
            var routeBWork = store.EnsureDisplayedSelectionAsync(routeB);
            store.SetOverlaySelection(routeC);
            var routeCWork = store.EnsureDisplayedSelectionAsync(routeC);
            await activeRouteA.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(3, endpoints.Calls.Count);

            activeRouteA.Return(Success(clock, routeA, 15));
            await routeAWork.WaitAsync(TimeSpan.FromSeconds(2));
            var next = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("city", next.Request.Scope);
            Assert.Empty(next.Request.RouteJoinKeys ?? Array.Empty<string>());
            next.Return(Success(clock, City, 15));
            await cityRefresh.WaitAsync(TimeSpan.FromSeconds(2));

            var following = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("all", following.Request.Scope);
            following.Return(Success(clock, all, 15));
            await allRefresh.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromMilliseconds(200));
            var latest = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { "route:c" }, latest.Request.RouteJoinKeys);
            latest.Return(Success(clock, routeC, 15));
            await routeCWork.WaitAsync(TimeSpan.FromSeconds(2));
            await routeBWork.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromMinutes(15));
            var cityAfterDisplayedTurn = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("city", cityAfterDisplayedTurn.Request.Scope);
            cityAfterDisplayedTurn.Return(Success(clock, City, 15));
            await store.EnsureRequestedAsync(City).WaitAsync(TimeSpan.FromSeconds(2));
            var allAfterCity = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("all", allAfterCity.Request.Scope);
            allAfterCity.Return(Success(clock, all, 15));
            await store.EnsureRequestedAsync(all).WaitAsync(TimeSpan.FromSeconds(2));
            var displayedRefresh = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { "route:c" }, displayedRefresh.Request.RouteJoinKeys);
            var displayedRefreshJoin = store.EnsureRequestedAsync(routeC);
            displayedRefresh.Return(Success(clock, routeC, 15));
            await displayedRefreshJoin.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(new[] { "city", "all", "city", "city", "all", "city", "city", "all", "city" },
                endpoints.Calls.Select(call => call.Request.Scope));
            Assert.DoesNotContain(endpoints.Calls, call => call.Request.RouteJoinKeys?.SequenceEqual(["route:b"]) == true);
        }
        finally
        {
            foreach (var call in endpoints.Calls)
            {
                if (call.Released.Task.IsCompleted) continue;
                var selection = call.Request.Scope == "all"
                    ? all
                    : new StatsSelection("city", call.Request.CitySlug,
                        call.Request.RouteJoinKeys ?? Array.Empty<string>());
                call.Return(Success(clock, selection, 15));
            }
            await store.StopAsync();
        }
    }

    [Fact]
    public async Task StopDuringDisplayedSelectionDebounceCancelsAndAwaitsThePendingDesire()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false);
        var filtered = new StatsSelection("city", "toronto", ["route:a"]);
        await store.StartAsync("toronto");
        store.SetOverlaySelection(filtered);
        var pending = store.EnsureDisplayedSelectionAsync(filtered);
        Assert.Equal(1, clock.ActiveTimerCount);

        await store.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await pending.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(endpoints.Calls);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task TransportCooldownUsesExactEligibilityAndInvalidSuccessResetsBackoffOnly()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false);
        try
        {
            await store.StartAsync("toronto");
            var firstAttempt = store.EnsureRequestedAsync(City);
            var firstCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            firstCall.Return(Failure(HttpStatusCode.ServiceUnavailable, "read_timeout"));
            await firstAttempt.WaitAsync(TimeSpan.FromSeconds(2));

            var immediate = store.EnsureRequestedAsync(City);
            Assert.Single(endpoints.Calls);
            await immediate.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1));
            await store.EnsureRequestedAsync(City).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(endpoints.Calls);
            clock.Advance(TimeSpan.FromTicks(1));

            var invalidAttempt = store.EnsureRequestedAsync(City);
            var invalidCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var malformed = Success(clock, City, 60).Result.Value with { Timeline = null! };
            invalidCall.Return(new HttpServiceResponse<StatsOverlayResponse>(
                Result<StatsOverlayResponse>.Success(malformed), HttpStatusCode.OK, null, null));
            await invalidAttempt.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("invalid_response", store.TryGet(City)!.ProblemCode);

            var afterInvalid = store.EnsureRequestedAsync(City);
            var secondFailure = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            secondFailure.Return(Failure(HttpStatusCode.ServiceUnavailable, "read_timeout"));
            await afterInvalid.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(3, endpoints.Calls.Count);

            clock.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1));
            await store.EnsureRequestedAsync(City).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(3, endpoints.Calls.Count);
            clock.Advance(TimeSpan.FromTicks(1));
            var recovered = store.EnsureRequestedAsync(City);
            var recoveredCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            recoveredCall.Return(Success(clock, City, 60));
            await recovered.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Null(store.TryGet(City)?.ProblemCode);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task IdenticalValidHttpSuccessClearsFailureWithoutReanchoringSnapshot()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false,
            jitter: new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero, TimeSpan.Zero));
        var failurePublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleNotified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StateChanged += (selection, state) =>
        {
            if (!SelectionEquals(selection, City)) return;
            if (state.ProblemCode == "read_timeout") failurePublished.TrySetResult();
            if (state.StaleNotified) staleNotified.TrySetResult();
            if (state.ProblemCode is null && state.LastContactUtc > state.ReceivedUtc) recovered.TrySetResult();
        };
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(City);
            var initial = store.EnsureRequestedAsync(City);
            var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            initialCall.Return(Success(clock, City, 60));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));
            var original = store.TryGet(City)!;

            clock.Advance(TimeSpan.FromHours(1));
            var failedCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            failedCall.Return(Failure(HttpStatusCode.ServiceUnavailable, "read_timeout"));
            await failurePublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromSeconds(30));
            await staleNotified.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (var attempt = 0; attempt < 100 && clock.ActiveTimerCount == 0; attempt++)
                await Task.Yield();
            clock.Advance(TimeSpan.FromSeconds(30));
            var identicalRetry = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            identicalRetry.Return(new HttpServiceResponse<StatsOverlayResponse>(
                Result<StatsOverlayResponse>.Success(original.Snapshot!), HttpStatusCode.OK, null, null));
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var after = store.TryGet(City)!;
            Assert.Same(original.Snapshot, after.Snapshot);
            Assert.Equal(original.ReceivedTimestamp, after.ReceivedTimestamp);
            Assert.Equal(original.RefreshBoundaryUtc, after.RefreshBoundaryUtc);
            Assert.Null(after.ProblemCode);
            Assert.True(after.LastContactUtc > original.LastContactUtc);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task ResumeAndManualRetryCannotBypassSelectionCooldown()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var visibility = new StatsSnapshotStoreTests.VisibilitySpy();
        var store = new StatsSnapshotStore(endpoints, clock, visibility,
            new StatsSnapshotStoreTests.NavigationSpy(), statsEnabled: true, preparationEnabled: false);
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(City);
            var firstAttempt = store.EnsureRequestedAsync(City);
            var firstCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            firstCall.Return(Failure(HttpStatusCode.ServiceUnavailable, "read_timeout"));
            await firstAttempt.WaitAsync(TimeSpan.FromSeconds(2));

            visibility.Raise(true);
            clock.Advance(TimeSpan.FromSeconds(30));
            visibility.Raise(false);
            var manual = store.EnsureRequestedAsync(City);
            Assert.Single(endpoints.Calls);
            await manual.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromSeconds(30));
            var eligible = store.EnsureRequestedAsync(City);
            var recovered = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            recovered.Return(Success(clock, City, 60));
            await eligible.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, endpoints.Calls.Count);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task HidingCancelsBlockedScheduledRefreshButKeepsAcceptedEvidence()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var visibility = new StatsSnapshotStoreTests.VisibilitySpy();
        var store = new StatsSnapshotStore(endpoints, clock, visibility,
            new StatsSnapshotStoreTests.NavigationSpy(), statsEnabled: true, preparationEnabled: false,
            jitter: new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero));
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(City);
            var initial = store.EnsureRequestedAsync(City);
            var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            initialCall.Return(Success(clock, City, 15));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromMinutes(15));
            var refresh = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await refresh.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, visibility.SubscriberCount);
            visibility.Raise(true);
            Assert.True(refresh.CancellationToken.IsCancellationRequested);
            await store.EnsureRequestedAsync(City).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, endpoints.Calls.Count);
            visibility.Raise(false);
            await refresh.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var resumed = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("city", resumed.Request.Scope);
            var resumedJoin = store.EnsureRequestedAsync(City);
            resumed.Return(Success(clock, City, 15));
            await resumedJoin.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(store.TryGet(City)?.Snapshot);
            Assert.Null(store.TryGet(City)?.ProblemCode);
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(3, endpoints.Calls.Count);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task ClosingOverlayDoesNotCancelItsStoreOwnedRefresh()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false,
            jitter: new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero, TimeSpan.Zero));
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(City);
            var initial = store.EnsureRequestedAsync(City);
            var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            initialCall.Return(Success(clock, City, 15));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromMinutes(15));
            var refresh = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            store.SetOverlaySelection(null);
            Assert.False(refresh.CancellationToken.IsCancellationRequested);

            refresh.Return(Success(clock, City, 15));
            await store.EnsureRequestedAsync(City).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(store.TryGet(City)?.Snapshot);
            Assert.False(refresh.CancellationToken.IsCancellationRequested);
            Assert.Equal(2, endpoints.Calls.Count);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task ResumeAfterSeveralBoundariesRefreshesDisplayedSelectionOnceBeforeBaselines()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var visibility = new StatsSnapshotStoreTests.VisibilitySpy();
        var store = new StatsSnapshotStore(endpoints, clock, visibility,
            new StatsSnapshotStoreTests.NavigationSpy(), statsEnabled: true, preparationEnabled: true,
            jitter: new StatsSnapshotStoreTests.FixedJitterSamples(Enumerable.Repeat(TimeSpan.Zero, 8).ToArray()));
        var all = new StatsSelection("all", null, []);
        var filtered = new StatsSelection("city", "toronto", ["route:a"]);
        try
        {
            await store.StartAsync("toronto");
            var cityJoin = store.EnsureRequestedAsync(City);
            var cityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            cityCall.Return(Success(clock, City, 15));
            await cityJoin.WaitAsync(TimeSpan.FromSeconds(2));

            var allJoin = store.EnsureRequestedAsync(all);
            var allCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            allCall.Return(Success(clock, all, 15));
            await allJoin.WaitAsync(TimeSpan.FromSeconds(2));

            var filteredJoin = store.EnsureRequestedAsync(filtered);
            var filteredCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            filteredCall.Return(Success(clock, filtered, 15));
            await filteredJoin.WaitAsync(TimeSpan.FromSeconds(2));
            store.SetOverlaySelection(filtered);

            visibility.Raise(true);
            clock.Advance(TimeSpan.FromMinutes(31));
            visibility.Raise(false);

            var resumedFiltered = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { "route:a" }, resumedFiltered.Request.RouteJoinKeys);
            var resumedFilteredJoin = store.EnsureRequestedAsync(filtered);
            resumedFiltered.Return(Success(clock, filtered, 15));
            await resumedFilteredJoin.WaitAsync(TimeSpan.FromSeconds(2));

            var resumedCity = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("city", resumedCity.Request.Scope);
            Assert.Empty(resumedCity.Request.RouteJoinKeys ?? Array.Empty<string>());
            var resumedCityJoin = store.EnsureRequestedAsync(City);
            resumedCity.Return(Success(clock, City, 15));
            await resumedCityJoin.WaitAsync(TimeSpan.FromSeconds(2));

            var resumedAll = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("all", resumedAll.Request.Scope);
            var resumedAllJoin = store.EnsureRequestedAsync(all);
            resumedAll.Return(Success(clock, all, 15));
            await resumedAllJoin.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(6, endpoints.Calls.Count);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task ResumeRemovesHardExpiredEvidenceBeforeStartingReplacementRequest()
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var visibility = new StatsSnapshotStoreTests.VisibilitySpy();
        var store = new StatsSnapshotStore(endpoints, clock, visibility,
            new StatsSnapshotStoreTests.NavigationSpy(), statsEnabled: true, preparationEnabled: false,
            jitter: new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero, TimeSpan.Zero));
        var order = new List<string>();
        store.StateChanged += (selection, state) =>
        {
            if (SelectionEquals(selection, City) && state.Snapshot is null) order.Add("expired");
        };
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(City);
            var initial = store.EnsureRequestedAsync(City);
            var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            initialCall.Return(Success(clock, City, 15));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));
            endpoints.CallCreated += _ => order.Add("request");

            visibility.Raise(true);
            clock.Advance(TimeSpan.FromMinutes(46));
            visibility.Raise(false);

            var replacement = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { "expired", "request" }, order);
            Assert.Null(store.TryGet(City)?.Snapshot);

            replacement.Return(Success(clock, City, 15));
            await store.EnsureRequestedAsync(City).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task StaleAndHardExpiryTransitionsWakeWhileScheduledRefreshIsBlocked()
    {
        var harness = await StartStoreAsync(15, TimeSpan.Zero);
        var stale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Store.StateChanged += (selection, state) =>
        {
            if (!SelectionEquals(selection, City)) return;
            if (state.StaleNotified) stale.TrySetResult();
            if (state.Freshness.IsExpired || state.Snapshot is null) expired.TrySetResult();
        };
        try
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(15));
            _ = await harness.Endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            harness.Clock.Advance(TimeSpan.FromSeconds(30));
            await stale.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(harness.Store.TryGet(City)!.Freshness.IsStale);

            harness.Clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(30));
            await expired.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Null(harness.Store.TryGet(City));
        }
        finally { await harness.Store.StopAsync(); }
    }

    [Fact]
    public async Task LongRetryAfterDoesNotDelayStaleOrHardExpiryTransitions()
    {
        var harness = await StartStoreAsync(1, TimeSpan.Zero, TimeSpan.Zero);
        var failurePublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Store.StateChanged += (selection, state) =>
        {
            if (!SelectionEquals(selection, City)) return;
            if (state.ProblemCode == "rate_limited") failurePublished.TrySetResult();
            if (state.StaleNotified) stale.TrySetResult();
            if (state.Snapshot is null) expired.TrySetResult();
        };
        try
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            var failedRefresh = await harness.Endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var refreshCompleted = harness.Store.EnsureRequestedAsync(City);
            failedRefresh.Return(Failure(HttpStatusCode.TooManyRequests, "rate_limited", TimeSpan.FromMinutes(10)));
            await failurePublished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            // Publication precedes scheduler recomputation; finish the refresh before advancing fake time.
            await refreshCompleted.WaitAsync(TimeSpan.FromSeconds(2));

            harness.Clock.Advance(TimeSpan.FromSeconds(30));
            await stale.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(harness.Store.TryGet(City)!.Freshness.IsStale);
            for (var attempt = 0; attempt < 100 && harness.Clock.ActiveTimerCount == 0; attempt++)
                await Task.Yield();
            Assert.Equal(1, harness.Clock.ActiveTimerCount);

            harness.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
            var almostExpired = Assert.IsType<StatsSnapshotState>(harness.Store.TryGet(City));
            Assert.Equal(TimeSpan.FromMinutes(3), almostExpired.HardExpiryAfter);
            Assert.NotNull(almostExpired.Snapshot);
            harness.Clock.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(TimeSpan.FromMinutes(3), harness.Clock.GetElapsedTime(0, harness.Clock.GetTimestamp()));
            Assert.Null(harness.Store.TryGet(City));
            await expired.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, harness.Endpoints.Calls.Count);
        }
        finally { await harness.Store.StopAsync(); }
    }

    [Fact]
    public async Task RootDayEndIsPublishedAsHistoricalAndCannotCreateAnImmediateRefreshLoop()
    {
        var start = new DateTimeOffset(2026, 10, 30, 23, 45, 0, TimeSpan.Zero);
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(start);
        var jitter = new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero, TimeSpan.Zero);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false, jitter: jitter);
        store.SetOverlaySelection(City);
        var historical = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StateChanged += (selection, state) =>
        {
            if (SelectionEquals(selection, City) && state.Freshness.IsHistorical)
                historical.TrySetResult();
        };
        try
        {
            await store.StartAsync("toronto");
            var initial = store.EnsureRequestedAsync(City);
            var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            initialCall.Return(Success(clock, City, 15, includePreviousDay: true));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));
            var originalTimestamp = store.TryGet(City)!.ReceivedTimestamp;

            clock.Advance(TimeSpan.FromMinutes(15));
            await historical.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var refresh = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var state = store.TryGet(City)!;
            Assert.True(state.Freshness.IsHistorical);
            Assert.Equal(new DateOnly(2026, 10, 30), state.Snapshot!.Cities[0].LocalDate);
            Assert.Equal(new DateTime(2026, 10, 30, 23, 45, 0, DateTimeKind.Utc), state.Snapshot.GeneratedAtUtc);
            Assert.Equal(new DateOnly(2026, 10, 29), state.Snapshot.PreviousDay!.City.LocalDate);
            Assert.Equal(originalTimestamp, state.ReceivedTimestamp);

            clock.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(2, endpoints.Calls.Count);
            refresh.Return(Success(clock, City, 15));
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task AllSelectionUsesEarliestUtcCityDayEndAcrossDstWindows()
    {
        var start = new DateTimeOffset(2026, 11, 1, 6, 50, 0, TimeSpan.Zero);
        var selection = new StatsSelection("all", null, []);
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false,
            jitter: new StatsSnapshotStoreTests.FixedJitterSamples(TimeSpan.Zero, TimeSpan.Zero));
        store.SetOverlaySelection(selection);
        var historical = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StateChanged += (changedSelection, state) =>
        {
            if (SelectionEquals(changedSelection, selection) && state.Freshness.IsHistorical)
                historical.TrySetResult();
        };
        try
        {
            await store.StartAsync("toronto");
            var initial = store.EnsureRequestedAsync(selection);
            var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var response = DstAllResponse(selection, start.UtcDateTime);
            initialCall.Return(new HttpServiceResponse<StatsOverlayResponse>(
                Result<StatsOverlayResponse>.Success(response), HttpStatusCode.OK, null, null));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromMinutes(10));
            await historical.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var refresh = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var state = store.TryGet(selection)!;
            Assert.True(state.Freshness.IsHistorical);
            Assert.Equal(new DateOnly(2026, 10, 31), state.Snapshot!.Cities.Single(city => city.CitySlug == "los-angeles").LocalDate);
            Assert.Equal(new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc),
                state.Snapshot.Cities.Single(city => city.CitySlug == "los-angeles").LocalDayEndUtc);
            Assert.Equal(new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc),
                state.Snapshot.Cities.Single(city => city.CitySlug == "new-york").LocalDayEndUtc);

            refresh.Return(Success(clock, selection, 15));
            await store.EnsureRequestedAsync(selection).WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.Equal(2, endpoints.Calls.Count);
        }
        finally { await store.StopAsync(); }
    }

    static async Task<ScheduleHarness> StartStoreAsync(int intervalMinutes, params TimeSpan[] jitterValues)
    {
        var endpoints = new StatsSnapshotStoreTests.ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var jitter = new StatsSnapshotStoreTests.FixedJitterSamples(jitterValues);
        var store = new StatsSnapshotStore(endpoints, clock,
            new StatsSnapshotStoreTests.VisibilitySpy(), new StatsSnapshotStoreTests.NavigationSpy(),
            statsEnabled: true, preparationEnabled: false, jitter: jitter);
        await store.StartAsync("toronto");
        store.SetOverlaySelection(City);
        var initial = store.EnsureRequestedAsync(City);
        var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        initialCall.Return(Success(clock, City, intervalMinutes));
        await initial.WaitAsync(TimeSpan.FromSeconds(2));
        return new ScheduleHarness(store, endpoints, clock, jitter);
    }

    static HttpServiceResponse<StatsOverlayResponse> Success(
        StatsSnapshotTestTimeProvider clock, StatsSelection selection, int intervalMinutes, bool includePreviousDay = false)
    {
        var generated = clock.GetUtcNow().UtcDateTime;
        var next = generated.AddMinutes(intervalMinutes);
        var response = StatsSnapshotAcceptanceTests.ValidResponse(selection, generated) with
        {
            RefreshIntervalMinutes = intervalMinutes,
            RefreshWindowStartUtc = generated,
            NextRefreshAtUtc = next
        };
        if (includePreviousDay) response = response with { PreviousDay = CreatePreviousDay(response) };
        return new HttpServiceResponse<StatsOverlayResponse>(
            Result<StatsOverlayResponse>.Success(response), HttpStatusCode.OK, null, null);
    }

    static HttpServiceResponse<StatsOverlayResponse> Failure(
        HttpStatusCode statusCode, string problemCode, TimeSpan? retryAfter = null) => new(
        Result<StatsOverlayResponse>.Error(problemCode), statusCode, problemCode, retryAfter);

    static StatsPreviousDaySnapshot CreatePreviousDay(StatsOverlayResponse current)
    {
        var city = current.Cities[0];
        var previousStart = city.LocalDayStartUtc.AddDays(-1);
        var previousCity = city with
        {
            LocalDate = city.LocalDate.AddDays(-1),
            LocalDayStartUtc = previousStart,
            LocalDayEndUtc = city.LocalDayStartUtc,
            RequestedTodayStartUtc = previousStart,
            RequestedTodayEndUtc = city.LocalDayStartUtc,
            EligibleTodayStartUtc = null,
            EligibleTodayEndUtc = null
        };
        return new StatsPreviousDaySnapshot
        {
            City = previousCity,
            Distance = current.TodayDistance,
            ReportedVehicles = StatsReportedVehicleSummary.Unavailable("today"),
            CategoryShares = [],
            Bubbles = [],
            Timeline = current.Timeline
        };
    }

    static StatsOverlayResponse DstAllResponse(StatsSelection selection, DateTime generated)
    {
        var cityTemplate = StatsSnapshotAcceptanceTests.ValidResponse(selection, generated).Cities[0];
        var cutoff = new DateTime(generated.Ticks - generated.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
        var newYork = CreateCityWindow(cityTemplate, "new-york", "America/New_York",
            new DateOnly(2026, 11, 1),
            new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc), cutoff);
        var losAngeles = CreateCityWindow(cityTemplate, "los-angeles", "America/Los_Angeles",
            new DateOnly(2026, 10, 31),
            new DateTime(2026, 10, 31, 7, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc), cutoff);
        var next = new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc);
        return StatsSnapshotAcceptanceTests.ValidResponse(selection, generated) with
        {
            Cities = [newYork, losAngeles],
            RefreshIntervalMinutes = 15,
            RefreshWindowStartUtc = next.AddMinutes(-15),
            NextRefreshAtUtc = next
        };
    }

    static StatsCityWindow CreateCityWindow(
        StatsCityWindow template, string slug, string zone, DateOnly date,
        DateTime start, DateTime end, DateTime cutoff)
    {
        var requestedEnd = cutoff < start ? start : cutoff > end ? end : cutoff;
        return template with
        {
            CitySlug = slug,
            DisplayNameKey = slug,
            TimeZone = zone,
            LocalDate = date,
            LocalDayStartUtc = start,
            LocalDayEndUtc = end,
            RequestedTodayStartUtc = start,
            RequestedTodayEndUtc = requestedEnd,
            Last24StartUtc = cutoff.AddHours(-24),
            Last24EndUtc = cutoff
        };
    }

    static bool SelectionEquals(StatsSelection actual, StatsSelection expected) =>
        actual.Scope == expected.Scope
        && actual.CitySlug == expected.CitySlug
        && actual.RouteJoinKeys.SequenceEqual(expected.RouteJoinKeys, StringComparer.Ordinal);

    sealed record ScheduleHarness(
        StatsSnapshotStore Store,
        StatsSnapshotStoreTests.ControlledStatsEndpoints Endpoints,
        StatsSnapshotTestTimeProvider Clock,
        StatsSnapshotStoreTests.FixedJitterSamples Jitter);
}
