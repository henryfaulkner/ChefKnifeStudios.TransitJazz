namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public enum LiveDeliveryFailureReason
{
    MissingConfiguration,
    InvalidConfiguration,
    StartFailed,
    CityJoinFailed,
    ProtocolRejected,
    OperationTimedOut,
    ConnectionClosed,
    ResumeFailed,
    Cancelled
}

public sealed record LiveDeliveryState(
    bool TransportConnected,
    bool DeliveryDesired,
    bool CityJoined,
    string City,
    long ConnectionGeneration,
    LiveDeliveryFailureReason? FailureReason)
{
    public bool IsReady => TransportConnected && DeliveryDesired && CityJoined && FailureReason is null;
}
