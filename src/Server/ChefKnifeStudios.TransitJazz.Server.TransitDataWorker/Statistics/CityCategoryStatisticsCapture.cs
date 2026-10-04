using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed class CityCategoryStatisticsCapture
{
    readonly CityCategoryInsightsOptions _options;
    readonly ICategoryStatisticsSink _sink;
    readonly TimeProvider _timeProvider;
    readonly ILogger<CityCategoryStatisticsCapture> _logger;
    readonly ConcurrentDictionary<string, CityState> _cities = new(StringComparer.OrdinalIgnoreCase);

    public CityCategoryStatisticsCapture(CityCategoryInsightsOptions options, ICategoryStatisticsSink sink,
        TimeProvider timeProvider, ILogger<CityCategoryStatisticsCapture> logger)
    {
        _options = options;
        _sink = sink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsEnabledFor(string city) => _options.IsEnabledFor(city);

    public CityCategoryStatisticsCycle? BeginCycle(string city, IReadOnlyCollection<string> configuredCategories, long geometryGeneration)
    {
        if (!IsEnabledFor(city)) return null;
        var state = _cities.GetOrAdd(city, name => new CityState(name, _options.GapLimitFor(name)));
        state.AlignGeneration(geometryGeneration);
        return new CityCategoryStatisticsCycle(city, configuredCategories, geometryGeneration);
    }

    public bool RecordEligibleVehicle(CityCategoryStatisticsCycle? cycle, string vehicleIdentity, string category)
    {
        if (cycle is null || string.IsNullOrWhiteSpace(vehicleIdentity)) return false;
        try
        {
            return cycle.RecordActivity(vehicleIdentity, category);
        }
        catch
        {
            cycle.MarkCaptureFailure();
            return false;
        }
    }

    public CategoryMovementObservation ObserveMovement(CityCategoryStatisticsCycle? cycle, string vehicleIdentity,
        string category, string routeKey, ulong? sourceTimestampUnixSeconds, double alongRouteMeters, long geometryGeneration)
    {
        if (cycle is null) return new(false, 0, null);
        try
        {
            if (cycle.GeometryGeneration != geometryGeneration)
            {
                cycle.MarkCaptureFailure();
                return new(false, 0, MovementRejectionReason.GeometryChanged);
            }
            var state = _cities[cycle.CitySlug];
            var result = state.ObserveMovement(vehicleIdentity, category, routeKey, sourceTimestampUnixSeconds, alongRouteMeters, geometryGeneration, _timeProvider.GetUtcNow().UtcDateTime);
            if (result.RejectionReason == MovementRejectionReason.GeometryChanged)
            {
                cycle.MarkCaptureFailure();
                return new(false, 0, MovementRejectionReason.GeometryChanged);
            }
            cycle.RecordMovement(category, result);
            return result;
        }
        catch
        {
            cycle.MarkCaptureFailure();
            return new(false, 0, MovementRejectionReason.InvalidGeometry);
        }
    }

    public void RecordCrossings(CityCategoryStatisticsCycle? cycle, IEnumerable<string> categories)
    {
        if (cycle is null) return;
        try
        {
            foreach (var category in categories) cycle.RecordCrossing(category);
        }
        catch { cycle.MarkCaptureFailure(); }
    }

    public void CompleteCycle(CityCategoryStatisticsCycle? cycle, bool activityEligible, bool publicationKnown, bool cycleFailed, DateTime completedAtUtc)
    {
        if (cycle is null) return;
        try
        {
            var state = _cities[cycle.CitySlug];
            var now = completedAtUtc.Kind == DateTimeKind.Utc ? completedAtUtc : completedAtUtc.ToUniversalTime();
            var batch = state.TryCompleteCycle(cycle, activityEligible, publicationKnown, cycleFailed, now);
            if (batch is not null) Enqueue(state, batch);
            var swept = state.Accumulator.Sweep(now);
            if (swept is not null) Enqueue(state, swept);
        }
        catch (Exception ex)
        {
            try { _cities[cycle.CitySlug].Accumulator.MarkCaptureLoss(); } catch { }
            SafeLog(() => _logger.LogWarning("Category statistics capture failed for {City}; exception type {ExceptionType}.", cycle.CitySlug, ex.GetType().Name));
        }
    }

    public void SweepPending()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var state in _cities.Values)
        {
            try
            {
                var batch = state.Accumulator.Sweep(now);
                if (batch is not null) Enqueue(state, batch);
            }
            catch (Exception ex)
            {
                state.Accumulator.MarkCaptureLoss();
                SafeLog(() => _logger.LogWarning("Category statistics sweep failed for {City}; exception type {ExceptionType}.", state.CitySlug, ex.GetType().Name));
            }
        }
    }

    public void FlushPending()
    {
        foreach (var state in _cities.Values)
        {
            try
            {
                var batch = state.Accumulator.FlushPending();
                if (batch is not null) Enqueue(state, batch);
            }
            catch (Exception ex)
            {
                state.Accumulator.MarkCaptureLoss();
                SafeLog(() => _logger.LogWarning("Category statistics shutdown flush failed for {City}; exception type {ExceptionType}.", state.CitySlug, ex.GetType().Name));
            }
        }
    }

    public void InvalidateGeometry(string city, long newGeneration)
    {
        if (_cities.TryGetValue(city, out var state)) state.InvalidateGeometry(newGeneration);
    }

    public void PruneMovement(DateTime cutoffUtc)
    {
        foreach (var state in _cities.Values) state.PruneMovement(cutoffUtc);
    }

    void Enqueue(CityState state, FinalizedCategoryStatisticsBatch batch)
    {
        try
        {
            if (_sink.TryEnqueue(batch)) return;
        }
        catch { }
        state.Accumulator.MarkQueueLoss(batch);
        SafeLog(() => _logger.LogWarning("Category statistics queue admission failed for {City}; minuteRows={MinuteRows}, hourRows={HourRows}.",
            state.CitySlug, batch.Minutes.Length, batch.Hours.Length));
    }

    static void SafeLog(Action log)
    {
        try { log(); }
        catch { }
    }

    sealed class CityState(string citySlug, int cadenceLimit)
    {
        readonly object _movementLock = new();
        readonly Dictionary<string, MovementBaseline> _movement = new(StringComparer.Ordinal);
        readonly Dictionary<string, TimestampWatermark> _timestampWatermarks = new(StringComparer.Ordinal);
        long _geometryGeneration = -1;
        public string CitySlug { get; } = citySlug;
        public CategoryStatisticsAccumulator Accumulator { get; } = new(citySlug, cadenceLimit);

        public CategoryMovementObservation ObserveMovement(string id, string category, string routeKey, ulong? timestamp, double meters, long generation, DateTime lastSeenUtc)
        {
            lock (_movementLock)
            {
                if (_geometryGeneration != generation)
                    return Reject(MovementRejectionReason.GeometryChanged);
                if (timestamp is null)
                {
                    _movement.Remove(id);
                    if (_timestampWatermarks.TryGetValue(id, out var current))
                        _timestampWatermarks[id] = current with { LastSeenUtc = lastSeenUtc };
                    return Reject(MovementRejectionReason.MissingTimestamp);
                }
                if (_timestampWatermarks.TryGetValue(id, out var watermark) && timestamp.Value <= watermark.TimestampUnixSeconds)
                {
                    _timestampWatermarks[id] = watermark with { LastSeenUtc = lastSeenUtc };
                    if (_movement.TryGetValue(id, out var currentBaseline))
                        _movement[id] = currentBaseline with { LastSeenUtc = lastSeenUtc };
                    return Reject(MovementRejectionReason.TimestampNotIncreasing);
                }
                _timestampWatermarks[id] = new(timestamp.Value, lastSeenUtc);
                if (!double.IsFinite(meters))
                {
                    _movement.Remove(id);
                    return Reject(MovementRejectionReason.InvalidGeometry);
                }
                if (_geometryGeneration != generation)
                {
                    _movement.Clear();
                    _geometryGeneration = generation;
                    Accumulator.MarkCaptureLoss();
                }

                if (!_movement.TryGetValue(id, out var prior))
                {
                    _movement[id] = new(routeKey, timestamp.Value, meters, generation, lastSeenUtc);
                    return Reject(MovementRejectionReason.FirstObservation);
                }
                if (prior.GeometryGeneration != generation)
                {
                    _movement[id] = new(routeKey, timestamp.Value, meters, generation, lastSeenUtc);
                    return Reject(MovementRejectionReason.GeometryChanged);
                }
                if (!string.Equals(prior.RouteKey, routeKey, StringComparison.Ordinal))
                {
                    _movement[id] = new(routeKey, timestamp.Value, meters, generation, lastSeenUtc);
                    return Reject(MovementRejectionReason.RouteTransfer);
                }
                var delta = Math.Abs(meters - prior.AlongRouteMeters);
                var accepted = double.IsFinite(delta) && delta <= 2000d;
                _movement[id] = new(routeKey, timestamp.Value, meters, generation, lastSeenUtc);
                if (!accepted) return Reject(MovementRejectionReason.ExcessiveDelta);
                var distance = decimal.Round((decimal)delta, 6, MidpointRounding.AwayFromZero);
                return new(true, distance, null);
            }
        }

        public void AlignGeneration(long generation)
        {
            lock (_movementLock)
            {
                if (_geometryGeneration == generation) return;
                if (_geometryGeneration >= 0)
                {
                    _movement.Clear();
                    Accumulator.MarkCaptureLoss();
                }
                _geometryGeneration = generation;
            }
        }

        public FinalizedCategoryStatisticsBatch? TryCompleteCycle(CityCategoryStatisticsCycle cycle,
            bool activityEligible, bool publicationKnown, bool cycleFailed, DateTime completedAtUtc)
        {
            lock (_movementLock)
            {
                if (_geometryGeneration != cycle.GeometryGeneration)
                {
                    cycle.MarkCaptureFailure();
                    Accumulator.MarkCaptureLoss();
                    return null;
                }
                return Accumulator.Observe(cycle.ToSnapshot(completedAtUtc,
                    activityEligible && !cycle.CaptureFailed,
                    publicationKnown && !cycle.CaptureFailed,
                    cycleFailed || cycle.CaptureFailed));
            }
        }

        public void InvalidateGeometry(long generation)
        {
            lock (_movementLock)
            {
                _movement.Clear();
                _geometryGeneration = generation;
                Accumulator.MarkCaptureLoss();
            }
        }

        public void PruneMovement(DateTime cutoffUtc)
        {
            lock (_movementLock)
            {
                foreach (var id in _movement.Where(x => x.Value.LastSeenUtc < cutoffUtc).Select(x => x.Key).ToArray())
                    _movement.Remove(id);
                foreach (var id in _timestampWatermarks.Where(x => x.Value.LastSeenUtc < cutoffUtc).Select(x => x.Key).ToArray())
                    _timestampWatermarks.Remove(id);
            }
        }

        static CategoryMovementObservation Reject(MovementRejectionReason reason) => new(false, 0, reason);
    }

    sealed record MovementBaseline(string RouteKey, ulong TimestampUnixSeconds, double AlongRouteMeters, long GeometryGeneration, DateTime LastSeenUtc);
    sealed record TimestampWatermark(ulong TimestampUnixSeconds, DateTime LastSeenUtc);
}

public sealed class CityCategoryStatisticsCycle(string citySlug, IReadOnlyCollection<string> configuredCategories, long geometryGeneration)
{
    readonly HashSet<string> _seenVehicles = new(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<string>> _activity = new(StringComparer.Ordinal);
    readonly Dictionary<string, MovementMetrics> _movement = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _crossings = new(StringComparer.Ordinal);
    int _completed;
    public string CitySlug { get; } = citySlug;
    public long GeometryGeneration { get; } = geometryGeneration;
    public bool CaptureFailed { get; private set; }

    public bool RecordActivity(string vehicleIdentity, string category)
    {
        if (!_seenVehicles.Add(vehicleIdentity)) return false;
        var normalized = Normalize(category);
        if (!_activity.TryGetValue(normalized, out var identities)) _activity[normalized] = identities = new(StringComparer.Ordinal);
        identities.Add(vehicleIdentity);
        return true;
    }

    public void RecordMovement(string category, CategoryMovementObservation observation)
    {
        var normalized = Normalize(category);
        if (!_movement.TryGetValue(normalized, out var prior)) prior = MovementMetrics.None;
        var next = observation.Accepted
            ? new MovementMetrics(checked(prior.AcceptedIntervalCount + 1), checked(prior.DistanceMeters + observation.DistanceMeters), prior.RejectedCount)
            : observation.RejectionReason is null ? prior : prior with { RejectedCount = checked(prior.RejectedCount + 1) };
        _movement[normalized] = next;
    }

    public void RecordCrossing(string category)
    {
        var normalized = Normalize(category);
        _crossings[normalized] = checked(_crossings.GetValueOrDefault(normalized) + 1);
    }

    public void MarkCaptureFailure() => CaptureFailed = true;

    public CategoryCycleSnapshot ToSnapshot(DateTime atUtc, bool activityEligible, bool publicationKnown, bool cycleFailed)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) throw new InvalidOperationException("A city-category cycle can be completed once.");
        return new(atUtc, configuredCategories, _activity, _movement, _crossings, activityEligible, publicationKnown, cycleFailed);
    }

    static string Normalize(string category) => string.IsNullOrWhiteSpace(category) ? "unknown" : category.Trim().ToLowerInvariant();
}
