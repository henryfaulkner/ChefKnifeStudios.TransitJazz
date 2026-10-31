namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

/// <summary>A test-local clock whose monotonic time and scheduled callbacks move only when asked.</summary>
internal sealed class NotificationTestTimeProvider : TimeProvider
{
    readonly object _sync = new();
    readonly List<ManualTimer> _timers = [];
    readonly Queue<Action> _queuedCallbacks = [];
    long _timestamp;
    long _nextTimerId;
    DateTimeOffset _utcNow;

    public NotificationTestTimeProvider(DateTimeOffset? utcNow = null)
    {
        _utcNow = utcNow ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_sync)
        {
            return _timestamp;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_sync)
        {
            var timer = new ManualTimer(this, ++_nextTimerId, callback, state);
            _timers.Add(timer);
            timer.ChangeLocked(dueTime, period);
            QueueDueCallbacksLocked(_timestamp);
            return timer;
        }
    }

    public int PendingCallbackCount
    {
        get
        {
            lock (_sync)
            {
                return _queuedCallbacks.Count;
            }
        }
    }

    public int ActiveTimerCount
    {
        get
        {
            lock (_sync)
            {
                return _timers.Count(timer => !timer.IsDisposedLocked && timer.DueTimestampLocked is not null);
            }
        }
    }

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_sync)
        {
            _utcNow = utcNow;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Monotonic time cannot move backwards.");
        }

        lock (_sync)
        {
            var target = checked(_timestamp + elapsed.Ticks);
            _utcNow += elapsed;
            QueueDueCallbacksLocked(target);
            _timestamp = target;
        }
    }

    public bool RunNextCallback()
    {
        Action callback;
        lock (_sync)
        {
            if (!_queuedCallbacks.TryDequeue(out callback!))
            {
                return false;
            }
        }

        callback();
        return true;
    }

    public void DrainCallbacks()
    {
        while (RunNextCallback())
        {
        }
    }

    void QueueDueCallbacksLocked(long targetTimestamp)
    {
        while (true)
        {
            var timer = _timers
                .Where(candidate => !candidate.IsDisposedLocked && candidate.DueTimestampLocked <= targetTimestamp)
                .OrderBy(candidate => candidate.DueTimestampLocked)
                .ThenBy(candidate => candidate.Id)
                .FirstOrDefault();

            if (timer is null)
            {
                return;
            }

            var dueTimestamp = timer.DueTimestampLocked!.Value;
            _timestamp = dueTimestamp;
            timer.QueueFireLocked();
            _queuedCallbacks.Enqueue(() => timer.Callback(timer.State));
            timer.ScheduleNextLocked(dueTimestamp);
        }
    }

    void RemoveTimer(ManualTimer timer)
    {
        lock (_sync)
        {
            timer.DisposeLocked();
            _timers.Remove(timer);
        }
    }

    sealed class ManualTimer(
        NotificationTestTimeProvider owner,
        long id,
        TimerCallback callback,
        object? state) : ITimer
    {
        long? _dueTimestamp;
        long _periodTicks;
        bool _disposed;

        public long Id { get; } = id;
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long? DueTimestampLocked => _dueTimestamp;
        public bool IsDisposedLocked => _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                if (_disposed)
                {
                    return false;
                }

                ChangeLocked(dueTime, period);
                owner.QueueDueCallbacksLocked(owner._timestamp);
                return true;
            }
        }

        public void Dispose() => owner.RemoveTimer(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void ChangeLocked(TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimeout(dueTime, nameof(dueTime));
            ValidateTimeout(period, nameof(period));

            _periodTicks = period > TimeSpan.Zero ? period.Ticks : 0;
            _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : checked(owner._timestamp + dueTime.Ticks);
        }

        public void QueueFireLocked()
        {
            if (_periodTicks == 0)
            {
                _dueTimestamp = null;
            }
        }

        public void ScheduleNextLocked(long firedAt)
        {
            if (_periodTicks > 0 && !_disposed)
            {
                _dueTimestamp = checked(firedAt + _periodTicks);
            }
        }

        public void DisposeLocked()
        {
            _disposed = true;
            _dueTimestamp = null;
        }

        static void ValidateTimeout(TimeSpan value, string parameterName)
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}

/// <summary>Holds one asynchronous operation until the test explicitly releases it.</summary>
internal sealed class HeldOperation
{
    readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;
    public Task Completed => _completed.Task;

    public async Task WaitAsync()
    {
        _entered.TrySetResult();
        await _release.Task.ConfigureAwait(false);
        _completed.TrySetResult();
    }

    public void Release() => _release.TrySetResult();
}
