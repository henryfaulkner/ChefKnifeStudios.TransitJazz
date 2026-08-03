using ChefKnifeStudios.TransitJazz.Shared.GtfsData;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

public interface ITransitCity
{
    string Name { get; }
    /// <summary>Frozen agency identifier written only to TelemetryEvent.city_name — never a
    /// SignalR group, query parameter, config key, or URL. Deliberately distinct from
    /// <see cref="Name"/> so renaming the public-facing slug can never rewrite parquet
    /// history (052-city-slug-migration FR-016).</summary>
    string TelemetryName { get; }
    Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);
    bool EmitsTelemetry { get; }
}
