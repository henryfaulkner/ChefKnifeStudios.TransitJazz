using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Enums;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>App-lifetime owner for prepared immutable Stats evidence.</summary>
public sealed class StatsSnapshotStore
{
    sealed class InFlight(
        StatsSelection selection, CancellationTokenSource cancellation, long cityGeneration, long filteredGeneration)
    {
        public StatsSelection Selection { get; } = selection;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public long CityGeneration { get; } = cityGeneration;
        public long FilteredGeneration { get; } = filteredGeneration;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    enum RetentionKind
    {
        CurrentCity,
        All,
        Filtered
    }

    enum ScheduledWakeKind
    {
        RootDayEnd,
        Refresh,
        Stale,
        HardExpiry,
        Retry
    }

    readonly object gate = new();
    readonly IStatsEndpointsService endpoints;
    readonly TimeProvider timeProvider;
    readonly IPageVisibilityJsInterop visibility;
    readonly IStatsSnapshotNavigation navigation;
    bool statsEnabled;
    readonly bool preparationEnabled;
    IFeatureFlagService? featureFlags;
    readonly long maximumRetainedBytes;
    readonly Func<StatsOverlayResponse, long> estimateRetainedBytes;
    readonly IStatsSnapshotDiagnostics? diagnostics;
    readonly IStatsSnapshotJitter jitter;
    readonly SemaphoreSlim requestSlot = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    readonly List<StatsSnapshotState> states = [];
    readonly List<StatsSnapshotRetryState> selectionRetryStates = [];
    readonly List<InFlight> inFlight = [];
    readonly List<Task> additionalOwners = [];
    readonly List<Task> schedulerOwners = [];
    CancellationTokenSource? schedulerCancellation;
    CancellationTokenSource? filteredDebounceCancellation;
    StatsSelection? filteredDebounceSelection;
    TaskCompletionSource? filteredDebounceOwner;
    ScheduledWake? scheduledWake;
    CancellationTokenRegistration lifetimeRegistration;
    TaskCompletionSource? startCompletion;
    TaskCompletionSource? stopCompletion;
    string currentCity = string.Empty;
    StatsSelection? overlaySelection;
    bool hidden;
    bool started;
    bool stopped;
    bool serverDisabled;
    long visibilityChangeVersion;
    long cityGeneration;
    long filteredGeneration;
    bool filteredTurnSinceLastBaseline;
    string? lastBaselineScope;
    StatsSnapshotRetryState? endpointRetryState;
    long retrySequence;
    const int MaximumRetrySlots = 128;

    readonly record struct ScheduledWake(
        StatsSelection Selection, DateTime BoundaryUtc, ScheduledWakeKind Kind, TimeSpan Delay,
        long RetryStartedTimestamp = 0);

    // Test seam for exercising request/scheduler behavior independently of startup preparation.
    // Application callers use the feature-flag constructor, which always prepares enabled stats.
    internal StatsSnapshotStore(
        IStatsEndpointsService endpoints,
        TimeProvider timeProvider,
        IPageVisibilityJsInterop visibility,
        IStatsSnapshotNavigation navigation,
        bool statsEnabled = true,
        bool preparationEnabled = false,
        long maximumRetainedBytes = 32L * 1024 * 1024,
        Func<StatsOverlayResponse, long>? estimateRetainedBytes = null,
        IStatsSnapshotDiagnostics? diagnostics = null,
        IStatsSnapshotJitter? jitter = null)
    {
        this.endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.statsEnabled = statsEnabled;
        this.preparationEnabled = preparationEnabled;
        if (maximumRetainedBytes is < 1 or > 32L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes));
        this.maximumRetainedBytes = maximumRetainedBytes;
        this.estimateRetainedBytes = estimateRetainedBytes ?? EstimateSnapshotBytes;
        this.diagnostics = diagnostics;
        this.jitter = jitter ?? new RandomStatsSnapshotJitter();
    }

    public StatsSnapshotStore(
        IStatsEndpointsService endpoints,
        TimeProvider timeProvider,
        IPageVisibilityJsInterop visibility,
        IStatsSnapshotNavigation navigation,
        IFeatureFlagService featureFlags,
        long maximumRetainedBytes = 32L * 1024 * 1024,
        Func<StatsOverlayResponse, long>? estimateRetainedBytes = null,
        IStatsSnapshotDiagnostics? diagnostics = null,
        IStatsSnapshotJitter? jitter = null)
        : this(endpoints, timeProvider, visibility, navigation,
            StatsOverlayFeaturePolicy.IsAvailable(featureFlags),
            preparationEnabled: true,
            maximumRetainedBytes, estimateRetainedBytes, diagnostics, jitter)
    {
        this.featureFlags = featureFlags ?? throw new ArgumentNullException(nameof(featureFlags));
    }

    public event Action<StatsSelection, StatsSnapshotState>? StateChanged;
    public bool IsAutomaticWorkSuppressed { get { lock (gate) return serverDisabled || !statsEnabled; } }

    /// <summary>Starts observation and preparation without waiting for either browser interop or HTTP.</summary>
    public Task StartAsync(string currentCitySlug, CancellationToken lifetimeToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentCitySlug)) throw new ArgumentException("A current city is required.", nameof(currentCitySlug));
        TaskCompletionSource owner;
        lock (gate)
        {
            if (started || stopped) return Task.CompletedTask;
            started = true;
            currentCity = currentCitySlug;
            startCompletion = owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lifetimeRegistration = lifetimeToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), lifetime);
            navigation.CityChanged += HandleCityChanged;
            visibility.VisibilityChanged += HandleVisibilityChanged;
        }

        _ = InitializeObservedAsync(owner);
        return Task.CompletedTask;
    }

    public void SetCurrentCity(string citySlug)
    {
        if (string.IsNullOrWhiteSpace(citySlug)) return;
        InFlight[] cancel;
        StatsSelection[] dropped;
        CancellationTokenSource? cancelDebounce;
        lock (gate)
        {
            if (string.Equals(currentCity, citySlug, StringComparison.Ordinal)) return;
            currentCity = citySlug;
            cityGeneration++;
            filteredGeneration++;
            cancel = inFlight.Where(flight => flight.Selection.Scope == "city").ToArray();
            foreach (var flight in cancel) SetRetryConsumedLocked(flight.Selection, false);
            dropped = states.Where(state => state.Selection.Scope == "city"
                && (state.Selection.CitySlug != citySlug || state.Selection.RouteJoinKeys.Count != 0))
                .Select(state => state.Selection).ToArray();
            states.RemoveAll(state => state.Selection.Scope == "city"
                && (state.Selection.CitySlug != citySlug || state.Selection.RouteJoinKeys.Count != 0));
            if (overlaySelection is { Scope: "city" } displayed
                && (displayed.CitySlug != citySlug || displayed.RouteJoinKeys.Count != 0))
                overlaySelection = null;
            cancelDebounce = filteredDebounceCancellation;
            filteredDebounceCancellation = null;
            filteredDebounceSelection = null;
            filteredDebounceOwner = null;
        }
        CancelDebounce(cancelDebounce);
        CancelFlights(cancel);
        foreach (var selection in dropped) Notify(selection, StatsSnapshotState.Empty(selection));
        RecomputeScheduler();
    }

    public void SetOverlaySelection(StatsSelection? selection)
    {
        var normalized = selection is null ? null : StatsSnapshotPolicy.NormalizeSelection(selection);
        InFlight[] cancel;
        CancellationTokenSource? cancelDebounce;
        lock (gate)
        {
            if (overlaySelection is { } current && normalized is { } next && SameSelection(current, next)
                || overlaySelection is null && normalized is null)
                return;
            overlaySelection = normalized;
            var matchingFlightExists = normalized is { RouteJoinKeys.Count: > 0 }
                && inFlight.Any(flight => SameSelection(flight.Selection, normalized));
            if (!matchingFlightExists) filteredGeneration++;
            cancel = inFlight.Where(flight => flight.Selection.RouteJoinKeys.Count != 0
                && (normalized is null || !SameSelection(flight.Selection, normalized))).ToArray();
            foreach (var flight in cancel) SetRetryConsumedLocked(flight.Selection, false);
            cancelDebounce = filteredDebounceCancellation;
            filteredDebounceCancellation = null;
            filteredDebounceSelection = null;
            filteredDebounceOwner = null;
        }
        CancelDebounce(cancelDebounce);
        CancelFlights(cancel);
        RecomputeScheduler();
    }

    /// <summary>Records open-time aggregates without exposing selection identity.</summary>
    public void RecordOverlayOpened(StatsSnapshotState? state, bool showLoader)
    {
        if (state?.Snapshot is { })
        {
            if (!state.Freshness.IsExpired)
                RecordDiagnostic(StatsSnapshotDiagnosticEvent.WarmOpen);
            if (state.ReceivedTimestamp is { } received)
            {
                var elapsed = timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp());
                RecordDiagnostic(StatsSnapshotDiagnosticEvent.SnapshotAgeOnOpen, AgeBucket(elapsed));
            }
        }
        if (showLoader) RecordLoader();
    }

    public void RecordLoader() => RecordDiagnostic(StatsSnapshotDiagnosticEvent.Loader);

    public void ReevaluateConfiguration()
    {
        TaskCompletionSource? owner = null;
        string city = string.Empty;
        lock (gate)
        {
            if (featureFlags is not null)
            {
                statsEnabled = StatsOverlayFeaturePolicy.IsAvailable(featureFlags);
            }
            serverDisabled = false;
            if (!started || stopped || hidden || !statsEnabled || !preparationEnabled) return;
            owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
            additionalOwners.Add(owner.Task);
            city = currentCity;
        }
        _ = PrepareAfterReevaluationAsync(owner!, city);
    }

    /// <summary>Returns exact-selection evidence with freshness evaluated from monotonic elapsed time.</summary>
    public StatsSnapshotState? TryGet(StatsSelection selection)
    {
        var normalized = StatsSnapshotPolicy.NormalizeSelection(selection);
        while (true)
        {
            var state = TryGetStored(normalized);
            if (state is null || state.Snapshot is null) return state;
            var elapsed = state.ReceivedTimestamp is { } received
                ? timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp())
                : TimeSpan.Zero;
            var freshness = StatsSnapshotPolicy.Evaluate(state, elapsed, timeProvider.GetUtcNow());
            if (!freshness.IsExpired) return state with { Freshness = freshness };
            if (RemoveExpired(normalized)) return null;
        }
    }

    /// <summary>Joins an exact active request or starts one request while preserving app ownership.</summary>
    public Task EnsureRequestedAsync(StatsSelection selection, CancellationToken cancellationToken = default)
        => EnsureRequestedAsync(selection, cancellationToken, scheduled: false);

    /// <summary>Ensures the overlay's displayed selection through interactive request admission.</summary>
    public Task EnsureDisplayedSelectionAsync(StatsSelection selection, CancellationToken cancellationToken = default)
    {
        var normalized = StatsSnapshotPolicy.NormalizeSelection(selection);
        if (normalized.Scope != "city" || normalized.RouteJoinKeys.Count == 0)
            return EnsureRequestedAsync(normalized, cancellationToken);

        CancellationTokenSource? previous;
        CancellationTokenSource debounce;
        TaskCompletionSource owner;
        long generation;
        lock (gate)
        {
            if (!started || stopped || hidden || !statsEnabled || serverDisabled || lifetime.IsCancellationRequested
                || !IsSelectionCurrentLocked(normalized)
                || overlaySelection is not { } displayed || !SameSelection(displayed, normalized))
                return Task.CompletedTask;

            if (filteredDebounceCancellation is not null
                && filteredDebounceSelection is { } pending
                && SameSelection(pending, normalized)
                && filteredDebounceOwner is { } pendingOwner)
                return cancellationToken.CanBeCanceled
                    ? pendingOwner.Task.WaitAsync(cancellationToken)
                    : pendingOwner.Task;

            previous = filteredDebounceCancellation;
            debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            filteredDebounceCancellation = debounce;
            filteredDebounceSelection = normalized;
            generation = filteredGeneration;
            owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
            filteredDebounceOwner = owner;
            additionalOwners.Add(owner.Task);
        }
        CancelDebounce(previous);
        _ = RunDisplayedSelectionObservedAsync(normalized, generation, debounce, owner);
        return cancellationToken.CanBeCanceled ? owner.Task.WaitAsync(cancellationToken) : owner.Task;
    }

    async Task RunDisplayedSelectionObservedAsync(
        StatsSelection selection,
        long generation,
        CancellationTokenSource debounce,
        TaskCompletionSource owner)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), timeProvider, debounce.Token);
            lock (gate)
            {
                if (!ReferenceEquals(filteredDebounceCancellation, debounce)
                    || generation != filteredGeneration
                    || !IsSelectionCurrentLocked(selection)
                    || overlaySelection is not { } displayed || !SameSelection(displayed, selection)
                    || stopped || hidden || !statsEnabled || serverDisabled || lifetime.IsCancellationRequested)
                    return;
                filteredDebounceCancellation = null;
                filteredDebounceSelection = null;
                filteredDebounceOwner = null;
            }
            await EnsureRequestedAsync(selection, lifetime.Token, scheduled: false, requiredFilteredGeneration: generation);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            owner.TrySetException(exception);
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(filteredDebounceCancellation, debounce))
                {
                    filteredDebounceCancellation = null;
                    filteredDebounceSelection = null;
                    filteredDebounceOwner = null;
                }
                additionalOwners.Remove(owner.Task);
            }
            debounce.Dispose();
            owner.TrySetResult();
        }
    }

    static void CancelDebounce(CancellationTokenSource? debounce)
    {
        if (debounce is null) return;
        try { debounce.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    Task EnsureRequestedAsync(
        StatsSelection selection,
        CancellationToken cancellationToken,
        bool scheduled,
        long? requiredFilteredGeneration = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = StatsSnapshotPolicy.NormalizeSelection(selection);
        InFlight flight;
        var startRequest = false;
        lock (gate)
        {
            if (!started || stopped || hidden || !statsEnabled || serverDisabled || lifetime.IsCancellationRequested)
                return Task.CompletedTask;
            if (!IsSelectionCurrentLocked(normalized)) return Task.CompletedTask;
            if (RetryDelayRemainingLocked(normalized) > TimeSpan.Zero)
            {
                EnsureRetrySlotLocked(normalized);
                PreserveRetryProblemLocked(normalized);
                RecomputeScheduler();
                return Task.CompletedTask;
            }
            if (requiredFilteredGeneration is { } expectedGeneration
                && (expectedGeneration != filteredGeneration
                    || overlaySelection is not { } displayed
                    || !SameSelection(displayed, normalized)))
                return Task.CompletedTask;
            flight = inFlight.FirstOrDefault(value => SameSelection(value.Selection, normalized))!;
            if (flight is not null && flight.Cancellation.IsCancellationRequested && !lifetime.IsCancellationRequested)
                return RetryAfterCanceledFlightAsync(flight, normalized, cancellationToken, scheduled);
            if (flight is null)
            {
                var existing = states.FirstOrDefault(value => SameSelection(value.Selection, normalized));
                if (existing?.Snapshot is not null)
                {
                    var elapsed = existing.ReceivedTimestamp is { } received
                        ? timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp())
                        : TimeSpan.Zero;
                    var freshness = StatsSnapshotPolicy.Evaluate(existing, elapsed, timeProvider.GetUtcNow());
                    if (existing.RefreshConsumed && !scheduled && !freshness.IsExpired) return Task.CompletedTask;
                    if (!freshness.IsDue && !freshness.IsExpired) return Task.CompletedTask;
                }
                flight = new InFlight(normalized, CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token),
                    cityGeneration, filteredGeneration);
                inFlight.Add(flight);
                SetRetryConsumedLocked(normalized, true);
                if (normalized.Scope == "city" && normalized.RouteJoinKeys.Count != 0)
                    filteredTurnSinceLastBaseline = true;
                startRequest = true;
            }
        }
        if (startRequest) _ = RunRequestObservedAsync(flight);
        return flight.Completion.Task.WaitAsync(cancellationToken);
    }

    async Task RetryAfterCanceledFlightAsync(
        InFlight canceledFlight, StatsSelection selection, CancellationToken cancellationToken, bool scheduled)
    {
        await canceledFlight.Completion.Task.WaitAsync(cancellationToken);
        await EnsureRequestedAsync(selection, cancellationToken, scheduled);
    }

    /// <summary>Cancels and awaits all app-owned initialization and request work.</summary>
    public Task StopAsync()
    {
        TaskCompletionSource completion;
        Task? startTask;
        Task[] activeRequests;
        CancellationTokenSource? cancelDebounce;
        lock (gate)
        {
            if (stopCompletion is not null) return stopCompletion.Task;
            stopped = true;
            stopCompletion = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            startTask = startCompletion?.Task;
            activeRequests = inFlight.Select(value => value.Completion.Task)
                .Concat(additionalOwners).Concat(schedulerOwners).ToArray();
            schedulerCancellation?.Cancel();
            schedulerCancellation = null;
            scheduledWake = null;
            cancelDebounce = filteredDebounceCancellation;
            filteredDebounceCancellation = null;
            filteredDebounceSelection = null;
            filteredDebounceOwner = null;
            navigation.CityChanged -= HandleCityChanged;
            visibility.VisibilityChanged -= HandleVisibilityChanged;
        }

        CancelDebounce(cancelDebounce);
        _ = CompleteStopObservedAsync(completion, startTask, activeRequests);
        return completion.Task;
    }

    async Task InitializeObservedAsync(TaskCompletionSource owner)
    {
        try
        {
            long readVersion;
            lock (gate) readVersion = visibilityChangeVersion;
            var hiddenNow = await visibility.GetHiddenAsync();
            string city;
            bool prepare;
            lock (gate)
            {
                if (stopped || lifetime.IsCancellationRequested)
                {
                    owner.TrySetResult();
                    return;
                }
                if (readVersion == visibilityChangeVersion) hidden = hiddenNow;
                hiddenNow = hidden;
                city = currentCity;
                prepare = !hiddenNow && statsEnabled && preparationEnabled;
            }
            if (prepare)
            {
                await EnsureRequestedAsync(new StatsSelection("city", city, []), lifetime.Token);
                if (!lifetime.IsCancellationRequested && !IsAutomaticWorkSuppressed)
                    await EnsureRequestedAsync(new StatsSelection("all", null, []), lifetime.Token);
                RecordDiagnostic(StatsSnapshotDiagnosticEvent.PreparationCompleted);
            }
            owner.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            owner.TrySetResult();
        }
        catch (Exception)
        {
            owner.TrySetResult();
        }
    }

    async Task RunRequestObservedAsync(InFlight flight)
    {
        try
        {
            await requestSlot.WaitAsync(flight.Cancellation.Token);
            try
            {
                var blockedByCooldown = false;
                lock (gate)
                {
                    if (!flight.Cancellation.IsCancellationRequested
                        && RetryDelayRemainingLocked(flight.Selection) > TimeSpan.Zero)
                    {
                        EnsureRetrySlotLocked(flight.Selection);
                        PreserveRetryProblemLocked(flight.Selection);
                        SetRetryConsumedLocked(flight.Selection, false);
                        blockedByCooldown = true;
                    }
                }
                if (blockedByCooldown)
                {
                    RecomputeScheduler();
                    return;
                }

                var request = new StatsOverlayRequest
                {
                    Scope = flight.Selection.Scope,
                    CitySlug = flight.Selection.CitySlug,
                    RouteJoinKeys = flight.Selection.RouteJoinKeys.ToArray()
                };
                var response = await endpoints.QueryAsync(request, flight.Cancellation.Token);
                if (flight.Cancellation.IsCancellationRequested) return;
                if (response.Result.IsSuccess)
                {
                    var resetRetry = false;
                    lock (gate)
                    {
                        if (!flight.Cancellation.IsCancellationRequested && IsFlightCurrentLocked(flight))
                        {
                            ResetRetryLocked(flight.Selection);
                            resetRetry = true;
                        }
                    }
                    if (resetRetry) RecomputeScheduler();
                }
                else if (response.ProblemCode != "stats_disabled")
                {
                    var recordedFailure = false;
                    lock (gate)
                    {
                        if (!flight.Cancellation.IsCancellationRequested && IsFlightCurrentLocked(flight))
                        {
                            RecordFailureLocked(flight.Selection,
                                response.StatusCode ?? HttpStatusCode.ServiceUnavailable,
                                response.ProblemCode, response.RetryAfter);
                            recordedFailure = true;
                        }
                    }
                    if (recordedFailure) RecomputeScheduler();
                }
                StatsSnapshotState next;
                var existing = TryGetStored(flight.Selection) ?? StatsSnapshotState.Empty(flight.Selection);
                if (response.Result.IsSuccess && response.Result.Value is { } dto)
                {
                    var result = StatsSnapshotPolicy.Accept(existing, dto, timeProvider.GetUtcNow(), timeProvider.GetTimestamp());
                    next = result.State;
                    if (result.Kind == StatsSnapshotAcceptanceKind.Accepted)
                    {
                        var boundary = RefreshBoundary(next);
                        var sameWindow = existing.RefreshBoundaryUtc == boundary;
                        next = next with
                        {
                            RefreshBoundaryUtc = boundary,
                            RefreshJitterAfter = sameWindow ? existing.RefreshJitterAfter : SampleJitter(),
                            RefreshConsumed = sameWindow && existing.RefreshConsumed,
                            StaleNotified = false,
                            HistoricalNotified = false,
                            ExpiredNotified = false
                        };
                        next = WithCurrentFreshness(next);
                        if (next.Freshness.IsExpired)
                        {
                            Publish(flight.Selection, StatsSnapshotState.Empty(flight.Selection), flight);
                            return;
                        }
                        long estimatedBytes;
                        try { estimatedBytes = estimateRetainedBytes(next.Snapshot!); }
                        catch (Exception) { estimatedBytes = long.MaxValue; }
                        next = next with { EstimatedRetainedBytes = estimatedBytes };
                        var admitted = TryAdmitSnapshot(next, flight, out var requestCanceled);
                        if (requestCanceled) return;
                        if (!admitted) RecordDiagnostic(StatsSnapshotDiagnosticEvent.AdmissionSkipped);
                        if (!IsFlightActive(flight)) return;
                        Notify(flight.Selection, next);
                        RecomputeScheduler();
                    }
                    else if (result.Kind == StatsSnapshotAcceptanceKind.Rejected)
                    {
                        RecordDiagnostic(StatsSnapshotDiagnosticEvent.Failure);
                        Publish(flight.Selection, next, flight);
                    }
                    else if (result.Kind == StatsSnapshotAcceptanceKind.Identical)
                    {
                        Publish(flight.Selection, next, flight);
                    }
                }
                else
                {
                    RecordDiagnostic(StatsSnapshotDiagnosticEvent.Failure);
                    if (response.ProblemCode == "stats_disabled")
                    {
                        lock (gate)
                        {
                            if (!flight.Cancellation.IsCancellationRequested && IsFlightCurrentLocked(flight))
                                serverDisabled = true;
                        }
                    }
                    next = existing with
                    {
                        ProblemCode = response.Result.IsSuccess
                            ? "invalid_response"
                            : SafeProblemCode(response.ProblemCode)
                    };
                    Publish(flight.Selection, next, flight);
                }
            }
            finally
            {
                requestSlot.Release();
            }
        }
        catch (OperationCanceledException) when (flight.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (flight.Cancellation.IsCancellationRequested) return;
            RecordDiagnostic(StatsSnapshotDiagnosticEvent.Failure);
            lock (gate)
            {
                if (!flight.Cancellation.IsCancellationRequested && IsFlightCurrentLocked(flight))
                    RecordFailureLocked(flight.Selection, HttpStatusCode.ServiceUnavailable,
                        "dependency_unavailable", null);
            }
            var existing = TryGetStored(flight.Selection) ?? StatsSnapshotState.Empty(flight.Selection);
            Publish(flight.Selection, existing with { ProblemCode = "dependency_unavailable" }, flight);
        }
        finally
        {
            lock (gate) inFlight.Remove(flight);
            flight.Cancellation.Dispose();
            flight.Completion.TrySetResult();
        }
    }

    async Task CompleteStopObservedAsync(TaskCompletionSource completion, Task? startTask, Task[] activeRequests)
    {
        lifetime.Cancel();
        lifetimeRegistration.Dispose();
        try
        {
            if (startTask is not null) await startTask;
            await Task.WhenAll(activeRequests);
        }
        catch (OperationCanceledException) { }
        finally
        {
            requestSlot.Dispose();
            lifetime.Dispose();
            completion.TrySetResult();
        }
    }

    async Task PrepareAfterReevaluationAsync(TaskCompletionSource owner, string city)
    {
        try
        {
            await EnsureRequestedAsync(new StatsSelection("city", city, []), lifetime.Token);
            if (!lifetime.IsCancellationRequested && !IsAutomaticWorkSuppressed)
                await EnsureRequestedAsync(new StatsSelection("all", null, []), lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally { owner.TrySetResult(); }
    }

    StatsSnapshotState? TryGetStored(StatsSelection selection)
    {
        lock (gate) return states.FirstOrDefault(value => SameSelection(value.Selection, selection));
    }

    bool Publish(StatsSelection selection, StatsSnapshotState state, InFlight? flight = null)
    {
        state = WithCurrentFreshness(state);
        var expired = state.Snapshot is not null && state.Freshness.IsExpired;
        lock (gate)
        {
            if (stopped || hidden || lifetime.IsCancellationRequested
                || flight is not null && !IsFlightCurrentLocked(flight)
                || flight is null && !IsSelectionCurrentLocked(selection))
                return false;
            var index = states.FindIndex(value => SameSelection(value.Selection, selection));
            if (expired)
            {
                if (index >= 0) states.RemoveAt(index);
            }
            else if (index < 0) states.Add(state);
            else states[index] = state;
        }

        Notify(selection, expired ? StatsSnapshotState.Empty(selection) : state);
        RecomputeScheduler();
        return true;
    }

    bool IsFlightActive(InFlight flight)
    {
        lock (gate)
            return !stopped && !hidden && !lifetime.IsCancellationRequested
                && IsFlightCurrentLocked(flight);
    }

    bool TryAdmitSnapshot(StatsSnapshotState state, InFlight flight, out bool canceled)
    {
        var kind = KindOf(state.Selection);
        lock (gate)
        {
            canceled = stopped || hidden || lifetime.IsCancellationRequested || !IsFlightCurrentLocked(flight);
            if (canceled) return false;
            states.RemoveAll(value => value.Snapshot is not null && KindOf(value.Selection) == kind);
            states.RemoveAll(value => value.Snapshot is null && SameSelection(value.Selection, state.Selection));
            if (state.EstimatedRetainedBytes <= 0 || state.EstimatedRetainedBytes > maximumRetainedBytes)
                return false;

            var retainedBytes = states.Where(value => value.Snapshot is not null)
                .Sum(value => value.EstimatedRetainedBytes);
            while (retainedBytes + state.EstimatedRetainedBytes > maximumRetainedBytes)
            {
                var victim = states.Where(value => value.Snapshot is not null && (int)KindOf(value.Selection) > (int)kind)
                    .OrderByDescending(value => (int)KindOf(value.Selection))
                    .FirstOrDefault();
                if (victim is null) return false;
                states.Remove(victim);
                retainedBytes -= victim.EstimatedRetainedBytes;
            }

            var errorIndex = states.FindIndex(value => SameSelection(value.Selection, state.Selection));
            if (errorIndex >= 0) states.RemoveAt(errorIndex);
            states.Add(state);
            return true;
        }
    }

    void Notify(StatsSelection selection, StatsSnapshotState state)
    {

        var handlers = StateChanged;
        if (handlers is null) return;
        foreach (Action<StatsSelection, StatsSnapshotState> handler in handlers.GetInvocationList())
        {
            try { handler(selection, state); }
            catch (Exception) { }
        }
    }

    StatsSnapshotState WithCurrentFreshness(StatsSnapshotState state)
    {
        if (state.Snapshot is null) return state with { Freshness = new(false, false, false, false) };
        var elapsed = state.ReceivedTimestamp is { } received
            ? timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp())
            : TimeSpan.Zero;
        var freshness = StatsSnapshotPolicy.Evaluate(state, elapsed, timeProvider.GetUtcNow());
        return state with
        {
            Freshness = freshness,
            StaleNotified = state.StaleNotified || elapsed >= state.StaleAfter || freshness.IsHistorical,
            HistoricalNotified = state.HistoricalNotified || freshness.IsHistorical,
            ExpiredNotified = state.ExpiredNotified || freshness.IsExpired
        };
    }

    bool RemoveExpired(StatsSelection selection)
    {
        var normalized = StatsSnapshotPolicy.NormalizeSelection(selection);
        StatsSnapshotState? expired = null;
        lock (gate)
        {
            var index = states.FindIndex(value => SameSelection(value.Selection, normalized));
            if (index >= 0 && states[index].Snapshot is not null)
            {
                var state = states[index];
                var elapsed = state.ReceivedTimestamp is { } received
                    ? timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp())
                    : TimeSpan.Zero;
                if (StatsSnapshotPolicy.Evaluate(state, elapsed, timeProvider.GetUtcNow()).IsExpired)
                {
                    states.RemoveAt(index);
                    expired = StatsSnapshotState.Empty(normalized);
                }
            }
        }
        if (expired is null) return false;
        Notify(normalized, expired);
        RecomputeScheduler();
        return true;
    }

    void RecomputeScheduler()
    {
        var next = FindNextWake();
        lock (gate)
        {
            if (stopped) return;
            schedulerCancellation?.Cancel();
            schedulerCancellation = null;
            scheduledWake = null;
            if (next is not { } wake || !statsEnabled || hidden) return;

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            schedulerCancellation = cancellation;
            scheduledWake = wake;
            schedulerOwners.Add(owner.Task);
            _ = RunScheduledWakeObservedAsync(wake, cancellation, owner);
        }
    }

    ScheduledWake? FindNextWake()
    {
        lock (gate)
        {
            if (!started || stopped || !statsEnabled || hidden) return null;
            var deferDisplayedFilteredRefresh = filteredTurnSinceLastBaseline && HasDueBaselineRefreshLocked();
            ScheduledWake? earliest = null;
            void Consider(StatsSelection selection, DateTime identityBoundary, ScheduledWakeKind kind,
                TimeSpan delay, long retryStartedTimestamp = 0)
            {
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                var candidate = new ScheduledWake(selection, identityBoundary, kind, delay, retryStartedTimestamp);
                if (earliest is null || candidate.Delay < earliest.Value.Delay
                    || candidate.Delay == earliest.Value.Delay && candidate.Kind < earliest.Value.Kind
                    || candidate.Delay == earliest.Value.Delay && candidate.Kind == earliest.Value.Kind
                       && SelectionPriority(candidate.Selection) < SelectionPriority(earliest.Value.Selection))
                    earliest = candidate;
            }

            int SelectionPriority(StatsSelection selection)
            {
                if (deferDisplayedFilteredRefresh && IsSchedulerBaseline(selection))
                {
                    if (lastBaselineScope is null) return selection.Scope == "city" ? 0 : 1;
                    return lastBaselineScope == "city"
                        ? selection.Scope == "all" ? 0 : 1
                        : selection.Scope == "city" ? 0 : 1;
                }
                if (overlaySelection is { } selected && SameSelection(selected, selection)) return 0;
                if (preparationEnabled && selection.Scope == "city" && selection.CitySlug == currentCity
                    && selection.RouteJoinKeys.Count == 0) return 1;
                if (preparationEnabled && selection.Scope == "all") return 2;
                return 3;
            }

            foreach (var state in states)
            {
                if (state.Snapshot is null) continue;
                var isDisplayed = overlaySelection is { } displayed && SameSelection(displayed, state.Selection);
                var isPreparedBaseline = preparationEnabled &&
                    (state.Selection.Scope == "all"
                        || state.Selection.Scope == "city" && state.Selection.CitySlug == currentCity
                           && state.Selection.RouteJoinKeys.Count == 0);
                if (!isDisplayed && !isPreparedBaseline) continue;

                var elapsed = state.ReceivedTimestamp is { } received
                    ? timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp())
                    : TimeSpan.Zero;
                var identityBoundary = state.RefreshBoundaryUtc
                    ?? state.ReceivedUtc?.UtcDateTime
                    ?? DateTime.MinValue;
                var canRefresh = (isDisplayed || isPreparedBaseline) && !serverDisabled && !state.RefreshConsumed;
                if (canRefresh && state.RefreshBoundaryUtc is not null
                    && !(deferDisplayedFilteredRefresh && state.Selection.RouteJoinKeys.Count != 0))
                    Consider(state.Selection, identityBoundary, ScheduledWakeKind.Refresh,
                        state.RefreshDueAfter + state.RefreshJitterAfter - elapsed);
                if (!state.StaleNotified)
                    Consider(state.Selection, identityBoundary, ScheduledWakeKind.Stale, state.StaleAfter - elapsed);
                if (!state.HistoricalNotified)
                    Consider(state.Selection, identityBoundary, ScheduledWakeKind.RootDayEnd, state.RootDayEndAfter - elapsed);
                if (!state.ExpiredNotified)
                    Consider(state.Selection, identityBoundary, ScheduledWakeKind.HardExpiry, state.HardExpiryAfter - elapsed);
            }

            if (!serverDisabled)
            {
                foreach (var retry in selectionRetryStates)
                {
                    if (retry.RetryConsumed || !IsRetrySelectionEligibleLocked(retry.Selection!)) continue;
                    var delay = RetryDelayRemainingLocked(retry.Selection!);
                    if (delay <= TimeSpan.Zero) continue;
                    Consider(retry.Selection!, DateTime.MinValue, ScheduledWakeKind.Retry, delay,
                        retry.RetryStartedTimestamp);
                }
            }
            return earliest;
        }
    }

    bool IsRetrySelectionEligibleLocked(StatsSelection selection) =>
        selection.Scope == "all"
            ? preparationEnabled || overlaySelection is { } displayedAll && SameSelection(displayedAll, selection)
            : selection.Scope == "city" && selection.CitySlug == currentCity
                && (selection.RouteJoinKeys.Count == 0
                    ? preparationEnabled || overlaySelection is { } displayedCity && SameSelection(displayedCity, selection)
                    : overlaySelection is { } displayedFiltered && SameSelection(displayedFiltered, selection));

    bool HasDueBaselineRefreshLocked()
    {
        if (serverDisabled) return false;
        foreach (var state in states)
        {
            if (state.Snapshot is null || state.RefreshBoundaryUtc is null || state.RefreshConsumed
                || !IsSchedulerBaseline(state.Selection)) continue;
            var elapsed = state.ReceivedTimestamp is { } received
                ? timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp())
                : TimeSpan.Zero;
            if (elapsed >= state.RefreshDueAfter + state.RefreshJitterAfter) return true;
        }
        return false;
    }

    bool IsSchedulerBaseline(StatsSelection selection) => selection.Scope == "all"
        ? preparationEnabled || overlaySelection is { } displayedAll && SameSelection(displayedAll, selection)
        : selection.Scope == "city" && selection.CitySlug == currentCity && selection.RouteJoinKeys.Count == 0
            && (preparationEnabled || overlaySelection is { } displayedCity && SameSelection(displayedCity, selection));

    async Task RunScheduledWakeObservedAsync(
        ScheduledWake wake, CancellationTokenSource cancellation, TaskCompletionSource owner)
    {
        var token = cancellation.Token;
        try
        {
            await Task.Delay(wake.Delay, timeProvider, token);
            await Task.Yield();
            StatsSnapshotState? changed = null;
            var expired = false;
            var retryReady = false;
            lock (gate)
            {
                if (stopped || !ReferenceEquals(schedulerCancellation, cancellation)
                    || scheduledWake is not { } activeWake
                    || !SameWake(activeWake, wake))
                    return;

                if (wake.Kind == ScheduledWakeKind.Retry)
                {
                    var retryIndex = selectionRetryStates.FindIndex(retry => retry.Selection is { } selection
                        && SameSelection(selection, wake.Selection)
                        && retry.RetryStartedTimestamp == wake.RetryStartedTimestamp
                        && !retry.RetryConsumed);
                    if (retryIndex >= 0 && RetryDelayRemainingLocked(wake.Selection) <= TimeSpan.Zero)
                    {
                        selectionRetryStates[retryIndex] = selectionRetryStates[retryIndex] with
                        {
                            RetryConsumed = true,
                            LastTouchedSequence = ++retrySequence
                        };
                        retryReady = true;
                    }
                }

                var index = wake.Kind == ScheduledWakeKind.Retry ? -1 : states.FindIndex(state => SameSelection(state.Selection, wake.Selection)
                    && state.Snapshot is not null
                    && (state.RefreshBoundaryUtc ?? state.ReceivedUtc?.UtcDateTime ?? DateTime.MinValue) == wake.BoundaryUtc);
                if (index >= 0)
                {
                    var state = states[index];
                    if (wake.Kind == ScheduledWakeKind.HardExpiry)
                    {
                        states.RemoveAt(index);
                        changed = StatsSnapshotState.Empty(wake.Selection);
                        expired = true;
                    }
                    else if (wake.Kind == ScheduledWakeKind.Refresh && !state.RefreshConsumed)
                    {
                        changed = states[index] = state with { RefreshConsumed = true };
                        if (wake.Selection.RouteJoinKeys.Count != 0)
                        {
                            filteredTurnSinceLastBaseline = true;
                        }
                        else
                        {
                            lastBaselineScope = wake.Selection.Scope;
                            if (!HasDueBaselineRefreshLocked()) filteredTurnSinceLastBaseline = false;
                        }
                    }
                    else
                    {
                        state = WithCurrentFreshness(state);
                        changed = states[index] = wake.Kind switch
                        {
                            ScheduledWakeKind.Stale => state with { StaleNotified = true },
                            ScheduledWakeKind.RootDayEnd => state with { HistoricalNotified = true },
                            _ => state
                        };
                    }
                }
            }

            if (changed is not null)
            {
                Notify(wake.Selection, changed);
                if (expired) RecomputeScheduler();
                if (wake.Kind == ScheduledWakeKind.Refresh)
                    StartScheduledRequest(wake.Selection);
            }
            if (retryReady) StartScheduledRequest(wake.Selection);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { }
        finally
        {
            var ownsScheduler = false;
            lock (gate)
            {
                if (ReferenceEquals(schedulerCancellation, cancellation))
                {
                    ownsScheduler = true;
                    schedulerCancellation = null;
                    scheduledWake = null;
                }
            }
            cancellation.Dispose();
            if (ownsScheduler) RecomputeScheduler();
            lock (gate)
            {
                schedulerOwners.Remove(owner.Task);
                owner.TrySetResult();
            }
        }
    }

    void StartScheduledRequest(StatsSelection selection)
    {
        TaskCompletionSource owner;
        lock (gate)
        {
            if (stopped) return;
            owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
            additionalOwners.Add(owner.Task);
        }
        _ = ObserveScheduledRequestAsync(selection, owner);
    }

    async Task ObserveScheduledRequestAsync(StatsSelection selection, TaskCompletionSource owner)
    {
        try { await EnsureRequestedAsync(selection, lifetime.Token, scheduled: true); }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            lock (gate) additionalOwners.Remove(owner.Task);
            owner.TrySetResult();
        }
    }

    static bool SameWake(ScheduledWake left, ScheduledWake right) =>
        left.Kind == right.Kind && left.BoundaryUtc == right.BoundaryUtc
        && left.RetryStartedTimestamp == right.RetryStartedTimestamp
        && SameSelection(left.Selection, right.Selection);

    TimeSpan SampleJitter()
    {
        try
        {
            var value = jitter.NextDelay();
            if (value < TimeSpan.Zero) return TimeSpan.Zero;
            return value > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : value;
        }
        catch (Exception) { return TimeSpan.Zero; }
    }

    static DateTime RefreshBoundary(StatsSnapshotState state) => state.ScheduleMode == StatsSnapshotScheduleMode.ScheduleAware
        ? state.Snapshot!.NextRefreshAtUtc!.Value
        : state.Snapshot!.GeneratedAtUtc.AddSeconds(60);

    void RecordDiagnostic(StatsSnapshotDiagnosticEvent diagnosticEvent, StatsSnapshotAgeBucket? ageBucket = null)
    {
        try { diagnostics?.Record(diagnosticEvent, ageBucket); }
        catch (Exception) { }
    }

    static StatsSnapshotAgeBucket AgeBucket(TimeSpan age) => age < TimeSpan.FromMinutes(1)
        ? StatsSnapshotAgeBucket.UnderOneMinute
        : age < TimeSpan.FromMinutes(5) ? StatsSnapshotAgeBucket.OneToFiveMinutes
        : age < TimeSpan.FromMinutes(15) ? StatsSnapshotAgeBucket.FiveToFifteenMinutes
        : age < TimeSpan.FromMinutes(60) ? StatsSnapshotAgeBucket.FifteenToSixtyMinutes
        : StatsSnapshotAgeBucket.OverSixtyMinutes;

    void HandleCityChanged(string citySlug)
    {
        SetCurrentCity(citySlug);
        if (statsEnabled && preparationEnabled && !hidden)
            _ = EnsureRequestedAsync(new StatsSelection("city", citySlug, []));
    }

    void HandleVisibilityChanged(bool nowHidden)
    {
        var resume = false;
        InFlight[] cancel = [];
        CancellationTokenSource? cancelDebounce = null;
        lock (gate)
        {
            if (stopped) return;
            resume = hidden && !nowHidden;
            visibilityChangeVersion++;
            hidden = nowHidden;
            if (nowHidden)
            {
                cancel = inFlight.ToArray();
                cancelDebounce = filteredDebounceCancellation;
                filteredDebounceCancellation = null;
                filteredDebounceSelection = null;
                filteredDebounceOwner = null;
                foreach (var flight in cancel)
                {
                    SetRetryConsumedLocked(flight.Selection, false);
                    var index = states.FindIndex(state => SameSelection(state.Selection, flight.Selection));
                    if (index >= 0 && states[index].Snapshot is not null)
                        states[index] = states[index] with { RefreshConsumed = false };
                }
            }
        }
        CancelDebounce(cancelDebounce);
        CancelFlights(cancel);
        if (resume) ReconcileStatesOnResume();
        if (resume) StartResumeWork();
        RecomputeScheduler();
    }

    static void CancelFlights(IEnumerable<InFlight> flights)
    {
        foreach (var flight in flights)
        {
            try { flight.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    void ReconcileStatesOnResume()
    {
        var notifications = new List<(StatsSelection Selection, StatsSnapshotState State)>();
        lock (gate)
        {
            if (stopped || hidden) return;
            for (var index = states.Count - 1; index >= 0; index--)
            {
                var state = states[index];
                if (state.Snapshot is null) continue;
                var elapsed = state.ReceivedTimestamp is { } received
                    ? timeProvider.GetElapsedTime(received, timeProvider.GetTimestamp())
                    : TimeSpan.Zero;
                var freshness = StatsSnapshotPolicy.Evaluate(state, elapsed, timeProvider.GetUtcNow());
                if (freshness.IsExpired)
                {
                    states.RemoveAt(index);
                    notifications.Add((state.Selection, StatsSnapshotState.Empty(state.Selection)));
                    continue;
                }

                var updated = WithCurrentFreshness(state);
                states[index] = updated;
                if (updated.Freshness != state.Freshness)
                    notifications.Add((state.Selection, updated));
            }
        }
        foreach (var (selection, state) in notifications) Notify(selection, state);
    }

    void StartResumeWork()
    {
        TaskCompletionSource owner;
        StatsSelection? displayed;
        string city;
        bool prepare;
        lock (gate)
        {
            if (stopped || hidden || !statsEnabled) return;
            owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
            additionalOwners.Add(owner.Task);
            displayed = overlaySelection;
            city = currentCity;
            prepare = preparationEnabled && !serverDisabled;
        }
        _ = ResumeObservedAsync(owner, displayed, city, prepare);
    }

    async Task ResumeObservedAsync(TaskCompletionSource owner, StatsSelection? displayed, string city, bool prepare)
    {
        try
        {
            if (displayed is { } selection)
                await EnsureRequestedAsync(selection, lifetime.Token);
            if (prepare && !lifetime.IsCancellationRequested && !IsAutomaticWorkSuppressed)
            {
                var citySelection = new StatsSelection("city", city, []);
                if (displayed is not { } shown || !SameSelection(shown, citySelection))
                    await EnsureRequestedAsync(citySelection, lifetime.Token);
                if (!lifetime.IsCancellationRequested && !IsAutomaticWorkSuppressed
                    && (displayed is not { } current || current.Scope != "all"))
                    await EnsureRequestedAsync(new StatsSelection("all", null, []), lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            lock (gate) additionalOwners.Remove(owner.Task);
            owner.TrySetResult();
        }
    }

    static string SafeProblemCode(string? value) => value is "stats_disabled" or "stats_unavailable"
        or "dependency_unavailable" or "invalid_response" or "busy" or "read_timeout" or "rate_limited"
        or "retry_wait" or "scope_too_large" or "response_too_large" or "request_too_large"
        ? value
        : "dependency_unavailable";

    TimeSpan RetryDelayRemainingLocked(StatsSelection selection)
    {
        var scoped = selectionRetryStates.FirstOrDefault(state => state.Selection is { } key
            && SameSelection(key, selection));
        var remaining = scoped is null ? TimeSpan.Zero : RetryStateRemaining(scoped);
        if (endpointRetryState is not null)
        {
            var endpointRemaining = RetryStateRemaining(endpointRetryState);
            if (endpointRemaining > remaining) remaining = endpointRemaining;
        }
        return remaining;
    }

    TimeSpan RetryStateRemaining(StatsSnapshotRetryState state)
    {
        var elapsed = timeProvider.GetElapsedTime(state.RetryStartedTimestamp, timeProvider.GetTimestamp());
        var remaining = state.RetryDelay - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    void SetRetryConsumedLocked(StatsSelection selection, bool consumed)
    {
        var index = selectionRetryStates.FindIndex(state => state.Selection is { } key && SameSelection(key, selection));
        if (index < 0) return;
        selectionRetryStates[index] = selectionRetryStates[index] with
        {
            RetryConsumed = consumed,
            LastTouchedSequence = ++retrySequence
        };
    }

    void EnsureRetrySlotLocked(StatsSelection selection)
    {
        if (selectionRetryStates.Any(state => state.Selection is { } key && SameSelection(key, selection))
            || endpointRetryState is null) return;
        if (selectionRetryStates.Count >= MaximumRetrySlots) EvictOldestRetryStateLocked();
        selectionRetryStates.Add(new StatsSnapshotRetryState(
            StatsSnapshotPolicy.NormalizeSelection(selection),
            ConsecutiveFailures: 0,
            RetryStartedTimestamp: endpointRetryState.RetryStartedTimestamp,
            RetryDelay: TimeSpan.Zero,
            RetryConsumed: false,
            LastTouchedSequence: ++retrySequence,
            ProblemCode: endpointRetryState.ProblemCode ?? "rate_limited"));
    }

    void PreserveRetryProblemLocked(StatsSelection selection)
    {
        var retry = selectionRetryStates.FirstOrDefault(state => state.Selection is { } key
            && SameSelection(key, selection));
        var problemCode = retry?.ProblemCode ?? endpointRetryState?.ProblemCode ?? "retry_wait";
        var index = states.FindIndex(state => SameSelection(state.Selection, selection));
        if (index >= 0)
        {
            if (states[index].Snapshot is not null || states[index].ProblemCode is not null) return;
            states[index] = states[index] with { ProblemCode = problemCode };
        }
        else
        {
            states.Add(StatsSnapshotState.Empty(selection) with { ProblemCode = problemCode });
        }
    }

    void RecordFailureLocked(StatsSelection selection, HttpStatusCode statusCode, string? problemCode, TimeSpan? retryAfter)
    {
        var started = timeProvider.GetTimestamp();
        RecordSelectionRetryFailureLocked(selection, started, retryAfter, SafeProblemCode(problemCode));
        if (statusCode == HttpStatusCode.TooManyRequests || problemCode == "rate_limited")
            endpointRetryState = NextRetryState(endpointRetryState, null, started, retryAfter, "rate_limited");
    }

    void RecordSelectionRetryFailureLocked(StatsSelection selection, long started, TimeSpan? retryAfter, string? problemCode)
    {
        var normalized = StatsSnapshotPolicy.NormalizeSelection(selection);
        var index = selectionRetryStates.FindIndex(state => state.Selection is { } key && SameSelection(key, normalized));
        var previous = index >= 0 ? selectionRetryStates[index] : null;
        var next = NextRetryState(previous, normalized, started, retryAfter, problemCode);
        if (index >= 0) selectionRetryStates[index] = next;
        else
        {
            if (selectionRetryStates.Count >= MaximumRetrySlots) EvictOldestRetryStateLocked();
            selectionRetryStates.Add(next);
        }
    }

    void EvictOldestRetryStateLocked()
    {
        var oldest = selectionRetryStates.MinBy(state => state.LastTouchedSequence);
        if (oldest is null) return;
        selectionRetryStates.Remove(oldest);
        if (oldest.Selection is { } selection)
            states.RemoveAll(state => state.Snapshot is null && SameSelection(state.Selection, selection));
    }

    StatsSnapshotRetryState NextRetryState(
        StatsSnapshotRetryState? previous, StatsSelection? selection, long started, TimeSpan? retryAfter,
        string? problemCode)
    {
        var failures = previous?.ConsecutiveFailures ?? 0;
        var delay = StatsOverlayRetryPolicy.BackoffDelay(failures);
        if (retryAfter is { } serverDelay && serverDelay > delay) delay = serverDelay;
        return new StatsSnapshotRetryState(selection, failures == int.MaxValue ? int.MaxValue : failures + 1,
            started, delay, RetryConsumed: false, LastTouchedSequence: ++retrySequence, ProblemCode: problemCode);
    }

    void ResetRetryLocked(StatsSelection selection)
    {
        selectionRetryStates.RemoveAll(state => state.Selection is { } key && SameSelection(key, selection));
        endpointRetryState = null;
    }

    static bool SameSelection(StatsSelection left, StatsSelection right) =>
        string.Equals(left.Scope, right.Scope, StringComparison.Ordinal)
        && string.Equals(left.CitySlug, right.CitySlug, StringComparison.Ordinal)
        && left.RouteJoinKeys.SequenceEqual(right.RouteJoinKeys, StringComparer.Ordinal);

    bool IsSelectionCurrentLocked(StatsSelection selection)
    {
        if (selection.Scope == "all") return true;
        if (!string.Equals(selection.CitySlug, currentCity, StringComparison.Ordinal)) return false;
        return true;
    }

    bool IsFlightCurrentLocked(InFlight flight)
    {
        if (flight.Cancellation.IsCancellationRequested) return false;
        if (flight.Selection.Scope == "all") return true;
        if (flight.CityGeneration != cityGeneration
            || !string.Equals(flight.Selection.CitySlug, currentCity, StringComparison.Ordinal)) return false;
        if (flight.Selection.RouteJoinKeys.Count == 0) return true;
        return flight.FilteredGeneration == filteredGeneration;
    }

    static RetentionKind KindOf(StatsSelection selection) => selection.Scope == "all"
        ? RetentionKind.All
        : selection.RouteJoinKeys.Count == 0 ? RetentionKind.CurrentCity : RetentionKind.Filtered;

    static long EstimateSnapshotBytes(StatsOverlayResponse response) =>
        checked((long)JsonSerializer.SerializeToUtf8Bytes(response, JsonSettings.DefaultOptions).Length * 2 + 2048);
}
