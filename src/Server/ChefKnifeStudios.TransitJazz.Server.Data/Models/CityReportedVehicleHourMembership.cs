namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

/// <summary>Exact ordinal vehicle identity retained as one complete city/hour membership.</summary>
public sealed class CityReportedVehicleHourMembership
{
    public string CitySlug { get; set; } = string.Empty;
    public DateTime HourStartUtc { get; set; }
    public string IdentityNamespace { get; set; } = string.Empty;
    public string IdentityKind { get; set; } = string.Empty;
    public string IdentityValue { get; set; } = string.Empty;
    public CityReportedVehicleHour Hour { get; set; } = null!;
}
