using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

public interface ITransitCity
{
    string Name { get; }
    Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct);
}
