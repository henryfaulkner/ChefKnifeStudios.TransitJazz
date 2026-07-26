namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>Marker interface for all event payloads posted on the notification bus.</summary>
public interface IEventArgs { }

/// <summary>Delegate type for subscribers to <see cref="IEventNotificationService.EventReceived"/>.</summary>
public delegate void EventReceivedEventHandler(object sender, IEventArgs e);

/// <summary>In-process notification bus — server-side mirror of <c>Client.Core</c>'s EventNotificationService.</summary>
public interface IEventNotificationService
{
    event EventReceivedEventHandler EventReceived;
    void PostEvent(object sender, IEventArgs e);
}

/// <summary>Singleton implementation; raises <see cref="EventReceived"/> synchronously on the caller thread.</summary>
public sealed class EventNotificationService : IEventNotificationService
{
    public event EventReceivedEventHandler? EventReceived;

    public void PostEvent(object sender, IEventArgs e) => EventReceived?.Invoke(sender, e);
}
