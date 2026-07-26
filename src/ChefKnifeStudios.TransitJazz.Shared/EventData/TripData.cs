namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record TripData(
    string? TripId,
    string? RouteId,
    int? DirectionId,
    string? StartTime,
    string? StartDate,
    TripScheduleRelationship? ScheduleRelationship
);

public enum TripScheduleRelationship
{
    Scheduled = 0,
    Unscheduled = 2,
    Canceled = 3,
    Replacement = 5,
    Duplicated = 6,
    Deleted = 7,
    New = 8
}
