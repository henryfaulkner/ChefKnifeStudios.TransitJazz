using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.ClientExperience;

public sealed record ClientExperienceAttemptCompletion(
    Guid AttemptId,
    string CitySlug,
    string Journey,
    Guid? CityActivationId,
    string Outcome,
    string Reason,
    long ForegroundElapsedMs,
    long WallElapsedMs,
    Guid? ParentActionId = null,
    string? Action = null);

/// <summary>Tracks accepted attempts using foreground time and closes uncertain callback gaps.</summary>
public sealed class ClientExperienceAttemptTracker(TimeProvider timeProvider, TimeSpan? suspensionTolerance = null, TimeSpan? correlationHorizon = null)
{
    readonly Dictionary<Guid, AttemptState> _attempts = [];
    readonly TimeSpan _suspensionTolerance = suspensionTolerance ?? TimeSpan.FromSeconds(15);
    readonly TimeSpan _correlationHorizon = correlationHorizon ?? TimeSpan.FromHours(24);
    bool _visible = true;

    public int ActiveCount => _attempts.Count;

    public bool Start(Guid attemptId, string citySlug, string journey, long deadlineForegroundMs, Guid? cityActivationId = null,
        Guid? parentActionId = null, string? action = null)
    {
        if (attemptId == Guid.Empty || deadlineForegroundMs < 1 || _attempts.ContainsKey(attemptId)) return false;
        var now = timeProvider.GetTimestamp();
        _attempts.Add(attemptId, new(citySlug, journey, deadlineForegroundMs, now, now, cityActivationId, parentActionId, action));
        return true;
    }

    public IReadOnlyList<ClientExperienceAttemptCompletion> Advance()
    {
        var now = timeProvider.GetTimestamp();
        var completed = new List<ClientExperienceAttemptCompletion>();
        foreach (var pair in _attempts.ToArray())
        {
            var state = pair.Value;
            var interval = timeProvider.GetElapsedTime(state.LastTick, now);
            if (_visible && interval <= _suspensionTolerance)
                state.ForegroundElapsed += interval;
            state.LastTick = now;
            var wall = timeProvider.GetElapsedTime(state.StartedAt, now);
            if (wall > _correlationHorizon)
                completed.Add(Complete(pair.Key, state, "superseded", "superseded", wall));
            else if (state.ForegroundElapsed >= TimeSpan.FromMilliseconds(state.DeadlineForegroundMs))
                completed.Add(Complete(pair.Key, state, "deadline_exceeded", "deadline", wall));
        }
        foreach (var result in completed) _attempts.Remove(result.AttemptId);
        return completed;
    }

    public IReadOnlyList<ClientExperienceAttemptCompletion> SetVisible(bool visible)
    {
        var completed = Advance();
        _visible = visible;
        return completed;
    }

    public ClientExperienceAttemptCompletion? Finish(Guid attemptId, string outcome, string reason)
    {
        if (!_attempts.Remove(attemptId, out var state)) return null;
        var wall = timeProvider.GetElapsedTime(state.StartedAt, timeProvider.GetTimestamp());
        return Complete(attemptId, state, outcome, reason, wall);
    }

    static ClientExperienceAttemptCompletion Complete(Guid id, AttemptState state, string outcome, string reason, TimeSpan wall)
        => new(id, state.CitySlug, state.Journey, state.CityActivationId, outcome, reason,
            Math.Max(0, (long)state.ForegroundElapsed.TotalMilliseconds), Math.Max(0, (long)wall.TotalMilliseconds),
            state.ParentActionId, state.Action);

    sealed class AttemptState(string citySlug, string journey, long deadlineForegroundMs, long startedAt, long lastTick,
        Guid? cityActivationId, Guid? parentActionId, string? action)
    {
        public string CitySlug { get; } = citySlug;
        public string Journey { get; } = journey;
        public long DeadlineForegroundMs { get; } = deadlineForegroundMs;
        public long StartedAt { get; } = startedAt;
        public long LastTick { get; set; } = lastTick;
        public Guid? CityActivationId { get; } = cityActivationId;
        public Guid? ParentActionId { get; } = parentActionId;
        public string? Action { get; } = action;
        public TimeSpan ForegroundElapsed { get; set; }
    }
}
