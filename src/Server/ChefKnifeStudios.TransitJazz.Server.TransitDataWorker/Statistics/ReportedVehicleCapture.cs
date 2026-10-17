using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>Owns bounded per-city accumulators and makes nonblocking whole-envelope delivery attempts.</summary>
public sealed class ReportedVehicleCapture
{
    readonly ReportedVehicleHistoryRuntimeOptions _runtime;
    readonly ReportedVehicleHistoryOptions _options;
    readonly ReportedVehicleSourceCatalog _catalog;
    readonly ReportedVehicleBufferBudget _budget;
    readonly IReportedVehicleStatisticsSink _sink;
    readonly TimeProvider _timeProvider;
    readonly ILogger<ReportedVehicleCapture> _logger;
    readonly Guid _runId = Guid.NewGuid();
    readonly Dictionary<string, ReportedVehicleAccumulatorState> _cities = new(StringComparer.OrdinalIgnoreCase);
    readonly object _citiesGate = new();

    public ReportedVehicleCapture(ReportedVehicleHistoryRuntimeOptions runtime,
        ReportedVehicleSourceCatalog catalog, ReportedVehicleBufferBudget budget,
        IReportedVehicleStatisticsSink sink, TimeProvider timeProvider, ILogger<ReportedVehicleCapture> logger)
    {
        _runtime = runtime;
        _options = runtime.Limits;
        _catalog = catalog;
        _budget = budget;
        _sink = sink;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsEnabledFor(string city) => _runtime.IsEnabledFor(city);

    public void Observe(string city, IEnumerable<ReportedVehicleSourceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var captured = observations.ToArray();
        if (!IsEnabledFor(city))
        {
            foreach (var observation in captured) observation.Dispose();
            return;
        }
        try
        {
            var state = GetCityState(city);
            Enqueue(state, state.Accumulator.Observe(captured));
        }
        catch (Exception exception)
        {
            SafeLog(() => _logger.LogWarning("Reported-vehicle capture failed for {City}; exception type {ExceptionType}.",
                city, exception.GetType().Name));
            try { GetCityState(city).Accumulator.MarkCaptureLoss(); } catch { }
        }
        finally
        {
            foreach (var observation in captured) observation.Dispose();
        }
    }

    public void Sweep()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var state in GetCityStates())
        {
            try { Enqueue(state, state.Accumulator.Sweep(now)); }
            catch (Exception exception)
            {
                state.Accumulator.MarkCaptureLoss();
                SafeLog(() => _logger.LogWarning("Reported-vehicle sweep failed for {City}; exception type {ExceptionType}.",
                    state.CitySlug, exception.GetType().Name));
            }
        }
    }

    public void FlushPending()
    {
        foreach (var state in GetCityStates())
        {
            try { Enqueue(state, state.Accumulator.FlushPending()); }
            catch (Exception exception)
            {
                state.Accumulator.MarkCaptureLoss();
                SafeLog(() => _logger.LogWarning("Reported-vehicle shutdown finalization failed for {City}; exception type {ExceptionType}.",
                    state.CitySlug, exception.GetType().Name));
            }
        }
    }

    ReportedVehicleAccumulatorState GetCityState(string city)
    {
        lock (_citiesGate)
        {
            if (_cities.TryGetValue(city, out var existing)) return existing;
            if (!_catalog.TryGet(city, out var catalog)) throw new InvalidOperationException("No reported-vehicle source catalog exists for an enabled city.");
            var state = new ReportedVehicleAccumulatorState(city, new ReportedVehicleAccumulator(city, _runId,
                catalog, _catalog.SourceSetFingerprint(city), _catalog.CadencePolicyFingerprint(city),
                _options, _budget, _timeProvider));
            _cities.Add(city, state);
            return state;
        }
    }

    ReportedVehicleAccumulatorState[] GetCityStates()
    {
        lock (_citiesGate) return _cities.Values.ToArray();
    }

    void Enqueue(ReportedVehicleAccumulatorState state, IReadOnlyList<FinalizedReportedVehicleHourBatch> batches)
    {
        foreach (var batch in batches)
        {
            var bytes = EstimateBatchBytes(batch);
            if (!_budget.TryReserve(ReportedVehicleBufferKind.Queued, bytes, out var reservation))
            {
                SafeLog(() => _logger.LogWarning("Reported-vehicle queue admission dropped {City} hour={HourStartUtc}; memberships={MembershipCount}, reservedBytes={ReservedBytes}.",
                    state.CitySlug, batch.Header.HourStartUtc, batch.Header.MembershipCount, _budget.ReservedBytes(ReportedVehicleBufferKind.Queued)));
                continue;
            }
            try
            {
                if (_sink.TryEnqueue(batch, reservation!)) continue;
            }
            catch (Exception exception)
            {
                SafeLog(() => _logger.LogWarning("Reported-vehicle sink rejected {City} hour={HourStartUtc}; exception type {ExceptionType}.",
                    state.CitySlug, batch.Header.HourStartUtc, exception.GetType().Name));
            }
            reservation!.Dispose();
        }
    }

    long EstimateBatchBytes(FinalizedReportedVehicleHourBatch batch)
    {
        long bytes = checked(2_048L + System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(batch.Header.SourceDiagnostics).Length);
        foreach (var identity in batch.Memberships) bytes = checked(bytes + _options.BytesPerIdentity(identity));
        return bytes;
    }

    static void SafeLog(Action log)
    {
        try { log(); } catch { }
    }

    sealed record ReportedVehicleAccumulatorState(string CitySlug, ReportedVehicleAccumulator Accumulator);
}
