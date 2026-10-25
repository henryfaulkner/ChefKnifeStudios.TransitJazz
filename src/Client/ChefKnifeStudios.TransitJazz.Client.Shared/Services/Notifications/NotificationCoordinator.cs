using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

public interface INotificationPresenter
{
    ValueTask<bool> PresentAsync(long presentationId, NotificationSummary summary, CancellationToken cancellationToken);
    ValueTask RemoveAsync(long presentationId, CancellationToken cancellationToken);
}

public enum NotificationBlockingOverlay
{
    GenericModal,
    AudioUnlock,
    Stats,
    CityMenu,
    SettingsBlade
}

/// <summary>Serializes bounded current incidents and revalidates every presentation against live state.</summary>
public sealed class NotificationCoordinator : IAsyncDisposable
{
    static readonly TimeSpan LiveInterruptionGrace = TimeSpan.FromSeconds(10);
    static readonly TimeSpan PresentationSpacing = TimeSpan.FromSeconds(10);
    static readonly TimeSpan HealthyRearm = TimeSpan.FromSeconds(30);

    readonly object _stateLock = new();
    readonly SemaphoreSlim _evaluationGate = new(1, 1);
    readonly TimeProvider _timeProvider;
    readonly NotificationPolicy _policy;
    readonly Dictionary<NotificationIssueCode, GraceIncident> _liveGrace = [];
    readonly Dictionary<NotificationCapability, long> _healthySince = [];
    readonly HashSet<NotificationCapability> _announcedCapabilities = [];
    readonly HashSet<NotificationCapability> _flappingSuppression = [];
    readonly HashSet<NotificationCapability> _escalatedCapabilities = [];
    readonly HashSet<NotificationBlockingOverlay> _blockingOverlays = [];
    readonly ILogger<NotificationCoordinator>? _logger;

    INotificationPresenter? _presenter;
    NotificationSummary? _currentPresentation;
    HashSet<NotificationIssueCode> _currentCoveredCodes = [];
    ITimer? _nextDeadlineTimer;
    CancellationTokenSource? _presentationAttempt;
    long _currentPresentationId;
    long _mutationVersion;
    long? _lastPresentationTimestamp;
    int _presenterGeneration;
    long _presentationAttemptGeneration;
    int? _failedPresenterGeneration;
    bool _visible = true;
    bool _modalBlocked;
    bool _deliveryDesired = true;
    bool _browserOffline;
    bool _disposed;

    public NotificationCoordinator(
        TimeProvider timeProvider,
        INotificationPresenter? presenter = null,
        ILogger<NotificationCoordinator>? logger = null,
        bool conditionalAudioSettingsMappingsEnabled = false,
        bool conditionalDeliveryMappingsEnabled = false)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _policy = new NotificationPolicy(
            timeProvider,
            conditionalAudioSettingsMappingsEnabled,
            conditionalDeliveryMappingsEnabled);
        _logger = logger;
        if (presenter is not null)
        {
            _presenter = presenter;
            _presenterGeneration = 1;
        }
    }

    public NotificationSummary? CurrentPresentation
    {
        get
        {
            lock (_stateLock)
            {
                return _currentPresentation;
            }
        }
    }

    public long? CurrentPresentationId
    {
        get
        {
            lock (_stateLock)
            {
                return _currentPresentation is null ? null : _currentPresentationId;
            }
        }
    }

    public event Action<long, NotificationSummary>? PresentationCommitted;
    public event Action<long>? PresentationRemoved;

    public int ActiveIncidentCount
    {
        get
        {
            lock (_stateLock)
            {
                return _policy.ActiveIncidentCount + _liveGrace.Count;
            }
        }
    }

    public int ScheduledTimerCount
    {
        get
        {
            lock (_stateLock)
            {
                return _nextDeadlineTimer is null ? 0 : 1;
            }
        }
    }

    public bool IsModalBlocked
    {
        get
        {
            lock (_stateLock)
            {
                return _modalBlocked;
            }
        }
    }

    public void SetScope(NotificationScope scope)
    {
        INotificationPresenter? presenter;
        long presentationId;
        int presenterGeneration;
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_policy.CurrentScope == scope)
            {
                return;
            }

            _policy.SetScope(scope);
            _liveGrace.Clear();
            _healthySince.Clear();
            _announcedCapabilities.Clear();
            _flappingSuppression.Clear();
            _escalatedCapabilities.Clear();
            _mutationVersion++;
            CancelPresentationAttemptLocked();
            presenter = _currentPresentation is null ? null : _presenter;
            presentationId = _currentPresentationId;
            presenterGeneration = _presenterGeneration;
            _currentPresentation = null;
            _currentCoveredCodes.Clear();
            ScheduleNextDeadlineLocked();
        }

        if (presenter is not null)
        {
            _ = RemoveStalePresentationAsync(presenter, presenterGeneration, presentationId);
        }
    }

    public void SetPresenter(INotificationPresenter? presenter)
    {
        INotificationPresenter? oldPresenter;
        long oldPresentationId;
        int oldPresenterGeneration;
        lock (_stateLock)
        {
            ThrowIfDisposed();
            oldPresenter = _presenter;
            if (ReferenceEquals(oldPresenter, presenter))
            {
                return;
            }

            oldPresentationId = _currentPresentationId;
            oldPresenterGeneration = _presenterGeneration;
            _presenter = presenter;
            _presenterGeneration++;
            _failedPresenterGeneration = null;
            if (!ReferenceEquals(oldPresenter, presenter))
            {
                _currentPresentation = null;
                _currentCoveredCodes.Clear();
            }

            _mutationVersion++;
            CancelPresentationAttemptLocked();
            ScheduleNextDeadlineLocked();
        }

        if (oldPresenter is not null && !ReferenceEquals(oldPresenter, presenter))
        {
            _ = RemoveStalePresentationAsync(oldPresenter, oldPresenterGeneration, oldPresentationId);
        }

        if (presenter is not null)
        {
            _ = EvaluateAsync();
        }
    }

    public void SetVisible(bool visible)
    {
        var recheck = false;
        INotificationPresenter? presenter = null;
        long presentationId = 0;
        int presenterGeneration = 0;
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_visible == visible)
            {
                return;
            }

            _visible = visible;
            _mutationVersion++;
            CancelPresentationAttemptLocked();
            if (!visible)
            {
                if (_currentPresentation is not null)
                {
                    presenter = _presenter;
                    presentationId = _currentPresentationId;
                    presenterGeneration = _presenterGeneration;
                    _currentPresentation = null;
                    _currentCoveredCodes.Clear();
                }

                foreach (var incident in _liveGrace.Values)
                {
                    incident.StartedAt = null;
                }

                if (_policy.TryGetActiveIssue(NotificationIssueCode.LiveInterrupted, out var liveIssue, out var communicated) && !communicated)
                {
                    _policy.Resolve(NotificationIssueCode.LiveInterrupted);
                    _liveGrace[NotificationIssueCode.LiveInterrupted] = new GraceIncident(liveIssue, null);
                }

                _policy.SuspendPendingBurst();

                _healthySince.Remove(NotificationCapability.Live);
            }
            else if (_deliveryDesired)
            {
                var now = _timeProvider.GetTimestamp();
                foreach (var incident in _liveGrace.Values)
                {
                    incident.StartedAt = now;
                }

                _policy.ResumePendingBurst();
                recheck = true;
            }

            ScheduleNextDeadlineLocked();
        }

        if (presenter is not null)
        {
            _ = RemoveStalePresentationAsync(presenter, presenterGeneration, presentationId);
        }

        if (recheck)
        {
            _ = EvaluateAsync();
        }
    }

    public void SetDeliveryDesired(bool desired)
    {
        INotificationPresenter? presenter = null;
        long presentationId = 0;
        int presenterGeneration = 0;
        var recheck = false;
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_deliveryDesired == desired)
            {
                return;
            }

            _deliveryDesired = desired;
            _mutationVersion++;
            CancelPresentationAttemptLocked();
            if (!desired)
            {
                _liveGrace.Clear();
                _policy.ResolveCapability(NotificationCapability.Live);
                _healthySince.Remove(NotificationCapability.Live);
                if (_announcedCapabilities.Contains(NotificationCapability.Live))
                {
                    _flappingSuppression.Add(NotificationCapability.Live);
                }

                if (_currentPresentation is not null && CoversCapability(_currentCoveredCodes, NotificationCapability.Live))
                {
                    presenter = _presenter;
                    presentationId = _currentPresentationId;
                    presenterGeneration = _presenterGeneration;
                    _currentPresentation = null;
                    _currentCoveredCodes.Clear();
                }
            }
            else if (_visible)
            {
                var now = _timeProvider.GetTimestamp();
                foreach (var incident in _liveGrace.Values)
                {
                    incident.StartedAt = now;
                }

                recheck = true;
            }

            ScheduleNextDeadlineLocked();
        }

        if (presenter is not null)
        {
            _ = RemoveStalePresentationAsync(presenter, presenterGeneration, presentationId);
        }

        if (recheck)
        {
            _ = EvaluateAsync();
        }
    }

    public void SetBrowserOnline(bool online)
    {
        var recheck = false;
        lock (_stateLock)
        {
            ThrowIfDisposed();
            var offline = !online;
            if (_browserOffline == offline)
            {
                return;
            }

            _browserOffline = offline;
            _mutationVersion++;
            CancelPresentationAttemptLocked();
            ScheduleNextDeadlineLocked();
            recheck = online;
        }

        if (recheck)
        {
            _ = EvaluateAsync();
        }
    }

    public void SetModalBlocked(bool blocked) => SetBlockingOverlay(NotificationBlockingOverlay.GenericModal, blocked);

    public void SetBlockingOverlay(NotificationBlockingOverlay overlay, bool blocked)
    {
        if (!Enum.IsDefined(overlay))
        {
            throw new ArgumentOutOfRangeException(nameof(overlay));
        }

        INotificationPresenter? presenter = null;
        long presentationId = 0;
        int presenterGeneration = 0;
        bool recheck = false;
        lock (_stateLock)
        {
            ThrowIfDisposed();
            var changed = blocked ? _blockingOverlays.Add(overlay) : _blockingOverlays.Remove(overlay);
            if (!changed)
            {
                return;
            }

            _modalBlocked = _blockingOverlays.Count != 0;
            _mutationVersion++;
            CancelPresentationAttemptLocked();
            if (_modalBlocked && _currentPresentation is not null)
            {
                presenter = _presenter;
                presentationId = _currentPresentationId;
                presenterGeneration = _presenterGeneration;
                _currentPresentation = null;
                _currentCoveredCodes.Clear();
            }

            recheck = !_modalBlocked;
            ScheduleNextDeadlineLocked();
        }

        if (presenter is not null)
        {
            _ = RemoveStalePresentationAsync(presenter, presenterGeneration, presentationId);
        }

        if (recheck)
        {
            _ = EvaluateAsync();
        }
    }

    public void Report(NotificationIssue issue)
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (!NotificationPolicy.IsKnownCode(issue.Code))
            {
                return;
            }

            if (issue.IsLocallyOwned || issue.OperationGeneration < 0 ||
                (_policy.CurrentScope is NotificationScope activeScope && issue.Scope != activeScope))
            {
                return;
            }

            var now = _timeProvider.GetTimestamp();
            RefreshHealthLatchesLocked(now);
            var capability = NotificationPolicy.GetCapability(issue.Code);
            if (_healthySince.TryGetValue(capability, out var healthySince) &&
                _timeProvider.GetElapsedTime(healthySince, now) < HealthyRearm)
            {
                _healthySince.Remove(capability);
                _flappingSuppression.Add(capability);
            }

            if (issue.Code == NotificationIssueCode.LiveInterrupted)
            {
                if (!_deliveryDesired)
                {
                    return;
                }

                if (_liveGrace.TryGetValue(issue.Code, out var existing))
                {
                    if (issue.OperationGeneration >= existing.Issue.OperationGeneration && issue != existing.Issue)
                    {
                        existing.Issue = issue;
                        _mutationVersion++;
                        CancelPresentationAttemptLocked();
                    }
                }
                else
                {
                    var startedAt = _visible ? now : (long?)null;
                    _liveGrace.Add(issue.Code, new GraceIncident(issue, startedAt));
                    _mutationVersion++;
                    CancelPresentationAttemptLocked();
                }

                ScheduleNextDeadlineLocked();
                return;
            }

            if (issue.Code == NotificationIssueCode.LiveStopped)
            {
                _liveGrace.Remove(NotificationIssueCode.LiveInterrupted);
            }

            var escalatesActiveWarning = IsActiveWarningEscalationLocked(issue.Code, capability);
            var shouldSuppress = (_flappingSuppression.Contains(capability) || _announcedCapabilities.Contains(capability)) &&
                !escalatesActiveWarning && !(issue.Code == NotificationIssueCode.AudioStopFailed && issue.IsVerifiedAudioStillPlaying);
            if (_policy.Report(issue))
            {
                if (shouldSuppress)
                {
                    _policy.MarkCommunicated([issue.Code]);
                }

                _mutationVersion++;
                CancelPresentationAttemptLocked();
            }

            if (!_visible)
            {
                _policy.SuspendPendingBurst();
            }

            ScheduleNextDeadlineLocked();
        }
    }

    public async Task ResolveAsync(NotificationCapability capability, CancellationToken cancellationToken = default)
    {
        INotificationPresenter? presenter = null;
        int presenterGeneration = 0;
        long presentationId = 0;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            var hadCommunication = _announcedCapabilities.Contains(capability) || _flappingSuppression.Contains(capability);
            _policy.ResolveCapability(capability);
            foreach (var code in _liveGrace.Keys.Where(code => NotificationPolicy.GetCapability(code) == capability).ToArray())
            {
                _liveGrace.Remove(code);
            }

            if (hadCommunication && (capability != NotificationCapability.Live || _deliveryDesired))
            {
                _healthySince[capability] = _timeProvider.GetTimestamp();
            }
            else
            {
                _healthySince.Remove(capability);
            }

            if (_currentPresentation is not null && CoversCapability(_currentCoveredCodes, capability))
            {
                presenter = _presenter;
                presentationId = _currentPresentationId;
                _currentPresentation = null;
                _currentCoveredCodes.Clear();
            }

            if (presenter is not null)
            {
                presenterGeneration = _presenterGeneration;
            }

            _mutationVersion++;
            CancelPresentationAttemptLocked();
            ScheduleNextDeadlineLocked();
        }

        if (presenter is null)
        {
            return;
        }

        await _evaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SafeRemoveAsync(presenter, presenterGeneration, presentationId, cancellationToken).ConfigureAwait(false);
            NotifyPresentationRemoved(presentationId);
        }
        finally
        {
            _evaluationGate.Release();
            lock (_stateLock)
            {
                ScheduleNextDeadlineLocked();
            }
        }
    }

    public async Task ResolveIssueAsync(
        NotificationIssueCode code,
        NotificationScope scope,
        long operationGeneration,
        CancellationToken cancellationToken = default)
    {
        INotificationPresenter? presenter = null;
        int presenterGeneration = 0;
        long presentationId = 0;
        lock (_stateLock)
        {
            if (_disposed || _policy.CurrentScope != scope || operationGeneration < 0
                || !_policy.TryGetActiveIssue(code, out var issue, out _)
                || issue.Scope != scope
                || issue.OperationGeneration > operationGeneration
                || !_policy.Resolve(code))
            {
                return;
            }

            if (_currentPresentation is not null && _currentCoveredCodes.Contains(code))
            {
                presenter = _presenter;
                presentationId = _currentPresentationId;
                _currentPresentation = null;
                _currentCoveredCodes.Clear();
            }

            if (presenter is not null)
                presenterGeneration = _presenterGeneration;

            _mutationVersion++;
            CancelPresentationAttemptLocked();
            ScheduleNextDeadlineLocked();
        }

        if (presenter is not null)
        {
            await _evaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SafeRemoveAsync(presenter, presenterGeneration, presentationId, cancellationToken).ConfigureAwait(false);
                NotifyPresentationRemoved(presentationId);
            }
            finally
            {
                _evaluationGate.Release();
                lock (_stateLock) ScheduleNextDeadlineLocked();
            }
        }

        await EvaluateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DismissAsync(CancellationToken cancellationToken = default) => RemoveCurrentAsync(cancellationToken);
    public Task TimeoutAsync(CancellationToken cancellationToken = default) => RemoveCurrentAsync(cancellationToken);

    public async Task EvaluateAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }
        }

        await _evaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? attempt = null;
        try
        {
            NotificationSummary? summary;
            INotificationPresenter? presenter;
            NotificationSummary? previous;
            long previousPresentationId;
            long attemptedPresentationId;
            long stateVersion;
            int presenterGeneration;
            bool emergency;
            bool replacing;

            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                var now = _timeProvider.GetTimestamp();
                RefreshHealthLatchesLocked(now);
                PromoteEligibleLiveGraceLocked(now);
                presenter = _presenter;
                if (!_visible || _modalBlocked || presenter is null || _failedPresenterGeneration == _presenterGeneration)
                {
                    ScheduleNextDeadlineLocked();
                    return;
                }

                summary = _policy.TryCreateSummary(_browserOffline);
                if (summary is null)
                {
                    ScheduleNextDeadlineLocked();
                    return;
                }

                emergency = _policy.CanBypassPresentationSpacing(summary);
                if (_lastPresentationTimestamp is long last &&
                    _timeProvider.GetElapsedTime(last, now) < PresentationSpacing && !emergency)
                {
                    ScheduleNextDeadlineLocked();
                    return;
                }

                previous = _currentPresentation;
                replacing = previous is not null;
                if (replacing && !CanReplaceCurrentLocked(previous!, summary, emergency))
                {
                    ScheduleNextDeadlineLocked();
                    return;
                }

                previousPresentationId = _currentPresentationId;
                attemptedPresentationId = checked(_currentPresentationId + 1);
                stateVersion = _mutationVersion;
                presenterGeneration = _presenterGeneration;
                attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _presentationAttempt = attempt;
                _presentationAttemptGeneration++;
            }

            if (replacing && previous is not null)
            {
                if (!await SafeRemoveAsync(presenter, presenterGeneration, previousPresentationId, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

            }

            lock (_stateLock)
            {
                if (_disposed || _modalBlocked || !_visible || attempt.IsCancellationRequested ||
                    _mutationVersion != stateVersion || _presenterGeneration != presenterGeneration ||
                    !ReferenceEquals(_presenter, presenter))
                {
                    if (replacing && _currentPresentationId == previousPresentationId)
                    {
                        _currentPresentation = null;
                        _currentCoveredCodes.Clear();
                    }

                    ScheduleNextDeadlineLocked();
                    return;
                }
            }

            bool presented;
            try
            {
                presented = await presenter.PresentAsync(attemptedPresentationId, summary, attempt.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPresenterFailure(presenterGeneration, exception);
                return;
            }

            if (!presented)
            {
                LogPresenterFailure(presenterGeneration, null);
                await SafeRemoveAsync(presenter, presenterGeneration, attemptedPresentationId, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            bool stale;
            lock (_stateLock)
            {
                stale = _disposed || _modalBlocked || !_visible || attempt.IsCancellationRequested ||
                    _mutationVersion != stateVersion || _presenterGeneration != presenterGeneration || !ReferenceEquals(_presenter, presenter);
                if (!stale)
                {
                    var covered = summary.IssueCodes.Concat(summary.ExplainedIssueCodes).Distinct().ToArray();
                    var coveredCapabilities = covered.Select(NotificationPolicy.GetCapability).Distinct().ToArray();
                    if (replacing && previous?.Severity == NotificationSeverity.Warning && summary.Severity == NotificationSeverity.Error)
                    {
                        foreach (var capability in coveredCapabilities)
                        {
                            _escalatedCapabilities.Add(capability);
                        }
                    }

                    _currentPresentation = summary;
                    _currentCoveredCodes = covered.ToHashSet();
                    _currentPresentationId = attemptedPresentationId;
                    _lastPresentationTimestamp = _timeProvider.GetTimestamp();
                    _policy.MarkCommunicated(covered);
                    foreach (var capability in coveredCapabilities)
                    {
                        _announcedCapabilities.Add(capability);
                    }

                    if (summary.Severity == NotificationSeverity.Warning)
                    {
                        foreach (var capability in coveredCapabilities)
                        {
                            _escalatedCapabilities.Remove(capability);
                        }
                    }
                }

                ScheduleNextDeadlineLocked();
            }

            if (stale)
            {
                await SafeRemoveAsync(presenter, presenterGeneration, attemptedPresentationId, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                NotifyPresentationCommitted(attemptedPresentationId, summary);
            }
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_presentationAttempt, attempt))
                {
                    _presentationAttempt = null;
                }
            }

            attempt?.Dispose();
            _evaluationGate.Release();
            lock (_stateLock)
            {
                ScheduleNextDeadlineLocked();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        INotificationPresenter? presenter;
        int presenterGeneration;
        long presentationId;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mutationVersion++;
            CancelPresentationAttemptLocked();
            _nextDeadlineTimer?.Dispose();
            _nextDeadlineTimer = null;
            presenter = _currentPresentation is null ? null : _presenter;
            presenterGeneration = _presenterGeneration;
            presentationId = _currentPresentationId;
            _currentPresentation = null;
            _currentCoveredCodes.Clear();
            _liveGrace.Clear();
            _healthySince.Clear();
        }

        await _evaluationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (presenter is not null)
            {
                await SafeRemoveAsync(presenter, presenterGeneration, presentationId, CancellationToken.None).ConfigureAwait(false);
                NotifyPresentationRemoved(presentationId);
            }
        }
        finally
        {
            _evaluationGate.Release();
        }
    }

    async Task RemoveCurrentAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }
        }

        await _evaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            INotificationPresenter? presenter;
            int presenterGeneration;
            long presentationId;
            lock (_stateLock)
            {
                presenter = _currentPresentation is null ? null : _presenter;
                presenterGeneration = _presenterGeneration;
                presentationId = _currentPresentationId;
                _currentPresentation = null;
                _currentCoveredCodes.Clear();
                ScheduleNextDeadlineLocked();
            }

            if (presenter is not null)
            {
                await SafeRemoveAsync(presenter, presenterGeneration, presentationId, cancellationToken).ConfigureAwait(false);
                NotifyPresentationRemoved(presentationId);
            }
        }
        finally
        {
            _evaluationGate.Release();
        }
    }

    bool CanReplaceCurrentLocked(NotificationSummary current, NotificationSummary candidate, bool emergency)
    {
        if (emergency)
        {
            return true;
        }

        if (candidate.Severity < current.Severity)
        {
            return false;
        }

        if (current.Severity == NotificationSeverity.Warning && candidate.Severity == NotificationSeverity.Error)
        {
            return !_currentCoveredCodes
                .Select(NotificationPolicy.GetCapability)
                .Any(_escalatedCapabilities.Contains);
        }

        return true;
    }

    bool IsActiveWarningEscalationLocked(NotificationIssueCode code, NotificationCapability capability)
    {
        if (_escalatedCapabilities.Contains(capability))
        {
            return false;
        }

        var activeWarning = _currentPresentation?.Severity == NotificationSeverity.Warning
            && _currentCoveredCodes.Any(currentCode => NotificationPolicy.GetCapability(currentCode) == capability)
            && NotificationPolicy.GetSeverity(code) == NotificationSeverity.Error;
        var dismissedLiveWarning = code == NotificationIssueCode.LiveStopped
            && _policy.TryGetActiveIssue(NotificationIssueCode.LiveInterrupted, out _, out var communicated)
            && communicated;
        return activeWarning || dismissedLiveWarning;
    }

    void PromoteEligibleLiveGraceLocked(long now)
    {
        foreach (var pair in _liveGrace.ToArray())
        {
            if (pair.Value.StartedAt is not long startedAt || _timeProvider.GetElapsedTime(startedAt, now) < LiveInterruptionGrace)
            {
                continue;
            }

            var capability = NotificationCapability.Live;
            var issue = pair.Value.Issue;
            var suppressed = _flappingSuppression.Contains(capability) || _announcedCapabilities.Contains(capability);
            _policy.Report(issue);
            if (suppressed)
            {
                _policy.MarkCommunicated([issue.Code]);
            }

            _liveGrace.Remove(pair.Key);
            _mutationVersion++;
        }
    }

    void RefreshHealthLatchesLocked(long now)
    {
        foreach (var capability in _healthySince.Keys.ToArray())
        {
            var startedAt = _healthySince[capability];
            if (_timeProvider.GetElapsedTime(startedAt, now) < HealthyRearm)
            {
                continue;
            }

            _healthySince.Remove(capability);
            _announcedCapabilities.Remove(capability);
            _flappingSuppression.Remove(capability);
            _escalatedCapabilities.Remove(capability);
        }
    }

    bool CoversCapability(IEnumerable<NotificationIssueCode> codes, NotificationCapability capability) =>
        codes.Any(code => NotificationPolicy.GetCapability(code) == capability);

    async Task<bool> SafeRemoveAsync(INotificationPresenter presenter, int presenterGeneration, long presentationId, CancellationToken cancellationToken)
    {
        try
        {
            await presenter.RemoveAsync(presentationId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPresenterFailure(presenterGeneration, exception);
            return false;
        }
    }

    async Task RemoveStalePresentationAsync(INotificationPresenter presenter, int presenterGeneration, long presentationId)
    {
        await _evaluationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_currentPresentation is not null || _currentPresentationId != presentationId)
                {
                    return;
                }
            }

            await SafeRemoveAsync(presenter, presenterGeneration, presentationId, CancellationToken.None).ConfigureAwait(false);
            NotifyPresentationRemoved(presentationId);
        }
        finally
        {
            _evaluationGate.Release();
        }
    }

    void NotifyPresentationCommitted(long presentationId, NotificationSummary summary)
    {
        if (PresentationCommitted is not { } handlers)
        {
            return;
        }

        foreach (Action<long, NotificationSummary> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(presentationId, summary);
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Notification presentation listener failed for {PresentationId}.", presentationId);
            }
        }
    }

    void NotifyPresentationRemoved(long presentationId)
    {
        if (PresentationRemoved is not { } handlers)
        {
            return;
        }

        foreach (Action<long> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(presentationId);
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Notification removal listener failed for {PresentationId}.", presentationId);
            }
        }
    }

    void LogPresenterFailure(int presenterGeneration, Exception? exception)
    {
        lock (_stateLock)
        {
            if (_failedPresenterGeneration == presenterGeneration)
            {
                return;
            }

            _failedPresenterGeneration = presenterGeneration;
        }

        if (exception is null)
        {
            _logger?.LogWarning("Notification presentation failed for host generation {HostGeneration}.", presenterGeneration);
        }
        else
        {
            _logger?.LogError(exception, "Notification presentation failed for host generation {HostGeneration}.", presenterGeneration);
        }
    }

    void ScheduleNextDeadlineLocked()
    {
        if (_disposed)
        {
            _nextDeadlineTimer?.Dispose();
            _nextDeadlineTimer = null;
            return;
        }

        var now = _timeProvider.GetTimestamp();
        var deadlines = new List<long>();
        if (_visible)
        {
            deadlines.AddRange(_liveGrace.Values
                .Where(incident => incident.StartedAt is not null)
                .Select(incident => AddTimestamp(incident.StartedAt!.Value, LiveInterruptionGrace)));

            if (!_modalBlocked && _presenter is not null && _failedPresenterGeneration != _presenterGeneration && _policy.BurstStartedAt is long burstStartedAt)
            {
                deadlines.Add(AddTimestamp(burstStartedAt, NotificationPolicy.BurstWindow));
                if (_lastPresentationTimestamp is long last)
                {
                    deadlines.Add(AddTimestamp(last, PresentationSpacing));
                }
            }
        }

        deadlines.AddRange(_healthySince.Values.Select(timestamp => AddTimestamp(timestamp, HealthyRearm)));
        var next = deadlines.Where(deadline => deadline >= now).DefaultIfEmpty(long.MaxValue).Min();
        if (next == long.MaxValue)
        {
            if (_nextDeadlineTimer is not null)
            {
                _nextDeadlineTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            return;
        }

        var due = next <= now ? TimeSpan.Zero : _timeProvider.GetElapsedTime(now, next);
        _nextDeadlineTimer ??= _timeProvider.CreateTimer(OnDeadline, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _nextDeadlineTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    long AddTimestamp(long timestamp, TimeSpan duration) => checked(timestamp + (long)(duration.TotalSeconds * _timeProvider.TimestampFrequency));

    void OnDeadline(object? state)
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }
        }

        _ = EvaluateAsync();
    }

    void CancelPresentationAttemptLocked()
    {
        try
        {
            _presentationAttempt?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _presentationAttempt = null;
        }
    }

    void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    sealed class GraceIncident(NotificationIssue issue, long? startedAt)
    {
        public NotificationIssue Issue { get; set; } = issue;
        public long? StartedAt { get; set; } = startedAt;
    }
}
