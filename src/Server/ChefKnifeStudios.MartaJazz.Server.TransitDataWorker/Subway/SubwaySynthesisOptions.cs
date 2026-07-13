namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Subway;

public sealed class SubwaySynthesisOptions
{
    public double NominalRunSeconds { get; set; } = 90;
    public string[] GtfsRtUrls { get; set; } = [];
}
