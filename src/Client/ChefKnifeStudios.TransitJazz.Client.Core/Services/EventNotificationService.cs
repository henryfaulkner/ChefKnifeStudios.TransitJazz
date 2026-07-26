namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public delegate void EventReceivedEventHandler(object sender, IEventArgs e);

public interface IEventArgs { }

public interface IEventNotificationService
{
    event EventReceivedEventHandler EventReceived;
    void PostEvent(object sender, IEventArgs e);
}

public class EventNotificationService : IEventNotificationService
{
    public event EventReceivedEventHandler? EventReceived;

    public void PostEvent(object sender, IEventArgs e)
    {
        EventReceived?.Invoke(sender, e);
    }
}
