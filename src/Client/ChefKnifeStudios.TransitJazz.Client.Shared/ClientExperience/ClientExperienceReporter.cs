using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Client.Core.ClientExperience;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using Microsoft.JSInterop;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.ClientExperience;

public sealed class ClientExperienceReporter : IClientExperienceCapture, IClientExperienceAttemptCapture, IAsyncDisposable
{
    const string ModulePath = "./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/client-experience.js";
    readonly IJSRuntime _jsRuntime;
    readonly ClientExperienceAttemptTracker _attemptTracker;
    readonly ClientExperienceObservationTracker _observationTracker;
    readonly Dictionary<string, Guid> _capabilityEpisodes = new(StringComparer.Ordinal);
    readonly NotificationCoordinator? _notificationCoordinator;
    IJSObjectReference? _module;
    DotNetObjectReference<ClientExperienceReporter>? _callback;
    int? _visibilitySubscription;
    int? _schedulerId;
    bool _enabled;
    bool _disposed;
    bool _presentationSubscribed;
    string _currentCitySlug = "unknown";
    bool _isVisible = true;
    bool? _deliveryRequested;
    bool? _audioRequested;

    public ClientExperienceReporter(IJSRuntime jsRuntime, TimeProvider? timeProvider = null, NotificationCoordinator? notificationCoordinator = null)
    {
        _jsRuntime = jsRuntime;
        var clock = timeProvider ?? TimeProvider.System;
        _attemptTracker = new(clock);
        _observationTracker = new(clock);
        _notificationCoordinator = notificationCoordinator;
    }

    public event Action<bool>? VisibilityChanged;

    public void ReassessCapability(string citySlug, Guid? cityActivationId, string capability, string currentState, long ownerGeneration, string? reason = null)
    {
        if (!_enabled) return;
        var previousState = _observationTracker.GetCapabilityState(capability);
        if (previousState is null || string.Equals(previousState, currentState, StringComparison.Ordinal)) return;
        if (!_capabilityEpisodes.TryGetValue(capability, out var episodeId))
            episodeId = Guid.NewGuid();
        TryCapture(CreateEvent("capability_changed", citySlug, cityActivationId) with
        {
            Capability = capability,
            PreviousState = previousState,
            CurrentState = currentState,
            EpisodeId = episodeId,
            OwnerGeneration = Math.Max(0, ownerGeneration),
            InitialSnapshot = false,
            Reason = reason ?? ReasonFor(currentState)
        });
    }

    public Guid StartAttempt(string citySlug, string journey, long deadlineForegroundMs, Guid? cityActivationId = null,
        Guid? parentActionId = null, string? action = null)
    {
        var attemptId = Guid.NewGuid();
        if (!_enabled || !_attemptTracker.Start(attemptId, citySlug, journey, deadlineForegroundMs, cityActivationId, parentActionId, action)) return attemptId;
        TryCapture(CreateEvent("attempt_started", citySlug, cityActivationId) with
        {
            AttemptId = attemptId,
            ParentActionId = parentActionId,
            Journey = journey,
            Action = action,
            Eligible = true,
            DeadlineForegroundMs = deadlineForegroundMs
        });
        return attemptId;
    }

    public void FinishAttempt(Guid attemptId, string citySlug, string journey, string outcome, string reason, Guid? cityActivationId = null)
    {
        EmitCompletions(_attemptTracker.Advance());
        var completion = _attemptTracker.Finish(attemptId, outcome, reason);
        if (completion is not null) EmitCompletion(completion);
    }

    public void CaptureRuntimeFailure(string citySlug, string category, Guid? cityActivationId = null)
        => TryCapture(CreateEvent("runtime_failed", citySlug, cityActivationId) with { RuntimeCategory = category });

    public async Task InitializeAsync()
    {
        if (_module is not null || _disposed) return;
        try
        {
            _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _module.InvokeVoidAsync("startClientExperienceBootstrap");
            var identity = await _module.InvokeAsync<BootstrapIdentity?>("bootstrapIdentity");
            _enabled = identity?.Enabled == true;
            if (_enabled)
            {
                if (_notificationCoordinator is not null)
                {
                    _notificationCoordinator.PresentationSnapshotCommitted += OnPresentationSnapshotCommitted;
                    _presentationSubscribed = true;
                }
                _callback = DotNetObjectReference.Create(this);
                var visible = await _module.InvokeAsync<bool>("isVisible");
                _isVisible = visible;
                _attemptTracker.SetVisible(visible);
                EmitWindows(_observationTracker.SetVisible(visible));
                CaptureContextChanged(visible);
                _visibilitySubscription = await _module.InvokeAsync<int>("observeVisibility", _callback);
                _schedulerId = await _module.InvokeAsync<int>("startScheduler", _callback);
            }
        }
        catch
        {
            _enabled = false;
            _module = null;
        }
    }

    public void TryCapture(ClientExperienceEvent observation)
    {
        if (!_enabled || _module is null || _disposed) return;
        _currentCitySlug = observation.CitySlug;
        if (observation.EventKind == "capability_changed" && observation.Capability is not null && observation.CurrentState is not null)
        {
            var changed = _observationTracker.SetCapabilityState(observation.Capability, observation.CurrentState, out var windows);
            EmitWindows(windows);
            if (!changed) return;
            if (observation.EpisodeId is Guid episodeId)
                _capabilityEpisodes[observation.Capability] = episodeId;
            if (observation.Capability == "live_delivery")
            {
                var requested = observation.CurrentState != "not_requested";
                if (_deliveryRequested != requested)
                {
                    _deliveryRequested = requested;
                    CaptureContextChanged(_isVisible);
                }
            }
            else if (observation.Capability == "audio")
            {
                var requested = observation.CurrentState != "not_requested";
                if (_audioRequested != requested)
                {
                    _audioRequested = requested;
                    CaptureContextChanged(_isVisible);
                }
            }
        }
        _ = SubmitSafelyAsync(observation);
    }

    void OnPresentationSnapshotCommitted(NotificationPresentationSnapshot snapshot)
    {
        try
        {
            TryCapture(CreateEvent("toast_presented", snapshot.CitySlug, null) with
            {
                OccurredAtUtc = snapshot.AcceptedAtUtc,
                PresentationId = snapshot.PresentationId,
                Severity = snapshot.Severity.ToString().ToLowerInvariant(),
                SummaryKind = snapshot.Kind switch
                {
                    NotificationSummaryKind.SingleCapability => "single_capability",
                    NotificationSummaryKind.MultipleCapabilities => "multiple_capabilities",
                    _ => "general"
                },
                CapabilitiesPresented = snapshot.Capabilities.Select(NotificationPolicy.GetCapabilityResourceKey).ToArray(),
                IssueCodes = snapshot.IssueCodes.Select(code => code.ToString()).ToArray(),
                ExplainedIssueCodes = snapshot.ExplainedIssueCodes.Select(code => code.ToString()).ToArray(),
                ResourceKeys = snapshot.ResourceKeys.ToArray()
            });
        }
        catch { /* Presentation telemetry cannot affect notification delivery. */ }
    }

    async Task SubmitSafelyAsync(ClientExperienceEvent observation)
    {
        try { await _module!.InvokeVoidAsync("report", observation); }
        catch { /* Never surface telemetry failure through the visitor experience. */ }
    }

    static ClientExperienceEvent CreateEvent(string eventKind, string citySlug, Guid? cityActivationId)
        => new()
        {
            SchemaVersion = ClientExperienceCatalog.SchemaVersion,
            MeasurementVersion = ClientExperienceCatalog.MeasurementVersion,
            EventKind = eventKind,
            EventId = Guid.NewGuid(),
            PageSessionId = Guid.Empty,
            CityActivationId = cityActivationId,
            Sequence = 0,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            PageElapsedMs = 0,
            CitySlug = citySlug,
            ClientRelease = "unknown"
        };

    [JSInvokable]
    public Task OnVisibilityChanged(bool visible)
    {
        EmitCompletions(_attemptTracker.SetVisible(visible));
        EmitWindows(_observationTracker.SetVisible(visible));
        CaptureContextChanged(visible);
        try { VisibilityChanged?.Invoke(visible); }
        catch { /* A passive subscriber cannot break the browser listener. */ }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSchedulerTick()
    {
        EmitCompletions(_attemptTracker.Advance());
        EmitWindows(_observationTracker.Advance());
        return Task.CompletedTask;
    }

    void CaptureContextChanged(bool visible)
    {
        _isVisible = visible;
        TryCapture(CreateEvent("context_changed", _currentCitySlug, null) with
        {
            IsVisible = visible,
            DeliveryRequested = _deliveryRequested,
            AudioRequested = _audioRequested,
            ExclusionReason = visible ? null : "hidden"
        });
    }

    static string ReasonFor(string state) => state switch
    {
        "healthy" => "owner_confirmed",
        "degraded" or "unavailable" => "owner_failed",
        "not_requested" => "not_requested",
        _ => "unknown"
    };

    void EmitWindows(IEnumerable<ClientExperienceObservationWindow> windows)
    {
        foreach (var window in windows)
            TryCapture(CreateEvent("observation_window", _currentCitySlug, null) with
            {
                WindowSequence = window.Sequence,
                WindowStartMs = window.WindowStartMs,
                WindowEndMs = window.WindowEndMs,
                ObservedMs = window.ObservedMs,
                GapMs = window.GapMs,
                ExcludedMs = window.ExcludedMs,
                StateDurations = window.StateDurations
            });
    }

    void EmitCompletions(System.Collections.Generic.IReadOnlyList<ClientExperienceAttemptCompletion> completions)
    {
        foreach (var completion in completions) EmitCompletion(completion);
    }

    void EmitCompletion(ClientExperienceAttemptCompletion completion)
        => TryCapture(CreateEvent("attempt_finished", completion.CitySlug, completion.CityActivationId) with
        {
            AttemptId = completion.AttemptId,
            ParentActionId = completion.ParentActionId,
            Journey = completion.Journey,
            Action = completion.Action,
            Outcome = completion.Outcome,
            Reason = completion.Reason,
            ForegroundElapsedMs = completion.ForegroundElapsedMs,
            WallElapsedMs = completion.WallElapsedMs
        });

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_enabled && _module is not null)
        {
            var finalWindows = _observationTracker.FlushScopeEnd();
            foreach (var window in finalWindows)
                await SubmitSafelyAsync(CreateEvent("observation_window", _currentCitySlug, null) with
                {
                    WindowSequence = window.Sequence,
                    WindowStartMs = window.WindowStartMs,
                    WindowEndMs = window.WindowEndMs,
                    ObservedMs = window.ObservedMs,
                    GapMs = window.GapMs,
                    ExcludedMs = window.ExcludedMs,
                    StateDurations = window.StateDurations
                });
        }
        _disposed = true;
        if (_presentationSubscribed && _notificationCoordinator is not null)
        {
            _notificationCoordinator.PresentationSnapshotCommitted -= OnPresentationSnapshotCommitted;
            _presentationSubscribed = false;
        }
        if (_module is not null)
        {
            try
            {
                if (_visibilitySubscription is int id)
                    await _module.InvokeVoidAsync("removeVisibility", id);
                if (_schedulerId is int schedulerId)
                    await _module.InvokeVoidAsync("stopScheduler", schedulerId);
                await _module.DisposeAsync();
            }
            catch { }
        }
        _callback?.Dispose();
    }

    sealed record BootstrapIdentity(Guid PageSessionId, bool Enabled);

}
