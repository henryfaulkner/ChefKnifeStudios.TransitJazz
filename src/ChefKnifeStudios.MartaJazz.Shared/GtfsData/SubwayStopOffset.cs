namespace ChefKnifeStudios.MartaJazz.Shared.GtfsData;

public sealed record SubwayStopOffsetSet(
    string RouteJoinKey,
    string Direction,
    double[][] Coordinates,
    double[] CumulativeDistanceMeters,
    SubwayStop[] Stops
);

public sealed record SubwayStop(
    string StopId,
    double Lat,
    double Lon,
    double DistanceAlongShapeMeters
);
