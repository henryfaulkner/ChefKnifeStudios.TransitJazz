using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.ClientExperience;

public sealed record ClientExperienceObservationWindow(
    long Sequence,
    long WindowStartMs,
    long WindowEndMs,
    long ObservedMs,
    long GapMs,
    long ExcludedMs,
    IReadOnlyList<ClientExperienceStateDuration> StateDurations);

/// <summary>Builds bounded, non-overlapping foreground windows from owner states and browser visibility.</summary>
public sealed class ClientExperienceObservationTracker(
    TimeProvider timeProvider,
    TimeSpan? callbackGapTolerance = null,
    TimeSpan? fullWindowDuration = null)
{
    const long MaxWindowMs = 86_400_000;
    static readonly string[] Capabilities = ["routes", "map", "live_delivery", "audio"];
    readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    readonly long _callbackGapToleranceMs = (long)(callbackGapTolerance ?? TimeSpan.FromSeconds(15)).TotalMilliseconds;
    readonly long _fullWindowDurationMs = (long)(fullWindowDuration ?? TimeSpan.FromSeconds(60)).TotalMilliseconds;
    readonly Dictionary<string, string> _states = Capabilities.ToDictionary(capability => capability, _ => "unknown", StringComparer.Ordinal);
    readonly Dictionary<(string Capability, string State), long> _stateDurations = [];
    long _lastTimestamp = timeProvider.GetTimestamp();
    long _windowSequence;
    long _elapsedMs;
    long _windowStartMs;
    long _observedMs;
    long _gapMs;
    long _excludedMs;
    bool _visible = true;
    bool _disposed;

    public IReadOnlyList<ClientExperienceObservationWindow> Advance() => AdvanceCore(forceGap: false);

    public string? GetCapabilityState(string capability)
        => _states.TryGetValue(capability, out var state) ? state : null;

    public bool SetCapabilityState(string capability, string state, out IReadOnlyList<ClientExperienceObservationWindow> windows)
    {
        var emitted = AdvanceCore(forceGap: false).ToList();
        windows = emitted;
        if (_disposed || !ClientExperienceCatalog.Capabilities.Contains(capability)
            || !ClientExperienceCatalog.States.Contains(state)) return false;
        if (string.Equals(_states[capability], state, StringComparison.Ordinal)) return false;
        _states[capability] = state;
        return true;
    }

    public IReadOnlyList<ClientExperienceObservationWindow> SetVisible(bool visible)
    {
        if (_disposed || visible == _visible) return [];
        var emitted = AdvanceCore(forceGap: true).ToList();
        _visible = visible;
        if (_elapsedMs > _windowStartMs)
            emitted.Add(FlushWindow());
        return emitted;
    }

    public IReadOnlyList<ClientExperienceObservationWindow> FlushScopeEnd()
    {
        if (_disposed) return [];
        var emitted = AdvanceCore(forceGap: true).ToList();
        if (_elapsedMs > _windowStartMs)
            emitted.Add(FlushWindow());
        _disposed = true;
        return emitted;
    }

    IReadOnlyList<ClientExperienceObservationWindow> AdvanceCore(bool forceGap)
    {
        if (_disposed) return [];
        var now = _timeProvider.GetTimestamp();
        var elapsed = _timeProvider.GetElapsedTime(_lastTimestamp, now);
        _lastTimestamp = now;
        var elapsedMs = Math.Max(0, (long)elapsed.TotalMilliseconds);
        if (elapsedMs == 0) return [];

        var kind = !_visible
            ? IntervalKind.Excluded
            : forceGap || elapsedMs > _callbackGapToleranceMs
                ? IntervalKind.Gap
                : IntervalKind.Observed;
        var emitted = new List<ClientExperienceObservationWindow>();
        while (elapsedMs > 0)
        {
            var remainingWindowMs = MaxWindowMs - (_elapsedMs - _windowStartMs);
            if (remainingWindowMs <= 0)
            {
                emitted.Add(FlushWindow());
                remainingWindowMs = MaxWindowMs;
            }
            var chunk = Math.Min(elapsedMs, remainingWindowMs);
            AddInterval(chunk, kind);
            elapsedMs -= chunk;
            if (_elapsedMs - _windowStartMs >= _fullWindowDurationMs || _elapsedMs - _windowStartMs >= MaxWindowMs)
                emitted.Add(FlushWindow());
        }
        return emitted;
    }

    void AddInterval(long durationMs, IntervalKind kind)
    {
        _elapsedMs += durationMs;
        switch (kind)
        {
            case IntervalKind.Observed:
                _observedMs += durationMs;
                foreach (var capability in Capabilities)
                {
                    var key = (capability, _states[capability]);
                    _stateDurations.TryGetValue(key, out var current);
                    _stateDurations[key] = current + durationMs;
                }
                break;
            case IntervalKind.Gap:
                _gapMs += durationMs;
                break;
            case IntervalKind.Excluded:
                _excludedMs += durationMs;
                break;
        }
    }

    ClientExperienceObservationWindow FlushWindow()
    {
        var durations = new List<ClientExperienceStateDuration>();
        foreach (var capability in Capabilities)
        {
            var capabilityRows = _stateDurations
                .Where(pair => pair.Key.Capability == capability)
                .Select(pair => new ClientExperienceStateDuration
                {
                    Capability = capability,
                    State = pair.Key.State,
                    DurationMs = pair.Value
                })
                .ToArray();
            if (capabilityRows.Length == 0)
                durations.Add(new ClientExperienceStateDuration { Capability = capability, State = _states[capability], DurationMs = 0 });
            else
                durations.AddRange(capabilityRows);
        }

        var result = new ClientExperienceObservationWindow(
            ++_windowSequence, _windowStartMs, _elapsedMs, _observedMs, _gapMs, _excludedMs, durations);
        _windowStartMs = _elapsedMs;
        _observedMs = 0;
        _gapMs = 0;
        _excludedMs = 0;
        _stateDurations.Clear();
        return result;
    }

    enum IntervalKind { Observed, Gap, Excluded }
}
