namespace ChefKnifeStudios.TransitJazz.Shared;

public static class CityNames
{
    public const string Marta = "marta";
    public const string Wmata = "wmata";
    public const string Mbta = "mbta";
    public const string Nymta = "nymta";
    public const string Ttc = "ttc";
    public const string Septa = "septa";
    public const string Rtd = "rtd";
}

public static class HubMethods
{
    public const string ReceiveBatch = "ReceiveBatch";
    public const string PublishBatch = "PublishBatch";
    // Version gate for the v2 RouteNearestPointRecord wire revision (feature 051, US4).
    // MessagePack's ReadDouble accepts int encodings, so a stale client bundle would decode
    // v2's scaled-int coordinates as garbage doubles and silently misrender every vehicle
    // rather than erroring. Renaming the join method makes that failure clean and immediate:
    // an old peer's invoke faults with HubException and it never joins the group.
    // LeaveCity is deliberately NOT versioned — it is only reachable post-join.
    public const string JoinCity = "JoinCityV2";
    public const string LeaveCity = "LeaveCity";
}
