using ChefKnifeStudios.TransitJazz.Shared.GtfsData;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

public interface ITransitCity
{
    string Name { get; }
    Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);
    bool EmitsTelemetry { get; }
}
