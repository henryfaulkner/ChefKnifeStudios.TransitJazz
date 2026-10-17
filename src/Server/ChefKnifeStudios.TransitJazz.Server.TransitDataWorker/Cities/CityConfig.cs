using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

public class CityConfig
{
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string[] GtfsRtUrls { get; set; } = [];
    /// <summary>Second, independently-fetched RT feed for cities that merge two transit
    /// modes under one city (e.g. nymta: GtfsRtUrls = subway line groups, BusGtfsRtUrls =
    /// the citywide bus feed). Empty for every other city.</summary>
    public string[] BusGtfsRtUrls { get; set; } = [];
    /// <summary>Explicit one-to-one identity policy for GtfsRtUrls. No endpoint URL is part of identity.</summary>
    public ReportedVehicleSourceConfig[] ReportedVehicleSources { get; set; } = [];
    /// <summary>Explicit one-to-one identity policy for BusGtfsRtUrls.</summary>
    public ReportedVehicleSourceConfig[] BusReportedVehicleSources { get; set; } = [];
    public string[] StaticZipUrls { get; set; } = [];
    public RailRealtimeConfig? RailRealtime { get; set; }
    public Dictionary<string, string>? RailRouteIdMap { get; set; }
    public string? ApiKeyEnvVar { get; set; }
    public string ApiKeyQueryParam { get; set; } = "api_key";
    public string[] RouteIdNormalization { get; set; } = [];

    public class RailRealtimeConfig
    {
        public string BaseUrl { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public ReportedVehicleSourceConfig? ReportedVehicleSource { get; set; }
    }
}

public sealed class ReportedVehicleSourceConfig
{
    public string SourceKey { get; set; } = string.Empty;
    public string IdentityNamespace { get; set; } = string.Empty;
    public string IdentityKind { get; set; } = "vehicle-descriptor-id";
    public string IdentityRuleVersion { get; set; } = "descriptor-id-v1";
    public int ExpectedCadenceSeconds { get; set; } = 30;
    public int GapLimitSeconds { get; set; } = 60;
    public bool AllowFallbackIdentity { get; set; }

    public ReportedVehicleSourceDefinition ToDefinition()
    {
        var definition = new ReportedVehicleSourceDefinition
        {
            SourceKey = SourceKey,
            IdentityNamespace = IdentityNamespace,
            IdentityKind = IdentityKind,
            IdentityRuleVersion = IdentityRuleVersion,
            ExpectedCadenceSeconds = ExpectedCadenceSeconds,
            GapLimitSeconds = GapLimitSeconds,
            AllowFallbackIdentity = AllowFallbackIdentity,
        };
        definition.Validate();
        return definition;
    }
}
