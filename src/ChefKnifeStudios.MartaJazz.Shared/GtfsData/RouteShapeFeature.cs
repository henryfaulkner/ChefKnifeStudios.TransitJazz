using ChefKnifeStudios.MartaJazz.Shared.Events;

namespace ChefKnifeStudios.MartaJazz.Shared.GtfsData;

public sealed record RouteShapeFeature(
    string Type,
    RouteShapeGeometry Geometry,
    RouteShapeProperties Properties
);

public sealed record RouteShapeGeometry(
    string Type,
    double[][] Coordinates
);

public sealed record RouteShapeProperties(
    string RouteId,
    string? RouteShortName,
    string? Color,
    string? TextColor,
    TransitMode Mode = TransitMode.Bus   // from GTFS route_type
);
