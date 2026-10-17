using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;

/// <summary>Extracts original MARTA train IDs before realtime, line, or coordinate filtering.</summary>
internal static class ReportedVehicleRailExtractor
{
    public static IEnumerable<string?> TrainIds(IEnumerable<RailArrivalDto> arrivals)
    {
        foreach (var arrival in arrivals) yield return arrival.TrainId;
    }
}
