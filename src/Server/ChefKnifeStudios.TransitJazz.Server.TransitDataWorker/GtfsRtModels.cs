using ProtoBuf;
using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

[ProtoContract]
public class FeedMessage
{
    [ProtoMember(1)]
    public FeedHeader Header { get; set; }

    [ProtoMember(2)]
    public List<FeedEntity> Entities { get; set; } = new();
}

[ProtoContract]
public class FeedHeader
{
    [ProtoMember(1)]
    public string GtfsRealtimeVersion { get; set; }

    [ProtoMember(2)]
    public Incrementality? Incrementality { get; set; }

    [ProtoMember(3)]
    public ulong? Timestamp { get; set; }
}

[ProtoContract]
public enum Incrementality
{
    FULL_DATASET = 0,
    DIFFERENTIAL = 1
}

[ProtoContract]
public class FeedEntity
{
    [ProtoMember(1)]
    public string Id { get; set; }

    [ProtoMember(2)]
    public bool? IsDeleted { get; set; }

    // trip_update = 3, vehicle = 4, alert = 5 (per spec)
    [ProtoMember(3)]
    public TripUpdate TripUpdate { get; set; }

    [ProtoMember(4)]
    public VehiclePosition Vehicle { get; set; }

    [ProtoMember(5)]
    public Alert Alert { get; set; }
}

[ProtoContract]
public class VehiclePosition
{
    [ProtoMember(1)]
    public TripDescriptor Trip { get; set; }

    [ProtoMember(2)]
    public Position Position { get; set; }

    [ProtoMember(3)]
    public uint? CurrentStopSequence { get; set; }

    [ProtoMember(4)]
    public VehicleStopStatus? CurrentStatus { get; set; }

    [ProtoMember(5)]
    public ulong? Timestamp { get; set; }

    [ProtoMember(6)]
    public CongestionLevel? CongestionLevel { get; set; }

    [ProtoMember(7)]
    public string StopId { get; set; }

    [ProtoMember(8)]
    public VehicleDescriptor Vehicle { get; set; }

    [ProtoMember(9)]
    public OccupancyStatus? OccupancyStatus { get; set; }

    [ProtoMember(10)]
    public uint? OccupancyPercentage { get; set; }
}

[ProtoContract]
public class Position
{
    [ProtoMember(1)]
    public float Latitude { get; set; }

    [ProtoMember(2)]
    public float Longitude { get; set; }

    [ProtoMember(3)]
    public float? Bearing { get; set; }

    [ProtoMember(4)]
    public double? Odometer { get; set; }

    [ProtoMember(5)]
    public float? Speed { get; set; }
}

[ProtoContract]
public class TripDescriptor
{
    [ProtoMember(1)]
    public string TripId { get; set; }

    [ProtoMember(2)]
    public string StartTime { get; set; }

    [ProtoMember(3)]
    public string StartDate { get; set; }

    [ProtoMember(4)]
    public TripScheduleRelationship? ScheduleRelationship { get; set; }

    [ProtoMember(5)]
    public string RouteId { get; set; }

    [ProtoMember(6)]
    public uint? DirectionId { get; set; }
}

[ProtoContract]
public class VehicleDescriptor
{
    [ProtoMember(1)]
    public string Id { get; set; }

    [ProtoMember(2)]
    public string Label { get; set; }

    [ProtoMember(3)]
    public string LicensePlate { get; set; }
}

[ProtoContract]
public class TripUpdate
{
    [ProtoMember(1)]
    public TripDescriptor Trip { get; set; }

    [ProtoMember(2)]
    public List<StopTimeUpdate> StopTimeUpdates { get; set; } = new();

    [ProtoMember(3)]
    public VehicleDescriptor Vehicle { get; set; }

    [ProtoMember(4)]
    public ulong? Timestamp { get; set; }
}

[ProtoContract]
public class StopTimeUpdate
{
    [ProtoMember(1)]
    public uint? StopSequence { get; set; }

    [ProtoMember(2)]
    public StopTimeEvent Arrival { get; set; }

    [ProtoMember(3)]
    public StopTimeEvent Departure { get; set; }

    [ProtoMember(4)]
    public string StopId { get; set; }

    [ProtoMember(5)]
    public StopTimeScheduleRelationship? ScheduleRelationship { get; set; }
}

[ProtoContract]
public class StopTimeEvent
{
    [ProtoMember(1)]
    public int? Delay { get; set; }

    [ProtoMember(2)]
    public long? Time { get; set; }

    [ProtoMember(3)]
    public int? Uncertainty { get; set; }
}

[ProtoContract]
public class Alert
{
    [ProtoMember(1)]
    public List<TimeRange> ActivePeriod { get; set; } = new();

    [ProtoMember(5)]
    public List<EntitySelector> InformedEntity { get; set; } = new();

    [ProtoMember(6)]
    public AlertCause? Cause { get; set; }

    [ProtoMember(7)]
    public AlertEffect? Effect { get; set; }

    [ProtoMember(8)]
    public TranslatedString Url { get; set; }

    [ProtoMember(10)]
    public TranslatedString HeaderText { get; set; }

    [ProtoMember(11)]
    public TranslatedString DescriptionText { get; set; }

    [ProtoMember(14)]
    public AlertSeverity? SeverityLevel { get; set; }
}

[ProtoContract]
public class TimeRange
{
    [ProtoMember(1)]
    public ulong? Start { get; set; }

    [ProtoMember(2)]
    public ulong? End { get; set; }
}

[ProtoContract]
public class EntitySelector
{
    [ProtoMember(1)]
    public string AgencyId { get; set; }

    [ProtoMember(2)]
    public string RouteId { get; set; }

    [ProtoMember(3)]
    public int? RouteType { get; set; }

    [ProtoMember(4)]
    public TripDescriptor Trip { get; set; }

    [ProtoMember(5)]
    public string StopId { get; set; }
}

[ProtoContract]
public class TranslatedString
{
    [ProtoMember(1)]
    public List<Translation> Translation { get; set; } = new();
}

[ProtoContract]
public class Translation
{
    [ProtoMember(1)]
    public string Text { get; set; }

    [ProtoMember(2)]
    public string Language { get; set; }
}
