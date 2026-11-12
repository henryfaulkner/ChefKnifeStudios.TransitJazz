namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

/// <summary>Single-row progress and failure state for correction-aware aggregation and retention.</summary>
public sealed class ClientExperienceMaintenance
{
    public int Id { get; set; } = 1;
    public DateTimeOffset? WatermarkUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public DateTimeOffset? LastFailureUtc { get; set; }
    public string? LastFailureCode { get; set; }
    public long LagSeconds { get; set; }
    public int JobVersion { get; set; } = 1;
}
