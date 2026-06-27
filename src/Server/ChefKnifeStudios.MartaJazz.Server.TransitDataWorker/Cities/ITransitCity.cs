using ChefKnifeStudios.MartaJazz.Shared.GtfsData;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;

public interface ITransitCity
{
    string Name { get; }
    Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);
    bool EmitsTelemetry { get; }
}
