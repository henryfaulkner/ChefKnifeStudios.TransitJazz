using System;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public delegate void EventReceivedEventHandler(object sender, IEventArgs e);
public delegate Task<bool> AsyncEventReceivedEventHandler(object sender, IEventArgs e);

public interface IEventArgs { }

public interface IEventNotificationService
{
    event EventReceivedEventHandler EventReceived;
    event AsyncEventReceivedEventHandler? EventReceivedAsync;
    void PostEvent(object sender, IEventArgs e);
    Task<bool> PostEventAsync(object sender, IEventArgs e);
}

public class EventNotificationService : IEventNotificationService
{
    public event EventReceivedEventHandler? EventReceived;
    public event AsyncEventReceivedEventHandler? EventReceivedAsync;

    public void PostEvent(object sender, IEventArgs e)
    {
        EventReceived?.Invoke(sender, e);
    }

    public async Task<bool> PostEventAsync(object sender, IEventArgs e)
    {
        try { PostEvent(sender, e); }
        catch { return false; }

        var handlers = EventReceivedAsync;
        if (handlers is null) return true;
        foreach (AsyncEventReceivedEventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                if (!await handler(sender, e).ConfigureAwait(false)) return false;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }
}
