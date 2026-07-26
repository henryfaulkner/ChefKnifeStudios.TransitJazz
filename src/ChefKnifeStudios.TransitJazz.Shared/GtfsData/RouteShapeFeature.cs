namespace ChefKnifeStudios.TransitJazz.Shared.GtfsData;

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
    string Category = "bus",   // from GTFS route_type, via WebAPI's per-city classifier
    int RouteType = 3,         // raw GTFS route_type; drives client category display order
    string? City = null)
{
    /// <summary>
    /// The value used to correlate this route across GTFS-RT real-time data and the
    /// static route index. Prefers the public-facing short name (matching GTFS-RT
    /// Trip.RouteId for most cities); falls back to the true GTFS static RouteId when
    /// no short name is present. This is NOT the same as <see cref="RouteId"/> whenever
    /// a short name exists — see constitution Principle VI.
    /// </summary>
    public string JoinKey => RouteShortName ?? RouteId;
}
