using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public enum MapRenderLayerKind
{
    PrimaryTransit,
    SecondaryLayer,
    OptionalResource
}

public sealed record MapCapabilityTransition(
    long Generation,
    NotificationIssueCode? IssueCode = null,
    NotificationIssueCode? ResolvedIssueCode = null);

public sealed record MapStyleChangeResult(
    bool RequestedStyleLoaded,
    bool PreviousRequiredSourcesAndLayersRestored,
    bool MapUsable,
    bool TerminalFailure = false,
    string? CheckpointVisible = null,
    long Generation = -1,
    long OperationGeneration = -1);

public sealed record MapRenderOperationOutcome(
    bool Rendered,
    bool TerminalFailure,
    long Generation,
    long OperationGeneration = -1);

/// <summary>Coarse, generation-scoped evidence for explicit style and transit-render outcomes.</summary>
public sealed class MapCapabilityOutcomeState(TimeProvider timeProvider)
{
    static readonly TimeSpan PersistentRenderFailureSpan = TimeSpan.FromSeconds(10);
    bool _ready;
    bool _active;
    bool _visible = true;
    bool _styleOperationCompleted;
    bool _styleIssueActive;
    long _styleOperationGeneration;
    long _visibleStartedAt;
    TimeSpan _visibleElapsed;
    TimeSpan? _firstFailureVisibleElapsed;
    int _failedValidBatches;
    bool _primaryFailureObserved;
    bool _secondaryFailureObserved;
    MapRenderLayerKind? _pendingFailureLayer;

    public long InstanceGeneration { get; private set; }
    public NotificationIssueCode? ActiveRenderIssue { get; private set; }

    public long BeginInstance()
    {
        InstanceGeneration++;
        _active = true;
        _ready = false;
        _visible = true;
        _styleOperationGeneration = 0;
        _styleOperationCompleted = false;
        _styleIssueActive = false;
        _visibleElapsed = TimeSpan.Zero;
        _firstFailureVisibleElapsed = null;
        _failedValidBatches = 0;
        _primaryFailureObserved = false;
        _secondaryFailureObserved = false;
        _pendingFailureLayer = null;
        ActiveRenderIssue = null;
        return InstanceGeneration;
    }

    public bool MarkReady(long generation)
    {
        if (!_active || generation != InstanceGeneration) return false;
        _ready = true;
        _visibleElapsed = TimeSpan.Zero;
        _visibleStartedAt = timeProvider.GetTimestamp();
        return true;
    }

    public long BeginStyleChange(long generation)
    {
        if (!_active || generation != InstanceGeneration || !_ready) return 0;
        _styleOperationCompleted = false;
        return ++_styleOperationGeneration;
    }

    public bool SetVisible(long generation, bool visible)
    {
        if (!_active || generation != InstanceGeneration) return false;
        if (_visible == visible) return true;
        if (_visible && _ready)
            _visibleElapsed += timeProvider.GetElapsedTime(_visibleStartedAt);
        else if (visible && _ready)
            _visibleStartedAt = timeProvider.GetTimestamp();
        _visible = visible;
        return true;
    }

    TimeSpan VisibleElapsed
        => _visibleElapsed + (_ready && _visible
            ? timeProvider.GetElapsedTime(_visibleStartedAt)
            : TimeSpan.Zero);

    public MapCapabilityTransition? CompleteStyleChange(
        long generation,
        long operationGeneration,
        bool requestedStyleLoaded,
        bool previousRequiredSourcesAndLayersRestored,
        bool mapUsable,
        bool terminalFailure = false)
    {
        if (generation != InstanceGeneration
            || !_active
            || operationGeneration == 0
            || operationGeneration != _styleOperationGeneration
            || _styleOperationCompleted)
            return null;

        _styleOperationCompleted = true;
        if (requestedStyleLoaded)
        {
            if (!_styleIssueActive) return null;
            _styleIssueActive = false;
            return new(generation, ResolvedIssueCode: NotificationIssueCode.MapStyleChangeFailed);
        }

        if (!mapUsable && terminalFailure)
        {
            var resolvedStyleIssue = _styleIssueActive
                ? NotificationIssueCode.MapStyleChangeFailed
                : (NotificationIssueCode?)null;
            _styleIssueActive = false;
            return new(generation, NotificationIssueCode.MapUnavailable,
                resolvedStyleIssue);
        }

        if (!mapUsable) return null;

        if (!previousRequiredSourcesAndLayersRestored) return null;
        _styleIssueActive = true;
        return new(generation, NotificationIssueCode.MapStyleChangeFailed);
    }

    public MapCapabilityTransition? ObserveRenderFailure(
        long generation,
        MapRenderLayerKind layer,
        bool validBatch,
        bool terminal)
    {
        if (!_active || generation != InstanceGeneration || !_ready || layer == MapRenderLayerKind.OptionalResource
            || (!validBatch && !terminal))
            return null;

        if (ActiveRenderIssue == NotificationIssueCode.MapTransitDisplayFailed)
            return null;
        if (ActiveRenderIssue == NotificationIssueCode.MapLayerUnavailable
            && layer == MapRenderLayerKind.SecondaryLayer
            && !terminal)
            return null;

        if (terminal)
        {
            var code = layer == MapRenderLayerKind.PrimaryTransit
                ? NotificationIssueCode.MapTransitDisplayFailed
                : NotificationIssueCode.MapLayerUnavailable;
            return SetRenderIssue(generation, code);
        }

        if (_firstFailureVisibleElapsed is null)
        {
            _firstFailureVisibleElapsed = VisibleElapsed;
            _failedValidBatches = 1;
        }
        else
        {
            _failedValidBatches++;
        }

        _primaryFailureObserved |= layer == MapRenderLayerKind.PrimaryTransit;
        _secondaryFailureObserved |= layer == MapRenderLayerKind.SecondaryLayer;
        _pendingFailureLayer = _primaryFailureObserved
            ? MapRenderLayerKind.PrimaryTransit
            : MapRenderLayerKind.SecondaryLayer;

        if (_failedValidBatches < 2
            || VisibleElapsed - _firstFailureVisibleElapsed.Value < PersistentRenderFailureSpan)
            return null;

        var codeAfterSpan = _pendingFailureLayer == MapRenderLayerKind.PrimaryTransit
            ? NotificationIssueCode.MapTransitDisplayFailed
            : NotificationIssueCode.MapLayerUnavailable;
        return SetRenderIssue(generation, codeAfterSpan);
    }

    public MapCapabilityTransition? ObserveSuccessfulRender(
        long generation,
        bool primaryLayerUsable,
        bool secondaryLayerUsable)
    {
        if (!_active || generation != InstanceGeneration || !_ready) return null;

        if (ActiveRenderIssue is NotificationIssueCode.MapTransitDisplayFailed && primaryLayerUsable)
            return ResolveRenderIssue(generation);
        if (ActiveRenderIssue is NotificationIssueCode.MapLayerUnavailable && secondaryLayerUsable)
            return ResolveRenderIssue(generation);

        if (ActiveRenderIssue is null
            && (( _pendingFailureLayer == MapRenderLayerKind.PrimaryTransit && primaryLayerUsable)
                || (_pendingFailureLayer == MapRenderLayerKind.SecondaryLayer && secondaryLayerUsable)))
            ResetPendingFailures();

        return null;
    }

    MapCapabilityTransition SetRenderIssue(long generation, NotificationIssueCode issueCode)
    {
        var resolved = ActiveRenderIssue;
        ActiveRenderIssue = issueCode;
        return new(generation, issueCode, resolved);
    }

    MapCapabilityTransition ResolveRenderIssue(long generation)
    {
        var resolved = ActiveRenderIssue;
        ActiveRenderIssue = null;
        ResetPendingFailures();
        return new(generation, ResolvedIssueCode: resolved);
    }

    void ResetPendingFailures()
    {
        _firstFailureVisibleElapsed = null;
        _failedValidBatches = 0;
        _primaryFailureObserved = false;
        _secondaryFailureObserved = false;
        _pendingFailureLayer = null;
    }

    public bool Invalidate(long generation)
    {
        if (!_active || generation != InstanceGeneration) return false;
        _active = false;
        _ready = false;
        ActiveRenderIssue = null;
        ResetPendingFailures();
        return true;
    }
}
