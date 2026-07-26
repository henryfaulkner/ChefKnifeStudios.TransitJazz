namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record StopTimeData(
    string? StopId,
    uint? StopSequence,
    long? ArrivalTime,
    int? ArrivalDelay,
    int? ArrivalUncertainty,
    long? DepartureTime,
    int? DepartureDelay,
    int? DepartureUncertainty,
    StopTimeScheduleRelationship? ScheduleRelationship
);

public enum StopTimeScheduleRelationship
{
    Scheduled = 0,
    Skipped = 1,
    NoData = 2,
    Unscheduled = 3
}
