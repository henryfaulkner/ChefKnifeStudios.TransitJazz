using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public enum MapReadinessStatus
{
    Pending,
    Ready,
    Unavailable
}

public enum MapFailureReason
{
    UnsupportedBrowser,
    InitializationFailed,
    RequiredLayerUnavailable
}

public enum MapReadinessSignal
{
    RequiredContentReady,
    UnsupportedBrowser,
    InitializationFailed,
    RequiredLayerUnavailable,
    IndividualResourceFailed
}

public sealed record MapReadinessOutcome(long InstanceGeneration, MapReadinessStatus Status, MapFailureReason? FailureReason)
{
    public bool IsReady => Status == MapReadinessStatus.Ready;
}

/// <summary>Tracks map readiness and visible time for the current map instance.</summary>
public sealed class MapReadinessState(TimeProvider timeProvider)
{
    static readonly TimeSpan PendingDelay = TimeSpan.FromSeconds(20);
    long _visibleStartedAt;
    TimeSpan _visibleElapsed;
    bool _isVisible = true;
    bool _active = true;

    public MapReadinessOutcome Current { get; private set; } = new(0, MapReadinessStatus.Pending, null);
    public bool IsVisible => _isVisible;
    public TimeSpan VisibleElapsed
        => _visibleElapsed + (_isVisible ? timeProvider.GetElapsedTime(_visibleStartedAt) : TimeSpan.Zero);
    public TimeSpan RemainingVisibleDelay
        => VisibleElapsed >= PendingDelay ? TimeSpan.Zero : PendingDelay - VisibleElapsed;

    public long BeginInstance()
    {
        var generation = Current.InstanceGeneration + 1;
        _active = true;
        _visibleElapsed = TimeSpan.Zero;
        _visibleStartedAt = timeProvider.GetTimestamp();
        _isVisible = true;
        Current = new(generation, MapReadinessStatus.Pending, null);
        return generation;
    }

    public bool Observe(long generation, MapReadinessSignal signal)
    {
        if (!_active || generation != Current.InstanceGeneration || Current.Status != MapReadinessStatus.Pending)
            return false;

        switch (signal)
        {
            case MapReadinessSignal.RequiredContentReady:
                Current = new(generation, MapReadinessStatus.Ready, null);
                break;
            case MapReadinessSignal.UnsupportedBrowser:
                Current = new(generation, MapReadinessStatus.Unavailable, MapFailureReason.UnsupportedBrowser);
                break;
            case MapReadinessSignal.InitializationFailed:
                Current = new(generation, MapReadinessStatus.Unavailable, MapFailureReason.InitializationFailed);
                break;
            case MapReadinessSignal.RequiredLayerUnavailable:
                Current = new(generation, MapReadinessStatus.Unavailable, MapFailureReason.RequiredLayerUnavailable);
                break;
            case MapReadinessSignal.IndividualResourceFailed:
                break;
        }
        return true;
    }

    public bool MarkUnavailable(long generation, MapFailureReason reason)
    {
        if (!_active || generation != Current.InstanceGeneration)
            return false;
        Current = new(generation, MapReadinessStatus.Unavailable, reason);
        return true;
    }

    public bool IsDelayedLoading
        => _active && Current.Status == MapReadinessStatus.Pending && VisibleElapsed >= PendingDelay;

    public bool SetVisible(long generation, bool visible)
    {
        if (!_active || generation != Current.InstanceGeneration) return false;
        if (_isVisible == visible) return true;
        if (_isVisible)
            _visibleElapsed += timeProvider.GetElapsedTime(_visibleStartedAt);
        else
            _visibleStartedAt = timeProvider.GetTimestamp();
        _isVisible = visible;
        return true;
    }

    public bool Invalidate(long generation)
    {
        if (generation != Current.InstanceGeneration || !_active) return false;
        _active = false;
        return true;
    }
}
