using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Subway;

public sealed class SubwaySynthesisOptions
{
    public double NominalRunSeconds { get; set; } = 90;
    public string[] GtfsRtUrls { get; set; } = [];
    public bool ReportedVehicleCaptureEnabled { get; set; }
    public ReportedVehicleSourceConfig[] ReportedVehicleSources { get; set; } = [];
}
