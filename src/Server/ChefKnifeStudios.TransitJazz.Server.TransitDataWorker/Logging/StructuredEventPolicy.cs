namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

public enum StructuredEventEmissionKind
{
    Initial,
    Transition,
    Reminder,
    Recovery,
    Suppressed,
}

public readonly record struct StructuredEventPolicyDecision(
    bool ShouldEmit,
    StructuredEventEmissionKind Kind,
    string Key,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// In-process coalescing state for sparse operational events. It never blocks a worker action and
/// does not persist telemetry state. State is keyed by city, event name, and reason code.
/// </summary>
public sealed class StructuredEventPolicy
{
    readonly object _gate = new();
    readonly TimeProvider _clock;
    readonly TimeSpan _reminderInterval;
    readonly Dictionary<string, ActiveCondition> _active = new(StringComparer.Ordinal);

    public StructuredEventPolicy(TimeProvider? clock = null, TimeSpan? reminderInterval = null)
    {
        _clock = clock ?? TimeProvider.System;
        _reminderInterval = reminderInterval ?? TimeSpan.FromMinutes(15);
        if (_reminderInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reminderInterval));
    }

    public int ActiveConditionCount
    {
        get { lock (_gate) return _active.Count; }
    }

    public StructuredEventPolicyDecision Decide(StructuredLogEvent logEvent, bool conditionActive = true)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        logEvent.Validate();
        return Decide(logEvent.City, logEvent.EventName, logEvent.ReasonCode, conditionActive);
    }

    public StructuredEventPolicyDecision Decide(
        string? city,
        string eventName,
        string? reasonCode,
        bool conditionActive,
        DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? _clock.GetUtcNow()).ToUniversalTime();
        var key = MakeKey(city, eventName, reasonCode);

        lock (_gate)
        {
            if (!conditionActive)
            {
                if (_active.Remove(key))
                    return new(true, StructuredEventEmissionKind.Recovery, key, now);
                return new(false, StructuredEventEmissionKind.Suppressed, key, now);
            }

            // A changed reason is a material transition. Close older reasons for the same city
            // and event before recording the new active key.
            var prefix = $"{city ?? "<worker>"}|{eventName}|";
            var olderKeys = _active.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)
                && !string.Equals(k, key, StringComparison.Ordinal)).ToArray();
            var transitioned = olderKeys.Length > 0;
            foreach (var olderKey in olderKeys) _active.Remove(olderKey);

            if (!_active.TryGetValue(key, out var active))
            {
                _active[key] = new ActiveCondition(now);
                return new(true,
                    transitioned ? StructuredEventEmissionKind.Transition : StructuredEventEmissionKind.Initial,
                    key, now);
            }

            if (_reminderInterval == TimeSpan.Zero || now - active.LastEmittedUtc >= _reminderInterval)
            {
                _active[key] = active with { LastEmittedUtc = now };
                return new(true, StructuredEventEmissionKind.Reminder, key, now);
            }

            return new(false, StructuredEventEmissionKind.Suppressed, key, now);
        }
    }

    public StructuredEventPolicyDecision DecideRecovery(string? city, string eventName, string? reasonCode,
        DateTimeOffset? nowUtc = null) => Decide(city, eventName, reasonCode, conditionActive: false, nowUtc);

    public void Clear() { lock (_gate) _active.Clear(); }

    public static string MakeKey(string? city, string eventName, string? reasonCode) =>
        $"{city ?? "<worker>"}|{eventName}|{reasonCode ?? "<none>"}";

    readonly record struct ActiveCondition(DateTimeOffset LastEmittedUtc);
}
