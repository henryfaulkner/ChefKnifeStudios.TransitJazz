using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;

public static class StatsOverlayRefreshPolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public static bool ShouldRunLoop(bool sessionOpen, bool hidden, bool disposed) =>
        sessionOpen && !hidden && !disposed;

    public static bool ShouldStartLoop(bool sessionOpen, bool hidden, bool disposed, bool loopAlreadyRunning) =>
        ShouldRunLoop(sessionOpen, hidden, disposed) && !loopAlreadyRunning;

    public static bool ShouldRefresh(
        DateTimeOffset nowUtc,
        bool hidden,
        bool needsVisibleRefresh,
        DateTimeOffset? lastSuccessAtUtc) =>
        !hidden && (needsVisibleRefresh || lastSuccessAtUtc is null || nowUtc - lastSuccessAtUtc >= Interval);
}

/// <summary>Owns the single cancellable refresh delay for the currently visible Stats session.</summary>
public sealed class StatsOverlayRefreshLoopState
{
    StatsOverlayRefreshLoopLease? _current;

    public bool IsRunning => _current is { IsCancellationRequested: false };

    public bool TryStart(CancellationToken sessionToken, out StatsOverlayRefreshLoopLease? lease)
    {
        if (sessionToken.IsCancellationRequested || IsRunning)
        {
            lease = null;
            return false;
        }

        lease = new StatsOverlayRefreshLoopLease(sessionToken);
        _current = lease;
        return true;
    }

    public void Stop()
    {
        var current = _current;
        _current = null;
        current?.Cancel();
    }

    public void Complete(StatsOverlayRefreshLoopLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (ReferenceEquals(_current, lease)) _current = null;
        lease.Dispose();
    }
}

public sealed class StatsOverlayRefreshLoopLease : IDisposable
{
    readonly CancellationTokenSource _cancellation;

    internal StatsOverlayRefreshLoopLease(CancellationToken sessionToken) =>
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);

    public CancellationToken Token => _cancellation.Token;
    public bool IsCancellationRequested => _cancellation.IsCancellationRequested;
    public void Cancel() => _cancellation.Cancel();
    public void Dispose() => _cancellation.Dispose();
}

public static class StatsOverlayErrorResources
{
    public static readonly string[] ProblemCodes =
    [
        "response_too_large", "scope_too_large", "busy", "read_timeout", "rate_limited", "retry_wait",
        "stats_disabled", "stats_unavailable", "dependency_unavailable", "invalid_scope", "invalid_city",
        "invalid_route_key", "invalid_request", "request_too_large", "unsupported_media_type", "invalid_response"
    ];

    public static string ResourceKey(string? code) => code switch
    {
        "response_too_large" => "StatsResponseTooLargeError",
        "scope_too_large" => "StatsScopeTooLargeError",
        "busy" => "StatsBusyError",
        "read_timeout" => "StatsTimeoutError",
        "rate_limited" => "StatsRateLimitedError",
        "retry_wait" => "StatsRetryWait",
        "stats_disabled" or "stats_unavailable" => "StatsDisabledError",
        "dependency_unavailable" => "StatsDependencyError",
        "invalid_scope" => "StatsInvalidScopeError",
        "invalid_city" => "StatsInvalidCityError",
        "invalid_route_key" => "StatsInvalidRouteKeyError",
        "invalid_request" or "request_too_large" or "unsupported_media_type" => "StatsInvalidRequestError",
        _ => "StatsProblemGeneric"
    };
}

public sealed class StatsOverlayRetryPolicy
{
    static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);
    static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);
    TimeSpan _nextDelay = InitialDelay;

    public DateTimeOffset? RetryAtUtc { get; private set; }

    public bool IsCoolingDown(DateTimeOffset nowUtc) => RetryAtUtc is { } retryAt && nowUtc < retryAt;

    public static string CooldownProblemCode(string? previousProblemCode) =>
        string.IsNullOrWhiteSpace(previousProblemCode) ? "retry_wait" : previousProblemCode;

    public DateTimeOffset RecordFailure(DateTimeOffset nowUtc, TimeSpan? serverRetryAfter = null)
    {
        var serviceDelay = serverRetryAfter is { } value && value > TimeSpan.Zero ? value : TimeSpan.Zero;
        var delay = _nextDelay > serviceDelay ? _nextDelay : serviceDelay;
        RetryAtUtc = nowUtc + delay;
        _nextDelay = TimeSpan.FromTicks(Math.Min(MaximumDelay.Ticks, _nextDelay.Ticks * 2));
        return RetryAtUtc.Value;
    }

    public void RecordSuccess()
    {
        RetryAtUtc = null;
        _nextDelay = InitialDelay;
    }

    public void Reset() => RecordSuccess();
}

public readonly record struct PendingStatsOverlayRequest(long Generation, bool RetainSnapshot);

/// <summary>Serializes Stats reads and remembers only the newest desired request while a read settles.</summary>
public sealed class StatsOverlayRequestGate
{
    PendingStatsOverlayRequest? _pending;

    public long? ActiveGeneration { get; private set; }
    public bool HasActiveRequest => ActiveGeneration is not null;

    public bool TryBegin(long generation)
    {
        if (ActiveGeneration is not null) return false;
        ActiveGeneration = generation;
        return true;
    }

    public void QueueLatest(long generation, bool retainSnapshot) =>
        _pending = new PendingStatsOverlayRequest(generation, retainSnapshot);

    public PendingStatsOverlayRequest? Complete(long completedGeneration, long currentGeneration)
    {
        if (ActiveGeneration != completedGeneration) return null;
        ActiveGeneration = null;
        var pending = _pending;
        _pending = null;
        return pending is { Generation: var generation } && generation == currentGeneration
            ? pending
            : null;
    }

    public void InvalidatePending() => _pending = null;
}

public enum StatsOverlayRequestAdmission
{
    Started,
    CoolingDown,
    AlreadyActive
}

/// <summary>Shared admission decision used before a Stats request reaches the endpoint client.</summary>
public static class StatsOverlayRequestAdmissionPolicy
{
    public static StatsOverlayRequestAdmission TryBegin(
        StatsOverlayRequestGate gate,
        StatsOverlayRetryPolicy retryPolicy,
        long generation,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        if (retryPolicy.IsCoolingDown(nowUtc)) return StatsOverlayRequestAdmission.CoolingDown;
        return gate.TryBegin(generation)
            ? StatsOverlayRequestAdmission.Started
            : StatsOverlayRequestAdmission.AlreadyActive;
    }
}

/// <summary>
/// Keeps a response and every derived chart model in one replaceable value. A response from an
/// obsolete request generation or a different normalized selection cannot partially update the
/// current cutoff/date metadata or its presentation.
/// </summary>
public sealed record StatsOverlayAcceptedSnapshot(
    StatsOverlayResponse Snapshot,
    StatsOverlayPresentation Presentation,
    bool IsStale = false);

public sealed class StatsOverlayAcceptedSnapshotState
{
    public StatsOverlayAcceptedSnapshot? Current { get; private set; }

    public bool TryAccept(
        long responseGeneration,
        long currentGeneration,
        StatsOverlayResponse snapshot,
        StatsOverlayPresentation presentation,
        string scope,
        string? citySlug,
        IReadOnlyList<string> routeJoinKeys)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(presentation);
        if (responseGeneration != currentGeneration
            || !ReferenceEquals(presentation.Snapshot, snapshot)
            || !StatsOverlayResponseSelection.Matches(snapshot, scope, citySlug, routeJoinKeys))
            return false;

        Current = new StatsOverlayAcceptedSnapshot(snapshot, presentation);
        return true;
    }

    public bool TryReplacePresentation(StatsOverlayPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (Current is not { } current || !ReferenceEquals(current.Snapshot, presentation.Snapshot))
            return false;
        Current = current with { Presentation = presentation };
        return true;
    }

    public void MarkStale()
    {
        if (Current is { IsStale: false } current) Current = current with { IsStale = true };
    }

    public void MarkFresh()
    {
        if (Current is { IsStale: true } current) Current = current with { IsStale = false };
    }

    public void Clear() => Current = null;
}

public static class StatsOverlayResponseSelection
{
    public static bool Matches(
        StatsOverlayResponse response,
        string scope,
        string? citySlug,
        IReadOnlyList<string> routeJoinKeys)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(routeJoinKeys);
        if (response.Selection is null
            || !string.Equals(response.Selection.Scope, scope, StringComparison.Ordinal)
            || !string.Equals(response.Selection.CitySlug, citySlug, StringComparison.Ordinal)
            || response.Selection.RouteJoinKeys is null)
            return false;

        return response.Selection.RouteJoinKeys.SequenceEqual(
            routeJoinKeys.OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
