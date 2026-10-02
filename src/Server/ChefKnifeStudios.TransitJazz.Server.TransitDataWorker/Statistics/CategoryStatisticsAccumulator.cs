using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed class CategoryStatisticsAccumulator(string citySlug, int cadenceLimitSeconds)
{
    const string DefinitionVersion = "observed-city-category-statistics-v1";
    readonly object _gate = new();
    readonly Dictionary<(string Category, DateTime Start), MinuteCounter> _minutes = [];
    readonly Dictionary<(string Category, DateTime Start), HourCounter> _hours = [];
    readonly Dictionary<string, DateTime> _lastCategoryObservation = new(StringComparer.Ordinal);
    readonly HashSet<DateTime> _unknownActiveHours = [];
    DateTime? _latestObservedAt;

    public FinalizedCategoryStatisticsBatch? Observe(CategoryCycleSnapshot cycle)
    {
        lock (_gate)
        {
            var finalizedMinutes = ImmutableArray.CreateBuilder<CategoryStatisticRow>();
            var finalizedHours = ImmutableArray.CreateBuilder<CategoryStatisticRow>();
            var at = CanonicalUtc(cycle.CompletedAtUtc);
            if (_latestObservedAt is { } latest && at < latest)
            {
                foreach (var minute in _minutes.Values) minute.QueueLoss = true;
                foreach (var hour in _hours.Values) hour.QueueLoss = true;
                return null;
            }
            _latestObservedAt = at;
            var minuteStart = FloorMinute(at);
            var hourStart = FloorHour(at);
            var categories = cycle.ConfiguredCategories.Select(NormalizeCategory).Where(x => x != "unknown").Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            foreach (var category in cycle.ActivityVehiclesByCategory.Keys) categories.Add(NormalizeCategory(category));
            foreach (var category in cycle.MovementByCategory.Keys) categories.Add(NormalizeCategory(category));
            foreach (var category in cycle.CrossingsByCategory.Keys) categories.Add(NormalizeCategory(category));
            categories.Remove("unknown");
            var unknownActivityObserved = cycle.ActivityEligible
                && cycle.ActivityVehiclesByCategory.TryGetValue("unknown", out var unknownVehicles)
                && unknownVehicles.Count > 0;
            var unknownMovementObserved = cycle.MovementByCategory.TryGetValue("unknown", out var unknownMovement)
                && unknownMovement.AcceptedIntervalCount > 0;
            var unknownPublicationObserved = cycle.PublicationKnown
                && cycle.CrossingsByCategory.GetValueOrDefault("unknown") > 0;
            var unknownActivationObserved = unknownActivityObserved || unknownMovementObserved || unknownPublicationObserved;
            var unknownActivatedNow = unknownActivationObserved && _unknownActiveHours.Add(hourStart);
            var unknownActive = _unknownActiveHours.Contains(hourStart);
            if (unknownActive) categories.Add("unknown");

            // Unknown is intentionally absent from configured zero-fill. Its first observation
            // in a new hour still closes the prior hour's final minute when it is a healthy
            // boundary successor, then activation resets for the new hour.
            if (!unknownActive
                && _lastCategoryObservation.TryGetValue("unknown", out var priorUnknownAt)
                && FloorHour(priorUnknownAt) < hourStart)
            {
                var priorKey = ("unknown", FloorMinute(priorUnknownAt));
                if (_minutes.TryGetValue(priorKey, out var priorMinute))
                {
                    var gap = (decimal)(at - priorUnknownAt).TotalSeconds;
                    var gapUs = decimal.Round(gap, 6, MidpointRounding.AwayFromZero);
                    priorMinute.AddGap(gapUs);
                    priorMinute.SuccessorEvidence = gapUs <= cadenceLimitSeconds;
                    var row = priorMinute.ToRow();
                    finalizedMinutes.Add(row);
                    var priorHourKey = ("unknown", FloorHour(priorKey.Item2));
                    if (!_hours.TryGetValue(priorHourKey, out var priorHour))
                        _hours[priorHourKey] = priorHour = new HourCounter(citySlug, "unknown", priorHourKey.Item2, cadenceLimitSeconds);
                    priorHour.AddMinute(row, queueLoss: false);
                    _minutes.Remove(priorKey);
                }
                _lastCategoryObservation.Remove("unknown");
            }

            foreach (var category in categories.Order(StringComparer.Ordinal))
            {
                var key = (category, minuteStart);
                if (!_minutes.TryGetValue(key, out var minute))
                    _minutes[key] = minute = new MinuteCounter(citySlug, category, minuteStart, cadenceLimitSeconds);
                if (category == "unknown" && unknownActivatedNow && at == hourStart)
                    minute.PredecessorEvidence = true;

                if (_lastCategoryObservation.TryGetValue(category, out var priorAt))
                {
                    var gap = (decimal)(at - priorAt).TotalSeconds;
                    var gapUs = decimal.Round(gap, 6, MidpointRounding.AwayFromZero);
                    minute.AddGap(gapUs);
                    var priorKey = (category, FloorMinute(priorAt));
                    if (priorKey != key && _minutes.TryGetValue(priorKey, out var priorMinute))
                    {
                        priorMinute.SuccessorEvidence = gapUs <= cadenceLimitSeconds;
                        priorMinute.AddGap(gapUs);
                        minute.PredecessorEvidence |= gapUs <= cadenceLimitSeconds;
                    }
                }

                foreach (var oldKey in _minutes.Keys.Where(x => x.Category == category && x.Start < minuteStart).OrderBy(x => x.Start).ToArray())
                {
                    var old = _minutes[oldKey];
                    var row = old.ToRow();
                    finalizedMinutes.Add(row);
                    var hourKey = (category, FloorHour(oldKey.Start));
                    if (!_hours.TryGetValue(hourKey, out var hour)) _hours[hourKey] = hour = new HourCounter(citySlug, category, hourKey.Item2, cadenceLimitSeconds);
                    hour.AddMinute(row, queueLoss: false);
                    _minutes.Remove(oldKey);
                }

                var movement = cycle.MovementByCategory.GetValueOrDefault(category);
                var activeVehicles = cycle.ActivityVehiclesByCategory.GetValueOrDefault(category);
                minute.AddCycle(at, cycle.ActivityEligible, cycle.PublicationKnown, cycle.CycleFailed,
                    activeVehicles?.Count ?? 0, movement, cycle.PublicationKnown ? cycle.CrossingsByCategory.GetValueOrDefault(category) : 0);
                _lastCategoryObservation[category] = at;

                var hourKeyCurrent = (category, hourStart);
                if (!_hours.TryGetValue(hourKeyCurrent, out var currentHour))
                    _hours[hourKeyCurrent] = currentHour = new HourCounter(citySlug, category, hourStart, cadenceLimitSeconds);
                if (cycle.ActivityEligible && activeVehicles is not null) currentHour.Population.UnionWith(activeVehicles);
                currentHour.LateActivation |= unknownActivatedNow && category == "unknown" && at > hourStart;
            }

            // Finalize hours only after the successor observation has closed the last minute.
            foreach (var key in _hours.Keys.Where(x => x.Start < hourStart
                && !_minutes.Keys.Any(m => m.Category == x.Category && FloorHour(m.Start) == x.Start))
                .OrderBy(x => x.Start).ThenBy(x => x.Category, StringComparer.Ordinal).ToArray())
            {
                var counter = _hours[key];
                finalizedHours.Add(counter.ToRow());
                _hours.Remove(key);
                if (key.Category == "unknown") _unknownActiveHours.Remove(key.Start);
            }

            if (finalizedMinutes.Count == 0 && finalizedHours.Count == 0) return null;
            return new FinalizedCategoryStatisticsBatch
            {
                CitySlug = citySlug,
                Minutes = finalizedMinutes.ToImmutable(),
                Hours = finalizedHours.ToImmutable(),
            };
        }
    }

    public FinalizedCategoryStatisticsBatch? Sweep(DateTime nowUtc)
    {
        lock (_gate)
        {
            var now = CanonicalUtc(nowUtc);
            var minutes = ImmutableArray.CreateBuilder<CategoryStatisticRow>();
            var hours = ImmutableArray.CreateBuilder<CategoryStatisticRow>();
            foreach (var key in _minutes.Keys.Where(x => x.Start.AddMinutes(1).AddSeconds(cadenceLimitSeconds) <= now).OrderBy(x => x.Start).ThenBy(x => x.Category, StringComparer.Ordinal).ToArray())
            {
                var counter = _minutes[key];
                counter.SuccessorEvidence = false;
                var row = counter.ToRow();
                minutes.Add(row);
                var hourKey = (key.Category, FloorHour(key.Start));
                if (!_hours.TryGetValue(hourKey, out var hour)) _hours[hourKey] = hour = new HourCounter(citySlug, key.Category, hourKey.Item2, cadenceLimitSeconds);
                hour.AddMinute(row, queueLoss: false);
                _minutes.Remove(key);
            }
            foreach (var key in _hours.Keys.Where(x => x.Start.AddHours(1) <= now
                && !_minutes.Keys.Any(m => m.Category == x.Category && FloorHour(m.Start) == x.Start))
                .OrderBy(x => x.Start).ThenBy(x => x.Category, StringComparer.Ordinal).ToArray())
            {
                var counter = _hours[key];
                hours.Add(counter.ToRow());
                _hours.Remove(key);
                if (key.Category == "unknown") _unknownActiveHours.Remove(key.Start);
            }
            if (minutes.Count == 0 && hours.Count == 0) return null;
            return new FinalizedCategoryStatisticsBatch { CitySlug = citySlug, Minutes = minutes.ToImmutable(), Hours = hours.ToImmutable() };
        }
    }

    public FinalizedCategoryStatisticsBatch? FlushPending()
    {
        lock (_gate)
        {
            var minutes = ImmutableArray.CreateBuilder<CategoryStatisticRow>();
            var hours = ImmutableArray.CreateBuilder<CategoryStatisticRow>();
            foreach (var key in _minutes.Keys.OrderBy(x => x.Start).ThenBy(x => x.Category, StringComparer.Ordinal).ToArray())
            {
                var row = _minutes[key].ToRow() with { Status = CategoryCoverageStatus.Partial };
                minutes.Add(row);
                var hourKey = (key.Category, FloorHour(key.Start));
                if (!_hours.TryGetValue(hourKey, out var hour))
                    _hours[hourKey] = hour = new HourCounter(citySlug, key.Category, hourKey.Item2, cadenceLimitSeconds);
                hour.AddMinute(row, queueLoss: false);
                _minutes.Remove(key);
            }
            foreach (var key in _hours.Keys.OrderBy(x => x.Start).ThenBy(x => x.Category, StringComparer.Ordinal).ToArray())
            {
                hours.Add(_hours[key].ToRow() with { Status = CategoryCoverageStatus.Partial });
                _hours.Remove(key);
            }
            _unknownActiveHours.Clear();
            _lastCategoryObservation.Clear();
            if (minutes.Count == 0 && hours.Count == 0) return null;
            return new FinalizedCategoryStatisticsBatch { CitySlug = citySlug, Minutes = minutes.ToImmutable(), Hours = hours.ToImmutable() };
        }
    }

    public void MarkQueueLoss(FinalizedCategoryStatisticsBatch batch)
    {
        lock (_gate)
        {
            // A lost finalized minute also invalidates its still-open parent-hour candidate.
            foreach (var minute in batch.Minutes)
            {
                var key = (minute.Category, FloorHour(minute.WindowStartUtc));
                if (_hours.TryGetValue(key, out var hour)) hour.QueueLoss = true;
            }
        }
    }

    public void MarkCaptureLoss()
    {
        lock (_gate)
        {
            foreach (var minute in _minutes.Values) minute.QueueLoss = true;
            foreach (var hour in _hours.Values) hour.QueueLoss = true;
        }
    }

    DateTime CanonicalUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) value = value.ToUniversalTime();
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }

    static DateTime FloorMinute(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
    static DateTime FloorHour(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);
    static string NormalizeCategory(string value) => value.Trim().ToLowerInvariant();

    sealed class MinuteCounter(string city, string category, DateTime start, int cadence)
    {
        public long ObservedCycles, ValidActiveSamples, ValidPublishCycles, FailedCycles, ActiveVehicles, DistanceIntervals, DistanceRejected, PublishedCrossings;
        public decimal Distance;
        public decimal? MaxGap;
        public DateTime? First, Last;
        public bool PredecessorEvidence, SuccessorEvidence, QueueLoss;
        public void AddGap(decimal value) => MaxGap = MaxGap is null ? value : Math.Max(MaxGap.Value, value);

        public void AddCycle(DateTime at, bool activeEligible, bool publicationKnown, bool cycleFailed, int activeCount, MovementMetrics movement, long crossings)
        {
            ObservedCycles = checked(ObservedCycles + 1);
            First ??= at;
            Last = at;
            if (activeEligible)
            {
                ValidActiveSamples = checked(ValidActiveSamples + 1);
                ActiveVehicles = checked(ActiveVehicles + activeCount);
            }
            if (publicationKnown) ValidPublishCycles = checked(ValidPublishCycles + 1);
            if (cycleFailed || !activeEligible || !publicationKnown) FailedCycles = checked(FailedCycles + 1);
            if (movement.Accepted)
            {
                Distance = checked(Distance + movement.DistanceMeters);
                DistanceIntervals = checked(DistanceIntervals + movement.AcceptedIntervalCount);
            }
            DistanceRejected = checked(DistanceRejected + movement.RejectedCount);
            if (publicationKnown) PublishedCrossings = checked(PublishedCrossings + crossings);
        }

        public CategoryStatisticRow ToRow()
        {
            var status = QueueLoss || !PredecessorEvidence || !SuccessorEvidence || FailedCycles > 0
                || ValidActiveSamples != ObservedCycles || ValidPublishCycles != ObservedCycles
                || MaxGap is null || MaxGap > cadence ? CategoryCoverageStatus.Partial
                : CategoryCoverageStatus.Complete;
            if (ValidActiveSamples == 0 && ValidPublishCycles == 0 && DistanceIntervals == 0 && PublishedCrossings == 0)
                status = CategoryCoverageStatus.NoData;
            return new(city, category, start, DefinitionVersion, status, cadence, ObservedCycles, ValidActiveSamples,
                ValidPublishCycles, FailedCycles, First, Last, MaxGap, ActiveVehicles, Distance, DistanceIntervals,
                DistanceRejected, PublishedCrossings);
        }
    }

    sealed class HourCounter(string city, string category, DateTime start, int cadence)
    {
        public readonly HashSet<string> Population = new(StringComparer.Ordinal);
        public long ObservedCycles, ValidActiveSamples, ValidPublishCycles, FailedCycles, ActiveVehicles, DistanceIntervals, DistanceRejected, PublishedCrossings;
        public decimal Distance;
        public decimal? MaxGap;
        public DateTime? First, Last;
        public int TotalMinutes, CoveredMinutes;
        public bool QueueLoss, LateActivation;

        public void AddMinute(CategoryStatisticRow row, bool queueLoss)
        {
            TotalMinutes++;
            if (row.Status == CategoryCoverageStatus.Complete) CoveredMinutes++;
            ObservedCycles = checked(ObservedCycles + row.ObservedCycleCount);
            ValidActiveSamples = checked(ValidActiveSamples + row.ValidActiveSampleCount);
            ValidPublishCycles = checked(ValidPublishCycles + row.ValidPublishCycleCount);
            FailedCycles = checked(FailedCycles + row.FailedCycleCount);
            ActiveVehicles = checked(ActiveVehicles + row.ActiveVehicleCountSum);
            Distance = checked(Distance + row.DistanceMetersSum);
            DistanceIntervals = checked(DistanceIntervals + row.DistanceIntervalCount);
            DistanceRejected = checked(DistanceRejected + row.DistanceRejectedCount);
            PublishedCrossings = checked(PublishedCrossings + row.CrossingsPublishedCount);
            if (row.FirstCycleUtc is { } first && (First is null || first < First)) First = first;
            if (row.LastCycleUtc is { } last && (Last is null || last > Last)) Last = last;
            if (row.MaxObservationGapSeconds is { } gap) MaxGap = MaxGap is null ? gap : Math.Max(MaxGap.Value, gap);
            QueueLoss |= queueLoss;
        }

        public CategoryStatisticRow ToRow()
        {
            var complete = TotalMinutes == 60 && CoveredMinutes == 60 && !QueueLoss && !LateActivation
                && FailedCycles == 0 && ValidActiveSamples == ObservedCycles && ValidPublishCycles == ObservedCycles
                && MaxGap is not null && MaxGap <= cadence;
            var status = complete ? CategoryCoverageStatus.Complete
                : ValidActiveSamples == 0 && ValidPublishCycles == 0 && DistanceIntervals == 0 && PublishedCrossings == 0
                    ? CategoryCoverageStatus.NoData : CategoryCoverageStatus.Partial;
            return new(city, category, start, DefinitionVersion, status, cadence, ObservedCycles, ValidActiveSamples,
                ValidPublishCycles, FailedCycles, First, Last, MaxGap, ActiveVehicles, Distance, DistanceIntervals,
                DistanceRejected, PublishedCrossings, QueueLoss ? null : Population.Count, CoveredMinutes);
        }
    }
}

public sealed record CategoryCycleSnapshot(
    DateTime CompletedAtUtc,
    IReadOnlyCollection<string> ConfiguredCategories,
    IReadOnlyDictionary<string, HashSet<string>> ActivityVehiclesByCategory,
    IReadOnlyDictionary<string, MovementMetrics> MovementByCategory,
    IReadOnlyDictionary<string, long> CrossingsByCategory,
    bool ActivityEligible,
    bool PublicationKnown,
    bool CycleFailed);

public readonly record struct MovementMetrics(long AcceptedIntervalCount, decimal DistanceMeters, long RejectedCount)
{
    public bool Accepted => AcceptedIntervalCount > 0;
    public static MovementMetrics Rejected => new(0, 0, 1);
    public static MovementMetrics None => new(0, 0, 0);
}
