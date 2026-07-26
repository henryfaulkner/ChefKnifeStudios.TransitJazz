namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record VehicleData(
    string Id,
    string? Label,
    string? LicensePlate,
    OccupancyStatus? OccupancyStatus,
    int? OccupancyPercentage
);

public enum OccupancyStatus
{
    Empty = 0,
    ManySeatsAvailable = 1,
    FewSeatsAvailable = 2,
    StandingRoomOnly = 3,
    CrushedStandingRoomOnly = 4,
    Full = 5,
    NotAcceptingPassengers = 6,
    NoDataAvailable = 7,
    NotBoardable = 8
}
