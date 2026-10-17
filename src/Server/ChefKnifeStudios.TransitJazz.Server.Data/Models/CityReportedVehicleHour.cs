namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public enum ReportedVehicleCollectionStatus { Complete, Partial, NoData }

/// <summary>Immutable source coverage and population header for one city and UTC hour.</summary>
public sealed class CityReportedVehicleHour
{
    public const string CurrentDefinitionVersion = "reported-vehicles-v1";

    public string CitySlug { get; set; } = string.Empty;
    public DateTime HourStartUtc { get; set; }
    public string DefinitionVersion { get; set; } = CurrentDefinitionVersion;
    public Guid CaptureRunId { get; set; }
    public string SourceSetFingerprint { get; set; } = string.Empty;
    public string CadencePolicyFingerprint { get; set; } = string.Empty;
    public ReportedVehicleCollectionStatus CollectionStatus { get; set; }
    public bool IdentityIntegrity { get; set; }
    public bool HasConflict { get; set; }
    public long MembershipCount { get; set; }
    public long SuccessfulPollCount { get; set; }
    public long FailedPollCount { get; set; }
    public long UnidentifiableRecordCount { get; set; }
    public long FallbackRecordCount { get; set; }
    public int ExpectedSourceCount { get; set; }
    public string SourceDiagnosticsJson { get; set; } = "[]";
    public string[] IncompleteReasons { get; set; } = [];
    public string[] IdentityLimitations { get; set; } = [];
    public DateTime? ObservedStartUtc { get; set; }
    public DateTime? ObservedEndUtc { get; set; }
    public DateTime PersistedAtUtc { get; set; }
    public ICollection<CityReportedVehicleHourMembership> Memberships { get; set; } = new List<CityReportedVehicleHourMembership>();
}
