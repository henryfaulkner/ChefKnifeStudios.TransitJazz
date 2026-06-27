namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;

public class CityConfig
{
    public string Name { get; set; } = string.Empty;
    public string[] GtfsRtUrls { get; set; } = [];
    public string[] StaticZipUrls { get; set; } = [];
    public RailRealtimeConfig? RailRealtime { get; set; }
    public Dictionary<string, string>? RailRouteIdMap { get; set; }
    public string? ApiKeyEnvVar { get; set; }
    public bool EmitsTelemetry { get; set; }

    public class RailRealtimeConfig
    {
        public string BaseUrl { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }
}
