namespace ChefKnifeStudios.MartaJazz.Shared;

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
    }

    public static class Transit
    {
        public const string GetLastBatch = "/transit/last-batch";
    }
}
