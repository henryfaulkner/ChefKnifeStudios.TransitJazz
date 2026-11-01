using System.Collections.Concurrent;
using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using System.Net;
using System.Threading.Channels;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsSnapshotStoreTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartReturnsWhileCurrentCityPreparationIsBlocked()
    {
        var endpoints = new ControlledStatsEndpoints();
        var visibility = new VisibilitySpy();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start), visibility, new NavigationSpy(), preparationEnabled: true);
        using var lifetime = new CancellationTokenSource();
        var published = 0;
        store.StateChanged += (_, _) => published++;

        var start = store.StartAsync("toronto", lifetime.Token);
        StatsEndpointCall? firstCall = null;
        try
        {
            firstCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await firstCall.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(start.IsCompleted);
            Assert.Equal("city", firstCall.Request.Scope);
            Assert.Equal("toronto", firstCall.Request.CitySlug);
        }
        finally
        {
            await store.StopAsync();
        }
        Assert.True(firstCall!.CancellationToken.IsCancellationRequested);
        Assert.True(firstCall.Completed.Task.IsCompleted);
        firstCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(
            new StatsSelection("city", "toronto", []), Start.UtcDateTime)));
        Assert.Equal(0, published);
        Assert.Null(store.TryGet(new StatsSelection("city", "toronto", []))?.Snapshot);
    }

    [Fact]
    public async Task VisibilityChangeDuringInitialReadOverridesTheOlderReadResult()
    {
        var endpoints = new ControlledStatsEndpoints();
        var visibility = new VisibilitySpy { PendingHiddenRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start), visibility,
            new NavigationSpy(), preparationEnabled: true);
        try
        {
            await store.StartAsync("toronto");
            await visibility.HiddenReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            visibility.Raise(true);
            visibility.ReleaseHiddenRead(false);

            await Assert.ThrowsAsync<TimeoutException>(() => endpoints.WaitForCallAsync().AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(250)));
            Assert.Empty(endpoints.Calls);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task StopDuringInitialVisibilityReadWaitsAndDoesNotStartPreparationAfterRelease()
    {
        var endpoints = new ControlledStatsEndpoints();
        var visibility = new VisibilitySpy { PendingHiddenRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var navigation = new NavigationSpy();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start), visibility,
            navigation, preparationEnabled: true);
        await store.StartAsync("toronto");
        await visibility.HiddenReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stop = store.StopAsync();
        Assert.False(stop.IsCompleted);
        visibility.ReleaseHiddenRead(false);
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        await store.StopAsync();

        Assert.Empty(endpoints.Calls);
        Assert.Equal(0, visibility.SubscriberCount);
        Assert.Equal(0, navigation.SubscriberCount);
        Assert.Equal(0, visibility.AddListenerCalls);
        Assert.Equal(0, visibility.RemoveListenerCalls);
    }

    [Fact]
    public async Task StopDuringScheduledDelayDisposesTimerAndUnsubscribesSharedSignals()
    {
        var endpoints = new ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var visibility = new VisibilitySpy();
        var navigation = new NavigationSpy();
        var store = new StatsSnapshotStore(endpoints, clock, visibility, navigation,
            statsEnabled: true, preparationEnabled: false,
            jitter: new FixedJitterSamples(TimeSpan.Zero));
        var selection = new StatsSelection("city", "toronto", []);
        var notifications = 0;
        store.StateChanged += (_, _) => notifications++;
        await store.StartAsync("toronto");
        store.SetOverlaySelection(selection);
        var initial = store.EnsureRequestedAsync(selection);
        var call = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        call.Return(SuccessResponse(ScheduledResponse(selection)));
        await initial.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, clock.ActiveTimerCount);
        Assert.Equal(1, visibility.SubscriberCount);
        Assert.Equal(1, navigation.SubscriberCount);
        Assert.Equal(0, visibility.AddListenerCalls);

        await store.StopAsync();
        await store.StopAsync();
        var notificationCount = notifications;
        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.Equal(0, visibility.SubscriberCount);
        Assert.Equal(0, navigation.SubscriberCount);
        Assert.Equal(0, visibility.RemoveListenerCalls);

        visibility.Raise(false);
        navigation.NavigateTo("montreal");
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(notificationCount, notifications);
        Assert.Single(endpoints.Calls);
    }

    [Fact]
    public async Task RepeatedStartAndOpeningJoinOneCityRequestBeforeAllPreparation()
    {
        var endpoints = new ControlledStatsEndpoints();
        var visibility = new VisibilitySpy();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start), visibility, new NavigationSpy(), preparationEnabled: true);
        using var lifetime = new CancellationTokenSource();
        var selection = new StatsSelection("city", "toronto", []);
        var changes = new List<StatsSnapshotState>();
        store.StateChanged += (_, state) => changes.Add(state);

        var start = store.StartAsync("toronto", lifetime.Token);
        try
        {
            var cityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await cityCall.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await store.StartAsync("toronto", lifetime.Token);
            store.SetOverlaySelection(selection);
            var open = store.EnsureRequestedAsync(selection);

            Assert.Single(endpoints.Calls);
            Assert.Equal("city", cityCall.Request.Scope);
            Assert.False(open.IsCompleted);

            cityCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(selection, Start.UtcDateTime)));
            await open.WaitAsync(TimeSpan.FromSeconds(2));
            var allCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await allCall.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("all", allCall.Request.Scope);
            Assert.Equal(2, endpoints.Calls.Count);
            var allOpen = store.EnsureRequestedAsync(new StatsSelection("all", null, []));

            allCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(new StatsSelection("all", null, []), Start.UtcDateTime)));
            await allOpen.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(start.IsCompleted);
            Assert.Equal(2, changes.Count);
            Assert.NotNull(store.TryGet(selection)?.Snapshot);
            Assert.Null(store.TryGet(new StatsSelection("city", "toronto", ["route:a"])));
        }
        finally
        {
            await store.StopAsync();
        }
    }

    [Fact]
    public async Task NavigationDropsLateFilteredWorkAndKeepsIndependentAllBaseline()
    {
        var endpoints = new ControlledStatsEndpoints
        {
            IgnoreCancellationForCall = call => call.Request.RouteJoinKeys is { } keys
                && keys.SequenceEqual(["route:a"], StringComparer.Ordinal)
        };
        var navigation = new NavigationSpy();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start),
            new VisibilitySpy(), navigation, statsEnabled: true, preparationEnabled: true);
        var toronto = new StatsSelection("city", "toronto", []);
        var all = new StatsSelection("all", null, []);
        var filtered = new StatsSelection("city", "toronto", ["route:a"]);
        var montreal = new StatsSelection("city", "montreal", []);
        var notifications = new List<StatsSelection>();
        store.StateChanged += (selection, state) =>
        {
            if (state.Snapshot is not null) notifications.Add(selection);
        };
        try
        {
            await store.StartAsync("toronto");
            var cityJoin = store.EnsureRequestedAsync(toronto);
            var cityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            cityCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(toronto, Start.UtcDateTime)));
            await cityJoin.WaitAsync(TimeSpan.FromSeconds(2));

            var allJoin = store.EnsureRequestedAsync(all);
            var allCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            allCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(all, Start.UtcDateTime)));
            await allJoin.WaitAsync(TimeSpan.FromSeconds(2));
            notifications.Clear();

            store.SetOverlaySelection(filtered);
            var filteredJoin = store.EnsureRequestedAsync(filtered);
            var oldFilteredCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                navigation.NavigateTo("montreal");
                var montrealJoin = store.EnsureRequestedAsync(montreal);
                await oldFilteredCall.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

                oldFilteredCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(
                    filtered, Start.AddMinutes(1).UtcDateTime)));
                await filteredJoin.WaitAsync(TimeSpan.FromSeconds(2));
                var montrealCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                montrealCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(
                    montreal, Start.UtcDateTime)));
                await montrealJoin.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                oldFilteredCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(
                    filtered, Start.AddMinutes(1).UtcDateTime)));
            }

            Assert.Null(store.TryGet(filtered));
            Assert.Null(store.TryGet(toronto));
            Assert.NotNull(store.TryGet(montreal)?.Snapshot);
            Assert.NotNull(store.TryGet(all)?.Snapshot);
            Assert.Single(notifications);
            Assert.Equal("montreal", Assert.Single(notifications).CitySlug);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task RouteChangeSupersedesInFlightFilteredGeneration()
    {
        var endpoints = new ControlledStatsEndpoints
        {
            IgnoreCancellationForCall = call => call.Request.RouteJoinKeys is { } keys
                && keys.SequenceEqual(["route:a"], StringComparer.Ordinal)
        };
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start),
            new VisibilitySpy(), new NavigationSpy());
        var oldSelection = new StatsSelection("city", "toronto", ["route:a"]);
        var displayedSelection = new StatsSelection("city", "toronto", ["route:b"]);
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(oldSelection);
            var oldJoin = store.EnsureRequestedAsync(oldSelection);
            var oldCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            store.SetOverlaySelection(displayedSelection);
            var displayedJoin = store.EnsureRequestedAsync(displayedSelection);
            await oldCall.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(endpoints.Calls);
            oldCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(
                oldSelection, Start.UtcDateTime)));
            await oldJoin.WaitAsync(TimeSpan.FromSeconds(2));

            var displayedCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            displayedCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(
                displayedSelection, Start.UtcDateTime)));
            await displayedJoin.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Null(store.TryGet(oldSelection));
            Assert.NotNull(store.TryGet(displayedSelection)?.Snapshot);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task CurrentCityBaselineCanFinishAfterOverlayCloses()
    {
        var endpoints = new ControlledStatsEndpoints();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start),
            new VisibilitySpy(), new NavigationSpy());
        var baseline = new StatsSelection("city", "toronto", []);
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(new StatsSelection("city", "toronto", ["route:a"]));
            var baselineJoin = store.EnsureRequestedAsync(baseline);
            var baselineCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            store.SetOverlaySelection(null);
            Assert.False(baselineCall.CancellationToken.IsCancellationRequested);

            baselineCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(
                baseline, Start.UtcDateTime)));
            await baselineJoin.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(store.TryGet(baseline)?.Snapshot);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task InvalidRefreshPublishesOneAtomicStateAndPreservesAcceptedEvidence()
    {
        var endpoints = new ControlledStatsEndpoints();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock, new VisibilitySpy(), new NavigationSpy());
        var selection = new StatsSelection("city", "toronto", []);
        var changes = new List<StatsSnapshotState>();
        store.StateChanged += (changedSelection, state) =>
        {
            if (changedSelection.Scope == selection.Scope && changedSelection.CitySlug == selection.CitySlug)
                changes.Add(state);
        };
        try
        {
            await store.StartAsync("toronto");
            store.SetOverlaySelection(selection);
            var initial = store.EnsureRequestedAsync(selection);
            var initialCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            initialCall.Return(SuccessResponse(ScheduledResponse(selection)));
            await initial.WaitAsync(TimeSpan.FromSeconds(2));
            var accepted = store.TryGet(selection)!;
            changes.Clear();

            clock.Advance(TimeSpan.FromHours(1));
            var refresh = store.EnsureRequestedAsync(selection);
            var refreshCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var malformed = ScheduledResponse(selection) with { Timeline = null! };
            refreshCall.Return(SuccessResponse(malformed));
            await refresh.WaitAsync(TimeSpan.FromSeconds(2));

            var state = store.TryGet(selection)!;
            Assert.Single(changes);
            Assert.Same(accepted.Snapshot, state.Snapshot);
            Assert.Equal(accepted.ReceivedTimestamp, state.ReceivedTimestamp);
            Assert.Equal("invalid_response", state.ProblemCode);
            var notification = Assert.Single(changes);
            Assert.Same(state.Snapshot, notification.Snapshot);
            Assert.Equal("invalid_response", notification.ProblemCode);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task RetentionAdmitsExactFitAndExcludesOneByteOverLatestFilteredSlot()
    {
        var endpoints = new ControlledStatsEndpoints();
        long Estimate(StatsOverlayResponse response) => response.Selection.Scope == "all" ? 50
            : response.Selection.RouteJoinKeys.Count == 0 ? 40 : 10;
        var store = await StartStoreAsync(endpoints, 100, Estimate);
        var city = new StatsSelection("city", "toronto", []);
        var all = new StatsSelection("all", null, []);
        var filtered = new StatsSelection("city", "toronto", ["route:a"]);
        try
        {
            await FetchAsync(store, endpoints, city);
            await FetchAsync(store, endpoints, all);
            await FetchAsync(store, endpoints, filtered);

            Assert.Equal(40, store.TryGet(city)!.EstimatedRetainedBytes);
            Assert.Equal(50, store.TryGet(all)!.EstimatedRetainedBytes);
            Assert.Equal(10, store.TryGet(filtered)!.EstimatedRetainedBytes);
            Assert.Equal(100, new[] { city, all, filtered }.Sum(selection => store.TryGet(selection)!.EstimatedRetainedBytes));
        }
        finally { await store.StopAsync(); }

        endpoints = new ControlledStatsEndpoints();
        var overStore = await StartStoreAsync(endpoints, 100, response => response.Selection.Scope == "all" ? 50
            : response.Selection.RouteJoinKeys.Count == 0 ? 40 : 11);
        try
        {
            await FetchAsync(overStore, endpoints, city);
            await FetchAsync(overStore, endpoints, all);
            await FetchAsync(overStore, endpoints, filtered);

            Assert.NotNull(overStore.TryGet(city));
            Assert.NotNull(overStore.TryGet(all));
            Assert.Null(overStore.TryGet(filtered));
        }
        finally { await overStore.StopAsync(); }
    }

    [Fact]
    public async Task RetentionPriorityKeepsCityAndAllBeforeFilteredEvidence()
    {
        var endpoints = new ControlledStatsEndpoints();
        long Estimate(StatsOverlayResponse response) => response.Selection.Scope == "all" ? 60
            : response.Selection.RouteJoinKeys.Count == 0 ? 40 : 40;
        var store = await StartStoreAsync(endpoints, 100, Estimate);
        var city = new StatsSelection("city", "toronto", []);
        var all = new StatsSelection("all", null, []);
        var filtered = new StatsSelection("city", "toronto", ["route:a"]);
        try
        {
            await FetchAsync(store, endpoints, all);
            await FetchAsync(store, endpoints, filtered);
            await FetchAsync(store, endpoints, city);

            Assert.NotNull(store.TryGet(city));
            Assert.NotNull(store.TryGet(all));
            Assert.Null(store.TryGet(filtered));
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task NewRouteSetReplacesTheLatestFilteredRetentionSlot()
    {
        var endpoints = new ControlledStatsEndpoints();
        var store = await StartStoreAsync(endpoints, 100, _ => 20);
        var first = new StatsSelection("city", "toronto", ["route:a"]);
        var next = new StatsSelection("city", "toronto", ["route:b"]);
        try
        {
            await FetchAsync(store, endpoints, first);
            await FetchAsync(store, endpoints, next);

            Assert.Null(store.TryGet(first));
            Assert.NotNull(store.TryGet(next));
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task OversizedEvidenceIsDeliveredToActiveConsumerButNotRetainedAndAdmissionEstimatesOnce()
    {
        var endpoints = new ControlledStatsEndpoints();
        var estimates = 0;
        var store = await StartStoreAsync(endpoints, 100, _ => { estimates++; return 101; });
        var selection = new StatsSelection("city", "toronto", []);
        StatsSnapshotState? delivered = null;
        store.StateChanged += (_, state) => delivered = state;
        try
        {
            await FetchAsync(store, endpoints, selection);

            Assert.NotNull(delivered?.Snapshot);
            Assert.Null(store.TryGet(selection));
            Assert.Equal(1, estimates);
            _ = store.TryGet(selection);
            _ = store.TryGet(selection);
            Assert.Equal(1, estimates);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task StatsDisabledResponseSuppressesAutomaticPreparationUntilConfigurationReevaluation()
    {
        var endpoints = new ControlledStatsEndpoints();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start), new VisibilitySpy(),
            new NavigationSpy(), statsEnabled: true, preparationEnabled: true);
        var city = new StatsSelection("city", "toronto", []);
        try
        {
            await store.StartAsync("toronto");
            var cityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var join = store.EnsureRequestedAsync(city);
            cityCall.Return(new HttpServiceResponse<StatsOverlayResponse>(
                Result<StatsOverlayResponse>.Error("disabled"), HttpStatusCode.ServiceUnavailable, "stats_disabled", null));
            await join.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(store.IsAutomaticWorkSuppressed);
            store.ReevaluateConfiguration();
            Assert.False(store.IsAutomaticWorkSuppressed);
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task DiagnosticsUseBoundedEventsAndQuantizedSnapshotAge()
    {
        var endpoints = new ControlledStatsEndpoints();
        var diagnostics = new SnapshotDiagnosticsSpy();
        var clock = new StatsSnapshotTestTimeProvider(Start);
        var store = new StatsSnapshotStore(endpoints, clock,
            new VisibilitySpy(), new NavigationSpy(), statsEnabled: true, preparationEnabled: true,
            estimateRetainedBytes: _ => 100, diagnostics: diagnostics);
        var city = new StatsSelection("city", "toronto", []);
        var all = new StatsSelection("all", null, []);
        try
        {
            await store.StartAsync("toronto");
            var cityCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            cityCall.Return(SuccessResponse(ScheduledResponse(city)));
            var allCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var allJoin = store.EnsureRequestedAsync(all);
            allCall.Return(SuccessResponse(ScheduledResponse(all)));
            await allJoin.WaitAsync(TimeSpan.FromSeconds(2));
            await diagnostics.WaitForEventAsync(StatsSnapshotDiagnosticEvent.PreparationCompleted);

            clock.AdvanceMonotonic(TimeSpan.FromMinutes(3));
            store.RecordOverlayOpened(store.TryGet(city), showLoader: false);
            store.RecordOverlayOpened(null, showLoader: true);

            Assert.Contains(diagnostics.Records, value => value.Event == StatsSnapshotDiagnosticEvent.WarmOpen);
            Assert.Contains(diagnostics.Records, value => value.Event == StatsSnapshotDiagnosticEvent.Loader);
            Assert.Contains(diagnostics.Records, value => value is
                { Event: StatsSnapshotDiagnosticEvent.SnapshotAgeOnOpen, Age: StatsSnapshotAgeBucket.OneToFiveMinutes });
            Assert.All(diagnostics.Records, value => Assert.IsType<StatsSnapshotDiagnosticEvent>(value.Event));
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task DiagnosticsRecordSkippedAdmissionAndFailureWithoutChangingRetrievalResults()
    {
        var endpoints = new ControlledStatsEndpoints();
        var diagnostics = new SnapshotDiagnosticsSpy();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start),
            new VisibilitySpy(), new NavigationSpy(), maximumRetainedBytes: 100,
            estimateRetainedBytes: _ => 101, diagnostics: diagnostics);
        var selection = new StatsSelection("city", "toronto", []);
        var failedSelection = new StatsSelection("city", "toronto", ["route:a"]);
        try
        {
            await store.StartAsync("toronto");
            var successful = store.EnsureRequestedAsync(selection);
            var acceptedCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            acceptedCall.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(selection, Start.UtcDateTime)));
            await successful.WaitAsync(TimeSpan.FromSeconds(2));

            var failed = store.EnsureRequestedAsync(failedSelection);
            var failedCall = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            failedCall.Return(new HttpServiceResponse<StatsOverlayResponse>(
                Result<StatsOverlayResponse>.Error("unavailable"), HttpStatusCode.ServiceUnavailable,
                "dependency_unavailable", null));
            await failed.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Contains(diagnostics.Records, value => value.Event == StatsSnapshotDiagnosticEvent.AdmissionSkipped);
            Assert.Contains(diagnostics.Records, value => value.Event == StatsSnapshotDiagnosticEvent.Failure);
            Assert.Null(store.TryGet(selection));
        }
        finally { await store.StopAsync(); }
    }

    [Fact]
    public async Task ThrowingDiagnosticDeliveryDoesNotFailSnapshotRetrieval()
    {
        var endpoints = new ControlledStatsEndpoints();
        var diagnostics = new ThrowingSnapshotDiagnostics();
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start),
            new VisibilitySpy(), new NavigationSpy(), diagnostics: diagnostics);
        var selection = new StatsSelection("city", "toronto", []);
        try
        {
            await store.StartAsync("toronto");
            var pending = store.EnsureRequestedAsync(selection);
            var call = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            call.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(selection, Start.UtcDateTime)));
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            store.RecordOverlayOpened(store.TryGet(selection), showLoader: false);

            Assert.True(diagnostics.ThrowCount > 0);
            Assert.NotNull(store.TryGet(selection)?.Snapshot);
        }
        finally { await store.StopAsync(); }
    }

    static async Task<StatsSnapshotStore> StartStoreAsync(
        ControlledStatsEndpoints endpoints,
        long budget,
        Func<StatsOverlayResponse, long> estimate)
    {
        var store = new StatsSnapshotStore(endpoints, new StatsSnapshotTestTimeProvider(Start), new VisibilitySpy(),
            new NavigationSpy(), maximumRetainedBytes: budget, estimateRetainedBytes: estimate);
        await store.StartAsync("toronto");
        return store;
    }

    static async Task FetchAsync(StatsSnapshotStore store, ControlledStatsEndpoints endpoints, StatsSelection selection)
    {
        var pending = store.EnsureRequestedAsync(selection);
        var call = await endpoints.WaitForCallAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        call.Return(SuccessResponse(StatsSnapshotAcceptanceTests.ValidResponse(selection, Start.UtcDateTime)));
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
    }

    static StatsOverlayResponse ScheduledResponse(StatsSelection selection) =>
        StatsSnapshotAcceptanceTests.ValidResponse(selection, Start.UtcDateTime) with
        {
            RefreshIntervalMinutes = 60,
            RefreshWindowStartUtc = Start.UtcDateTime,
            NextRefreshAtUtc = Start.UtcDateTime.AddHours(1)
        };

    internal static HttpServiceResponse<StatsOverlayResponse> SuccessResponse(StatsOverlayResponse response) => new(
        Result<StatsOverlayResponse>.Success(response), HttpStatusCode.OK, null, null);

    [Fact]
    public async Task ManualClockUsesMonotonicTimersAndDisposesCanceledOrCompletedDelays()
    {
        var clock = new StatsSnapshotTestTimeProvider(Start);
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

        var called = false;
        var timer = clock.CreateTimer(_ => called = true, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        await timer.DisposeAsync();
        clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        Assert.False(called);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public void JitterSequenceSuppliesZeroInteriorAndMaximumSamplesDeterministically()
    {
        var samples = new FixedJitterSamples(TimeSpan.Zero, TimeSpan.FromMilliseconds(2500), TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.Zero, samples.Next());
        Assert.Equal(TimeSpan.FromMilliseconds(2500), samples.Next());
        Assert.Equal(TimeSpan.FromSeconds(5), samples.Next());
        Assert.Equal(3, samples.SamplesTaken);
    }

    internal sealed class ControlledStatsEndpoints : IStatsEndpointsService
    {
        readonly ConcurrentQueue<StatsEndpointCall> calls = new();
        readonly Channel<StatsEndpointCall> created = Channel.CreateUnbounded<StatsEndpointCall>();

        public IReadOnlyCollection<StatsEndpointCall> Calls => calls.ToArray();
        public Func<StatsEndpointCall, Task<HttpServiceResponse<StatsOverlayResponse>>>? Handler { get; set; }
        public bool IgnoreCancellation { get; set; }
        public Func<StatsEndpointCall, bool>? IgnoreCancellationForCall { get; set; }
        public event Action<StatsEndpointCall>? CallCreated;
        public ValueTask<StatsEndpointCall> WaitForCallAsync(CancellationToken cancellationToken = default) =>
            created.Reader.ReadAsync(cancellationToken);

        public Task<HttpServiceResponse<StatsOverlayResponse>> QueryAsync(
            StatsOverlayRequest request,
            CancellationToken cancellationToken = default)
        {
            var call = new StatsEndpointCall(request, cancellationToken);
            calls.Enqueue(call);
            created.Writer.TryWrite(call);
            CallCreated?.Invoke(call);
            return ExecuteAsync(call);
        }

        async Task<HttpServiceResponse<StatsOverlayResponse>> ExecuteAsync(StatsEndpointCall call)
        {
            using var registration = call.CancellationToken.Register(() => call.Canceled.TrySetResult());
            call.Started.TrySetResult();
            try
            {
                return Handler is { } handler
                    ? await handler(call)
                    : await call.WaitForResponseAsync(IgnoreCancellation || IgnoreCancellationForCall?.Invoke(call) == true);
            }
            finally
            {
                call.Completed.TrySetResult();
            }
        }
    }

    internal sealed class StatsEndpointCall(StatsOverlayRequest request, CancellationToken cancellationToken)
    {
        readonly TaskCompletionSource<HttpServiceResponse<StatsOverlayResponse>> response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StatsOverlayRequest Request { get; } = request;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Return(HttpServiceResponse<StatsOverlayResponse> value)
        {
            Released.TrySetResult();
            response.TrySetResult(value);
        }

        public void Throw(Exception error)
        {
            Released.TrySetResult();
            response.TrySetException(error);
        }

        public Task<HttpServiceResponse<StatsOverlayResponse>> WaitForResponseAsync(bool ignoreCancellation) =>
            ignoreCancellation ? response.Task : response.Task.WaitAsync(CancellationToken);
    }

    internal sealed class VisibilitySpy(bool initiallyHidden = false) : IPageVisibilityJsInterop
    {
        Action<bool>? visibilityChanged;
        public event Action<bool>? VisibilityChanged
        {
            add => visibilityChanged += value;
            remove => visibilityChanged -= value;
        }
        public bool Hidden { get; private set; } = initiallyHidden;
        public int AddListenerCalls { get; private set; }
        public int RemoveListenerCalls { get; private set; }
        public bool Disposed { get; private set; }
        public int SubscriberCount => visibilityChanged?.GetInvocationList().Length ?? 0;
        public TaskCompletionSource<bool>? PendingHiddenRead { get; set; }
        public TaskCompletionSource HiddenReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ListenerAdded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AddVisibilityChangeListenerAsync(Action<bool> onVisibilityChanged)
        {
            AddListenerCalls++;
            VisibilityChanged += onVisibilityChanged;
            ListenerAdded.TrySetResult();
            return Task.CompletedTask;
        }

        public Task RemoveVisibilityChangeListenerAsync()
        {
            RemoveListenerCalls++;
            return Task.CompletedTask;
        }

        public Task<bool> GetHiddenAsync()
        {
            HiddenReadStarted.TrySetResult();
            return PendingHiddenRead?.Task ?? Task.FromResult(Hidden);
        }

        public void ReleaseHiddenRead(bool hidden) => PendingHiddenRead?.TrySetResult(hidden);

        public void Raise(bool hidden)
        {
            Hidden = hidden;
            visibilityChanged?.Invoke(hidden);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class NavigationSpy : IStatsSnapshotNavigation
    {
        Action<string>? cityChanged;
        public event Action<string>? CityChanged
        {
            add => cityChanged += value;
            remove => cityChanged -= value;
        }
        public int SubscriberCount => cityChanged?.GetInvocationList().Length ?? 0;
        public void NavigateTo(string city) => cityChanged?.Invoke(city);
    }

    internal sealed class FixedJitterSamples(params TimeSpan[] values) : IStatsSnapshotJitter
    {
        readonly Queue<TimeSpan> remaining = new(values);
        public int SamplesTaken { get; private set; }

        public TimeSpan Next()
        {
            if (!remaining.TryDequeue(out var sample)) throw new InvalidOperationException("No jitter sample remains.");
            SamplesTaken++;
            return sample;
        }

        public TimeSpan NextDelay() => Next();
    }

    internal sealed class SnapshotDiagnosticsSpy : IStatsSnapshotDiagnostics
    {
        readonly ConcurrentQueue<(StatsSnapshotDiagnosticEvent Event, StatsSnapshotAgeBucket? Age)> records = new();
        readonly Channel<StatsSnapshotDiagnosticEvent> events = Channel.CreateUnbounded<StatsSnapshotDiagnosticEvent>();

        public IReadOnlyCollection<(StatsSnapshotDiagnosticEvent Event, StatsSnapshotAgeBucket? Age)> Records => records.ToArray();

        public void Record(StatsSnapshotDiagnosticEvent diagnosticEvent, StatsSnapshotAgeBucket? ageBucket = null)
        {
            records.Enqueue((diagnosticEvent, ageBucket));
            events.Writer.TryWrite(diagnosticEvent);
        }

        public async Task WaitForEventAsync(StatsSnapshotDiagnosticEvent expected)
        {
            if (records.Any(value => value.Event == expected)) return;
            while (await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)) != expected) { }
        }
    }

    internal sealed class ThrowingSnapshotDiagnostics : IStatsSnapshotDiagnostics
    {
        public int ThrowCount { get; private set; }
        public void Record(StatsSnapshotDiagnosticEvent diagnosticEvent, StatsSnapshotAgeBucket? ageBucket = null)
        {
            ThrowCount++;
            throw new InvalidOperationException("Diagnostics delivery is unavailable.");
        }
    }
}
