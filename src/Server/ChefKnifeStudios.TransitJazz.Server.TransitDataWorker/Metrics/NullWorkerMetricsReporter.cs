namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

/// <summary>Disabled-by-default reporter that leaves the worker behaviour unchanged.</summary>
public sealed class NullWorkerMetricsReporter : IWorkerMetricsReporter
{
    public void InitializeCities(IEnumerable<string> cityNames) { }
    public void ReportCityCycle(CityCycleMetrics metrics) { }
    public void ReportCityError(string cityName) { }
    public void ReportWorkerCycleCompleted(WorkerCycleMetrics metrics) { }
    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
