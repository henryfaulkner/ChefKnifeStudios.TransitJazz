namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;

public class CityConfig
{
    public string Name { get; set; } = string.Empty;
    public string[] GtfsRtUrls { get; set; } = [];
    /// <summary>Second, independently-fetched RT feed for cities that merge two transit
    /// modes under one city (e.g. nymta: GtfsRtUrls = subway line groups, BusGtfsRtUrls =
    /// the citywide bus feed). Empty for every other city.</summary>
    public string[] BusGtfsRtUrls { get; set; } = [];
    public string[] StaticZipUrls { get; set; } = [];
    public RailRealtimeConfig? RailRealtime { get; set; }
    public Dictionary<string, string>? RailRouteIdMap { get; set; }
    public string? ApiKeyEnvVar { get; set; }
    public string ApiKeyQueryParam { get; set; } = "api_key";
    public bool EmitsTelemetry { get; set; }
    public string[] RouteIdNormalization { get; set; } = [];

    public class RailRealtimeConfig
    {
        public string BaseUrl { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }
}
