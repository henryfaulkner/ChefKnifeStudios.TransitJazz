namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Abstract base for all logging-sidecar events. Carries a <see cref="CycleId"/> that
/// correlates Snap/Lerp rows to their parent Cycle row across all three datasets.
/// </summary>
public abstract class LogEventArgs : IEventArgs
{
    /// <summary>Per-cycle correlation key generated at the top of each reconciliation cycle.</summary>
    public required string CycleId { get; init; }
}
