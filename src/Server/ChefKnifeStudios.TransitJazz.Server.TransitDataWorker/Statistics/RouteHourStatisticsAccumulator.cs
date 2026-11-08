using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

internal sealed record RouteHourCycleRouteSnapshot(
    RouteHourCatalogEntry Catalog,
    ImmutableArray<string> ActiveVehicleIds,
    long VehicleObservationsProcessedCount,
    long StaleObservationsCount,
    decimal DistanceMetersSum,
    long DistanceIntervalCount,
    long DistanceRejectedCount,
    long CrossingsDetectedCount,
    long CrossingsPublishedCount,
    long CrossingsSuppressedFirstSeen,
    long CrossingsSuppressedDeltaLeqZero,
    long CrossingsSuppressedTeleport,
    long CrossingsSuppressedTransfer)
{
    public RouteHourSourceSummary? SourceEvidence { get; init; }
    public string? SourceSetFingerprint { get; init; }
    public bool DistanceEvidenceLost { get; init; }
    public bool ActivityEvidenceLost { get; init; }
    public bool PublicationEvidenceLost { get; init; }
    public bool ActivityEligible { get; init; }
    public bool PublicationKnown { get; init; }
}

internal sealed record RouteHourCycleSnapshot(
    string CitySlug,
    DateTime CompletedAtUtc,
    RouteHourCatalog Catalog,
    RouteHourCatalog FinalCatalog,
    ImmutableDictionary<string, RouteHourCycleRouteSnapshot> Routes,
    bool ActivityEligible,
    bool PublicationKnown,
    bool CycleFailed,
    bool CaptureFailed,
    bool PopulationLost,
    ImmutableHashSet<string> IncompleteReasons);

internal sealed class RouteHourIdentityLimitException(string message) : InvalidOperationException(message);

/// <summary>Bounded current and boundary-pending UTC hour state for one city.</summary>
public sealed class RouteHourStatisticsAccumulator
{
    readonly string _citySlug;
    readonly Guid _captureRunId;
    readonly int _cadenceLimitSeconds;
    readonly int _maxRoutes;
    readonly int _maxMemberships;
    readonly object _gate = new();
    readonly SortedDictionary<DateTime, HourCounter> _hours = [];
    readonly HashSet<string> _pendingCaptureLossReasons = new(StringComparer.Ordinal);
    DateTime? _lastObservationUtc;
    DateTime? _sealedThroughUtc;
    RouteHourCatalog? _lastObservedCatalog;

    public RouteHourStatisticsAccumulator(string citySlug, Guid captureRunId, RouteHourHistoryOptions options, int cadenceLimitSeconds)
    {
        _citySlug = citySlug;
        _captureRunId = captureRunId;
        _cadenceLimitSeconds = cadenceLimitSeconds;
        _maxRoutes = options.MaxRoutesPerCityHour;
        _maxMemberships = options.MaxVehicleRouteMembershipsPerCityHour;
    }

    internal IReadOnlyList<FinalizedRouteHourStatisticsBatch> Observe(RouteHourCycleSnapshot cycle)
    {
        lock (_gate)
        {
            var completed = CanonicalUtc(cycle.CompletedAtUtc);
            var hourStart = FloorHour(completed);
            var output = new List<FinalizedRouteHourStatisticsBatch>(1);
            var lastBefore = _lastObservationUtc;
            var clockRegressed = _lastObservationUtc is { } previousAt && completed < previousAt;
            if (clockRegressed)
            {
                foreach (var old in _hours.Values) old.AddReason("clock_regression");
            }
            if (_sealedThroughUtc is { } sealedThrough && hourStart <= sealedThrough) return output;
            if (clockRegressed)
            {
                if (_hours.Count > 0 && hourStart != _hours.Keys.Max())
                    return output;
            }

            if (_hours.Count > 0 && hourStart > _hours.Keys.Max())
            {
                foreach (var previousHour in _hours.Keys.Where(key => key < hourStart).ToArray())
                {
                    var previous = _hours[previousHour];
                    var gap = previous.LastCycleUtc is { } last ? (decimal)(completed - last).TotalSeconds : (decimal?)null;
                    var catalogChanged = !SameFingerprint(previous.LastCatalogFingerprint, cycle.Catalog)
                        || !SameFingerprint(previous.LastCatalogFingerprint, cycle.FinalCatalog)
                        || !SameFingerprint(cycle.Catalog.ContentFingerprint, cycle.FinalCatalog.ContentFingerprint);
                    var boundaryOk = cycle.ActivityEligible && cycle.PublicationKnown && !cycle.CycleFailed && !cycle.CaptureFailed
                        && gap is not null && gap <= _cadenceLimitSeconds && !clockRegressed
                        && !catalogChanged;
                    previous.EndBoundaryOk = boundaryOk;
                    if (gap is { } seconds) previous.AddGap(seconds);
                    if (!boundaryOk)
                    {
                        previous.AddReason(gap is { } value && value > _cadenceLimitSeconds ? "gap_exceeded" : "boundary_unproven");
                        if (catalogChanged)
                        {
                            previous.CatalogChanged = true;
                            previous.AddReason("catalog_changed");
                            previous.MergeCatalog(cycle.Catalog, _maxRoutes);
                            previous.MergeCatalog(cycle.FinalCatalog, _maxRoutes);
                        }
                    }
                    output.Add(Freeze(previousHour, previous));
                    _hours.Remove(previousHour);
                    _sealedThroughUtc = previousHour;
                }
            }

            if (!_hours.TryGetValue(hourStart, out var hour))
            {
                var catalogChangedAtStart = _lastObservedCatalog is not null
                    && !SameFingerprint(_lastObservedCatalog.ContentFingerprint, cycle.Catalog.ContentFingerprint);
                var catalogChangedInFlight = !SameFingerprint(cycle.Catalog.ContentFingerprint, cycle.FinalCatalog.ContentFingerprint);
                var initialCatalog = catalogChangedAtStart ? _lastObservedCatalog! : cycle.Catalog;
                hour = new HourCounter(_citySlug, initialCatalog, _captureRunId, hourStart);
                foreach (var reason in _pendingCaptureLossReasons) hour.MarkCaptureLoss(reason);
                _pendingCaptureLossReasons.Clear();
                if (catalogChangedAtStart || catalogChangedInFlight)
                {
                    hour.CatalogChanged = true;
                    hour.AddReason("catalog_changed");
                    hour.MergeCatalog(cycle.Catalog, _maxRoutes);
                    hour.MergeCatalog(cycle.FinalCatalog, _maxRoutes);
                }
                var predecessor = _lastObservationUtc is { } prior && FloorHour(prior) < hourStart;
                if (predecessor)
                {
                    var gap = (decimal)(completed - _lastObservationUtc!.Value).TotalSeconds;
                    hour.StartBoundaryOk = cycle.ActivityEligible && cycle.PublicationKnown && !cycle.CycleFailed
                        && !cycle.CaptureFailed && gap <= _cadenceLimitSeconds && !clockRegressed
                        && !catalogChangedAtStart && !catalogChangedInFlight;
                    hour.AddGap(gap);
                    if (catalogChangedAtStart || catalogChangedInFlight) hour.AddReason("catalog_changed");
                }
                else
                {
                    hour.StartBoundaryOk = false;
                    hour.AddReason("startup_fragment");
                }
                if (!hour.StartBoundaryOk) hour.AddReason("boundary_unproven");
                _hours.Add(hourStart, hour);
            }

            if (!SameFingerprint(hour.LastCatalogFingerprint, cycle.Catalog.ContentFingerprint)
                || !SameFingerprint(cycle.Catalog.ContentFingerprint, cycle.FinalCatalog.ContentFingerprint))
            {
                hour.CatalogChanged = true;
                hour.AddReason("catalog_changed");
                hour.MergeCatalog(cycle.Catalog, _maxRoutes);
                hour.MergeCatalog(cycle.FinalCatalog, _maxRoutes);
            }

            if (_lastObservationUtc is { } priorObservation && completed > priorObservation)
            {
                var gap = (decimal)(completed - priorObservation).TotalSeconds;
                hour.AddGap(gap);
                if (gap > _cadenceLimitSeconds) hour.AddReason("gap_exceeded");
            }
            else if (clockRegressed)
            {
                hour.AddReason("clock_regression");
            }

            if (cycle.CaptureFailed) hour.AddReason("capture_failure");
            foreach (var reason in cycle.IncompleteReasons) hour.AddReason(reason);
            if (cycle.Catalog.AmbiguousAliases.Length > 0 || !cycle.Catalog.GeometryIdentityAvailable
                || cycle.FinalCatalog.AmbiguousAliases.Length > 0 || !cycle.FinalCatalog.GeometryIdentityAvailable)
                hour.AddReason("route_index_unavailable");
            hour.AddCycle(cycle, _maxMemberships);
            hour.LastCatalogFingerprint = cycle.FinalCatalog.ContentFingerprint;
            _lastObservationUtc = completed;
            if (!clockRegressed) _lastObservedCatalog = cycle.FinalCatalog;

            if (clockRegressed)
            {
                hour.AddReason("clock_regression");
                _lastObservationUtc = lastBefore;
            }

            return output;
        }
    }

    public IReadOnlyList<FinalizedRouteHourStatisticsBatch> Sweep(DateTime nowUtc)
    {
        lock (_gate)
        {
            var now = CanonicalUtc(nowUtc);
            var ready = new List<FinalizedRouteHourStatisticsBatch>();
            var expiredThrough = FloorHour(now.AddHours(-1).AddSeconds(-_cadenceLimitSeconds));
            if (_sealedThroughUtc is null || expiredThrough > _sealedThroughUtc)
                _sealedThroughUtc = expiredThrough;
            foreach (var key in _hours.Keys.Where(hour => hour.AddHours(1).AddSeconds(_cadenceLimitSeconds) <= now).ToArray())
            {
                var counter = _hours[key];
                counter.EndBoundaryOk = false;
                counter.AddReason("boundary_unproven");
                ready.Add(Freeze(key, counter));
                _hours.Remove(key);
                if (_sealedThroughUtc is null || key > _sealedThroughUtc) _sealedThroughUtc = key;
            }
            return ready;
        }
    }

    public IReadOnlyList<FinalizedRouteHourStatisticsBatch> FlushPending()
    {
        lock (_gate)
        {
            var ready = new List<FinalizedRouteHourStatisticsBatch>(_hours.Count);
            foreach (var (key, counter) in _hours)
            {
                counter.EndBoundaryOk = false;
                counter.AddReason("shutdown_fragment");
                counter.AddReason("boundary_unproven");
                ready.Add(Freeze(key, counter));
                _sealedThroughUtc = key;
            }
            _hours.Clear();
            return ready;
        }
    }

    public void MarkCaptureLoss(string reason = "capture_failure")
    {
        lock (_gate)
        {
            if (_hours.Count == 0) _pendingCaptureLossReasons.Add(reason);
            foreach (var hour in _hours.Values) hour.MarkCaptureLoss(reason);
        }
    }

    FinalizedRouteHourStatisticsBatch Freeze(DateTime hourStart, HourCounter hour)
    {
        var rows = hour.Routes.Values.OrderBy(route => route.RouteJoinKey, StringComparer.Ordinal)
            .Select(route => route.ToRow(hourStart, _captureRunId, _cadenceLimitSeconds, hour)).ToImmutableArray();
        var batch = new FinalizedRouteHourStatisticsBatch(_citySlug, hourStart, _captureRunId, rows);
        batch.Validate(_maxRoutes);
        hour.ReleasePopulationSets();
        return batch;
    }

    static DateTime CanonicalUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) value = value.ToUniversalTime();
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }

    static bool SameFingerprint(string first, RouteHourCatalog second) =>
        string.Equals(first, second.ContentFingerprint, StringComparison.Ordinal);

    static bool SameFingerprint(string first, string second) =>
        string.Equals(first, second, StringComparison.Ordinal);

    static DateTime FloorHour(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    sealed class HourCounter(string citySlug, RouteHourCatalog catalog, Guid runId, DateTime hourStart)
    {
        readonly HashSet<string> _reasons = new(StringComparer.Ordinal);
        public readonly Dictionary<string, RouteCounter> Routes = catalog.Entries.Values.ToDictionary(
            entry => entry.RouteJoinKey, entry => new RouteCounter(citySlug, entry, runId, hourStart), StringComparer.Ordinal);
        readonly HashSet<(string Route, string Vehicle)> _populationMemberships = [];
        bool _populationLost;
        public string LastCatalogFingerprint = catalog.ContentFingerprint;
        public DateTime? LastCycleUtc;
        public bool StartBoundaryOk;
        public bool EndBoundaryOk;
        public bool CatalogChanged;
        public void AddReason(string reason) => _reasons.Add(reason);
        public void MarkCaptureLoss(string reason)
        {
            AddReason(reason);
            foreach (var route in Routes.Values) route.MarkLoss(reason);
        }
        public void AddGap(decimal seconds)
        {
            foreach (var route in Routes.Values) route.AddGap(seconds);
        }

        public void MergeCatalog(RouteHourCatalog current, int maxRoutes)
        {
            var additions = current.Entries.Values.Where(entry => !Routes.ContainsKey(entry.RouteJoinKey)).ToArray();
            if (Routes.Count + additions.Length > maxRoutes)
                throw new RouteHourIdentityLimitException("Route-hour cohort exceeds its configured route limit.");
            foreach (var entry in additions)
                if (!Routes.ContainsKey(entry.RouteJoinKey))
                {
                    var added = new RouteCounter(citySlug, entry, runId, hourStart);
                    if (_populationLost) added.MarkPopulationLost();
                    Routes.Add(entry.RouteJoinKey, added);
                }
        }

        public void AddCycle(RouteHourCycleSnapshot cycle, int maxMemberships)
        {
            var completedAtUtc = CanonicalUtc(cycle.CompletedAtUtc);
            LastCycleUtc = LastCycleUtc is { } last ? (last > completedAtUtc ? last : completedAtUtc) : completedAtUtc;
            var current = cycle.Routes;
            var populationLost = cycle.PopulationLost;
            if (!populationLost && cycle.ActivityEligible)
            {
                foreach (var sample in current.Values.Where(sample => sample.ActivityEligible))
                {
                    foreach (var vehicle in sample.ActiveVehicleIds)
                    {
                        if (_populationMemberships.Add((sample.Catalog.RouteJoinKey, vehicle)) && _populationMemberships.Count > maxMemberships)
                        {
                            populationLost = true;
                            break;
                        }
                    }
                    if (populationLost) break;
                }
            }
            if (populationLost)
            {
                _populationLost = true;
                foreach (var route in Routes.Values) route.MarkPopulationLost();
                AddReason("identity_limit_exceeded");
            }
            foreach (var route in Routes.Values)
            {
                if (current.TryGetValue(route.RouteJoinKey, out var sample))
                    route.AddCycle(sample, cycle, !_populationLost, _reasons);
                else
                    route.AddMissingCycle(cycle.CompletedAtUtc, _reasons);
            }
        }

        public IEnumerable<string> Reasons => _reasons;
        public void ReleasePopulationSets()
        {
            foreach (var route in Routes.Values) route.ReleasePopulationSet();
        }

        public sealed class RouteCounter(string city, RouteHourCatalogEntry catalog, Guid runId, DateTime hourStart)
        {
            HashSet<string>? _population = new(StringComparer.Ordinal);
            ActivitySupportFingerprintBuilder? _activitySupport;
            public string RouteJoinKey { get; } = catalog.RouteJoinKey;
            long _observed, _validActive, _validPublish, _failed, _activeSum, _peak, _processed, _stale,
                _intervals, _rejected, _detected, _published, _firstSeen, _deltaLeqZero, _teleport, _transfer;
            decimal _distance;
            decimal? _maxGap;
            DateTime? _first, _last;
            bool _populationLost;
            bool _distanceEvidenceLost;
            bool _activityEvidenceLost;
            bool _publicationEvidenceLost;
            bool _sourceEvidenceIncomplete;
            bool _sourceEvidenceConflict;
            RouteHourSourceSummary? _sourceEvidence;
            readonly HashSet<string> _reasons = new(StringComparer.Ordinal);
            readonly HashSet<string> _distanceReasons = new(StringComparer.Ordinal);
            readonly HashSet<string> _activityReasons = new(StringComparer.Ordinal);
            readonly HashSet<string> _publicationReasons = new(StringComparer.Ordinal);

            public void AddGap(decimal seconds) => _maxGap = _maxGap is null ? seconds : Math.Max(_maxGap.Value, seconds);

            public void AddCycle(RouteHourCycleRouteSnapshot sample, RouteHourCycleSnapshot cycle,
                bool trackPopulation, HashSet<string> hourReasons)
            {
                _observed = checked(_observed + 1);
                var completedAtUtc = CanonicalUtc(cycle.CompletedAtUtc);
                _first = _first is { } first && first < completedAtUtc ? first : completedAtUtc;
                _last = _last is { } last && last > completedAtUtc ? last : completedAtUtc;
                _processed = checked(_processed + sample.VehicleObservationsProcessedCount);
                _stale = checked(_stale + sample.StaleObservationsCount);
                _distance = checked(_distance + sample.DistanceMetersSum);
                _intervals = checked(_intervals + sample.DistanceIntervalCount);
                _rejected = checked(_rejected + sample.DistanceRejectedCount);
                _detected = checked(_detected + sample.CrossingsDetectedCount);
                _published = checked(_published + sample.CrossingsPublishedCount);
                _firstSeen = checked(_firstSeen + sample.CrossingsSuppressedFirstSeen);
                _deltaLeqZero = checked(_deltaLeqZero + sample.CrossingsSuppressedDeltaLeqZero);
                _teleport = checked(_teleport + sample.CrossingsSuppressedTeleport);
                _transfer = checked(_transfer + sample.CrossingsSuppressedTransfer);
                _distanceEvidenceLost |= sample.DistanceEvidenceLost;
                _activityEvidenceLost |= sample.ActivityEvidenceLost || cycle.PopulationLost;
                _publicationEvidenceLost |= sample.PublicationEvidenceLost;

                if (sample.SourceEvidence is null)
                {
                    _sourceEvidenceIncomplete = true;
                }
                else if (_sourceEvidence is null && _observed == 1)
                {
                    _sourceEvidence = sample.SourceEvidence;
                }
                else if (sample.SourceEvidence is not null && _sourceEvidence is not null)
                {
                    try { _sourceEvidence = MergeSourceEvidence(_sourceEvidence, sample.SourceEvidence); }
                    catch (Exception) { _sourceEvidenceConflict = true; }
                }
                else
                {
                    _sourceEvidenceIncomplete = true;
                }

                if (sample.SourceEvidence is { } sourceEvidence)
                {
                    foreach (var source in sourceEvidence.Sources)
                        foreach (var reason in source.Reasons) _reasons.Add(reason);
                }

                if (sample.ActivityEligible)
                {
                    _validActive = checked(_validActive + 1);
                    var activeCount = sample.ActiveVehicleIds.Length;
                    _activeSum = checked(_activeSum + activeCount);
                    _peak = Math.Max(_peak, activeCount);
                    if (trackPopulation && !_populationLost && _population is not null)
                    {
                        foreach (var vehicle in sample.ActiveVehicleIds) _population.Add(vehicle);
                    }
                    if (sample.SourceEvidence is not null && !sample.ActivityEvidenceLost
                        && !cycle.PopulationLost && !_populationLost)
                    {
                        try
                        {
                            _activitySupport ??= new ActivitySupportFingerprintBuilder(runId, hourStart);
                            _activitySupport.AddAcceptedCycle(completedAtUtc);
                        }
                        catch (ArgumentException)
                        {
                            _activityEvidenceLost = true;
                            _activityReasons.Add("invalid_measure_evidence");
                        }
                    }
                }
                if (sample.PublicationKnown) _validPublish = checked(_validPublish + 1);
                if (!sample.ActivityEligible || !sample.PublicationKnown || cycle.CycleFailed || cycle.CaptureFailed)
                    _failed = checked(_failed + 1);
                foreach (var reason in cycle.IncompleteReasons)
                {
                    _reasons.Add(reason);
                    if (sample.DistanceEvidenceLost && IsMeasureReason(reason)) _distanceReasons.Add(reason);
                    if ((sample.ActivityEvidenceLost || cycle.PopulationLost) && IsMeasureReason(reason)) _activityReasons.Add(reason);
                    if (sample.PublicationEvidenceLost && IsMeasureReason(reason)) _publicationReasons.Add(reason);
                }
                if (_distanceEvidenceLost && _distanceReasons.Count == 0) _distanceReasons.Add("invalid_measure_evidence");
                if (_activityEvidenceLost && _activityReasons.Count == 0) _activityReasons.Add("invalid_measure_evidence");
                if (_publicationEvidenceLost && _publicationReasons.Count == 0) _publicationReasons.Add("invalid_measure_evidence");
            }

            public void MarkPopulationLost()
            {
                _populationLost = true;
                _population = null;
                _activityEvidenceLost = true;
                _reasons.Add("identity_limit_exceeded");
                _activityReasons.Add("identity_limit_exceeded");
                _activityReasons.Add("population_incomplete");
            }

            public void MarkLoss(string reason)
            {
                _reasons.Add(reason);
                if (reason == "identity_limit_exceeded")
                {
                    _activityEvidenceLost = true;
                    _activityReasons.Add(reason);
                    _activityReasons.Add("population_incomplete");
                }
                else if (reason == "publication_unavailable")
                {
                    _publicationEvidenceLost = true;
                    _publicationReasons.Add(reason);
                }
                else if (reason is "source_failure" or "source_disabled" or "invalid_source_positions" or "catalog_changed")
                {
                    // Coverage evidence is not automatically a loss of already measured values.
                }
                else
                {
                    _distanceEvidenceLost = true;
                    _activityEvidenceLost = true;
                    _publicationEvidenceLost = true;
                    if (IsMeasureReason(reason))
                    {
                        _distanceReasons.Add(reason);
                        _activityReasons.Add(reason);
                        _publicationReasons.Add(reason);
                    }
                    else
                    {
                        _distanceReasons.Add("invalid_measure_evidence");
                        _activityReasons.Add("invalid_measure_evidence");
                        _publicationReasons.Add("invalid_measure_evidence");
                    }
                }
            }

            public void AddMissingCycle(DateTime atUtc, HashSet<string> hourReasons)
            {
                _observed = checked(_observed + 1);
                _failed = checked(_failed + 1);
                _first ??= CanonicalUtc(atUtc);
                _last = CanonicalUtc(atUtc);
                _reasons.Add("catalog_changed");
                hourReasons.Add("catalog_changed");
                _sourceEvidenceIncomplete = true;
                _activityEvidenceLost = true;
            }

            public RouteHourStatisticRow ToRow(DateTime hourStart, Guid runId, int cadenceLimit, HourCounter hour)
            {
                if (_sourceEvidence is not null && (_sourceEvidenceIncomplete || _sourceEvidenceConflict))
                    _reasons.Add("capture_failure");
                var reasons = _reasons.Concat(hour.Reasons).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
                var noData = _validActive == 0 && _validPublish == 0 && _activeSum == 0 && _peak == 0 && _distance == 0 && _intervals == 0 && _published == 0;
                var hasVersionedEvidence = _sourceEvidence is not null && !_sourceEvidenceIncomplete && !_sourceEvidenceConflict;
                var hasPositionSupport = hasVersionedEvidence && _sourceEvidence!.MembershipKnown
                    && _sourceEvidence.ContributingSourceKeys.Length > 0
                    && _sourceEvidence.Sources.Any(source => source.SuccessfulSupportCount > 0 && source.PositionedCoverageValid);
                var distanceValid = hasPositionSupport && !_distanceEvidenceLost;
                var activityValid = hasPositionSupport && !_activityEvidenceLost && !_populationLost
                    && _validActive > 0 && _activitySupport?.Count == _validActive;
                var publicationValid = hasVersionedEvidence && _validPublish > 0 && !_publicationEvidenceLost;
                AddSourceFailureReasonsIfInvalid(distanceValid, _distanceReasons);
                AddSourceFailureReasonsIfInvalid(activityValid, _activityReasons);
                AddSourceFailureReasonsIfInvalid(publicationValid, _publicationReasons);

                var complete = !noData && _observed > 0 && _validActive == _observed && _validPublish == _observed && _failed == 0
                    && hour.StartBoundaryOk && hour.EndBoundaryOk && _maxGap is not null && _maxGap <= cadenceLimit
                    && !_populationLost && !hour.CatalogChanged && reasons.Length == 0;
                if (hasVersionedEvidence)
                    complete &= distanceValid && activityValid && publicationValid && SourceEvidenceIsComplete(_sourceEvidence!);
                var status = complete ? RouteHourCoverageStatus.Complete : noData ? RouteHourCoverageStatus.NoData : RouteHourCoverageStatus.Partial;
                var row = new RouteHourStatisticRow(city, RouteJoinKey, hourStart, catalog.RouteShortName, catalog.StaticRouteId,
                    catalog.Category, catalog.Fingerprint, hour.CatalogChanged, RouteHourStatisticRow.Definition,
                    runId, status, false, reasons, cadenceLimit, _observed, _validActive, _validPublish, _failed,
                    _first, _last, _maxGap, hour.StartBoundaryOk, hour.EndBoundaryOk, _activeSum, _peak,
                    _populationLost ? null : _population?.Count ?? 0, _processed, _stale, _distance, _intervals,
                    _rejected, _detected, _published, _firstSeen, _deltaLeqZero, _teleport, _transfer);
                if (!hasVersionedEvidence) return row;

                return row with
                {
                    DefinitionVersion = RouteHourStatisticRow.SourceEvidenceDefinition,
                    SourceEvidence = _sourceEvidence,
                    DistanceValid = distanceValid,
                    ActivityValid = activityValid,
                    PublicationValid = publicationValid,
                    DistanceReasons = FreezeReasons(distanceValid, _distanceReasons),
                    ActivityReasons = FreezeReasons(activityValid, _activityReasons),
                    PublicationReasons = FreezeReasons(publicationValid, _publicationReasons),
                    SourceSetFingerprint = _sourceEvidence!.SourceSetFingerprint,
                    ActivitySupportFingerprint = activityValid ? _activitySupport!.GetFingerprint() : null,
                };
            }

            void AddSourceFailureReasonsIfInvalid(bool valid, HashSet<string> target)
            {
                if (valid) return;
                if (!_sourceEvidenceIncomplete && _sourceEvidence is { } evidence)
                {
                    foreach (var reason in evidence.Sources.SelectMany(source => source.Reasons))
                        if (IsMeasureReason(reason)) target.Add(reason);
                    if (!evidence.MembershipKnown) target.Add("unknown_source_provenance");
                }
                if (target.Count == 0) target.Add(_sourceEvidence is null ? "unknown_source_provenance" : "invalid_measure_evidence");
            }

            static ImmutableArray<string> FreezeReasons(bool valid, HashSet<string> source) => valid
                ? []
                : source.Order(StringComparer.Ordinal).ToImmutableArray();

            static bool IsMeasureReason(string reason) => MeasureReasonCodes.Contains(reason);

            static bool SourceEvidenceIsComplete(RouteHourSourceSummary evidence) => evidence.MembershipKnown
                && evidence.ExpectedSourceKeys.Length > 0
                && evidence.Sources.All(source => source.FailedPollCount == 0 && source.DisabledPollCount == 0
                    && source.PositionedCoverageValid && !source.RecoveredDuringHour && source.StartBoundaryOk && source.EndBoundaryOk);

            static RouteHourSourceSummary MergeSourceEvidence(RouteHourSourceSummary first, RouteHourSourceSummary next)
            {
                if (!first.MembershipKnown || !next.MembershipKnown
                    || !string.Equals(first.SourceSetFingerprint, next.SourceSetFingerprint, StringComparison.Ordinal)
                    || !first.ExpectedSourceKeys.SequenceEqual(next.ExpectedSourceKeys, StringComparer.Ordinal))
                    throw new InvalidOperationException("Route source membership changed during the hour.");

                var sources = ImmutableArray.CreateBuilder<SourceHourEvidenceSummary>(first.Sources.Length);
                foreach (var prior in first.Sources)
                {
                    var current = next.Sources.FirstOrDefault(item => item.SourceKey == prior.SourceKey)
                        ?? throw new InvalidOperationException("Route source inventory changed during the hour.");
                    var gap = prior.LastSuccessfulReceiptUtc is { } priorLast && current.FirstSuccessfulReceiptUtc is { } currentFirst
                        && currentFirst >= priorLast
                        ? (int)Math.Ceiling((currentFirst - priorLast).TotalSeconds)
                        : 0;
                    var maxGap = new[] { prior.MaxSuccessfulGapSeconds ?? 0, current.MaxSuccessfulGapSeconds ?? 0, gap }.Max();
                    var successful = checked(prior.SuccessfulPollCount + current.SuccessfulPollCount);
                    var empty = checked(prior.EmptyPollCount + current.EmptyPollCount);
                    var failed = checked(prior.FailedPollCount + current.FailedPollCount);
                    var disabled = checked(prior.DisabledPollCount + current.DisabledPollCount);
                    if (checked(successful + empty + failed + disabled) > SourceEvidenceValidation.MaximumPollsPerHour)
                        throw new InvalidOperationException("Route source poll evidence exceeded its hourly bound.");
                    var recovered = prior.RecoveredDuringHour || current.RecoveredDuringHour
                        || (prior.FailedPollCount > 0 || prior.DisabledPollCount > 0) && current.SuccessfulSupportCount > 0;
                    var reasons = prior.Reasons.Concat(current.Reasons).ToHashSet(StringComparer.Ordinal);
                    if (failed > 0) reasons.Add("source_failure");
                    if (disabled > 0) reasons.Add("source_disabled");
                    if (recovered) reasons.Add("source_recovered");
                    var anyPriorSupport = prior.SuccessfulSupportCount > 0;
                    var anyCurrentSupport = current.SuccessfulSupportCount > 0;
                    var positionedValid = anyPriorSupport && anyCurrentSupport
                        ? prior.PositionedCoverageValid && current.PositionedCoverageValid
                        : anyPriorSupport ? prior.PositionedCoverageValid : current.PositionedCoverageValid;
                    sources.Add(new SourceHourEvidenceSummary(prior.SourceKey, successful, empty, failed, disabled,
                        Min(prior.FirstSuccessfulReceiptUtc, current.FirstSuccessfulReceiptUtc),
                        Max(prior.LastSuccessfulReceiptUtc, current.LastSuccessfulReceiptUtc),
                        successful + empty == 0 ? null : maxGap,
                        prior.StartBoundaryOk, current.EndBoundaryOk, positionedValid, recovered,
                        reasons.Order(StringComparer.Ordinal).ToImmutableArray()));
                }

                var frozen = sources.ToImmutable();
                return first with
                {
                    ContributingSourceKeys = frozen.Where(source => source.SuccessfulSupportCount > 0 && source.PositionedCoverageValid)
                        .Select(source => source.SourceKey).Order(StringComparer.Ordinal).ToImmutableArray(),
                    Sources = frozen,
                };
            }

            static DateTime? Min(DateTime? first, DateTime? next) => first is null ? next : next is null ? first : first < next ? first : next;
            static DateTime? Max(DateTime? first, DateTime? next) => first is null ? next : next is null ? first : first > next ? first : next;

            public void ReleasePopulationSet()
            {
                _population = null;
                _activitySupport?.Dispose();
            }
        }
    }

    static readonly ImmutableHashSet<string> MeasureReasonCodes =
    [
        "boundary_unproven", "capture_failure", "catalog_changed", "gap_exceeded", "identity_limit_exceeded",
        "invalid_measure_evidence", "invalid_source_positions", "population_incomplete", "processing_failure",
        "publication_unavailable", "route_index_unavailable", "source_disabled", "source_failure",
        "source_recovered", "unaligned_activity_samples", "unknown_source_provenance", "unsupported_definition",
    ];
}
