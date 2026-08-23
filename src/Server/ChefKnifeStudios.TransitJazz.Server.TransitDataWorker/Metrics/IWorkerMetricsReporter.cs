namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

/// <summary>Worker-owned monitoring boundary; implementations never receive entity-level data.</summary>
public interface IWorkerMetricsReporter
{
    void InitializeCities(IEnumerable<string> cityNames);
    void ReportCityCycle(CityCycleMetrics metrics);
    void ReportCityError(string cityName);
    void ReportWorkerCycleCompleted(WorkerCycleMetrics metrics);
    Task FlushAsync(CancellationToken cancellationToken);
}
