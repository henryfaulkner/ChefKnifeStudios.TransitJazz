namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

/// <summary>Mergeable daily client-experience totals. Percentages and percentiles are derived at query time.</summary>
public sealed class ClientExperienceDaily
{
    public DateOnly DayUtc { get; set; }
    public string Environment { get; set; } = "production";
    public string CitySlug { get; set; } = "unknown";
    public string ClientRelease { get; set; } = "unknown";
    public int MeasurementVersion { get; set; }
    public string TimeBasis { get; set; } = "receipt";
    public string SeriesKind { get; set; } = "journey";
    public string SeriesCode { get; set; } = "unknown";
    public long EventCount { get; set; }
    public long EligibleStarts { get; set; }
    public long Succeeded { get; set; }
    public long Failed { get; set; }
    public long DeadlineExceeded { get; set; }
    public long Unresolved { get; set; }
    public long Excluded { get; set; }
    public long ObservedMs { get; set; }
    public long HealthyMs { get; set; }
    public long DegradedMs { get; set; }
    public long UnavailableMs { get; set; }
    public long PendingMs { get; set; }
    public long UnknownMs { get; set; }
    public long GapMs { get; set; }
    public long ExcludedMs { get; set; }
    public long LatencyLe500Ms { get; set; }
    public long LatencyLe1000Ms { get; set; }
    public long LatencyLe5000Ms { get; set; }
    public long LatencyLe15000Ms { get; set; }
    public long LatencyLe30000Ms { get; set; }
    public long LatencyLe60000Ms { get; set; }
    public long LatencyOverflow { get; set; }
    public long Presentations { get; set; }
    public long QualityCount { get; set; }
    public long PageSessionDays { get; set; }
}
