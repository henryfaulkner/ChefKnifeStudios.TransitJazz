using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;

public class RailRealtimeOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool ReportedVehicleCaptureEnabled { get; set; }
    public ReportedVehicleSourceConfig? BusReportedVehicleSource { get; set; }
    public ReportedVehicleSourceConfig? RailReportedVehicleSource { get; set; }
}
