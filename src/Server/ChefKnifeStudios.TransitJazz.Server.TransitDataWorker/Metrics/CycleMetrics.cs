namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

/// <summary>One immutable, city-scoped observation written exactly once per configured city.</summary>
public sealed record CityCycleMetrics(
    string CityName,
    DateTimeOffset CompletedAtUtc,
    CityFetchResult Fetch,
    Worker.CityTickResult Tick,
    TimeSpan Duration,
    bool DidWork,
    bool HasError);

/// <summary>One complete worker-cycle observation, written from the cycle's outer finally block.</summary>
public sealed record WorkerCycleMetrics(
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration,
    bool DidWork,
    bool HasError,
    int CycleIntervalSeconds,
    long AllocatedBytes,
    long GcHeapBytes,
    long WorkingSetBytes,
    int ConfiguredCityCount);
