using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;

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
    int _consecutiveFailures;

    public DateTimeOffset? RetryAtUtc { get; private set; }

    public bool IsCoolingDown(DateTimeOffset nowUtc) => RetryAtUtc is { } retryAt && nowUtc < retryAt;

    public static string CooldownProblemCode(string? previousProblemCode) =>
        string.IsNullOrWhiteSpace(previousProblemCode) ? "retry_wait" : previousProblemCode;

    public DateTimeOffset RecordFailure(DateTimeOffset nowUtc, TimeSpan? serverRetryAfter = null)
    {
        var serviceDelay = serverRetryAfter is { } value && value > TimeSpan.Zero ? value : TimeSpan.Zero;
        var backoffDelay = BackoffDelay(_consecutiveFailures);
        var delay = backoffDelay > serviceDelay ? backoffDelay : serviceDelay;
        if (_consecutiveFailures < int.MaxValue) _consecutiveFailures++;
        RetryAtUtc = nowUtc + delay;
        return RetryAtUtc.Value;
    }

    public static TimeSpan BackoffDelay(int consecutiveFailures) =>
        TimeSpan.FromSeconds(Math.Min(MaximumDelay.TotalSeconds,
            InitialDelay.TotalSeconds * Math.Pow(2, Math.Clamp(consecutiveFailures, 0, 3))));

    public void RecordSuccess()
    {
        RetryAtUtc = null;
        _consecutiveFailures = 0;
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

public sealed record StatsOverlayOpenDecision(
    StatsSnapshotState? Evidence,
    bool ShowInitialLoader,
    bool ShouldRequest);

public static class StatsOverlayOpenPolicy
{
    public static StatsOverlayOpenDecision Evaluate(StatsSnapshotState? matchingEvidence)
    {
        var usable = matchingEvidence?.Snapshot is not null && !matchingEvidence.Freshness.IsExpired;
        return new(
            matchingEvidence,
            ShowInitialLoader: !usable,
            ShouldRequest: !usable || matchingEvidence!.Freshness.IsDue || matchingEvidence.ProblemCode is not null);
    }
}
