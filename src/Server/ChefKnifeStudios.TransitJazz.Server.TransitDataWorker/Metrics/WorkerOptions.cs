using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>The liveness basis for the worker and all monitoring alerts.</summary>
    public int CycleIntervalSeconds { get; init; } = 10;

    public void Validate()
    {
        if (CycleIntervalSeconds <= 0)
            throw new OptionsValidationException(SectionName, typeof(WorkerOptions), ["CycleIntervalSeconds must be positive."]);
    }
}
