using OpenTelemetry.Metrics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

/// <summary>
/// Creates the bounded operational instruments once. City measurements use only
/// <c>transit.city</c>; instance identity is a resource attribute configured by the host.
/// </summary>
public sealed class WorkerMetricsReporter : IWorkerMetricsReporter, IDisposable
{
    public const string MeterName = "ChefKnifeStudios.TransitJazz.TransitDataWorker";

    readonly Meter _meter;
    readonly MeterProvider? _meterProvider;
    readonly Counter<long> _workerCycles;
    readonly Counter<long> _workerCycleErrors;
    readonly Histogram<double> _workerCycleDuration;
    readonly Counter<long> _cityCycles;
    readonly Counter<long> _cityCycleErrors;
    readonly Histogram<double> _cityCycleDuration;

    readonly Gauge<double> _workerLastCycled;
    readonly Gauge<double> _workerLastWorked;
    readonly Gauge<int> _workerCycleInterval;
    readonly Gauge<long> _workerCycleAllocated;
    readonly Gauge<long> _workerGcHeap;
    readonly Gauge<long> _workerWorkingSet;
    readonly Gauge<double> _cityLastCycled;
    readonly Gauge<double> _cityLastWorked;
    readonly Gauge<int> _cityHealthy;
    readonly Gauge<int> _cityInputFetchOk;
    readonly Gauge<int> _cityInputRecordsValid;
    readonly Gauge<int> _cityHasInputRecords;
    readonly Gauge<double> _cityInputLag;
    readonly Gauge<int> _cityInputTimestampKnown;
    readonly Gauge<int> _cityInputSourceFailures;
    readonly Gauge<int> _cityVehiclesProcessed;
    readonly Gauge<int> _cityTonesEmitted;
    readonly Gauge<int> _cityVehicleStateCache;
    readonly Gauge<int> _cityCrossingBaselineCache;
    readonly Gauge<int> _cityRouteIndex;
    readonly Gauge<int> _cityRouteTriggerPointCache;
    readonly Gauge<int> _cityCrossingsSuppressedFirstSeen;
    readonly Gauge<int> _cityCrossingsSuppressedDeltaLeq0;
    readonly Gauge<int> _cityCrossingsSuppressedTeleport;
    readonly Gauge<int> _cityCrossingsSuppressedTransfer;
    readonly Gauge<long> _cityBatchWire;

    public WorkerMetricsReporter(IMeterFactory meterFactory, MeterProvider? meterProvider = null)
    {
        _meter = meterFactory.Create(new MeterOptions(MeterName));
        _meterProvider = meterProvider;

        _workerCycles = _meter.CreateCounter<long>("transitjazz.worker.cycles", description: "Completed worker cycles.");
        _workerCycleErrors = _meter.CreateCounter<long>("transitjazz.worker.cycle_errors", description: "Unhandled non-cancellation worker cycle failures.");
        _workerCycleDuration = _meter.CreateHistogram<double>("transitjazz.worker.cycle_duration", "s", "Full worker cycle duration.");
        _cityCycles = _meter.CreateCounter<long>("transitjazz.worker.city.cycles", description: "Completed city cycles.");
        _cityCycleErrors = _meter.CreateCounter<long>("transitjazz.worker.city.cycle_errors", description: "Non-cancellation city failures.");
        _cityCycleDuration = _meter.CreateHistogram<double>("transitjazz.worker.city.cycle_duration", "s", "City cycle duration.");

        _workerLastCycled = _meter.CreateGauge<double>("transitjazz.worker.last_cycled", "s", "UTC Unix time of the latest completed worker cycle.");
        _workerLastWorked = _meter.CreateGauge<double>("transitjazz.worker.last_worked", "s", "UTC Unix time of the latest work-producing worker cycle.");
        _workerCycleInterval = _meter.CreateGauge<int>("transitjazz.worker.cycle_interval", "s", "Configured worker cycle interval.");
        _workerCycleAllocated = _meter.CreateGauge<long>("transitjazz.worker.cycle_allocated", "By", "Bytes allocated during the worker cycle.");
        _workerGcHeap = _meter.CreateGauge<long>("transitjazz.worker.gc_heap", "By", "Process managed heap size.");
        _workerWorkingSet = _meter.CreateGauge<long>("transitjazz.worker.working_set", "By", "Process working set.");
        _cityLastCycled = _meter.CreateGauge<double>("transitjazz.worker.city.last_cycled", "s", "UTC Unix time of the latest completed city cycle.");
        _cityLastWorked = _meter.CreateGauge<double>("transitjazz.worker.city.last_worked", "s", "UTC Unix time of the latest work-producing city cycle.");
        _cityHealthy = _meter.CreateGauge<int>("transitjazz.worker.city.healthy", "1", "Whether the latest city cycle was healthy.");
        _cityInputFetchOk = _meter.CreateGauge<int>("transitjazz.worker.city.input_fetch_ok", "1", "Whether at least one city input source succeeded.");
        _cityInputRecordsValid = _meter.CreateGauge<int>("transitjazz.worker.city.input_records_valid", description: "Valid normalized input records.");
        _cityHasInputRecords = _meter.CreateGauge<int>("transitjazz.worker.city.has_input_records", "1", "Whether the city had valid input records.");
        _cityInputLag = _meter.CreateGauge<double>("transitjazz.worker.city.input_lag", "s", "Lag from a known input timestamp; zero when unknown.");
        _cityInputTimestampKnown = _meter.CreateGauge<int>("transitjazz.worker.city.input_timestamp_known", "1", "Whether the city input timestamp was known.");
        _cityInputSourceFailures = _meter.CreateGauge<int>("transitjazz.worker.city.input_source_failures", description: "Input sources that failed this cycle.");
        _cityVehiclesProcessed = _meter.CreateGauge<int>("transitjazz.worker.city.vehicles_processed", description: "Vehicles processed during the city cycle.");
        _cityTonesEmitted = _meter.CreateGauge<int>("transitjazz.worker.city.tones_emitted", description: "Tones emitted during the city cycle.");
        _cityVehicleStateCache = _meter.CreateGauge<int>("transitjazz.worker.city.vehicle_state_cache", description: "City vehicle state cache size.");
        _cityCrossingBaselineCache = _meter.CreateGauge<int>("transitjazz.worker.city.crossing_baseline_cache", description: "City crossing baseline cache size.");
        _cityRouteIndex = _meter.CreateGauge<int>("transitjazz.worker.city.route_index", description: "City route-index size.");
        _cityRouteTriggerPointCache = _meter.CreateGauge<int>("transitjazz.worker.city.route_trigger_point_cache", description: "City route trigger-point cache size.");
        _cityCrossingsSuppressedFirstSeen = _meter.CreateGauge<int>("transitjazz.worker.city.crossings_suppressed_first_seen", description: "Crossings suppressed because the vehicle was first seen.");
        _cityCrossingsSuppressedDeltaLeq0 = _meter.CreateGauge<int>("transitjazz.worker.city.crossings_suppressed_delta_leq0", description: "Crossings suppressed because distance did not advance.");
        _cityCrossingsSuppressedTeleport = _meter.CreateGauge<int>("transitjazz.worker.city.crossings_suppressed_teleport", description: "Crossings suppressed because a teleport was detected.");
        _cityCrossingsSuppressedTransfer = _meter.CreateGauge<int>("transitjazz.worker.city.crossings_suppressed_transfer", description: "Crossings suppressed during route transfer.");
        _cityBatchWire = _meter.CreateGauge<long>("transitjazz.worker.city.batch_wire", "By", "Published city batch size.");
    }

    public void InitializeCities(IEnumerable<string> cityNames)
    {
        foreach (var cityName in cityNames.Distinct(StringComparer.Ordinal))
        {
            var tags = CityTags(cityName);
            _cityCycles.Add(0, tags);
            _cityLastCycled.Record(0, tags);
            _cityLastWorked.Record(0, tags);
            _cityHealthy.Record(0, tags);
            _cityInputFetchOk.Record(0, tags);
            _cityInputRecordsValid.Record(0, tags);
            _cityHasInputRecords.Record(0, tags);
            _cityInputLag.Record(0, tags);
            _cityInputTimestampKnown.Record(0, tags);
            _cityInputSourceFailures.Record(0, tags);
        }
    }

    public void ReportCityCycle(CityCycleMetrics metrics)
    {
        var tags = CityTags(metrics.CityName);
        var completedUnixSeconds = metrics.CompletedAtUtc.ToUnixTimeSeconds();
        var timestampKnown = metrics.Fetch.SourceTimestampUtc is not null;
        var lag = timestampKnown
            ? Math.Max(0, (metrics.CompletedAtUtc - metrics.Fetch.SourceTimestampUtc!.Value).TotalSeconds)
            : 0;

        _cityCycles.Add(1, tags);
        _cityCycleDuration.Record(metrics.Duration.TotalSeconds, tags);
        _cityLastCycled.Record(completedUnixSeconds, tags);
        if (metrics.DidWork)
            _cityLastWorked.Record(completedUnixSeconds, tags);
        _cityHealthy.Record(metrics.Tick.HealthOk && !metrics.HasError ? 1 : 0, tags);
        _cityInputFetchOk.Record(metrics.Fetch.HasSuccessfulSource ? 1 : 0, tags);
        _cityInputRecordsValid.Record(metrics.Fetch.ValidRecordCount, tags);
        _cityHasInputRecords.Record(metrics.Fetch.ValidRecordCount > 0 ? 1 : 0, tags);
        _cityInputLag.Record(lag, tags);
        _cityInputTimestampKnown.Record(timestampKnown ? 1 : 0, tags);
        _cityInputSourceFailures.Record(metrics.Fetch.FailedSourceCount, tags);
        _cityVehiclesProcessed.Record(metrics.Tick.VehiclesProcessed, tags);
        _cityTonesEmitted.Record(metrics.Tick.TonesEmitted, tags);
        _cityVehicleStateCache.Record(metrics.Tick.VehicleStateCacheSize, tags);
        _cityCrossingBaselineCache.Record(metrics.Tick.CrossingBaselineCacheSize, tags);
        _cityRouteIndex.Record(metrics.Tick.RouteIndexSize, tags);
        _cityRouteTriggerPointCache.Record(metrics.Tick.RouteTriggerPointCacheSize, tags);
        _cityCrossingsSuppressedFirstSeen.Record(metrics.Tick.CrossingsSuppressedFirstSeen, tags);
        _cityCrossingsSuppressedDeltaLeq0.Record(metrics.Tick.CrossingsSuppressedDeltaLeq0, tags);
        _cityCrossingsSuppressedTeleport.Record(metrics.Tick.CrossingsSuppressedTeleport, tags);
        _cityCrossingsSuppressedTransfer.Record(metrics.Tick.CrossingsSuppressedTransfer, tags);
        _cityBatchWire.Record(metrics.Tick.BatchWireBytes ?? 0, tags);
    }

    public void ReportCityError(string cityName) => _cityCycleErrors.Add(1, CityTags(cityName));

    public void ReportWorkerCycleCompleted(WorkerCycleMetrics metrics)
    {
        var completedUnixSeconds = metrics.CompletedAtUtc.ToUnixTimeSeconds();
        _workerCycles.Add(1);
        _workerCycleDuration.Record(metrics.Duration.TotalSeconds);
        _workerLastCycled.Record(completedUnixSeconds);
        if (metrics.DidWork)
            _workerLastWorked.Record(completedUnixSeconds);
        _workerCycleInterval.Record(metrics.CycleIntervalSeconds);
        _workerCycleAllocated.Record(metrics.AllocatedBytes);
        _workerGcHeap.Record(metrics.GcHeapBytes);
        _workerWorkingSet.Record(metrics.WorkingSetBytes);
        if (metrics.HasError)
            _workerCycleErrors.Add(1);
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        _meterProvider?.ForceFlush((int)Math.Min(5_000, cancellationToken.CanBeCanceled ? 5_000 : 5_000));
        return Task.CompletedTask;
    }

    static TagList CityTags(string cityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cityName);
        return new TagList { { "transit.city", cityName } };
    }

    public void Dispose() => _meter.Dispose();
}
