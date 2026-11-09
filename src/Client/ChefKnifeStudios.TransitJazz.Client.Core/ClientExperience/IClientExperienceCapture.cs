using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using System;

namespace ChefKnifeStudios.TransitJazz.Client.Core.ClientExperience;

/// <summary>Passive, no-throw observation seam for capability owners.</summary>
public interface IClientExperienceCapture
{
    void TryCapture(ClientExperienceEvent observation);
}

public interface IClientExperienceAttemptCapture
{
    Guid StartAttempt(string citySlug, string journey, long deadlineForegroundMs, Guid? cityActivationId = null, Guid? parentActionId = null, string? action = null);
    void FinishAttempt(Guid attemptId, string citySlug, string journey, string outcome, string reason, Guid? cityActivationId = null);
}

public sealed class NullClientExperienceCapture : IClientExperienceCapture, IClientExperienceAttemptCapture
{
    public static NullClientExperienceCapture Instance { get; } = new();
    NullClientExperienceCapture() { }
    public void TryCapture(ClientExperienceEvent observation) { }
    public Guid StartAttempt(string citySlug, string journey, long deadlineForegroundMs, Guid? cityActivationId = null, Guid? parentActionId = null, string? action = null) => Guid.Empty;
    public void FinishAttempt(Guid attemptId, string citySlug, string journey, string outcome, string reason, Guid? cityActivationId = null) { }
}

/// <summary>Singleton owner-facing seam whose target is attached from the active Blazor scope.</summary>
public sealed class ClientExperienceCaptureProxy : IClientExperienceCapture, IClientExperienceAttemptCapture
{
    readonly object _gate = new();
    IClientExperienceCapture? _target;

    public void SetTarget(IClientExperienceCapture? target)
    {
        lock (_gate) _target = target;
    }

    public void TryCapture(ClientExperienceEvent observation)
    {
        IClientExperienceCapture? target;
        lock (_gate) target = _target;
        try { target?.TryCapture(observation); }
        catch { /* Capture is passive and must never affect an owner operation. */ }
    }

    public Guid StartAttempt(string citySlug, string journey, long deadlineForegroundMs, Guid? cityActivationId = null, Guid? parentActionId = null, string? action = null)
    {
        lock (_gate)
            return _target is IClientExperienceAttemptCapture attemptCapture
                ? SafeStart(attemptCapture, citySlug, journey, deadlineForegroundMs, cityActivationId, parentActionId, action)
                : Guid.Empty;
    }

    public void FinishAttempt(Guid attemptId, string citySlug, string journey, string outcome, string reason, Guid? cityActivationId = null)
    {
        IClientExperienceCapture? target;
        lock (_gate) target = _target;
        if (target is not IClientExperienceAttemptCapture attemptCapture) return;
        try { attemptCapture.FinishAttempt(attemptId, citySlug, journey, outcome, reason, cityActivationId); }
        catch { }
    }

    static Guid SafeStart(IClientExperienceAttemptCapture target, string citySlug, string journey, long deadlineForegroundMs,
        Guid? cityActivationId, Guid? parentActionId, string? action)
    {
        try { return target.StartAttempt(citySlug, journey, deadlineForegroundMs, cityActivationId, parentActionId, action); }
        catch { return Guid.Empty; }
    }
}
