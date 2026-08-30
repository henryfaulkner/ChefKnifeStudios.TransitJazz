namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

/// <summary>Read-only readiness state exposed to the co-hosted API health check.</summary>
public interface IRouteIndexReadiness
{
    bool IsReady { get; }
}
