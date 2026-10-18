using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>Bounded current and predecessor UTC-hour populations with source-local continuity.</summary>
public sealed class ReportedVehicleAccumulator
{
    readonly string _citySlug;
    readonly Guid _captureRunId;
    readonly ImmutableArray<ReportedVehicleSourceDefinition> _sources;
    readonly string _sourceFingerprint;
    readonly string _cadenceFingerprint;
    readonly ReportedVehicleHistoryOptions _options;
    readonly ReportedVehicleBufferBudget _budget;
    readonly TimeProvider _timeProvider;
    readonly object _gate = new();
    readonly SortedDictionary<DateTime, HourCounter> _hours = [];
    readonly Dictionary<string, DateTime> _lastSuccessfulUtc = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTime> _lastReceiptUtc = new(StringComparer.Ordinal);
    DateTime? _latestHourStart;
    DateTime? _sealedThroughUtc;
    bool _sourceSetChanged;

    public ReportedVehicleAccumulator(string citySlug, Guid captureRunId,
        ReportedVehicleSourceCatalogEntry catalog, string sourceFingerprint, string cadenceFingerprint,
        ReportedVehicleHistoryOptions options, ReportedVehicleBufferBudget budget, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(citySlug)) throw new ArgumentException("City slug is required.", nameof(citySlug));
        if (captureRunId == Guid.Empty) throw new ArgumentException("Capture run ID is required.", nameof(captureRunId));
        _citySlug = citySlug;
        _captureRunId = captureRunId;
        _sources = catalog.Sources;
        _sourceFingerprint = sourceFingerprint;
        _cadenceFingerprint = cadenceFingerprint;
        _options = options;
        _budget = budget;
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (_sources.IsDefaultOrEmpty) throw new ArgumentException("A collector needs an explicit source catalog.", nameof(catalog));
    }

    public IReadOnlyList<FinalizedReportedVehicleHourBatch> Observe(ReportedVehicleSourceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();
        lock (_gate)
        {
            var receipt = observation.ReceiptUtc;
            var hourStart = FloorHour(receipt);
            var output = new List<FinalizedReportedVehicleHourBatch>(1);
            if (_sealedThroughUtc is { } sealedThrough && hourStart <= sealedThrough) return output;
            if (!_sources.Any(x => string.Equals(x.SourceKey, observation.SourceKey, StringComparison.Ordinal)))
            {
                _sourceSetChanged = true;
                foreach (var hour in _hours.Values) hour.AddReason("source_set_changed");
                return output;
            }

            var lastReceipt = _lastReceiptUtc.GetValueOrDefault(observation.SourceKey, DateTime.MinValue);
            var clockRegressed = lastReceipt != DateTime.MinValue && receipt < lastReceipt;
            if (clockRegressed)
                foreach (var hour in _hours.Values) hour.AddReason("clock_regression");
            if (_latestHourStart is { } known && hourStart < known.AddHours(-1)) return output;
            if (_latestHourStart is { } newest && hourStart > newest.AddHours(1))
            {
                foreach (var hour in _hours.Values)
                {
                    hour.AddReason("clock_regression");
                    hour.AddReason("end_boundary_missing");
                }
                foreach (var key in _hours.Keys.ToArray())
                {
                    output.Add(Freeze(key, _hours[key]));
                    _hours.Remove(key);
                    _sealedThroughUtc = key;
                }
                _lastSuccessfulUtc.Clear();
                _lastReceiptUtc.Clear();
            }
            if (_hours.Count >= _options.MaxOpenHoursPerCity && !_hours.ContainsKey(hourStart))
            {
                var oldest = _hours.Keys.Min();
                _hours[oldest].AddReason("end_boundary_missing");
                output.Add(Freeze(oldest, _hours[oldest]));
                _hours.Remove(oldest);
                _sealedThroughUtc = oldest;
            }

            if (!_hours.TryGetValue(hourStart, out var counter))
            {
                counter = new HourCounter(_citySlug, hourStart, _sources, _options, _budget);
                _hours.Add(hourStart, counter);
            }
            _latestHourStart = _latestHourStart is { } previousHour && previousHour > hourStart ? previousHour : hourStart;
            counter.AddObservation(observation, _options);
            if (clockRegressed) counter.AddReason("clock_regression");

            if (observation.IsSuccessful)
            {
                var lastSuccess = _lastSuccessfulUtc.GetValueOrDefault(observation.SourceKey, DateTime.MinValue);
                if (lastSuccess != DateTime.MinValue)
                {
                    var gap = (decimal)(receipt - lastSuccess).TotalSeconds;
                    var definition = _sources.Single(x => x.SourceKey == observation.SourceKey);
                    if (gap < 0)
                    {
                        counter.AddReason("clock_regression");
                        foreach (var old in _hours.Values) old.AddReason("clock_regression");
                    }
                    else
                    {
                        counter.RecordGap(observation.SourceKey, gap);
                        if (gap > definition.GapLimitSeconds)
                        {
                            counter.AddSourceReason(observation.SourceKey, "cadence_gap");
                            counter.AddReason("cadence_gap");
                            foreach (var old in _hours.Where(x => x.Key < hourStart).Select(x => x.Value))
                                if (old.SourceLastSuccessful(observation.SourceKey) is { } prior && prior < receipt)
                                {
                                    old.RecordGap(observation.SourceKey, gap);
                                    old.AddSourceReason(observation.SourceKey, "cadence_gap");
                                    old.AddReason("cadence_gap");
                                }
                        }
                        else if (lastSuccess <= hourStart && receipt >= hourStart)
                        {
                            counter.SetStartBoundary(observation.SourceKey, lastSuccess);
                        }
                    }

                    foreach (var (oldStart, oldHour) in _hours.Where(x => x.Key < hourStart))
                    {
                        if (receipt < oldStart.AddHours(1)) continue;
                        var prior = oldHour.SourceLastSuccessful(observation.SourceKey);
                        if (prior is null || prior >= oldStart.AddHours(1)) continue;
                        var boundaryGap = (decimal)(receipt - prior.Value).TotalSeconds;
                        if (boundaryGap <= definition.GapLimitSeconds)
                        {
                            oldHour.RecordGap(observation.SourceKey, boundaryGap);
                            oldHour.SetEndBoundary(observation.SourceKey, receipt);
                        }
                        else
                        {
                            oldHour.AddSourceReason(observation.SourceKey, "end_boundary_missing");
                            oldHour.AddReason("end_boundary_missing");
                        }
                    }

                    if (receipt >= lastSuccess) _lastSuccessfulUtc[observation.SourceKey] = receipt;
                }
                else
                {
                    counter.AddSourceReason(observation.SourceKey, "start_boundary_missing");
                    counter.AddReason("startup_fragment");
                    _lastSuccessfulUtc[observation.SourceKey] = receipt;
                }

                foreach (var key in _hours.Keys.Where(x => x < hourStart).ToArray())
                {
                    var older = _hours[key];
                    if (older.AllSourcesEnded)
                    {
                        output.Add(Freeze(key, older));
                        _hours.Remove(key);
                        _sealedThroughUtc = key;
                    }
                }
            }
            if (!clockRegressed) _lastReceiptUtc[observation.SourceKey] = receipt;
            return output;
        }
    }

    public IReadOnlyList<FinalizedReportedVehicleHourBatch> Observe(IEnumerable<ReportedVehicleSourceObservation> observations)
    {
        var output = new List<FinalizedReportedVehicleHourBatch>();
        foreach (var observation in observations) output.AddRange(Observe(observation));
        return output;
    }

    public IReadOnlyList<FinalizedReportedVehicleHourBatch> Sweep(DateTime nowUtc)
    {
        lock (_gate)
        {
            var now = CanonicalUtc(nowUtc);
            var ready = new List<FinalizedReportedVehicleHourBatch>();
            foreach (var key in _hours.Keys.ToArray())
            {
                var counter = _hours[key];
                var maxGap = _sources.Max(x => x.GapLimitSeconds);
                if (!counter.AllSourcesEnded && now < key.AddHours(1).AddSeconds(maxGap)) continue;
                if (!counter.AllSourcesEnded) counter.AddReason("end_boundary_missing");
                ready.Add(Freeze(key, counter));
                _hours.Remove(key);
                _sealedThroughUtc = key;
            }
            return ready;
        }
    }

    public IReadOnlyList<FinalizedReportedVehicleHourBatch> FlushPending()
    {
        lock (_gate)
        {
            var ready = new List<FinalizedReportedVehicleHourBatch>(_hours.Count);
            foreach (var (key, counter) in _hours)
            {
                counter.AddReason("shutdown_fragment");
                counter.AddReason("end_boundary_missing");
                ready.Add(Freeze(key, counter));
                _sealedThroughUtc = key;
            }
            _hours.Clear();
            return ready;
        }
    }

    public void MarkSourceSetChanged()
    {
        lock (_gate)
        {
            _sourceSetChanged = true;
            foreach (var hour in _hours.Values) hour.AddReason("source_set_changed");
        }
    }

    public void MarkCaptureLoss()
    {
        lock (_gate)
            foreach (var hour in _hours.Values) hour.MarkIdentityLoss();
    }

    FinalizedReportedVehicleHourBatch Freeze(DateTime key, HourCounter counter)
    {
        try
        {
            var header = counter.ToHeader(key, _captureRunId, _sourceFingerprint, _cadenceFingerprint,
                _sourceSetChanged, _timeProvider.GetUtcNow().UtcDateTime);
            IEnumerable<ReportedVehicleIdentity> memberships = counter.IdentityIntegrity
                ? counter.Memberships.OrderBy(x => x.Namespace, StringComparer.Ordinal)
                    .ThenBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal)
                : Array.Empty<ReportedVehicleIdentity>();
            var batch = new FinalizedReportedVehicleHourBatch(header, memberships);
            batch.Validate(_options);
            return batch;
        }
        finally
        {
            counter.ReleaseReservations();
        }
    }

    static DateTime CanonicalUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) value = value.ToUniversalTime();
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }

    static DateTime FloorHour(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    sealed class HourCounter
    {
        readonly Dictionary<string, SourceCounter> _sources;
        readonly ReportedVehicleHistoryOptions _options;
        readonly ReportedVehicleBufferBudget _budget;
        readonly HashSet<ReportedVehicleIdentity> _memberships = [];
        readonly List<IDisposable> _reservations = [];
        readonly HashSet<string> _reasons = new(StringComparer.Ordinal);
        readonly HashSet<string> _limitations = new(StringComparer.Ordinal) { "descriptor_identity" };
        public DateTime HourStartUtc { get; }
        public string CitySlug { get; }
        public bool IdentityIntegrity { get; private set; } = true;
        public IEnumerable<ReportedVehicleIdentity> Memberships => _memberships;
        public bool AllSourcesEnded => _sources.Values.All(x => x.EndBoundaryOk);

        public HourCounter(string city, DateTime hourStartUtc, ImmutableArray<ReportedVehicleSourceDefinition> definitions,
            ReportedVehicleHistoryOptions options, ReportedVehicleBufferBudget budget)
        {
            CitySlug = city;
            HourStartUtc = hourStartUtc;
            _options = options;
            _budget = budget;
            _sources = definitions.ToDictionary(x => x.SourceKey, x => new SourceCounter(x), StringComparer.Ordinal);
        }

        public void AddObservation(ReportedVehicleSourceObservation observation, ReportedVehicleHistoryOptions options)
        {
            if (observation.ReceiptUtc < HourStartUtc || observation.ReceiptUtc >= HourStartUtc.AddHours(1))
            {
                AddReason("clock_regression");
                return;
            }
            var source = _sources[observation.SourceKey];
            source.Add(observation);
            if (!observation.IsSuccessful) AddReason("source_failure");
            if (observation.UnidentifiableRecordCount > 0)
            {
                AddReason("missing_identity");
                source.AddReason("missing_identity");
            }
            if (!observation.IdentityIntegrity) MarkIdentityLoss();
            if (!IdentityIntegrity || !observation.IsSuccessful) return;
            foreach (var identity in observation.Identities)
            {
                if (_memberships.Contains(identity)) continue;
                if (_memberships.Count >= options.MaxDistinctPerCityHour
                    || !_budget.TryReserve(ReportedVehicleBufferKind.Active, options.BytesPerIdentity(identity), out var reservation))
                {
                    MarkIdentityLoss();
                    return;
                }
                _memberships.Add(identity);
                _reservations.Add(reservation!);
            }
        }

        public void SetStartBoundary(string key, DateTime witness) => _sources[key].SetStartBoundary(witness);
        public void SetEndBoundary(string key, DateTime witness) => _sources[key].SetEndBoundary(witness);
        public DateTime? SourceLastSuccessful(string key) => _sources[key].LastSuccessfulInHour;
        public void AddSourceReason(string key, string reason) => _sources[key].AddReason(reason);
        public void RecordGap(string key, decimal gap) => _sources[key].RecordGap(gap);
        public void AddReason(string reason) => _reasons.Add(reason);

        public void MarkIdentityLoss()
        {
            IdentityIntegrity = false;
            AddReason("identity_limit_exceeded");
            _limitations.Add("identity_limit_exceeded");
        }

        public void ReleaseReservations()
        {
            foreach (var reservation in _reservations) reservation.Dispose();
            _reservations.Clear();
            _memberships.Clear();
        }

        public ReportedVehicleHourHeader ToHeader(DateTime hour, Guid runId, string sourceFingerprint,
            string cadenceFingerprint, bool sourceSetChanged, DateTime nowUtc)
        {
            if (sourceSetChanged) AddReason("source_set_changed");
            foreach (var source in _sources.Values)
            {
                if (source.Successes == 0) AddReason("missing_source");
                if (!source.StartBoundaryOk) AddReason("start_boundary_missing");
                if (!source.EndBoundaryOk) AddReason("end_boundary_missing");
                foreach (var reason in source.Reasons) AddReason(reason);
                if (source.FallbackRecordCount > 0) _limitations.Add("agency_fallback_identity");
            }

            var successful = _sources.Values.Sum(x => x.Successes);
            var failed = _sources.Values.Sum(x => x.Failures);
            var unidentifiable = _sources.Values.Sum(x => x.UnidentifiableRecords);
            var fallback = _sources.Values.Sum(x => x.FallbackRecordCount);
            var status = successful == 0 ? ReportedVehicleCollectionStatus.NoData
                : IdentityIntegrity && failed == 0 && unidentifiable == 0 && _reasons.Count == 0
                    && _sources.Values.All(x => x.StartBoundaryOk && x.EndBoundaryOk && x.MaximumGapSeconds is not null)
                    ? ReportedVehicleCollectionStatus.Complete
                    : ReportedVehicleCollectionStatus.Partial;
            var diagnostics = _sources.Values.OrderBy(x => x.Definition.SourceKey, StringComparer.Ordinal)
                .Select(x => x.ToDiagnostic()).ToImmutableArray();
            var allTimes = _sources.Values.SelectMany(x => new[] { x.FirstSuccessfulUtc, x.LastSuccessfulUtc })
                .Where(x => x is not null).Select(x => x!.Value).ToArray();
            return new ReportedVehicleHourHeader(CitySlug, hour, ReportedVehicleHistoryOptions.DefinitionVersion,
                runId, sourceFingerprint, cadenceFingerprint, status, IdentityIntegrity, false,
                IdentityIntegrity ? _memberships.Count : 0, successful, failed, unidentifiable, fallback, _sources.Count,
                diagnostics, _reasons.Order(StringComparer.Ordinal).ToImmutableArray(),
                _limitations.Order(StringComparer.Ordinal).ToImmutableArray(),
                allTimes.Length == 0 ? null : allTimes.Min(), allTimes.Length == 0 ? null : allTimes.Max(), CanonicalUtc(nowUtc));
        }
    }

    sealed class SourceCounter(ReportedVehicleSourceDefinition definition)
    {
        readonly HashSet<string> _reasons = new(StringComparer.Ordinal);
        public ReportedVehicleSourceDefinition Definition { get; } = definition;
        public long Successes { get; private set; }
        public long Failures { get; private set; }
        public long RawVehicleRecords { get; private set; }
        public long UnidentifiableRecords { get; private set; }
        public long FallbackRecordCount { get; private set; }
        public DateTime? FirstSuccessfulUtc { get; private set; }
        public DateTime? LastSuccessfulUtc { get; private set; }
        public DateTime? LastSuccessfulInHour { get; private set; }
        public DateTime? PredecessorWitnessUtc { get; private set; }
        public DateTime? SuccessorWitnessUtc { get; private set; }
        public decimal? MaximumGapSeconds { get; private set; }
        public bool StartBoundaryOk { get; private set; }
        public bool EndBoundaryOk { get; private set; }
        public IEnumerable<string> Reasons => _reasons;

        public void Add(ReportedVehicleSourceObservation observation)
        {
            if (!observation.IsSuccessful)
            {
                Failures = checked(Failures + 1);
                AddReason("source_failure");
                return;
            }
            Successes = checked(Successes + 1);
            RawVehicleRecords = checked(RawVehicleRecords + observation.RawVehicleRecordCount);
            UnidentifiableRecords = checked(UnidentifiableRecords + observation.UnidentifiableRecordCount);
            FallbackRecordCount = checked(FallbackRecordCount + observation.FallbackRecordCount);
            FirstSuccessfulUtc ??= observation.ReceiptUtc;
            LastSuccessfulUtc = observation.ReceiptUtc;
            LastSuccessfulInHour = observation.ReceiptUtc;
        }

        public void RecordGap(decimal gap) => MaximumGapSeconds = MaximumGapSeconds is { } previous
            ? Math.Max(previous, gap) : gap;

        public void SetStartBoundary(DateTime witness)
        {
            StartBoundaryOk = true;
            PredecessorWitnessUtc = witness;
            _reasons.Remove("start_boundary_missing");
        }

        public void SetEndBoundary(DateTime witness)
        {
            EndBoundaryOk = true;
            SuccessorWitnessUtc = witness;
            _reasons.Remove("end_boundary_missing");
        }

        public void AddReason(string reason) => _reasons.Add(reason);

        public ReportedVehicleSourceDiagnostic ToDiagnostic() => new(
            Definition.SourceKey, Successes, Failures, FirstSuccessfulUtc, LastSuccessfulUtc,
            PredecessorWitnessUtc, SuccessorWitnessUtc, MaximumGapSeconds, StartBoundaryOk, EndBoundaryOk,
            RawVehicleRecords, UnidentifiableRecords, FallbackRecordCount,
            _reasons.Order(StringComparer.Ordinal).ToImmutableArray());
    }
}
