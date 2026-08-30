namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

public sealed class StructuredLoggingOptions
{
    public const string SectionName = "Logging:Structured";

    /// <summary>Kill switch for structured application events. The host still owns console setup.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum time between reminders for one active city/event/reason condition.</summary>
    public TimeSpan ReminderInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Optional safe deployment revision supplied by the platform/deployment.</summary>
    public string? DeploymentRevision { get; set; }

    /// <summary>Allows tests and controlled environments to disable coalescing explicitly.</summary>
    public bool CoalesceRepeatedConditions { get; set; } = true;
}

