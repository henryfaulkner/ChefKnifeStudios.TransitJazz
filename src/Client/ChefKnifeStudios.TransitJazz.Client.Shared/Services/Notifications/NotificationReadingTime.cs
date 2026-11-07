using System;
using System.Collections.Generic;
using System.Threading;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

public enum NotificationReadingPauseReason
{
    Hidden,
    Hover,
    Focus
}

/// <summary>Owns one monotonic reading budget and pauses it for independent visitor states.</summary>
public sealed class NotificationReadingTime : IDisposable
{
    static readonly TimeSpan WarningDuration = TimeSpan.FromSeconds(8);
    static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(12);

    readonly object _sync = new();
    readonly TimeProvider _timeProvider;
    readonly HashSet<NotificationReadingPauseReason> _pauseReasons = [];
    ITimer? _timer;
    long? _activePresentationId;
    long? _startedAt;
    long _timerGeneration;
    TimeSpan _remaining;
    bool _disposed;

    public NotificationReadingTime(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public event Action<long>? Expired;

    public long? ActivePresentationId
    {
        get
        {
            lock (_sync)
            {
                return _activePresentationId;
            }
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            lock (_sync)
            {
                return GetRemainingLocked(_timeProvider.GetTimestamp());
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _activePresentationId is not null;
            }
        }
    }

    public void Start(long presentationId, NotificationSeverity severity)
    {
        if (presentationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(presentationId));
        }

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelTimerLocked();
            _activePresentationId = presentationId;
            _remaining = severity == NotificationSeverity.Warning ? WarningDuration : ErrorDuration;
            _startedAt = _pauseReasons.Count == 0 ? _timeProvider.GetTimestamp() : null;
            if (_startedAt is not null)
            {
                ScheduleTimerLocked();
            }
        }
    }

    public void SetPaused(long presentationId, NotificationReadingPauseReason reason, bool paused)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        lock (_sync)
        {
            if (_disposed || _activePresentationId != presentationId)
            {
                return;
            }

            if (paused)
            {
                if (!_pauseReasons.Add(reason))
                {
                    return;
                }

                if (_pauseReasons.Count == 1)
                {
                    PauseTimerLocked();
                }
            }
            else if (_pauseReasons.Remove(reason) && _pauseReasons.Count == 0)
            {
                _startedAt = _timeProvider.GetTimestamp();
                ScheduleTimerLocked();
            }
        }
    }

    public void Cancel(long presentationId)
    {
        lock (_sync)
        {
            if (_disposed || _activePresentationId != presentationId)
            {
                return;
            }

            ClearActiveLocked();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ClearActiveLocked();
            Expired = null;
        }
    }

    void PauseTimerLocked()
    {
        _remaining = GetRemainingLocked(_timeProvider.GetTimestamp());
        _startedAt = null;
        CancelTimerLocked();
    }

    void ScheduleTimerLocked()
    {
        CancelTimerLocked();
        if (_disposed || _activePresentationId is null || _startedAt is null || _pauseReasons.Count != 0)
        {
            return;
        }

        var generation = ++_timerGeneration;
        _timer = _timeProvider.CreateTimer(
            _ => OnTimer(generation),
            null,
            _remaining,
            Timeout.InfiniteTimeSpan);
    }

    void CancelTimerLocked()
    {
        _timerGeneration++;
        _timer?.Dispose();
        _timer = null;
    }

    void OnTimer(long generation)
    {
        long? expiredPresentationId = null;
        lock (_sync)
        {
            if (_disposed || generation != _timerGeneration || _activePresentationId is null || _pauseReasons.Count != 0)
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;
            var remaining = GetRemainingLocked(_timeProvider.GetTimestamp());
            if (remaining > TimeSpan.Zero)
            {
                _remaining = remaining;
                _startedAt = _timeProvider.GetTimestamp();
                ScheduleTimerLocked();
                return;
            }

            expiredPresentationId = _activePresentationId;
            ClearActiveLocked();
        }

        if (expiredPresentationId is long id)
        {
            Expired?.Invoke(id);
        }
    }

    TimeSpan GetRemainingLocked(long now)
    {
        if (_startedAt is not long startedAt)
        {
            return _remaining;
        }

        var elapsed = _timeProvider.GetElapsedTime(startedAt, now);
        return elapsed >= _remaining ? TimeSpan.Zero : _remaining - elapsed;
    }

    void ClearActiveLocked()
    {
        CancelTimerLocked();
        _activePresentationId = null;
        _startedAt = null;
        _remaining = TimeSpan.Zero;
        _pauseReasons.Clear();
    }
}
