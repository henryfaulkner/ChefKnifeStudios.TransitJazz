using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Metrics;

public sealed class WorkerDashboardStatisticsContractTests
{
    [Fact]
    public void WorkerMetricInstrumentsAndCityLabelRemainStable()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/WorkerMetricsReporter.cs"));

        Assert.Contains("transit.city", source, StringComparison.Ordinal);
        foreach (var instrument in RequiredCityInstruments)
            Assert.Contains($"\"{instrument}\"", source, StringComparison.Ordinal);
    }

    static readonly string[] RequiredCityInstruments =
    [
        "transitjazz.worker.city.last_cycled",
        "transitjazz.worker.city.last_worked",
        "transitjazz.worker.city.cycles",
        "transitjazz.worker.city.cycle_errors",
        "transitjazz.worker.city.cycle_duration",
        "transitjazz.worker.city.healthy",
        "transitjazz.worker.city.input_fetch_ok",
        "transitjazz.worker.city.input_records_valid",
        "transitjazz.worker.city.has_input_records",
        "transitjazz.worker.city.input_lag",
        "transitjazz.worker.city.input_timestamp_known",
        "transitjazz.worker.city.input_source_failures",
        "transitjazz.worker.city.vehicles_processed",
        "transitjazz.worker.city.tones_emitted",
        "transitjazz.worker.city.batch_wire",
        "transitjazz.worker.city.crossings_suppressed_first_seen",
        "transitjazz.worker.city.crossings_suppressed_delta_leq0",
        "transitjazz.worker.city.crossings_suppressed_teleport",
        "transitjazz.worker.city.crossings_suppressed_transfer",
        "transitjazz.worker.city.vehicle_state_cache",
        "transitjazz.worker.city.crossing_baseline_cache",
        "transitjazz.worker.city.route_index",
        "transitjazz.worker.city.route_trigger_point_cache",
    ];

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
