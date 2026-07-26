namespace ChefKnifeStudios.TransitJazz.Shared;

public static class ApiEndpoints
{
    public static class Test
    {
        public const string SignalR = "/test/signalr";
    }

    public static class Gtfs
    {
        public const string GetRouteShape = "/gtfs/routes/{routeId}/shape";
        public const string GetAllRouteShapes = "/gtfs/routes/shapes";
        public const string GetAllRoutes = "/gtfs/routes";
        public const string GetSubwayStopOffsets = "/gtfs/subway/stop-offsets";
    }

    public static class Transit
    {
        public const string GetLastBatch = "/transit/last-batch";
    }

    public static class Telemetry
    {
        public const string GetTodaySummary = "/telemetry/today/summary";
        public const string GetToday = "/telemetry/today";
    }
}
