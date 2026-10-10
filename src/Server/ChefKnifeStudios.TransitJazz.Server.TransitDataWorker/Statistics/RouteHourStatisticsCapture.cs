using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Checkpoints;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed class RouteHourStatisticsCycle
{
    readonly Dictionary<string, CycleRouteCounter> _routes;
    readonly HashSet<string> _activityRepresentatives = new(StringComparer.Ordinal);
    readonly int _maxTrackedVehicles;
    readonly int _maxMemberships;

    internal RouteHourStatisticsCycle(string city, RouteHourCatalog catalog, int maxTrackedVehicles, int maxMemberships)
    {
        CitySlug = city;
        Catalog = catalog;
        _maxTrackedVehicles = maxTrackedVehicles;
        _maxMemberships = maxMemberships;
        _routes = catalog.Entries.Values.ToDictionary(entry => entry.RouteJoinKey,
            entry => new CycleRouteCounter(entry), StringComparer.Ordinal);
        if (!catalog.GeometryIdentityAvailable) MarkFailure("route_index_unavailable");
    }

    public string CitySlug { get; }
    public RouteHourCatalog Catalog { get; }
    public bool CaptureFailed { get; private set; }
    public bool ActivityEvidenceLost { get; private set; }
    public bool PopulationLost { get; private set; }
    public bool PublicationEvidenceLost { get; private set; }
    public ImmutableHashSet<string> IncompleteReasons => _incompleteReasons.ToImmutableHashSet(StringComparer.Ordinal);
    readonly HashSet<string> _incompleteReasons = new(StringComparer.Ordinal);

    internal bool RecordActivity(string vehicleId, string routeAlias)
    {
        if (string.IsNullOrWhiteSpace(vehicleId) || !Catalog.TryResolve(routeAlias, out var entry)) return false;
        if (!_activityRepresentatives.Add(vehicleId)) return false;
        if (_activityRepresentatives.Count > _maxTrackedVehicles || _activityRepresentatives.Count > _maxMemberships)
        {
            ActivityEvidenceLost = true;
            PopulationLost = true;
            MarkFailure("identity_limit_exceeded");
            return false;
        }
        _routes[entry.RouteJoinKey].ActiveVehicleIds.Add(vehicleId);
        return true;
    }

    internal void RecordMovement(string routeKey, bool accepted, decimal distanceMeters)
    {
        if (!_routes.TryGetValue(routeKey, out var route)) { MarkFailure("route_index_unavailable"); return; }
        if (accepted)
        {
            route.DistanceMetersSum = checked(route.DistanceMetersSum + distanceMeters);
            route.DistanceIntervalCount = checked(route.DistanceIntervalCount + 1);
        }
        else route.DistanceRejectedCount = checked(route.DistanceRejectedCount + 1);
    }

    internal void RecordProcessed(string routeKey, bool stale)
    {
        if (!_routes.TryGetValue(routeKey, out var route)) { MarkFailure("route_index_unavailable"); return; }
        route.VehicleObservationsProcessedCount = checked(route.VehicleObservationsProcessedCount + 1);
        if (stale) route.StaleObservationsCount = checked(route.StaleObservationsCount + 1);
    }

    internal void RecordSuppressed(string routeKey, CrossingSuppressionReason reason)
    {
        if (!_routes.TryGetValue(routeKey, out var route)) { MarkFailure("route_index_unavailable"); return; }
        switch (reason)
        {
            case CrossingSuppressionReason.FirstSeen: route.CrossingsSuppressedFirstSeen = checked(route.CrossingsSuppressedFirstSeen + 1); break;
            case CrossingSuppressionReason.DeltaLeqZero: route.CrossingsSuppressedDeltaLeqZero = checked(route.CrossingsSuppressedDeltaLeqZero + 1); break;
            case CrossingSuppressionReason.Teleport: route.CrossingsSuppressedTeleport = checked(route.CrossingsSuppressedTeleport + 1); break;
            case CrossingSuppressionReason.RouteTransfer: route.CrossingsSuppressedTransfer = checked(route.CrossingsSuppressedTransfer + 1); break;
        }
    }

    internal void RecordDetected(string routeKey, long count)
    {
        if (!_routes.TryGetValue(routeKey, out var route)) { MarkFailure("route_index_unavailable"); return; }
        route.CrossingsDetectedCount = checked(route.CrossingsDetectedCount + count);
    }

    internal void RecordPublished(IEnumerable<(string RouteKey, long Count)> published)
    {
        foreach (var (routeKey, count) in published)
        {
            if (!_routes.TryGetValue(routeKey, out var route)) { MarkFailure("route_index_unavailable"); PublicationEvidenceLost = true; continue; }
            route.CrossingsPublishedCount = checked(route.CrossingsPublishedCount + count);
        }
    }

    internal void MarkFailure(string reason = "capture_failure")
    {
        CaptureFailed = true;
        _incompleteReasons.Add(reason);
        if (reason == "identity_limit_exceeded")
        {
            ActivityEvidenceLost = true;
            PopulationLost = true;
        }
    }

    internal RouteHourCycleSnapshot ToSnapshot(DateTime completedAtUtc, bool activityEligible,
        bool publicationKnown, bool cycleFailed, RouteHourCatalog finalCatalog)
    {
        if (!string.Equals(Catalog.ContentFingerprint, finalCatalog.ContentFingerprint, StringComparison.Ordinal))
            MarkFailure("catalog_changed");
        if (!finalCatalog.GeometryIdentityAvailable) MarkFailure("route_index_unavailable");
        var routes = _routes.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Snapshot(), StringComparer.Ordinal);
        return new(CitySlug, completedAtUtc, Catalog, finalCatalog, routes,
            activityEligible && !ActivityEvidenceLost,
            publicationKnown && !PublicationEvidenceLost,
            cycleFailed || !activityEligible || !publicationKnown,
            CaptureFailed,
            PopulationLost,
            IncompleteReasons);
    }

    sealed class CycleRouteCounter(RouteHourCatalogEntry catalog)
    {
        public HashSet<string> ActiveVehicleIds { get; } = new(StringComparer.Ordinal);
        public long VehicleObservationsProcessedCount;
        public long StaleObservationsCount;
        public decimal DistanceMetersSum;
        public long DistanceIntervalCount;
        public long DistanceRejectedCount;
        public long CrossingsDetectedCount;
        public long CrossingsPublishedCount;
        public long CrossingsSuppressedFirstSeen;
        public long CrossingsSuppressedDeltaLeqZero;
        public long CrossingsSuppressedTeleport;
        public long CrossingsSuppressedTransfer;

        public RouteHourCycleRouteSnapshot Snapshot() => new(catalog, ActiveVehicleIds.ToImmutableArray(),
            VehicleObservationsProcessedCount, StaleObservationsCount, DistanceMetersSum, DistanceIntervalCount,
            DistanceRejectedCount, CrossingsDetectedCount, CrossingsPublishedCount, CrossingsSuppressedFirstSeen,
            CrossingsSuppressedDeltaLeqZero, CrossingsSuppressedTeleport, CrossingsSuppressedTransfer);
    }
}

/// <summary>Failure-isolated instrumentation around worker events and the bounded hour accumulator.</summary>
public sealed class RouteHourStatisticsCapture
{
    readonly RouteHourCaptureRuntimeOptions _runtime;
    readonly IRouteHourStatisticsSink _sink;
    readonly TimeProvider _timeProvider;
    readonly ILogger<RouteHourStatisticsCapture> _logger;
    readonly Guid _captureRunId = Guid.NewGuid();
    readonly Dictionary<string, CityState> _cities = new(StringComparer.OrdinalIgnoreCase);
    readonly object _citiesGate = new();

    public RouteHourStatisticsCapture(RouteHourCaptureRuntimeOptions runtime, IRouteHourStatisticsSink sink,
        TimeProvider timeProvider, ILogger<RouteHourStatisticsCapture> logger)
    {
        _runtime = runtime;
        _sink = sink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsEnabledFor(string city) => _runtime.IsEnabledFor(city);

    public RouteHourStatisticsCycle? BeginCycle(string city, RouteHourCatalog? catalog)
    {
        if (!IsEnabledFor(city)) return null;
        try
        {
            if (catalog is null || catalog.Entries.Count == 0 || catalog.Entries.Count > _runtime.Limits.MaxRoutesPerCityHour)
            {
                var reason = catalog is null || catalog.Entries.Count == 0 ? "route_index_unavailable" : "identity_limit_exceeded";
                try { GetCityState(city).Accumulator.MarkCaptureLoss(reason); } catch { }
                SafeLog(() => _logger.LogWarning("Route-hour capture unavailable for {City}; catalog entry count is outside its configured bounds.", city));
                return null;
            }
            GetCityState(city).AlignCatalog(catalog);
            return new RouteHourStatisticsCycle(city, catalog, _runtime.Limits.MaxTrackedVehiclesPerCity,
                _runtime.Limits.MaxVehicleRouteMembershipsPerCityHour);
        }
        catch (Exception ex)
        {
            try { GetCityState(city).Accumulator.MarkCaptureLoss(); } catch { }
            SafeLog(() => _logger.LogWarning("Route-hour capture begin failed for {City}; exception type {ExceptionType}.", city, ex.GetType().Name));
            return null;
        }
    }

    public bool RecordEligibleVehicle(RouteHourStatisticsCycle? cycle, string vehicleId, string routeAlias)
    {
        if (cycle is null) return false;
        try { return cycle.RecordActivity(vehicleId, routeAlias); }
        catch { cycle.MarkFailure(); return false; }
    }

    public void ObserveMovement(RouteHourStatisticsCycle? cycle, string vehicleId, string routeKey,
        ulong? sourceTimestampUnixSeconds, double alongRouteMeters)
    {
        if (cycle is null) return;
        try
        {
            if (!cycle.Catalog.TryGet(routeKey, out var entry))
            {
                cycle.MarkFailure("route_index_unavailable");
                return;
            }
            var state = GetCityState(cycle.CitySlug);
            var movement = state.ObserveMovement(vehicleId, entry, sourceTimestampUnixSeconds, alongRouteMeters,
                _timeProvider.GetUtcNow().UtcDateTime, _runtime.Limits.MaxTrackedVehiclesPerCity);
            if (movement.IdentityLimitExceeded) cycle.MarkFailure("identity_limit_exceeded");
            cycle.RecordMovement(entry.RouteJoinKey, movement.Accepted, movement.DistanceMeters);
        }
        catch { cycle.MarkFailure(); }
    }

    public void RecordProcessedObservation(RouteHourStatisticsCycle? cycle, string routeKey, bool stale)
    {
        if (cycle is null) return;
        TryHook(cycle, () => cycle.RecordProcessed(routeKey, stale));
    }

    public void RecordSuppression(RouteHourStatisticsCycle? cycle, string routeKey, CrossingSuppressionReason reason)
    {
        if (cycle is null) return;
        TryHook(cycle, () => cycle.RecordSuppressed(routeKey, reason));
    }

    public void RecordDetected(RouteHourStatisticsCycle? cycle, string routeKey, long count)
    {
        if (cycle is null || count <= 0) return;
        TryHook(cycle, () => cycle.RecordDetected(routeKey, count));
    }

    public void RecordPublished(RouteHourStatisticsCycle? cycle, IEnumerable<(string RouteKey, long Count)> published)
    {
        if (cycle is null) return;
        try { cycle.RecordPublished(published); }
        catch { cycle.MarkFailure(); }
    }

    public void CompleteCycle(RouteHourStatisticsCycle? cycle, bool activityEligible, bool publicationKnown,
        bool cycleFailed, DateTime completedAtUtc, RouteHourCatalog? finalCatalog, RouteHourIncompleteReason? failureReason = null)
    {
        if (cycle is null) return;
        try
        {
            if (failureReason is { } reason) cycle.MarkFailure(RouteHourStatisticRow.ToReason(reason));
            var final = finalCatalog ?? cycle.Catalog;
            var snapshot = cycle.ToSnapshot(completedAtUtc, activityEligible, publicationKnown, cycleFailed, final);
            Enqueue(GetCityState(cycle.CitySlug), GetCityState(cycle.CitySlug).Accumulator.Observe(snapshot));
        }
        catch (RouteHourIdentityLimitException exception)
        {
            try { GetCityState(cycle.CitySlug).Accumulator.MarkCaptureLoss("identity_limit_exceeded"); } catch { }
            SafeLog(() => _logger.LogWarning("Route-hour identity limit reached for {City}; exception type {ExceptionType}.", cycle.CitySlug, exception.GetType().Name));
        }
        catch (Exception ex)
        {
            try { GetCityState(cycle.CitySlug).Accumulator.MarkCaptureLoss(); } catch { }
            SafeLog(() => _logger.LogWarning("Route-hour capture commit failed for {City}; exception type {ExceptionType}.", cycle.CitySlug, ex.GetType().Name));
        }
    }

    public void SweepPending()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var state in GetCityStates())
        {
            try { Enqueue(state, state.Accumulator.Sweep(now)); }
            catch (Exception ex)
            {
                state.Accumulator.MarkCaptureLoss();
                SafeLog(() => _logger.LogWarning("Route-hour sweep failed for {City}; exception type {ExceptionType}.", state.CitySlug, ex.GetType().Name));
            }
        }
    }

    public void FlushPending()
    {
        foreach (var state in GetCityStates())
        {
            try { Enqueue(state, state.Accumulator.FlushPending()); }
            catch (Exception ex)
            {
                state.Accumulator.MarkCaptureLoss();
                SafeLog(() => _logger.LogWarning("Route-hour shutdown flush failed for {City}; exception type {ExceptionType}.", state.CitySlug, ex.GetType().Name));
            }
        }
    }

    public void PruneMovement(DateTime cutoffUtc)
    {
        foreach (var state in GetCityStates())
        {
            try { state.PruneMovement(cutoffUtc); }
            catch (Exception ex) { SafeLog(() => _logger.LogWarning("Route-hour movement pruning failed for {City}; exception type {ExceptionType}.", state.CitySlug, ex.GetType().Name)); }
        }
    }

    CityState GetCityState(string city)
    {
        lock (_citiesGate)
        {
            if (_cities.TryGetValue(city, out var state)) return state;
            state = new CityState(city, _captureRunId, _runtime.Limits, _runtime.Limits.GapLimitFor(city));
            _cities.Add(city, state);
            return state;
        }
    }

    CityState[] GetCityStates()
    {
        lock (_citiesGate) return _cities.Values.ToArray();
    }

    void Enqueue(CityState state, IReadOnlyList<FinalizedRouteHourStatisticsBatch> batches)
    {
        foreach (var batch in batches)
        {
            try
            {
                if (_sink.TryEnqueue(batch)) continue;
            }
            catch { }
            SafeLog(() => _logger.LogWarning("Route-hour batch admission failed for {City}; hour={HourStartUtc}, rows={Rows}.",
                state.CitySlug, batch.HourStartUtc, batch.RowCount));
        }
    }

    static void TryHook(RouteHourStatisticsCycle cycle, Action hook)
    {
        try { hook(); }
        catch { cycle.MarkFailure(); }
    }

    static void SafeLog(Action log)
    {
        try { log(); } catch { }
    }

    sealed class CityState(string citySlug, Guid runId, RouteHourHistoryOptions options, int cadenceLimit)
    {
        readonly object _movementGate = new();
        readonly Dictionary<string, MovementBaseline> _baselines = new(StringComparer.Ordinal);
        readonly Dictionary<string, TimestampWatermark> _watermarks = new(StringComparer.Ordinal);
        readonly HashSet<string> _trackedIdentities = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> _routeFingerprints = new(StringComparer.Ordinal);
        string? _lastCatalogFingerprint;
        public string CitySlug { get; } = citySlug;
        public RouteHourStatisticsAccumulator Accumulator { get; } = new(citySlug, runId, options, cadenceLimit);

        public void AlignCatalog(RouteHourCatalog catalog)
        {
            lock (_movementGate)
            {
                if (string.Equals(_lastCatalogFingerprint, catalog.ContentFingerprint, StringComparison.Ordinal)) return;
                foreach (var key in _routeFingerprints.Keys.Union(catalog.Entries.Keys, StringComparer.Ordinal).ToArray())
                {
                    var hadOld = _routeFingerprints.TryGetValue(key, out var oldFingerprint);
                    var hasNew = catalog.Entries.TryGetValue(key, out var entry);
                    if (hadOld && (!hasNew || !string.Equals(oldFingerprint, entry!.Fingerprint, StringComparison.Ordinal)))
                    {
                        foreach (var vehicle in _baselines.Where(pair => string.Equals(pair.Value.RouteJoinKey, key, StringComparison.Ordinal)).Select(pair => pair.Key).ToArray())
                            _baselines.Remove(vehicle);
                    }
                }
                _routeFingerprints.Clear();
                foreach (var entry in catalog.Entries.Values) _routeFingerprints.Add(entry.RouteJoinKey, entry.Fingerprint);
                _lastCatalogFingerprint = catalog.ContentFingerprint;
            }
        }

        public MovementResult ObserveMovement(string vehicle, RouteHourCatalogEntry route, ulong? timestamp,
            double meters, DateTime observedAtUtc, int maxTrackedVehicles)
        {
            lock (_movementGate)
            {
                if (string.IsNullOrWhiteSpace(vehicle)) return MovementResult.Rejected;
                if (timestamp is null)
                {
                    _baselines.Remove(vehicle);
                    if (_watermarks.TryGetValue(vehicle, out var old)) _watermarks[vehicle] = old with { LastSeenUtc = observedAtUtc };
                    return MovementResult.Rejected;
                }
                if (_watermarks.TryGetValue(vehicle, out var watermark) && timestamp.Value <= watermark.TimestampUnixSeconds)
                {
                    _watermarks[vehicle] = watermark with { LastSeenUtc = observedAtUtc };
                    if (_baselines.TryGetValue(vehicle, out var current)) _baselines[vehicle] = current with { LastSeenUtc = observedAtUtc };
                    return MovementResult.Rejected;
                }

                if (!_trackedIdentities.Contains(vehicle) && _trackedIdentities.Count >= maxTrackedVehicles)
                    return MovementResult.IdentityLimit;
                _watermarks[vehicle] = new(timestamp.Value, observedAtUtc);
                _trackedIdentities.Add(vehicle);
                if (!double.IsFinite(meters))
                {
                    _baselines.Remove(vehicle);
                    return MovementResult.Rejected;
                }
                if (!_baselines.TryGetValue(vehicle, out var prior))
                {
                    _baselines[vehicle] = new(route.RouteJoinKey, route.Fingerprint, timestamp.Value, meters, observedAtUtc);
                    return MovementResult.Rejected;
                }
                if (!string.Equals(prior.Fingerprint, route.Fingerprint, StringComparison.Ordinal))
                {
                    _baselines[vehicle] = new(route.RouteJoinKey, route.Fingerprint, timestamp.Value, meters, observedAtUtc);
                    return MovementResult.Rejected;
                }
                if (!string.Equals(prior.RouteJoinKey, route.RouteJoinKey, StringComparison.Ordinal))
                {
                    _baselines[vehicle] = new(route.RouteJoinKey, route.Fingerprint, timestamp.Value, meters, observedAtUtc);
                    return MovementResult.Rejected;
                }
                var delta = Math.Abs(meters - prior.AlongRouteMeters);
                _baselines[vehicle] = new(route.RouteJoinKey, route.Fingerprint, timestamp.Value, meters, observedAtUtc);
                if (!double.IsFinite(delta) || delta > 2000d) return MovementResult.Rejected;
                return new(true, decimal.Round((decimal)delta, 6, MidpointRounding.AwayFromZero));
            }
        }

        public void PruneMovement(DateTime cutoffUtc)
        {
            lock (_movementGate)
            {
                foreach (var key in _baselines.Where(pair => pair.Value.LastSeenUtc < cutoffUtc).Select(pair => pair.Key).ToArray()) _baselines.Remove(key);
                foreach (var key in _watermarks.Where(pair => pair.Value.LastSeenUtc < cutoffUtc).Select(pair => pair.Key).ToArray()) _watermarks.Remove(key);
                foreach (var key in _trackedIdentities.Where(key => !_baselines.ContainsKey(key) && !_watermarks.ContainsKey(key)).ToArray()) _trackedIdentities.Remove(key);
            }
        }

        sealed record MovementBaseline(string RouteJoinKey, string Fingerprint, ulong TimestampUnixSeconds,
            double AlongRouteMeters, DateTime LastSeenUtc);
        sealed record TimestampWatermark(ulong TimestampUnixSeconds, DateTime LastSeenUtc);
    }

    readonly record struct MovementResult(bool Accepted, decimal DistanceMeters, bool IdentityLimitExceeded = false)
    {
        public static MovementResult Rejected => new(false, 0);
        public static MovementResult IdentityLimit => new(false, 0, true);
    }
}
