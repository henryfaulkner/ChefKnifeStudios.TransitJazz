using ChefKnifeStudios.TransitJazz.Shared.GtfsData;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

/// <summary>
/// Supplies the current static route-shape catalogue to the co-hosted worker.
/// Implementations must wait for a non-empty catalogue rather than returning an
/// empty result while static GTFS data is still loading.
/// </summary>
public interface IRouteShapeSource
{
    Task<IReadOnlyList<RouteShapeFeature>> GetAllShapesAsync(CancellationToken ct);

    /// <summary>Completes after the loader publishes a newer route-shape generation.</summary>
    Task WaitForNextRefreshAsync(CancellationToken ct);
}

/// <summary>
/// Makes the standalone worker fail clearly rather than silently running without a route index.
/// The deployed Web API host replaces this with its co-hosted in-memory source.
/// </summary>
public sealed class UnavailableRouteShapeSource : IRouteShapeSource
{
    public static readonly UnavailableRouteShapeSource Instance = new();

    private const string Message = "A route-shape source is required. Run the worker in the co-hosted Web API application or register an IRouteShapeSource.";

    private UnavailableRouteShapeSource() { }

    public Task<IReadOnlyList<RouteShapeFeature>> GetAllShapesAsync(CancellationToken ct) =>
        Task.FromException<IReadOnlyList<RouteShapeFeature>>(new InvalidOperationException(Message));

    public Task WaitForNextRefreshAsync(CancellationToken ct) =>
        Task.FromException(new InvalidOperationException(Message));
}
