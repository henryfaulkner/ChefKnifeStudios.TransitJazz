namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

/// <summary>Controls wall-clock time and monotonic timer time independently for statistics tests.</summary>
internal sealed class StatsOverlayTestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    readonly object gate = new();
    readonly List<ManualTimer> timers = [];
    DateTimeOffset currentUtc = utcNow.ToUniversalTime();
    long timestamp;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return currentUtc;
    }

    public override long GetTimestamp()
    {
        lock (gate) return timestamp;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public int ActiveTimerCount
    {
        get
        {
            lock (gate) return timers.Count(timer => !timer.IsDisposed && timer.DueTimestamp is not null);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
        AdvanceCore(amount, advanceUtc: true);
    }

    public void AdvanceMonotonic(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
        AdvanceCore(amount, advanceUtc: false);
    }

    public void AdvanceUtc(TimeSpan amount)
    {
        lock (gate) currentUtc += amount;
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        lock (gate) currentUtc = value.ToUniversalTime();
    }

    void AdvanceCore(TimeSpan amount, bool advanceUtc)
    {
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (gate)
        {
            timestamp = checked(timestamp + amount.Ticks);
            if (advanceUtc) currentUtc += amount;

            foreach (var timer in timers.ToArray())
            {
                if (timer.IsDisposed || timer.DueTimestamp is not long due || due > timestamp) continue;
                callbacks.Add((timer.Callback, timer.State));
                timer.ScheduleNext(timestamp);
            }

            timers.RemoveAll(timer => timer.IsDisposed);
        }

        foreach (var (callback, state) in callbacks) callback(state);
    }

    void Change(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        ValidateTimerDuration(dueTime, nameof(dueTime));
        ValidateTimerDuration(period, nameof(period));
        lock (gate)
        {
            if (timer.IsDisposed) return;
            timer.PeriodTicks = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period.Ticks;
            timer.DueTimestamp = dueTime == Timeout.InfiniteTimeSpan ? null : checked(timestamp + dueTime.Ticks);
            if (!timers.Contains(timer)) timers.Add(timer);
        }
    }

    void Dispose(ManualTimer timer)
    {
        lock (gate)
        {
            timer.IsDisposed = true;
            timer.DueTimestamp = null;
            timers.Remove(timer);
        }
    }

    static void ValidateTimerDuration(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    sealed class ManualTimer(StatsOverlayTestTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long? DueTimestamp { get; set; }
        public long? PeriodTicks { get; set; }
        public bool IsDisposed { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed) return false;
            owner.Change(this, dueTime, period);
            return !IsDisposed;
        }

        public void Dispose() => owner.Dispose(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void ScheduleNext(long now) => DueTimestamp = PeriodTicks is long period
            ? checked(now + period)
            : null;
    }
}
