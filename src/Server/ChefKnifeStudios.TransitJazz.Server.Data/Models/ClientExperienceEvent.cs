namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public sealed class ClientExperienceEvent
{
    public Guid EventId { get; set; }
    public Guid PageSessionId { get; set; }
    public Guid? CityActivationId { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public bool ClockSkewed { get; set; }
    public string Environment { get; set; } = "production";
    public string CitySlug { get; set; } = "unknown";
    public string ClientRelease { get; set; } = "unknown";
    public int SchemaVersion { get; set; }
    public int MeasurementVersion { get; set; }
    public string EventKind { get; set; } = "";
    public long PageElapsedMs { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? EpisodeId { get; set; }
    public string? Journey { get; set; }
    public string? Capability { get; set; }
    public string? Outcome { get; set; }
    public string Payload { get; set; } = "{}";
}
